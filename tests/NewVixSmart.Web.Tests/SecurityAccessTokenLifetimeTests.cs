using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NewVixSmart.Web.Infrastructure;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// A bearer JWT has no revocation channel of its own, so the damage window of a stolen copy is
/// exactly its remaining lifetime. Before this change the API token lived for a full day, which meant
/// a token lifted from a browser, a log or a proxy cache stayed valid for a whole shift.
///
/// These tests pin the 15-minute default, the hard 60-minute ceiling a misconfigured
/// Jwt__AccessTokenMinutes cannot get past, and the fact that the ceiling is a ceiling and not a
/// replacement value.
///
/// MEASURING THE LIFETIME. The assertion is on the token's own "exp" claim, never on how long the
/// test took. An earlier version of this file measured the wall clock between asking for the token
/// and reading the response and asserted the result was inside [4.5, 5] minutes, which is wrong
/// twice over: the elapsed time includes this test's own latency, so a busier machine produces a
/// LARGER reading and the upper bound of exactly 5 is the one assertion no loaded machine can
/// honour; and because the window admitted "just over 5", a token that genuinely lived 60 minutes
/// was indistinguishable from a correct one. It measured latency, not lifetime.
///
/// The replacement asserts the lifetime the token actually claims, using two one-sided bounds that
/// machine load can only ever push further inside the passing side:
///
///   too LONG  ->  exp - UtcNow <= lifetime. The clock sample is taken at assert time, which is
///                 never earlier than the mint, so any extra slowness makes this reading SMALLER.
///                 A longer effective lifetime breaks it by exactly the overshoot.
///   too SHORT ->  exp >= before + lifetime - 1s. "before" is sampled before the controller runs,
///                 so the mint is never before it however long the controller then takes; the 1s is
///                 not a load allowance but the whole-second NumericDate encoding, which is the most
///                 exp can lag mint+lifetime. A shorter effective lifetime breaks it.
///
/// Together the two bounds pin the effective lifetime to the requested one, so a 5-minute token and
/// a 60-minute token differ by a whole hour and cannot be confused.
///
/// HOW THE TOKEN IS OBTAINED. Every token here is minted by the running application: a real Kestrel
/// host over HTTPS, a real POST to /api/auth/token, a real Identity password check. An earlier
/// version called TokensController.CreateToken directly on a hand-built UserManager, which proved the
/// controller's arithmetic but not that the route, the JSON binding, the antiforgery opt-out and the
/// signing key the host actually configured all agree with it.
///
/// WHAT REPLACED CeilingIsNotHigherThanAnHour. That test read Api/TokensController.cs as text and
/// asserted the two minute constants and the "1, _maxAccessTokenMinutes" clamp expression were
/// present. It could not tell a working clamp from a deleted one - the constants would have gone in
/// the same edit - and it asserted a comment could satisfy. It is gone. Every property it claimed is
/// now asserted against the token the server returns: Clamp_IsAppliedAtItsEdges sweeps the boundary
/// and every value outside it, and LifetimeBeyondTheCeiling_IsPulledBackToAnHour proves a
/// day-long configuration still comes back as an hour.
///
/// THE ONE THING NOT PROVEN HERE. LiveWebApp transcribes the JwtBearer validation block out of
/// Program.cs, so if Program.cs stopped validating the lifetime, the expired-token test below would
/// keep passing against the transcription. That cross-check cannot be behavioural - it compares the
/// harness with a file that is never executed in this suite - so it lives next to the same file's
/// other pin, in SecurityResponseHeadersTests.
/// </summary>
public sealed class SecurityAccessTokenLifetimeTests
{
    private const int _defaultMinutes = 15;
    private const int _minMinutes = 1;
    private const int _maxMinutes = 60;

    private const string _username = "lifetimeuser";
    private const string _password = "Life@123456";

    /// <summary>
    /// A JWT NumericDate is whole seconds, so exp is minted-lifetime rounded DOWN and can sit up to
    /// one second short. This is the largest gap the encoding itself can open, so it is the entire
    /// tolerance the lower bound needs.
    /// </summary>
    private static readonly TimeSpan _numericDateEncodingSlack = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task UnconfiguredLifetime_IsFifteenMinutes()
    {
        await using var app = await StartAsync(configuredMinutes: null);

        AssertLifetime(await IssueAsync(app), _defaultMinutes);
    }

