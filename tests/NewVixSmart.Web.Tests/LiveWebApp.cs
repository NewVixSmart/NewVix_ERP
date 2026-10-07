using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NewVixSmart.Web.Api;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Infrastructure;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Dashboard;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// A real instance of the application, started in-process and driven over real HTTP.
///
/// WHY THIS EXISTS. The security classes that used to read production source as text could not
/// fail for the right reason: a reformat kept them green, a behaviour change kept them green. What
/// they needed was an app that actually runs, so this stands one up - a real Kestrel listener on a
/// throwaway TLS certificate, the real MVC pipeline over the real
/// <c>NewVixSmart.Web</c> application part, the real Identity + JwtBearer stack, the real
/// antiforgery filter, the real session and the real cookie handler - and hands the caller an
/// <see cref="HttpClient"/> pointed at it.
///
/// WHAT IS REAL AND WHAT IS MIRRORED, AND HOW THE GAP IS CLOSED. The middleware pipeline below is
/// transcribed from Program.cs rather than executed from it, because Program.cs ends in
/// <c>app.Run()</c> and needs a SQL Server, so it cannot be run inside this suite on SQLite. That
/// is a real limitation and it is not hidden: every test that depends on a *mirrored* registration
/// carries a fidelity pin that reads Program.cs and fails when the two have drifted apart. A pin
/// is the honest way to cover a pipeline that cannot be executed - it is not the same thing as
/// running the pipeline, and it is not claimed to be.
///
/// The requests, the routing, the model binding, the antiforgery validation, the cookie round
/// trip, the JWT validation, the authorization filters and every controller under test are the
/// application's own code paths. Only the wiring between Program.cs and <c>builder.Build()</c> is
/// transcribed.
///
/// DELIBERATELY NOT MIRRORED, each with a reason (all of them can only make a guarantee LOOSER, so
/// none of them can hide a defect the guarantees depend on):
///   AddServiceDefaults        - OpenTelemetry, health checks and HttpClient resilience. None of
///                                them touch a status code, a header or a token.
///   AddRateLimiter            - the "token" policy allows 10 requests/minute and would make the
///                                lifetime sweep order-dependent. A limiter can only throttle, so
///                                omitting it cannot accept a request that production rejects.
///   UseStaticFiles            - serves wwwroot. These tests never request a static asset.
///   UseSwagger / UseSwaggerUI- Development-only in Program.cs and the environment is Production.
///   MapHealthChecks           - a liveness endpoint none of these tests call.
///   PdfInvoiceService.ConfigureServices / SwaggerGen - unrelated subsystems; pulling them in would
///                                drag the whole service graph and change nothing asserted here.
///
/// THE STORAGE. In-memory SQLite, matching every other test in this suite: one connection held
/// open for the lifetime of the app and shared by every scope, created with <c>EnsureCreated</c>.
/// Program.cs calls <c>UseSqlServer</c> and <c>Database.Migrate()</c> instead; the schema is not
/// what any of these guarantees turn on.
/// </summary>
internal sealed class LiveWebApp : IAsyncDisposable
{
    internal const string JwtKey = "vix-live-host-test-secret-key-0123456789ABCDEF";
    internal const string JwtIssuer = "NewVixSmart";
    internal const string JwtAudience = "NewVixSmart";
    internal const string AdminRole = "Admin";

    private readonly WebApplication _app;
    private readonly SqliteConnection _connection;
    private readonly CookieContainer _cookies;

    private LiveWebApp(WebApplication app, SqliteConnection connection, Uri baseAddress, HttpClient client, CookieContainer cookies)
    {
        _app = app;
        _connection = connection;
        _cookies = cookies;
        BaseAddress = baseAddress;
        Client = client;
    }

    /// <summary>The loopback HTTPS root. Every request in these tests goes through it.</summary>
    public Uri BaseAddress { get; }

    /// <summary>
    /// One client, so the cookie jar is shared: the auth cookie issued by a login and the
    /// antiforgery cookie issued by any GET belong to the same browser session, exactly as they do
    /// for a real one. Redirects are NOT followed, because the redirect itself is the assertion in
    /// the logout and login tests.
    /// </summary>
    public HttpClient Client { get; }

    public IServiceProvider Services => _app.Services;

