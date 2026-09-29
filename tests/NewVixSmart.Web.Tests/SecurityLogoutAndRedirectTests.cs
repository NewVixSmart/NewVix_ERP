using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NewVixSmart.Web.Api;
using NewVixSmart.Web.Api.Dtos;
using NewVixSmart.Web.Controllers;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Infrastructure;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// Two auth defects, both reachable by an unauthenticated visitor or by anyone who once held a
/// token:
///
///   1. The API hands out a stateless bearer JWT, but logging out only cleared the cookie, so a
///      stolen token kept working until it expired. Logout now rotates the security stamp, which is
///      the revocation channel the app already had: JwtBearer's OnTokenValidated event compares the
///      token's stamp claim against the live one through TokenStampChecks.
///   2. The login POST passed ?returnUrl= straight to LocalRedirect, which THROWS on a non-local
///      URL. Not an open redirect - the throw stopped it - but a user-triggerable 500, and the kind
///      of 500 that invites a "try /%2f%2fevil.com" probe from anyone auditing the login page.
/// </summary>
public sealed class SecurityLogoutAndRedirectTests : IAsyncLifetime
{
    private const string JwtKey = "vix-token-test-secret-key-0123456789ABCDEF";
    private const string Username = "logoutuser";
    private const string Password = "Log@123456";