    [Fact]
    public async Task ConfiguredLifetime_IsHonoured()
    {
        await using var app = await StartAsync(configuredMinutes: 5);

        AssertLifetime(await IssueAsync(app), 5);
    }

    /// <summary>
    /// The clamp, edge by edge. Every row pins what the token CLAIMS, so the ceiling is proven to be
    /// a ceiling and not a silent replacement value: 2 and 59 come back untouched, 1 and 60 are
    /// exactly on the boundary and come back untouched, and only what falls outside is moved.
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(-30, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(59, 59)]
    [InlineData(60, 60)]
    [InlineData(61, 60)]
    [InlineData(1440, 60)]
    [InlineData(100000, 60)]
    public async Task Clamp_IsAppliedAtItsEdges(int configured, int expectedMinutes)
    {
        await using var app = await StartAsync(configured);

        AssertLifetime(await IssueAsync(app), expectedMinutes);
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(100000)]
    public async Task LifetimeBeyondTheCeiling_IsPulledBackToAnHour(int configured)
    {
        await using var app = await StartAsync(configured);

        // A day-long token is exactly the window this change exists to close, so the ceiling has to
        // hold no matter what the configuration asks for.
        AssertLifetime(await IssueAsync(app), _maxMinutes);
    }

    /// <summary>
    /// The token is signed, not encrypted, so exp is readable by anyone holding it - which is exactly
    /// why the lifetime has to be correct. The claim is also the value the JwtBearer handler
    /// enforces, so this pins the enforcement boundary rather than a server-side intention.
    /// </summary>
    [Fact]
    public async Task ExpiryClaim_IsReadableAndIsWhatTheResponseAdvertises()
    {
        await using var app = await StartAsync(configuredMinutes: 5);

        var issued = await IssueAsync(app);

        Assert.True(issued.Token.ValidTo > DateTime.UtcNow, "التوكن يجب ألا يكون منتهيًا وقت الإصدار.");
        // ExpiresAt is what the client caches; if it drifted from exp the client would think it has
        // longer than the validation path will actually allow.
        Assert.Equal(DateTime.SpecifyKind(issued.Token.ValidTo, DateTimeKind.Utc), issued.ExpiresAt);
        Assert.Equal(DateTimeKind.Utc, issued.ExpiresAt.Kind);
    }

