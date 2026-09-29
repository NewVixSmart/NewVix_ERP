using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NewVixSmart.Web.Api;
using NewVixSmart.Web.Api.Dtos;
using NewVixSmart.Web.Data;
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
/// </summary>
public sealed class SecurityAccessTokenLifetimeTests : IDisposable
{
    private const int DefaultMinutes = 15;
    private const int MinMinutes = 1;
    private const int MaxMinutes = 60;

    /// <summary>
    /// A JWT NumericDate is whole seconds, so exp is minted-lifetime rounded DOWN and can sit up to
    /// one second short. This is the largest gap the encoding itself can open, so it is the entire
    /// tolerance the lower bound needs.
    /// </summary>
    private static readonly TimeSpan NumericDateEncodingSlack = TimeSpan.FromSeconds(1);

    private const string JwtKey = "vix-token-test-secret-key-0123456789ABCDEF";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public SecurityAccessTokenLifetimeTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task UnconfiguredLifetime_IsFifteenMinutes()
    {
        var token = await IssueAsync(configuredMinutes: null);

        AssertLifetime(token, DefaultMinutes);
    }

    [Fact]
    public async Task ConfiguredLifetime_IsHonoured()
    {
        var token = await IssueAsync(configuredMinutes: 5);

        AssertLifetime(token, 5);
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
        var token = await IssueAsync(configured);

        AssertLifetime(token, expectedMinutes);
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(100000)]
    public async Task LifetimeBeyondTheCeiling_IsPulledBackToAnHour(int configured)
    {
        var token = await IssueAsync(configured);

        // A day-long token is exactly the window this change exists to close, so the ceiling has to
        // hold no matter what the configuration asks for.
        AssertLifetime(token, MaxMinutes);
    }

    /// <summary>
    /// The token is signed, not encrypted, so exp is readable by anyone holding it - which is exactly
    /// why the lifetime has to be correct. The claim is also the value the JwtBearer handler
    /// enforces, so this pins the enforcement boundary rather than a server-side intention.
    /// </summary>
    [Fact]
    public async Task ExpiryClaim_IsReadableAndIsWhatTheResponseAdvertises()
    {
        var issued = await IssueAsync(configuredMinutes: 5);

        Assert.True(issued.Token.ValidTo > DateTime.UtcNow, "التوكن يجب ألا يكون منتهيًا وقت الإصدار.");
        // ExpiresAt is what the client caches; if it drifted from exp the client would think it has
        // longer than the validation path will actually allow.
        Assert.Equal(DateTime.SpecifyKind(issued.Token.ValidTo, DateTimeKind.Utc), issued.ExpiresAt);
        Assert.Equal(DateTimeKind.Utc, issued.ExpiresAt.Kind);
    }

    [Fact]
    public void CeilingIsNotHigherThanAnHour()
    {
        var source = File.ReadAllText(TestPaths.WebProjectFile("Api", "TokensController.cs"));

        Assert.Contains($"MaxAccessTokenMinutes = {MaxMinutes}", source, StringComparison.Ordinal);
        Assert.Contains($"DefaultAccessTokenMinutes = {DefaultMinutes}", source, StringComparison.Ordinal);
        // The clamp is what makes the ceiling real; without it the constant would be decorative.
        Assert.Contains("1, MaxAccessTokenMinutes", source, StringComparison.Ordinal);
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
        var earliestPossibleExpiry = issued.SampledBefore + expected - NumericDateEncodingSlack;
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

    private async Task<IssuedToken> IssueAsync(int? configuredMinutes)
    {
        using var db = CreateContext();
        var userManager = TestUserManager.Create(db);
        const string username = "lifetimeuser";
        await userManager.CreateAsync(new IdentityUser { UserName = username }, "Life@123456");

        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = JwtKey,
            ["Jwt:Issuer"] = "NewVixSmart",
            ["Jwt:Audience"] = "NewVixSmart"
        };
        if (configuredMinutes is not null)
        {
            settings["Jwt:AccessTokenMinutes"] = configuredMinutes.Value.ToString();
        }

        var controller = new TokensController(userManager, new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        // Sampled immediately before the controller runs: it is a lower bound on the mint instant,
        // and no amount of subsequent slowness can invalidate that.
        var sampledBefore = DateTime.UtcNow;
        var result = await controller.CreateToken(new TokenRequest(username, "Life@123456"));
        var response = Assert.IsAssignableFrom<TokenResponse>(Assert.IsAssignableFrom<OkObjectResult>(result).Value);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.Token);
        return new IssuedToken(token, response.ExpiresAt, sampledBefore);
    }

    private AppDbContext CreateContext() => new(_options);

    private readonly record struct IssuedToken(JwtSecurityToken Token, DateTime ExpiresAt, DateTime SampledBefore);
}

internal static class TestUserManager
{
    public static UserManager<IdentityUser> Create(AppDbContext db)
    {
        var options = new OptionsWrapper<IdentityOptions>(new IdentityOptions());
        IdentityOptionsFactory.ApplyDefaults(options.Value);

        return new UserManager<IdentityUser>(
            new UserStore<IdentityUser>(db),
            options,
            new PasswordHasher<IdentityUser>(),
            new[] { new UserValidator<IdentityUser>() },
            new[] { new PasswordValidator<IdentityUser>() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<IdentityUser>>.Instance);
    }
}

internal static class TestPaths
{
    public static string WebProjectFile(params string[] segments) =>
        Path.Combine([WebProjectDirectory(), .. segments]);

    public static string WebProjectDirectory()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "NewVixSmart.Web");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            $"تعذر العثور على src\\NewVixSmart.Web بالبحث الصاعد من {AppContext.BaseDirectory}.");
    }
}