    private SqliteConnection _connection = null!;
    private ServiceProvider _provider = null!;
    private string _userId = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        // The real Identity stack: the cookie handler is what makes SignOutAsync do anything at all,
        // and PasswordSignInAsync needs the same stack to answer Succeeded.
        services.AddIdentity<IdentityUser, IdentityRole>(options => options.Password.RequiredLength = 8)
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "vix-test-cookie";
            options.Cookie.HttpOnly = true;
            options.LoginPath = "/Account/Login";
        });
        // Keeps the cookie handler away from the real ASP.NET Core data protection keys.
        services.AddDataProtection().UseEphemeralDataProtectionProvider();

        _provider = services.BuildServiceProvider();
        using (var scope = _provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var created = await userManager.CreateAsync(new IdentityUser { UserName = Username }, Password);
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
            _userId = Assert.IsAssignableFrom<IdentityUser>(
                await userManager.FindByNameAsync(Username)).Id;
        }
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Logout_RotatesTheSecurityStamp_SoAnAlreadyIssuedTokenStopsMatching()
    {
        using var scope = _provider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = Assert.IsAssignableFrom<IdentityUser>(await userManager.FindByNameAsync(Username));

        var token = await IssueTokenAsync(userManager);
        var tokenStamp = ReadStamp(token);
        var stampBefore = await userManager.GetSecurityStampAsync(user);
        Assert.True(TokenStampChecks.StampMatches(tokenStamp, stampBefore),
            "قبل الخروج: التوكن يطابق الختم الحي.");

        var result = await BuildAccountController(scope).Logout();

        Assert.Equal("Login", Assert.IsType<RedirectToActionResult>(result).ActionName);
        var stampAfter = await userManager.GetSecurityStampAsync(user);
        Assert.NotEqual(stampBefore, stampAfter);
        // This single assertion is the whole revocation story: the JwtBearer OnTokenValidated event
        // fails any token whose stamp claim no longer matches, so this token is now worth nothing.
        Assert.False(TokenStampChecks.StampMatches(tokenStamp, stampAfter),
            "بعد الخروج: يجب أن يفشل الختم القديم فيطير التوكن المصدَر قبله.");
    }

    [Fact]
    public async Task Logout_WithoutAUser_StillRedirectsAndDoesNotThrow()
    {
        using var scope = _provider.CreateScope();
        var controller = BuildAccountController(scope, authenticated: false);

        var result = await controller.Logout();

        Assert.Equal("Login", Assert.IsType<RedirectToActionResult>(result).ActionName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/Stock/Index")]
    [InlineData("//evil.com")]
    [InlineData("https://evil.com/x")]
    [InlineData("\\\\evil.com")]
    [InlineData("/\\evil.com")]
    [InlineData("http://evil.com")]
    public async Task Login_NeverRedirectsOffSite_AndNeverThrowsOnAHostileReturnUrl(string? returnUrl)
    {
        using var scope = _provider.CreateScope();
        var controller = BuildAccountController(scope, authenticated: false);

        var result = await controller.Login(
            new LoginViewModel { Username = Username, Password = Password },
            returnUrl);

        // A non-local returnUrl is dropped and the role's landing page is used, so the browser only
        // ever gets a same-origin path. No 500 either way.
        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.StartsWith("/", redirect.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("evil.com", redirect.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_KeepsTheRequestedLocalReturnUrl()
    {
        using var scope = _provider.CreateScope();
        var controller = BuildAccountController(scope, authenticated: false);

        var result = await controller.Login(
            new LoginViewModel { Username = Username, Password = Password },
            "/StockReservations/Create");

        Assert.Equal("/StockReservations/Create", Assert.IsType<LocalRedirectResult>(result).Url);
    }

    [Fact]
    public async Task Login_GuardsAgainstOffSiteReturnUrlBeforeLocalRedirect()
    {
        // The guard has to be an explicit IsLocalUrl check: LocalRedirect on its own throws.
        var source = File.ReadAllText(TestPaths.WebProjectFile("Controllers", "AccountController.cs"));
        var guard = source.IndexOf("if (!Url.IsLocalUrl(returnUrl)) returnUrl = null;", StringComparison.Ordinal);
        var localRedirect = source.IndexOf("return LocalRedirect(", StringComparison.Ordinal);
        Assert.True(guard >= 0, "يجب فحص returnUrl بـ Url.IsLocalUrl قبل إعادة التوجيه.");
        Assert.True(guard < localRedirect, "الفحص يجب أن يسبق LocalRedirect.");
    }

    private async Task<string> IssueTokenAsync(UserManager<IdentityUser> userManager)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = JwtKey,
            ["Jwt:Issuer"] = "NewVixSmart",
            ["Jwt:Audience"] = "NewVixSmart"
        }).Build();

        var result = await new TokensController(userManager, configuration)
            .CreateToken(new TokenRequest(Username, Password));
        var ok = Assert.IsAssignableFrom<OkObjectResult>(result);
        return Assert.IsAssignableFrom<TokenResponse>(ok.Value).Token;
    }

    private static string ReadStamp(string token)
    {
        var read = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var claim = read.Claims.SingleOrDefault(c => c.Type == TokenStampChecks.StampClaimType);
        Assert.NotNull(claim);
        return claim.Value;
    }

    /// <summary>
    /// Wires a controller against the real SignInManager, the real cookie handler and the real
    /// UrlHelperBase.IsLocalUrl, so the redirect guard is the framework's own implementation and
    /// not a stand-in. Only IsLocalUrl is exercised, so the two link-generating members are unused.
    ///
    /// Deliberately synchronous: SignInManager reads HttpContext from IHttpContextAccessor, which is
    /// AsyncLocal-backed, and an AsyncLocal written inside an async helper is discarded when that
    /// helper returns.
    /// </summary>
    private AccountController BuildAccountController(IServiceScope scope, bool authenticated = true)
    {
        var services = scope.ServiceProvider;
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var signInManager = services.GetRequiredService<SignInManager<IdentityUser>>();

        ClaimsPrincipal principal;
        if (authenticated)
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, _userId), new Claim(ClaimTypes.Name, Username) },
                CookieAuthenticationDefaults.AuthenticationScheme);
            principal = new ClaimsPrincipal(identity);
        }
        else
        {
            principal = new ClaimsPrincipal(new ClaimsIdentity());
        }

        var httpContext = new DefaultHttpContext { RequestServices = services, User = principal };
        services.GetRequiredService<IHttpContextAccessor>().HttpContext = httpContext;

        // Every piece of the ActionContext is spelled out because UrlHelperBase's constructor reads
        // RouteData.Values straight away, and a ControllerContext built from an object initializer
        // leaves RouteData null.
        var controller = new AccountController(signInManager, userManager)
        {
            ControllerContext = new ControllerContext(new ActionContext(
                httpContext,
                new RouteData(),
                new ControllerActionDescriptor { ControllerTypeInfo = typeof(AccountController).GetTypeInfo() },
                new ModelStateDictionary()))
        };
        controller.Url = new LocalUrlHelper(controller.ControllerContext);
        return controller;
    }

    /// <summary>
    /// UrlHelperBase.IsLocalUrl is where the framework decides what "local" means, and it is concrete,
    /// so inheriting it keeps the real rule - "/" is local, "//host" and "/\host" are not - while the
    /// two link-generating members stay unused.
    /// </summary>
    private sealed class LocalUrlHelper(ActionContext actionContext) : UrlHelperBase(actionContext)
    {
        public override string Action(UrlActionContext actionContext) =>
            throw new NotSupportedException("هذا الاختبار لا يبني روابط.");

        public override string RouteUrl(UrlRouteContext routeContext) =>
            throw new NotSupportedException("هذا الاختبار لا يبني روابط.");
    }
}