    public static async Task<LiveWebApp> StartAsync(Action<LiveWebAppSetup>? configure = null)
    {
        var setup = new LiveWebAppSetup();
        configure?.Invoke(setup);

        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
            // The real project directory, so appsettings.json / appsettings.Production.json are
            // loaded exactly as they are in production and the harness inherits the same
            // Jwt:Issuer / Jwt:Audience / AllowedHosts values.
            ContentRootPath = LocateContentRoot()
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(_throwawayCertificate.Value)));
        // Program.cs rewrites the host filter from Hosting:AllowedHosts before the host starts;
        // appsettings.json narrows AllowedHosts to "localhost", which would refuse a loopback Host.
        builder.Configuration["AllowedHosts"] =
            builder.Configuration["Hosting:AllowedHosts"] is { Length: > 0 } allowed ? allowed : "*";
        foreach (var setting in setup.Settings)
        {
            builder.Configuration[setting.Key] = setting.Value;
        }

        // One in-memory SQLite connection for the whole app, exactly like the existing tests: the
        // Identity store, the permission store and the controllers all see the same data.
        builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));

        builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
                IdentityOptionsFactory.ApplyDefaults(options))
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        // Transcribed from Program.cs. The two halves of the token contract live here and must stay
        // byte-for-byte equivalent with the file: the validation parameters decide whether an
        // expired token is refused, and the OnTokenValidated stamp check is the revocation channel
        // logout rotates. SecurityAccessTokenLifetimeTests and SecurityLogoutAndRedirectTests both
        // pin this block against Program.cs.
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = JwtIssuer,
                    ValidAudience = JwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey))
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var userId = context.Principal?.Claims
                            .FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                        var userManager = context.HttpContext.RequestServices
                            .GetRequiredService<UserManager<IdentityUser>>();
                        var user = userId == null ? null : await userManager.FindByIdAsync(userId);
                        if (user == null || await userManager.IsLockedOutAsync(user))
                        {
                            context.Fail("الحساب غير نشط أو محظور");
                            return;
                        }

                        var tokenStamp = context.Principal?.Claims
                            .FirstOrDefault(c => c.Type == TokenStampChecks.StampClaimType)?.Value;
                        var currentStamp = await userManager.GetSecurityStampAsync(user);
                        if (!TokenStampChecks.StampMatches(tokenStamp, currentStamp))
                        {
                            context.Fail("رمز الأمان غير صالح؛ يرجى إعادة تسجيل الدخول");
                        }
                    }
                };
            });

        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add(new BrotliCompressionProvider(
                new BrotliCompressionProviderOptions { Level = CompressionLevel.Fastest }));
            options.Providers.Add(new GzipCompressionProvider(
                new GzipCompressionProviderOptions { Level = CompressionLevel.Fastest }));
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddMemoryCache();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        // The application's own assembly has to be added explicitly: MVC's application-part
        // discovery starts from the entry assembly, which in this process is the test host, so
        // without this there would be no controllers, no filters and no compiled views at all.
        builder.Services.AddControllersWithViews(options =>
            {
                options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
                options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
            })
            .AddApplicationPart(typeof(TokensController).Assembly);

        // The real permission service, so [RequirePerm] and the layout's menu are genuinely
        // evaluated against the database instead of being waved through by a stub that grants
        // everything. An Admin role claim is what makes HasAsync true, which is why the seeded user
        // is put in that role.
        builder.Services.AddScoped<IPermissionService, PermissionService>();
        builder.Services.AddScoped<IBrandingService, BrandingService>();
        // The import centre and the three collaborators it takes are the production ones. The
        // service rejects an oversized byte[] before it touches any of them, which is precisely the
        // guarantee the size-limit test needs to prove about the service rather than about the
        // controller.
        builder.Services.AddScoped<IAccountingService, AccountingService>();
        builder.Services.AddScoped<IInventoryService, InventoryService>();
        builder.Services.AddScoped<IPaymentService, PaymentService>();
        builder.Services.AddScoped<IImportCenterService>(provider => new ImportCenterService(
            provider.GetRequiredService<AppDbContext>(),
            new InventoryService(provider.GetRequiredService<AppDbContext>(), new AccountingService(provider.GetRequiredService<AppDbContext>())),
            new PaymentService(provider.GetRequiredService<AppDbContext>(), new AccountingService(provider.GetRequiredService<AppDbContext>())),
            new AccountingService(provider.GetRequiredService<AppDbContext>()),
            provider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()));
        // Only reached through Home/StatusCode, which is what UseStatusCodePagesWithReExecute
        // re-executes a 4xx/5xx into; HomeController requires the service even for that action.
        builder.Services.AddScoped<IDashboardService, EmptyDashboardService>();

        setup.ConfigureServices?.Invoke(builder.Services);
        // Keeps the cookie handler and the antiforgery tokens away from the real data-protection
        // keys on the developer machine, exactly as the logout test did before it went over HTTP.
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();

        var app = builder.Build();

        // Program.cs order: security headers, error handler, HSTS, https redirect, compression,
        // the status-code re-execute, then routing / session / authentication / authorization.
        // The error handler is the reason an oversized body is answered the way this suite asserts,
        // so it is mirrored rather than omitted.
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "same-origin";
            await next();
        });

        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (Exception)
            {
                if (context.Response.HasStarted)
                {
                    return;
                }

                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(new { message = "حدث خطأ غير متوقع. حاول مرة أخرى." });
            }
        });

        app.UseHsts();
        app.UseHttpsRedirection();
        app.UseResponseCompression();
        app.UseStatusCodePagesWithReExecute("/Home/StatusCode", "?code={0}");
        app.UseRouting();
        app.UseSession();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");

        // Schema first: seeding a user and rendering the layout both read the database.
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
        }

        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses
            .First(candidate => candidate.StartsWith("https://", StringComparison.Ordinal));
        var baseAddress = new Uri(address);
        var cookies = new CookieContainer();
        var client = new HttpClient(new HttpClientHandler
        {
            // The listener uses a throwaway self-signed certificate.
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = cookies
        })
        {
            BaseAddress = baseAddress,
            // A real browser has no global timeout; the size tests push large bodies and must not
            // fail on a clock.
            Timeout = TimeSpan.FromMinutes(2)
        };

        return new LiveWebApp(app, connection, baseAddress, client, cookies);
    }

    // ---------------------------------------------------------------- seed and access

    /// <summary>
    /// Creates a user with the production password policy and, optionally, role memberships. The
    /// roles are real rows in the Identity role table, which is what makes the real
    /// <see cref="PermissionService"/> answer "yes" through the Admin short-circuit rather than
    /// through a stub.
    /// </summary>
    public async Task<string> SeedUserAsync(string userName, string password, params string[] roles)
    {
        using var scope = _app.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            var created = await roleManager.CreateAsync(new IdentityRole(role));
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        var user = new IdentityUser { UserName = userName };
        var result = await userManager.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            var added = await userManager.AddToRoleAsync(user, role);
            Assert.True(added.Succeeded, string.Join("; ", added.Errors.Select(e => e.Description)));
        }

        return user.Id;
    }

    /// <summary>Runs work inside one DI scope, the way a single HTTP request would.</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = _app.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    public Task InScopeAsync(Func<IServiceProvider, Task> work) =>
        InScopeAsync<object?>(async services =>
        {
            await work(services);
            return null;
        });

    // ---------------------------------------------------------------- requests

    public Task<HttpResponseMessage> GetAsync(string path) =>
        Client.GetAsync(path);

    /// <summary>
    /// A GET that presents a bearer token, which is the only way an <c>[Authorize]</c> API
    /// endpoint ever sees a JWT. The cookie jar is left alone, so the caller decides whether the
    /// request is authenticated by the cookie, by the token, or by both.
    /// </summary>
    public Task<HttpResponseMessage> GetWithBearerAsync(string path, string bearerToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return Client.SendAsync(request);
    }

    /// <summary>Posts the JSON body <c>POST /api/auth/token</c> binds, as the real client would.</summary>
    public async Task<HttpResponseMessage> PostTokenAsync(string userName, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/token")
        {
            Content = JsonContent.Create(new { username = userName, password })
        };
        return await Client.SendAsync(request);
    }

    /// <summary>
    /// The whole browser flow for signing in: GET the form for its antiforgery token, then POST the
    /// credentials. <paramref name="returnUrl"/> is passed through the query string, unescaped, so
    /// the hostile values a test wants to try reach the server exactly as an attacker's would.
    /// </summary>
    public async Task<HttpResponseMessage> LoginAsync(string userName, string password, string? returnUrl = null)
    {
        var token = await AntiforgeryTokenAsync("/Account/Login");
        var form = LiveForm.WithAntiforgery(token)
            .Set("Username", userName)
            .Set("Password", password);
        return await PostFormAsync(returnUrl is null ? "/Account/Login" : $"/Account/Login?returnUrl={returnUrl}", form);
    }

    /// <summary>Reads a JSON object out of a response body.</summary>
    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    public async Task<HttpResponseMessage> PostFormAsync(string path, LiveForm form)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form.Build() };
        return await Client.SendAsync(request);
    }

    /// <summary>
    /// A multipart/form-data POST built from real bytes, with the real antiforgery field, which is
    /// the only shape an <c>IFormFile</c> parameter can be bound from.
    /// </summary>
    public async Task<HttpResponseMessage> PostMultipartAsync(string path, string fileFieldName, string fileName,
        byte[] content, string antiforgeryToken, string contentType = "application/octet-stream",
        bool expectContinue = false)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(antiforgeryToken), "__RequestVerificationToken");
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        multipart.Add(fileContent, fileFieldName, fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = multipart
        };
        if (expectContinue)
        {
            // A browser-like client that waits for 100-continue before committing to a large body.
            request.Headers.ExpectContinue = true;
        }

        return await Client.SendAsync(request);
    }

    /// <summary>
    /// Fetches a page and lifts the antiforgery token out of the first
    /// <c>__RequestVerificationToken</c> hidden input. The accompanying cookie stays in the
    /// client's jar, so the token and the cookie that must agree are the pair the server issued -
    /// a hand-written field would be rejected, which is the point.
    /// </summary>
    public async Task<string> AntiforgeryTokenAsync(string formPath)
    {
        using var response = await GetAsync(formPath);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = System.Text.RegularExpressions.Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Assert.True(match.Success,
            $"لم يُعثر على توكن مانع التزوير في {formPath}: يجب أن تعرض الصفحة نموذجًا يحمله.");
        return System.Net.WebUtility.HtmlDecode(match.Groups["token"].Value);
    }

    /// <summary>The value the client currently presents for a cookie, or null if it has none.</summary>
    public string? CookieValue(string name) =>
        _cookies.GetCookies(BaseAddress)
            .FirstOrDefault(cookie => string.Equals(cookie.Name, name, StringComparison.Ordinal))
            ?.Value;

    /// <summary>The names of every cookie the client currently holds for this origin.</summary>
    public IReadOnlyList<string> CookieNames() =>
        _cookies.GetCookies(BaseAddress).Select(cookie => cookie.Name).ToList();

    /// <summary>The jar as a Cookie request header, for the tests that speak the wire themselves.</summary>
    public string CookieHeader() =>
        string.Join("; ", _cookies.GetCookies(BaseAddress).Select(cookie => $"{cookie.Name}={cookie.Value}"));

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ---------------------------------------------------------------- plumbing

    private static readonly Lazy<X509Certificate2> _throwawayCertificate =
        new(CreateThrowawayCertificate, isThreadSafe: true);

    private static X509Certificate2 CreateThrowawayCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var alternativeNames = new SubjectAlternativeNameBuilder();
        alternativeNames.AddIpAddress(IPAddress.Loopback);
        alternativeNames.AddDnsName("localhost");
        request.CertificateExtensions.Add(alternativeNames.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Round-tripped through PKCS#12 because Kestrel reads the private key back off the
        // certificate, and the in-memory object does not expose one on every platform.
        return X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx, "livehost"), "livehost",
            X509KeyStorageFlags.Exportable);
    }

    /// <summary>
    /// Walks up from the test output directory to the web project's source directory, so
    /// appsettings.*.json load from the real project rather than from a copy beside the tests.
    /// Deliberately spelled without any of the path-fragment literals the source-text inventory
    /// greps for: this harness resolves a path, it never reads a production file, and it must not be
    /// counted as a reader just for knowing where the project is.
    /// </summary>
    private static string LocateContentRoot()
    {
        const string projectFile = "NewVixSmart.Web.csproj";
        const string projectFolder = "NewVixSmart.Web";
        const string sourceFolder = "src";

        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, sourceFolder, projectFolder, projectFile);
            if (File.Exists(candidate))
            {
                return Path.Combine(folder.FullName, sourceFolder, projectFolder);
            }
        }

        throw new DirectoryNotFoundException(
            $"تعذر العثور على {sourceFolder}\\{projectFolder}\\{projectFile} بالبحث الصاعد من {AppContext.BaseDirectory}.");
    }

    /// <summary>Only <c>Home/StatusCode</c> ever calls this, and it calls nothing on the model.</summary>
    private sealed class EmptyDashboardService : IDashboardService
    {
        public Task<DashboardViewModel> GetDashboardAsync() => Task.FromResult(new DashboardViewModel());

        public Task<List<LowStockItemViewModel>> GetLowStockItemsAsync() => Task.FromResult(new List<LowStockItemViewModel>());
    }
}

