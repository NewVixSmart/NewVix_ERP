using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using NewVixSmart.Web.Extensions;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// Response-header hardening gates. Two defects are pinned here, both of which let the app answer
/// without its security headers on some paths:
///
///   1. The error handler was the OUTERMOST middleware and called Response.Clear(), which wiped
///      Strict-Transport-Security and the whole Content-Security-Policy off every 500 - and threw
///      InvalidOperationException whenever the response had already started, so an exception raised
///      mid-stream escaped the handler and aborted the connection.
///   2. The header middleware sat BELOW UseHttpsRedirection and UseStaticFiles, so the 307 of a
///      plain-http request and every JS/CSS/font response from the static file middleware shipped
///      without nosniff / Referrer-Policy / CSP.
///
/// The behavioural half runs a real Kestrel listener on loopback and speaks HTTP to it. The test
/// project carries only the Microsoft.AspNetCore.App framework reference, so
/// Microsoft.AspNetCore.Mvc.Testing and Microsoft.AspNetCore.TestHost are deliberately NOT added -
/// Kestrel is in the shared framework and gives the same end-to-end answer (real status codes,
/// real static-file serving, real HSTS) without a new dependency. The source-level gates at the
/// bottom pin that Program.cs itself keeps the fixed order, so the harness cannot quietly pass
/// while the real file regresses.
/// </summary>
public sealed class SecurityResponseHeadersTests
{
    private const string _arabicErrorMessage = "حدث خطأ غير متوقع. حاول مرة أخرى.";

    /// <summary>Mirrors Program.cs: outside Development the handler swallows and re-asserts instead of rethrowing.</summary>
    private static bool IsDevelopment => false;

    private static readonly string[] _requiredHeaders =
    [
        "Content-Security-Policy",
        "X-Content-Type-Options",
        "X-Frame-Options",
        "Referrer-Policy"
    ];

