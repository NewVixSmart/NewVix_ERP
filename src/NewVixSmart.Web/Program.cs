using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Vix.ServiceDefaults;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Infrastructure;
using NewVixSmart.Web.Services;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using System.IO.Compression;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var hostingAllowedHosts = builder.Configuration["Hosting:AllowedHosts"];
if (!string.IsNullOrWhiteSpace(hostingAllowedHosts))
    builder.Configuration["AllowedHosts"] = hostingAllowedHosts;

var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");
if (!builder.Environment.IsDevelopment()
    && (string.IsNullOrWhiteSpace(defaultConnection)
        || defaultConnection.Contains("mssqllocaldb", StringComparison.OrdinalIgnoreCase)))
{
    throw new InvalidOperationException(
        "DefaultConnection must point to a real SQL Server in production. Set ConnectionStrings__DefaultConnection (Docker/env) to a reachable server; the localdb default cannot boot outside a dev machine.");
}
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(defaultConnection));

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
    NewVixSmart.Web.Infrastructure.IdentityOptionsFactory.ApplyDefaults(options))
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

var jwtKey = builder.Configuration["Jwt:Key"];
if (!builder.Environment.IsDevelopment()
    && (string.IsNullOrWhiteSpace(jwtKey)
        || jwtKey.Length < 32
        || jwtKey.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase)))
{
    throw new InvalidOperationException(
        "Jwt:Key must be a strong secret (>= 32 chars) in production and is not shipped in appsettings.json. Set it via the Jwt__Key environment variable or User Secrets; refusing to start in production with a missing, placeholder, or weak key.");
}
if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException(
        "Jwt:Key is not configured. Set Jwt__Key via User Secrets or appsettings.Development.json for local development.");
}
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "NewVixSmart";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "NewVixSmart";

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
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var userId = context.Principal?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            var userManager = context.HttpContext.RequestServices
                .GetRequiredService<UserManager<IdentityUser>>();
            var user = userId == null ? null : await userManager.FindByIdAsync(userId);
            if (user == null || await userManager.IsLockedOutAsync(user))
            {
                context.Fail("الحساب غير نشط أو محظور");
                return;
            }
            var tokenStamp = context.Principal?.Claims.FirstOrDefault(c => c.Type == TokenStampChecks.StampClaimType)?.Value;
            var currentStamp = await userManager.GetSecurityStampAsync(user);
            if (!TokenStampChecks.StampMatches(tokenStamp, currentStamp))
            {
                context.Fail("رمز الأمان غير صالح؛ يرجى إعادة تسجيل الدخول");
            }
        }
    };
});

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    if (!builder.Environment.IsDevelopment())
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("token", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddControllersWithViews(options =>
{
    // M-2 tradeoff, left as-is on purpose: implicit [Required] on non-nullable reference-type
    // parameters is suppressed app-wide so legacy endpoints keep accepting null/empty input.
    // What protects us today: the global AutoValidateAntiforgeryToken filter below (every
    // mutating request needs a token) plus explicit null/length guards in the controllers.
    // Re-enabling safely needs: removing this line, annotating/validating every affected
    // action (explicit [Required], [Bind], ModelState checks) and re-running the full MVC +
    // API test suites, because the suppression is currently hiding missing validation.
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = new[]
    {
        "text/plain",
        "text/css",
        "application/javascript",
        "text/javascript",
        "application/json",
        "application/xml",
        "image/svg+xml",
        "text/html"
    };
    options.Providers.Add(new BrotliCompressionProvider(new BrotliCompressionProviderOptions
    {
        Level = CompressionLevel.Fastest
    }));
    options.Providers.Add(new GzipCompressionProvider(new GzipCompressionProviderOptions
    {
        Level = CompressionLevel.Fastest
    }));
});

var corsOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
{
    options.AddPolicy("ApiCors", policy =>
    {
        if (corsOrigins.Length == 0)
        {
            // Locked by default: no cross-origin origins allowed unless configured.
            policy.WithOrigins();
        }
        else
        {
            policy.WithOrigins(corsOrigins)
                  .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS")
                  .AllowAnyHeader()
                  .DisallowCredentials();
        }
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "NewVix Smart Solutions API",
        Version = "v1"
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<ISalesQuotesService, SalesQuotesService>();
builder.Services.AddScoped<IAccountingService, AccountingService>();
builder.Services.AddScoped<IFinancialReportService, FinancialReportService>();
builder.Services.AddScoped<IProcurementService, ProcurementService>();
    builder.Services.AddScoped<ISalesOrdersService, SalesOrdersService>();
builder.Services.AddScoped<IStockReservationsService, StockReservationsService>();
builder.Services.AddScoped<IDeliveriesInvoicingService, DeliveriesInvoicingService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<ReportExportService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IBatchService, BatchService>();
builder.Services.AddScoped<IFiscalService, FiscalService>();
builder.Services.AddScoped<AccountsService>();
builder.Services.AddScoped<IBrandingService, BrandingService>();
builder.Services.AddScoped<IExportCenterService, ExportCenterService>();
builder.Services.AddScoped<IImportCenterService, ImportCenterService>();
builder.Services.AddScoped<IPrintSettingsService, PrintSettingsService>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    if (!builder.Environment.IsDevelopment())
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

var app = builder.Build();

// Every guard in this file is keyed on !IsDevelopment(), so an accidental
// ASPNETCORE_ENVIRONMENT=Development on a production host silently disables all of
// them at once: the JWT key gate, the seed-password gate, the connection-string gate,
// HSTS, the cookie Secure flag, and the Swagger UI - leaving the publicly published
// dev JWT key and dev seed passwords live. Nothing else warns about it, so say so loudly.
if (app.Environment.IsDevelopment())
{
    app.Logger.LogCritical(
        "SECURITY: ASPNETCORE_ENVIRONMENT=Development. The Jwt:Key strength gate, the Seed__*Password "
        + "gate, the SQL Server connection-string gate, HSTS, the auth/session cookie Secure flag and the "
        + "Swagger UI are all DISABLED, and the dev JWT key plus the dev seed passwords are in effect. "
        + "Never run this configuration on a reachable host.");
}

PdfInvoiceService.ConfigureServices(app.Services);

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        if (app.Environment.IsDevelopment())
            throw;
        app.Logger.LogError(ex, "Unhandled exception");
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new { message = "حدث خطأ غير متوقع. حاول مرة أخرى.", detail = string.Empty });
    }
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseResponseCompression();

// Forwarded headers run first so a trusted proxy can set the client scheme via
// X-Forwarded-Proto before UseHttpsRedirection decides whether to redirect.
// Skipped entirely when ForwardedHeaders:Enabled is false, so direct (non proxied)
// deployments keep the default single-host behaviour.
ConfigureForwardedHeaders(app);

app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers[Microsoft.Net.Http.Headers.HeaderNames.CacheControl] = "public,max-age=31536000,immutable";
    }
});