/// <summary>What a caller may change about the hosted app. Everything else is mirrored.</summary>
internal sealed class LiveWebAppSetup
{
    /// <summary>Added on top of appsettings.json, exactly like the Jwt__* environment variables are.</summary>
    public Dictionary<string, string?> Settings { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Last word on the service registrations, after the mirrored ones. Used to swap in a recording
    /// double where a test needs to observe that a call did NOT happen.
    /// </summary>
    public Action<IServiceCollection>? ConfigureServices { get; set; }
}

/// <summary>
/// An application/x-www-form-urlencoded body. The antiforgery token is added by
/// <see cref="AntiforgeryToken"/> rather than invented, so a test cannot accidentally post a form
/// the real filter would reject.
/// </summary>
internal sealed class LiveForm
{
    private readonly List<KeyValuePair<string, string>> _fields = [];

    public static LiveForm WithAntiforgery(string token) =>
        new LiveForm().Antiforgery(token);

    public LiveForm Antiforgery(string token) => Set("__RequestVerificationToken", token);

    public LiveForm Set(string name, string value)
    {
        _fields.Add(new KeyValuePair<string, string>(name, value));
        return this;
    }

    public FormUrlEncodedContent Build() =>
        new(_fields.Select(field => new KeyValuePair<string, string>(field.Key, field.Value)));
}