    /// <summary>
    /// The token is not decoration: the endpoint that mints it and the handler that validates it are
    /// wired to the same key, issuer and audience, so a freshly minted token opens a protected API
    /// endpoint. This is the half the direct-controller tests could not see.
    /// </summary>
    [Fact]
    public async Task TheMintedToken_IsAcceptedByAProtectedApiEndpoint()
    {
        await using var app = await StartAsync(configuredMinutes: null);

        var issued = await IssueAsync(app);

        using var response = await app.GetWithBearerAsync("/api/customers", issued.Text);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The lifetime has to be enforced, not merely advertised. This mints a token with the same
    /// signing key, issuer, audience, user and security stamp as a live one and moves only exp into
    /// the past, so the single failed check is the lifetime: if ValidateLifetime were off, or the
    /// handler skipped exp, this request would succeed.
    /// </summary>
    [Fact]
    public async Task AnExpiredToken_IsRefusedByAProtectedApiEndpoint()
    {
        await using var app = await StartAsync(configuredMinutes: null);
        var issued = await IssueAsync(app);

        // The freshly minted token works, so a 401 below cannot be blamed on a misconfigured host.
        using (var live = await app.GetWithBearerAsync("/api/customers", issued.Text))
        {
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }

        var (userId, stamp, roles) = await app.InScopeAsync(async services =>
        {
            var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
            var user = (await userManager.FindByNameAsync(_username))!;
            return (user.Id, await userManager.GetSecurityStampAsync(user), await userManager.GetRolesAsync(user));
        });

        var expired = new JwtSecurityToken(
            issuer: LiveWebApp.JwtIssuer,
            audience: LiveWebApp.JwtAudience,
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, _username),
                new Claim(TokenStampChecks.StampClaimType, stamp),
                .. roles.Select(role => new Claim(ClaimTypes.Role, role)),
            ],
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(LiveWebApp.JwtKey)), SecurityAlgorithms.HmacSha256));

        using var response = await app.GetWithBearerAsync(
            "/api/customers", new JwtSecurityTokenHandler().WriteToken(expired));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Hosts one application with the real JWT settings, then seeds the user the endpoint
    /// authenticates. The configuration is the only thing a caller varies, and it travels the same
    /// way an operator's Jwt__AccessTokenMinutes would: into the host's configuration before it
    /// starts.
    /// </summary>
    private static async Task<LiveWebApp> StartAsync(int? configuredMinutes)
    {
        var app = await LiveWebApp.StartAsync(setup =>
        {
            setup.Settings["Jwt:Key"] = LiveWebApp.JwtKey;
            setup.Settings["Jwt:Issuer"] = LiveWebApp.JwtIssuer;
            setup.Settings["Jwt:Audience"] = LiveWebApp.JwtAudience;
            if (configuredMinutes is not null)
            {
                setup.Settings["Jwt:AccessTokenMinutes"] =
                    configuredMinutes.Value.ToString(CultureInfo.InvariantCulture);
            }
        });

        await app.SeedUserAsync(_username, _password, LiveWebApp.AdminRole);
        return app;
    }

    /// <summary>
    /// Asks the running API for a token and reads back what it issued. The clock sample is taken
    /// immediately before the POST, so it is a lower bound on the mint instant that no amount of
    /// subsequent slowness can invalidate.
    /// </summary>
    private static async Task<IssuedToken> IssueAsync(LiveWebApp app)
    {
        var sampledBefore = DateTime.UtcNow;

        using var response = await app.PostTokenAsync(_username, _password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await LiveWebApp.ReadJsonAsync(response);
        var text = json.GetProperty("token").GetString()!;
        var token = new JwtSecurityTokenHandler().ReadJwtToken(text);
        var expiresAt = DateTime.Parse(
            json.GetProperty("expiresAt").GetString()!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        return new IssuedToken(text, token, expiresAt, sampledBefore);
    }

    /// <summary>
    /// Asserts the lifetime the token itself carries. Both bounds are one-sided on purpose:
    /// whichever way the machine is loaded, they cannot cross.
    /// </summary>
    private static void AssertLifetime(IssuedToken issued, int expectedMinutes)
    {
        var expiresAt = DateTime.SpecifyKind(issued.Token.ValidTo, DateTimeKind.Utc);
        var expected = TimeSpan.FromMinutes(expectedMinutes);

        // Too SHORT. The mint is never earlier than the sample taken before the controller ran, and
        // NumericDate encoding loses at most a second, so a claim shorter than this means the
        // effective lifetime was below the requested one - a 0-minute token cannot pass this.
        var earliestPossibleExpiry = issued.SampledBefore + expected - _numericDateEncodingSlack;
        Assert.True(
            expiresAt >= earliestPossibleExpiry,
            $"العمر الفعّال قصير: انتهى التوكن في {expiresAt:O} أي بعد {issued.SampledBefore:O} بـ " +
            $"أقل من {expectedMinutes} دقيقة (المتوقع {expected.TotalSeconds} ثانية).");

        // Too LONG. This clock read happens at assert time and is therefore never earlier than the
        // mint, so the remaining lifetime is at most the lifetime the token was minted with, and
        // every extra second of machine slowness only shrinks it further. An unclamped 61, 1440 or
        // 100000-minute token blows straight through this; exactly 60 still fits.
        var remaining = expiresAt - DateTime.UtcNow;
        Assert.InRange(remaining, TimeSpan.Zero, expected);
    }

    private readonly record struct IssuedToken(
        string Text, JwtSecurityToken Token, DateTime ExpiresAt, DateTime SampledBefore);
}