app.Use(async (context, next) =>
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
    await next();
});

static string[] SplitForwardedHeaderSetting(string? value) =>
    (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

// M-1: forwarded headers are strictly opt-in (ForwardedHeaders:Enabled, default false) and are only
// honoured from the proxies/networks listed in configuration - unknown proxies are never trusted.
static void ConfigureForwardedHeaders(WebApplication app)
{
    if (!app.Configuration.GetValue("ForwardedHeaders:Enabled", false))
    {
        if (!app.Environment.IsDevelopment())
        {
            app.Logger.LogWarning(
                "ForwardedHeaders:Enabled is false, so X-Forwarded-For / X-Forwarded-Proto are ignored. Behind a reverse proxy or " +
                "load balancer this means HttpContext.Connection.RemoteIpAddress is the proxy address and the rate limiter " +
                "(20 requests / 5 minutes on the login policy) is ONE shared bucket for every client. " +
                "فعّل ForwardedHeaders__Enabled مع ForwardedHeaders__KnownProxies أو KnownNetworks خلف أي وسيط عكسي.");
        }

        return;
    }

    var forwardedOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = app.Configuration.GetValue("ForwardedHeaders:ForwardLimit", 1)
    };

    foreach (var proxy in SplitForwardedHeaderSetting(app.Configuration["ForwardedHeaders:KnownProxies"]))
    {
        try
        {
            forwardedOptions.KnownProxies.Add(IPAddress.Parse(proxy));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"ForwardedHeaders:KnownProxies contains an invalid IP address '{proxy}'. عيّن عنوان IPv4 صحيحًا لكل وسيط موثوق.", ex);
        }
    }

    foreach (var network in SplitForwardedHeaderSetting(app.Configuration["ForwardedHeaders:KnownNetworks"]))
    {
        try
        {
            // IPv4 CIDR, e.g. "10.0.0.0/8".
            forwardedOptions.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            throw new InvalidOperationException(
                $"ForwardedHeaders:KnownNetworks contains an invalid network '{network}'. عيّن شبكة بصيغة CIDR مثل 10.0.0.0/8.", ex);
        }
    }

    if (forwardedOptions.KnownProxies.Count == 0 && forwardedOptions.KnownIPNetworks.Count == 0)
    {
        app.Logger.LogWarning(
            "ForwardedHeaders:Enabled is true but ForwardedHeaders__KnownProxies / ForwardedHeaders__KnownNetworks are empty; " +
            "only loopback proxies are trusted, so X-Forwarded-For from the real proxy is ignored and the rate limiter " +
            "still buckets by the proxy IP. عيّن عناوين الوسائط الموثوقة قبل تفعيل هذا الخيار.");
    }

    app.UseForwardedHeaders(forwardedOptions);
}