    /// <summary>
    /// Starts a pipeline in exactly the order Program.cs uses: security headers outermost, then the
    /// error handler, then HSTS, then the middleware that can answer on its own (the 307 redirect
    /// and static files), then the terminal. Two loopback listeners are opened - a real TLS one so
    /// UseHsts behaves exactly as it does behind the production certificate, and a plain one to
    /// drive the redirect. Nothing is faked, so no middleware has to be reordered to make a test
    /// pass.
    /// </summary>
    private static async Task<HeaderProbe> StartAsync(RequestDelegate terminal, IFileProvider? webRoot = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(_selfSignedCertificate.Value));
            kestrel.Listen(IPAddress.Loopback, 0);
        });
        // Deterministic regardless of an ASPNETCORE_ENVIRONMENT on the build agent.
        builder.Environment.EnvironmentName = "Production";
        builder.Services.AddRouting();
        // Appended to the builder's own configuration (not a replacement IConfiguration): the host
        // filtering middleware still needs AllowedHosts, and HttpsRedirectionMiddleware needs a port.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HTTPS_PORT"] = "443",
            ["AllowedHosts"] = "*"
        });

        // One instance, exactly like Program.cs: AddHsts copies from it and the error handler
        // re-asserts from it, so a 200 and a 500 cannot advertise different HSTS policies.
        var hstsOptions = new HstsOptions
        {
            MaxAge = TimeSpan.FromDays(365),
            IncludeSubDomains = true,
            Preload = true
        };
        builder.Services.AddHsts(options =>
        {
            options.MaxAge = hstsOptions.MaxAge;
            options.IncludeSubDomains = hstsOptions.IncludeSubDomains;
            options.Preload = hstsOptions.Preload;
        });

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            ApplySecurityHeaders(context);
            await next();
        });

        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (Exception) when (!IsDevelopment)
            {
                if (context.Response.HasStarted)
                {
                    return;
                }

                context.Response.Clear();
                ApplySecurityHeaders(context);
                ApplyHstsHeader(context, hstsOptions);
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(new { message = _arabicErrorMessage, detail = string.Empty });
            }
        });

        if (!IsDevelopment)
        {
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = webRoot ?? new NullFileProvider(),
            OnPrepareResponse = ctx =>
                ctx.Context.Response.Headers["Cache-Control"] = "public,max-age=31536000,immutable"
        });
        app.Run(terminal);

        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
            .Addresses;
        return new HeaderProbe(app, addresses.First(a => a.StartsWith("https://", StringComparison.Ordinal)),
            addresses.First(a => a.StartsWith("http://", StringComparison.Ordinal)));
    }

    private static readonly Lazy<X509Certificate2> _selfSignedCertificate = new(CreateSelfSignedCertificate, isThreadSafe: true);

    private static X509Certificate2 CreateSelfSignedCertificate()
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
        return X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx, "probe"), "probe",
            X509KeyStorageFlags.Exportable);
    }

    private static void ApplySecurityHeaders(HttpContext context)
    {
        context.SetCspNonce();
        var nonce = context.GetCspNonce();
        context.Response.Headers["Content-Security-Policy"] =
            $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'unsafe-inline'; " +
            "font-src 'self' data:; img-src 'self' data:; base-uri 'self'; object-src 'none'; " +
            "frame-ancestors 'none'; form-action 'self'";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "same-origin";
    }

    private static void ApplyHstsHeader(HttpContext context, HstsOptions options)
    {
        if (options.MaxAge <= TimeSpan.Zero || !context.Request.IsHttps)
        {
            return;
        }

        var value = "max-age=" + (long)options.MaxAge.TotalSeconds;
        if (options.IncludeSubDomains)
        {
            value += "; includeSubDomains";
        }

        if (options.Preload)
        {
            value += "; preload";
        }

        context.Response.Headers["Strict-Transport-Security"] = value;
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        foreach (var name in _requiredHeaders)
        {
            Assert.True(TryGetHeader(response, name, out _),
                $"Response is missing the {name} header. Present: {string.Join(", ", AllHeaderNames(response))}");
        }

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("same-origin", Header(response, "Referrer-Policy"));

        var csp = Header(response, "Content-Security-Policy");
        Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("form-action 'self'", csp, StringComparison.Ordinal);
        // script-src must stay nonce-only. One inline <script> was missing its nonce and the
        // tempting "fix" for that is 'unsafe-inline', which would void the whole policy.
        var scriptSrc = csp.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Single(d => d.StartsWith("script-src", StringComparison.Ordinal));
        Assert.Contains("'nonce-", scriptSrc, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-inline'", scriptSrc, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-eval'", scriptSrc, StringComparison.Ordinal);
    }

    private static IEnumerable<string> AllHeaderNames(HttpResponseMessage response) =>
        response.Headers.Concat(response.Content.Headers).Select(h => h.Key);

    private static bool TryGetHeader(HttpResponseMessage response, string name, out string value)
    {
        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = string.Join(", ", header.Value);
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static string Header(HttpResponseMessage response, string name)
    {
        Assert.True(TryGetHeader(response, name, out var value), $"Missing header {name}.");
        return value;
    }

    // ---------------------------------------------------------------- behaviour

    [Fact]
    public async Task NormalResponse_CarriesEverySecurityHeader()
    {
        await using var probe = await StartAsync(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync("<!doctype html><title>ok</title>");
        });

        using var response = await probe.GetAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task NotFoundResponse_CarriesEverySecurityHeader()
    {
        await using var probe = await StartAsync(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("لا يوجد");
        });

        using var response = await probe.GetAsync(https: true);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task HttpsRedirect_CarriesEverySecurityHeader()
    {
        await using var probe = await StartAsync(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.WriteAsync("unreachable over plain http");
        });

        // The plain-http listener really is plain http, so UseHttpsRedirection answers.
        using var response = await probe.GetAsync("/missing.css", https: false);
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.StartsWith("https://", Header(response, "Location"), StringComparison.Ordinal);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task StaticAsset_CarriesSecurityHeadersAndKeepsImmutableCaching()
    {
        using var site = new StaticSite();
        await using var probe = await StartAsync(
            _ => Task.CompletedTask,
            site.WebRoot);

        using var response = await probe.GetAsync("/site.css", https: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        // Compared directive by directive: the server is free to re-serialise the commas.
        var cacheControl = Header(response, "Cache-Control")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(directive => directive, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["immutable", "max-age=31536000", "public"], cacheControl);
        Assert.Contains("body{color:red}", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task ErrorHandler_ReassertsSecurityHeadersAndKeepsTheArabicBody()
    {
        await using var probe = await StartAsync(_ => throw new InvalidOperationException("boom"));

        using var response = await probe.GetAsync(https: true);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        AssertSecurityHeaders(response);

        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(_arabicErrorMessage, document.RootElement.GetProperty("message").GetString());
        // The API contract promises an empty detail string; the exception text must never leak there.
        Assert.Equal(string.Empty, document.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ErrorHandler_ReassertsTheSameHstsValueUseHstsWrites()
    {
        await using var probe = await StartAsync(context => context.Request.Path.StartsWithSegments("/boom")
            ? throw new InvalidOperationException("boom")
            : context.Response.WriteAsync("ok"));

        // HstsOptions.ExcludedHosts skips loopback by default, and this test runs on loopback, so the
        // Host is presented as a normal public name - which is what a browser would send.
        using var ok = await probe.GetAsync("/", https: true, host: "erp.example.com");
        using var failed = await probe.GetAsync("/boom", https: true, host: "erp.example.com");

        var expected = "max-age=31536000; includeSubDomains; preload";
        Assert.Equal(expected, Header(ok, "Strict-Transport-Security"));
        // If the re-asserted value ever drifted from the configured policy, the browser would see
        // one HSTS state on a 500 and another on a 200 for the same origin.
        Assert.Equal(expected, Header(failed, "Strict-Transport-Security"));
    }

    [Fact]
    public async Task ErrorHandler_LeavesAStartedResponseAloneInsteadOfAbortingTheConnection()
    {
        await using var probe = await StartAsync(async context =>
        {
            // Flush the head so the response has genuinely started on the wire, exactly like a PDF
            // stream that fails halfway through.
            context.Response.ContentType = "application/pdf";
            await context.Response.Body.WriteAsync("partial"u8.ToArray());
            await context.Response.Body.FlushAsync();
            throw new InvalidOperationException("boom while streaming");
        });

        using var response = await probe.GetAsync(https: true);

        // The connection stays usable and the partial body is delivered: no 500 rewrite, and above
        // all no InvalidOperationException escaping the pipeline, which used to abort the request.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("partial", await response.Content.ReadAsStringAsync());
        AssertSecurityHeaders(response);
    }

    // ---------------------------------------------------------------- source gates

    [Fact]
    public void Program_SecurityHeadersRunBeforeAnythingThatCanAnswer()
    {
        var program = File.ReadAllText(WebProjectFile("Program.cs"));

        var headerMiddleware = program.IndexOf("app.Use(async (context, next) =>", StringComparison.Ordinal);
        var headerCall = program.IndexOf("ApplySecurityHeaders(context);", StringComparison.Ordinal);
        var httpsRedirection = program.IndexOf("app.UseHttpsRedirection()", StringComparison.Ordinal);
        var staticFiles = program.IndexOf("app.UseStaticFiles(", StringComparison.Ordinal);

        Assert.True(headerMiddleware >= 0 && headerCall > headerMiddleware,
            "The security headers must be applied by a middleware of their own.");
        Assert.True(headerCall < httpsRedirection,
            "The security headers must be applied before UseHttpsRedirection, or the 307 leaves without them.");
        Assert.True(headerCall < staticFiles,
            "The security headers must be applied before UseStaticFiles, or JS/CSS/font responses leave without them.");
    }

    [Fact]
    public void Program_ErrorHandlerGuardsClearAndReassertsTheHeaders()
    {
        var program = File.ReadAllText(WebProjectFile("Program.cs"));

        var clear = program.IndexOf("context.Response.Clear();", StringComparison.Ordinal);
        var reassert = program.IndexOf("ApplySecurityHeaders(context);", clear, StringComparison.Ordinal);

        Assert.True(clear >= 0, "The error handler must still clear the response before rewriting it.");
        Assert.True(reassert > clear,
            "After Response.Clear() the security headers are gone, so the handler must put them back.");
        Assert.Contains("if (context.Response.HasStarted)", program, StringComparison.Ordinal);
        Assert.Contains("context.Response.Headers[\"Strict-Transport-Security\"] = value;", program, StringComparison.Ordinal);
        // The Arabic body, the empty-detail contract and the development rethrow must all survive.
        Assert.Contains(_arabicErrorMessage, program, StringComparison.Ordinal);
        Assert.Contains("detail = string.Empty", program, StringComparison.Ordinal);
        // Brace-agnostic: the rethrow must stay inside the IsDevelopment branch, whether or not
        // the formatter has wrapped its body in braces.
        Assert.Contains("if (app.Environment.IsDevelopment())", program, StringComparison.Ordinal);
        var isDevelopment = program.IndexOf("if (app.Environment.IsDevelopment())", StringComparison.Ordinal);
        var rethrow = program.IndexOf("throw;", isDevelopment, StringComparison.Ordinal);
        Assert.True(rethrow > isDevelopment, "يجب إعادة رمي الاستثناء في بيئة التطوير.");
    }

    [Fact]
    public void Program_HstsPolicyHasOneSourceSharedByUseHstsAndTheErrorHandler()
    {
        var program = File.ReadAllText(WebProjectFile("Program.cs"));

        // One HstsOptions instance feeds both AddHsts and the handler that re-asserts the header
        // after Clear(); two copies would be free to drift apart.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, "new HstsOptions"));
        Assert.Contains("options.MaxAge = hstsOptions.MaxAge;", program, StringComparison.Ordinal);
        Assert.Contains("options.IncludeSubDomains = hstsOptions.IncludeSubDomains;", program, StringComparison.Ordinal);
        Assert.Contains("options.Preload = hstsOptions.Preload;", program, StringComparison.Ordinal);
        Assert.Contains("ApplyHstsHeader(context, hstsOptions);", program, StringComparison.Ordinal);
    }

    /// <summary>
    /// LiveWebApp transcribes the JwtBearer block below, because Program.cs cannot be started in
    /// this suite: it calls UseSqlServer and Database.Migrate(). A transcription can drift, and the
    /// drift would be silent - SecurityAccessTokenLifetimeTests asserts that an expired token is
    /// refused, so if Program.cs dropped ValidateLifetime while the harness kept it, that test would
    /// stay green against a production app that accepts expired tokens. This is the pin that keeps
    /// the harness honest, and it is the only reason the three converted security classes still
    /// depend on Program.cs text at all.
    /// </summary>
    [Fact]
    public void Program_KeepsTheJwtBearerValidationLiveWebAppTranscribes()
    {
        var program = File.ReadAllText(WebProjectFile("Program.cs"));

        Assert.Contains("AddJwtBearer(JwtBearerDefaults.AuthenticationScheme", program, StringComparison.Ordinal);
        Assert.Contains("ValidateIssuer = true", program, StringComparison.Ordinal);
        Assert.Contains("ValidateAudience = true", program, StringComparison.Ordinal);
        Assert.Contains("ValidateLifetime = true", program, StringComparison.Ordinal);
        Assert.Contains("ValidateIssuerSigningKey = true", program, StringComparison.Ordinal);
        Assert.Contains("ValidIssuer = jwtIssuer", program, StringComparison.Ordinal);
        Assert.Contains("ValidAudience = jwtAudience", program, StringComparison.Ordinal);
        Assert.Contains("IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))",
            program, StringComparison.Ordinal);
        // The stamp check is the revocation channel logout rotates, so the harness's transcription of
        // it must be pinned too, not just the lifetime checks.
        Assert.Contains("options.Events = new JwtBearerEvents", program, StringComparison.Ordinal);
        Assert.Contains("onTokenValidated", program, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TokenStampChecks.StampMatches(tokenStamp, currentStamp)", program, StringComparison.Ordinal);
    }

    /// <summary>
    /// The cookie settings LiveWebApp transcribes. The logout tests assert behaviour that depends on
    /// these: the deletion cookie carries the same name as the one issued, and a Secure/HttpOnly
    /// cookie is what makes the whole exchange meaningful.
    /// </summary>
    [Fact]
    public void Program_KeepsTheApplicationCookieSettingsLiveWebAppTranscribes()
    {
        var program = File.ReadAllText(WebProjectFile("Program.cs"));

        Assert.Contains("ConfigureApplicationCookie(options =>", program, StringComparison.Ordinal);
        Assert.Contains("options.LoginPath = \"/Account/Login\";", program, StringComparison.Ordinal);
        Assert.Contains("options.LogoutPath = \"/Account/Logout\";", program, StringComparison.Ordinal);
        Assert.Contains("options.Cookie.HttpOnly = true;", program, StringComparison.Ordinal);
        Assert.Contains("options.Cookie.SameSite = SameSiteMode.Lax;", program, StringComparison.Ordinal);
        Assert.Contains("options.Cookie.SecurePolicy = CookieSecurePolicy.Always;", program, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- harness

    private sealed class HeaderProbe(WebApplication app, string httpsAddress, string httpAddress) : IAsyncDisposable
    {
        private readonly WebApplication _app = app;
        private readonly HttpClient _https = CreateClient(httpsAddress);
        private readonly HttpClient _plainHttp = CreateClient(httpAddress);

        /// <summary>Sends over the real TLS listener unless the test deliberately wants plain http.</summary>
        public async Task<HttpResponseMessage> GetAsync(string path = "/", bool https = true, string? host = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (host is not null)
            {
                request.Headers.Host = host;
            }
            return await (https ? _https : _plainHttp).SendAsync(request);
        }

        private static HttpClient CreateClient(string address) =>
            new(new HttpClientHandler
            {
                // The listener uses a throwaway self-signed certificate.
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                // The redirect test asserts on the 307 itself; following it would leave the process.
                AllowAutoRedirect = false
            })
            { BaseAddress = new Uri(address) };

        public async ValueTask DisposeAsync()
        {
            _https.Dispose();
            _plainHttp.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class StaticSite : IDisposable
    {
        private readonly string _root;
        private readonly PhysicalFileProvider _provider;

        public StaticSite()
        {
            _root = Path.Combine(Path.GetTempPath(), "opencode", "nvs-static-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, "site.css"), "body{color:red}");
            _provider = new PhysicalFileProvider(_root);
            WebRoot = _provider;
        }

        public IFileProvider WebRoot { get; }

        public void Dispose()
        {
            _provider.Dispose();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // best effort: the test only needs the scratch directory out of the way
            }
        }
    }

    private static string WebProjectFile(string relative)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "NewVixSmart.Web", relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            $"تعذر العثور على src\\NewVixSmart.Web\\{relative} بالبحث الصاعد من {AppContext.BaseDirectory}.");
    }
}
