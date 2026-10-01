using System.IdentityModel.Tokens.Jwt;
using System.Net;
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
///
/// These are the same two defects as before, but every request now goes over HTTPS to a real Kestrel
/// host instead of being handed to a controller that the test constructed itself. The previous
/// version wired AccountController by hand - it built a ControllerContext, a DefaultHttpContext and a
/// UrlHelperBase subclass just to reach LocalRedirect - which meant the framework's routing, the
/// cookie handler's own SignOutAsync, the antiforgery filter and the site's real HomeLanding mapping
/// were all replaced by the test's model of them. None of that machinery survives here: the client is
/// an HttpClient with a cookie jar, and the assertions are on status codes, Location and cookies.
///
/// Login_GuardsAgainstOffSiteReturnUrlBeforeLocalRedirect grepped AccountController.cs for
/// "!Url.IsLocalUrl(returnUrl)", the "returnUrl = null" assignment and their order relative to
/// "return LocalRedirect(". The hostile-returnUrl theory below is the behavioural form of exactly
/// that guard: with the guard removed, LocalRedirect("//evil.com") throws, the error handler turns
/// the throw into a 500, and the theory fails on the status code. The grep test is gone because the
/// behaviour it stood in for is now observable end to end.
/// </summary>
public sealed class SecurityLogoutAndRedirectTests
{
    private const string _username = "logoutuser";
    private const string _password = "Log@123456";

    /// <summary>The cookie the Identity application cookie handler issues and, on logout, deletes.</summary>
    private const string _authCookie = ".AspNetCore.Identity.Application";

    [Fact]
    public async Task Logout_RotatesTheSecurityStamp_SoAnAlreadyIssuedTokenStopsWorking()
    {
        await using var app = await StartAsync();

        var token = await IssueTokenAsync(app);
        // Before logout the bearer token is a working credential, so a 401 after logout cannot be
        // blamed on a host that never accepted it in the first place.
        using (var live = await app.GetWithBearerAsync("/api/customers", token))
        {
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }

        using (var login = await app.LoginAsync(_username, _password))
        {
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        }

        using (var home = await app.GetAsync("/"))
        {
            Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        }

        var antiforgery = await app.AntiforgeryTokenAsync("/");
        using (var logout = await app.PostFormAsync("/Account/Logout", LiveForm.WithAntiforgery(antiforgery)))
        {
            Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
            Assert.Equal("/Account/Login", logout.Headers.Location!.ToString());
        }

        // This single assertion is the whole revocation story: the JwtBearer OnTokenValidated event
        // fails any token whose stamp claim no longer matches the rotated one, so the token minted
        // before logout is now worth nothing. If logout cleared the cookie but skipped the stamp
        // rotation, the token would still open this endpoint and the test would fail here.
        using var afterLogout = await app.GetWithBearerAsync("/api/customers", token);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    /// <summary>
    /// The cookie that authenticated the browser session must be gone after logout, and it must
    /// still have been issued hardened in the first place.
    /// </summary>
    [Fact]
    public async Task Logout_DeletesTheAuthenticationCookie()
    {
        await using var app = await StartAsync();

        using (var login = await app.LoginAsync(_username, _password))
        {
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            var issued = SetCookies(login);
            var cookie = Assert.Single(issued, value => value.StartsWith(_authCookie + "=", StringComparison.Ordinal));
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        }

        Assert.NotNull(app.CookieValue(_authCookie));

        var antiforgery = await app.AntiforgeryTokenAsync("/");
        using var logout = await app.PostFormAsync("/Account/Logout", LiveForm.WithAntiforgery(antiforgery));

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        var deletion = Assert.Single(SetCookies(logout), value => value.StartsWith(_authCookie + "=", StringComparison.Ordinal));
        // A blank value plus an expiry in the past is how a cookie is deleted; the jar honours it,
        // which the assertion below proves rather than assuming.
        Assert.Contains("expires=Thu, 01 Jan 1970", deletion, StringComparison.OrdinalIgnoreCase);
        Assert.Null(app.CookieValue(_authCookie));
    }

    /// <summary>
    /// The old form of this checked a RedirectToActionResult on a controller with no signed-in user.
    /// Over HTTP the same situation is an anonymous POST: [Authorize] challenges it away to the
    /// login page. The defect being fenced off is a throw (a 500), so any non-5xx redirect passes;
    /// a 500 is what this would have produced if Logout assumed a live user.
    /// </summary>
    [Fact]
    public async Task Logout_WithoutAUser_StillRedirectsAndDoesNotThrow()
    {
        await using var app = await StartAsync();

        var antiforgery = await app.AntiforgeryTokenAsync("/Account/Login");
        using var logout = await app.PostFormAsync("/Account/Logout", LiveForm.WithAntiforgery(antiforgery));

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Contains("/Account/Login", logout.Headers.Location!.ToString(), StringComparison.Ordinal);
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
        await using var app = await StartAsync();

        using var response = await app.LoginAsync(_username, _password, returnUrl);

        // LocalRedirect throws on a non-local URL, so a hostile returnUrl that reached LocalRedirect
        // would leave through the error handler as a 500. A redirect is the whole point.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // A successful sign-in issues the auth cookie; without it the 302 would be the failure
        // redirect back to the login page and the Location assertion below would be meaningless.
        Assert.NotNull(app.CookieValue(_authCookie));

        var location = response.Headers.Location!;
        Assert.StartsWith("/", location.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("evil.com", location.ToString(), StringComparison.OrdinalIgnoreCase);

        // A non-local returnUrl is dropped and the role's landing page is used, so the browser only
        // ever gets a same-origin path - never a host the caller chose.
        var resolved = location.IsAbsoluteUri ? location : new Uri(app.BaseAddress, location);
        Assert.Equal(app.BaseAddress.Host, resolved.Host);
        Assert.Equal(app.BaseAddress.Port, resolved.Port);
        Assert.NotEqual("/Account/Login", resolved.AbsolutePath);
    }

    [Fact]
    public async Task Login_KeepsTheRequestedLocalReturnUrl()
    {
        await using var app = await StartAsync();

        using var response = await app.LoginAsync(_username, _password, "/StockReservations/Create");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(app.CookieValue(_authCookie));
        Assert.Equal("/StockReservations/Create", response.Headers.Location!.ToString());
    }

    private static async Task<LiveWebApp> StartAsync()
    {
        var app = await LiveWebApp.StartAsync(setup =>
        {
            setup.Settings["Jwt:Key"] = LiveWebApp.JwtKey;
            setup.Settings["Jwt:Issuer"] = LiveWebApp.JwtIssuer;
            setup.Settings["Jwt:Audience"] = LiveWebApp.JwtAudience;
        });

        await app.SeedUserAsync(_username, _password, LiveWebApp.AdminRole);
        return app;
    }

    private static async Task<string> IssueTokenAsync(LiveWebApp app)
    {
        using var response = await app.PostTokenAsync(_username, _password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await LiveWebApp.ReadJsonAsync(response);
        var token = json.GetProperty("token").GetString()!;
        // Readable, signed and well formed: a 200 from the endpoint with a body the handler can read.
        Assert.NotEmpty(new JwtSecurityTokenHandler().ReadJwtToken(token).Claims);
        return token;
    }

    private static IReadOnlyList<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToList() : [];
}