// Renders 4xx/5xx through Home/StatusCode so error responses keep lang + title (WCAG 3.1.1, 2.4.2).
// This must sit BEFORE UseRouting: a re-execute continues the pipeline from the position of this
// middleware, so registering it after UseAuthentication/UseAuthorization skipped authorization on
// the re-executed request and EndpointMiddleware threw "a middleware was not found that supports
// authorization" — every error page answered with a 500 developer page instead of the titled,
// Arabic error view this line exists to produce.
app.UseStatusCodePagesWithReExecute("/Home/StatusCode", "?code={0}");

app.UseRouting();

app.UseRateLimiter();

app.UseCors("ApiCors");

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHealthChecks("/healthz");

// M-4 production gate. The Seed section is no longer shipped in appsettings.json, so a missing key
// is a hard failure here exactly like a shipped default or a "REPLACE_WITH" placeholder: production
// must never fall back to SeedData's generated password. Development stays permissive (it still
// reads the dev defaults from appsettings.Development.json).
if (!app.Environment.IsDevelopment())
{
    string[] insecureSeedDefaults = ["Admin@123", "Acc@12345", "War@12345"];
    string[] seedKeys = ["Seed:AdminPassword", "Seed:AccountantPassword", "Seed:WarehousePassword"];
    var seedProblems = new List<string>();
    if (!app.Configuration.GetSection("Seed").Exists())
    {
        seedProblems.Add("Seed section missing entirely (no defaults are shipped any more)");
    }
    foreach (var key in seedKeys)
    {
        var value = app.Configuration[key];
        var envVar = key.Replace(":", "__", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(value))
            seedProblems.Add($"{envVar} (missing or empty)");
        else if (insecureSeedDefaults.Contains(value, StringComparer.Ordinal)
            || value.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase))
            seedProblems.Add($"{envVar} (shipped default / placeholder value)");
    }
    if (seedProblems.Count > 0)
    {
        throw new InvalidOperationException(
            $"Missing or insecure seed passwords in production: {string.Join(", ", seedProblems)}. " +
            "Set Seed__AdminPassword, Seed__AccountantPassword and Seed__WarehousePassword (environment variables, user secrets or a mounted config file) to strong unique values; no default seed passwords are shipped in the repository any more. " +
            "لن يبدأ النظام في بيئة الإنتاج ما لم تُعيَّن كلمات مرور قوية للصلاحيات الثلاث عبر المتغيرات البيئية Seed__*.");
    }
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    await SeedData.InitializeAsync(scope.ServiceProvider);
}

app.Run();
