using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Vix.ServiceDefaults;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Services;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

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
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
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

app.UseHttpsRedirection();
app.UseStaticFiles();

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

var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
foreach (var proxy in (app.Configuration["ForwardedHeaders:KnownProxies"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    forwardedOptions.KnownProxies.Add(IPAddress.Parse(proxy));
foreach (var network in (app.Configuration["ForwardedHeaders:KnownNetworks"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    forwardedOptions.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
if (forwardedOptions.KnownProxies.Count > 0 || forwardedOptions.KnownIPNetworks.Count > 0)
    app.UseForwardedHeaders(forwardedOptions);

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

if (!app.Environment.IsDevelopment())
{
    var adminSeedPassword = app.Configuration["Seed:AdminPassword"];
    string[] insecureSeedDefaults = ["Admin@123", "Acc@12345", "War@12345"];
    if (string.IsNullOrWhiteSpace(adminSeedPassword)
        || insecureSeedDefaults.Contains(adminSeedPassword, StringComparer.Ordinal))
    {
        throw new InvalidOperationException(
            "Insecure or missing default admin seed password in production. Set the Seed__AdminPassword environment variable to a strong password and remove the shipped defaults from appsettings.json. لن يُشغَّل النظام في بيئة الإنتاج بكلمة مرور مدير افتراضية غير آمنة؛ عيّن متغير البيئة Seed__AdminPassword.");
    }
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    await SeedData.InitializeAsync(scope.ServiceProvider);
}

app.Run();

public static class TokenStampChecks
{
    public const string StampClaimType = "stamp";

    public static bool StampMatches(string? tokenStamp, string currentStamp) =>
        string.Equals(tokenStamp, currentStamp, StringComparison.Ordinal);
}
