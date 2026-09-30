using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Access;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// A standIn is only useful if it decides the way the real service decides. When the two
/// disagree, a test written against the standIn passes or fails for a reason that has nothing
/// to do with the code under test - which is exactly how a suite ends up green with a broken
/// authorization check.
///
/// These tests therefore run the <b>real</b> <see cref="PermissionService"/> against a
/// seeded database and compare its answers, case by case, with both test doubles. The
/// decision table is the production one:
/// <c>HasAsync = IsAuthenticated &amp;&amp; (IsAdmin || granted.Contains(key))</c>.
/// </summary>
public sealed class PermissionDoubleFidelityTests : IDisposable
{
    private const string _userId = "u-1";
    private const string _granted = "Sales.View";
    private const string _alsoGranted = "Sales.Edit";
    private const string _notGranted = "PurchaseOrders.Create";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly List<AppDbContext> _contexts = [];

    public PermissionDoubleFidelityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
        db.UserPermissions.AddRange(
            new UserPermission { UserId = _userId, PermissionKey = _alsoGranted },
            new UserPermission { UserId = _userId, PermissionKey = _granted });
        db.SaveChanges();
    }

    public void Dispose()
    {
        foreach (var context in _contexts)
        {
            context.Dispose();
        }

        _connection.Dispose();
    }

    public static TheoryData<bool, bool> CallerStates => new()
    {
        { false, false }, // anonymous
        { true, false },  // signed in, no Admin role
        { true, true },   // signed in, Admin role
    };

    public static TheoryData<bool> SignedInStates => new() { false, true };

    [Theory]
    [MemberData(nameof(CallerStates))]
    public async Task AuthzTestPermissionService_DecidesExactlyLikeTheRealService(bool authenticated, bool admin)
    {
        var real = RealService(authenticated, admin);
        var standIn = new AuthzTestPermissionService(_granted, _alsoGranted)
        {
            IsAuthenticated = authenticated,
            IsAdmin = admin,
        };

        await AssertSameDecisionsAsync(real, standIn, authenticated, admin);
    }

    [Theory]
    [MemberData(nameof(SignedInStates))]
    public async Task TestPermissionService_DecidesExactlyLikeTheRealService_ForSignedInCallers(bool admin)
    {
        var real = RealService(authenticated: true, admin: admin);
        var standIn = admin ? TestPermissionService.Admin : new TestPermissionService(_granted, _alsoGranted);

        await AssertSameDecisionsAsync(real, standIn, authenticated: true, admin);
    }

    /// <summary>
    /// The default constructor of <see cref="TestPermissionService"/> used to grant
    /// everything. A test that instantiates it with no keys and expects access is now
    /// asserting a denial, which is the point of the change: it can no longer launder a
    /// missing permission check into a passing test.
    /// </summary>
    [Fact]
    public async Task TestPermissionService_WithNoKeys_GrantsNothing_EvenWhenTheRealServiceGrantsTheKey()
    {
        var real = RealService(authenticated: true, admin: false);
        var standIn = new TestPermissionService();

        Assert.True(await real.HasAsync(_granted));
        Assert.False(await standIn.HasAsync(_granted));
        Assert.False(await standIn.HasAnyAsync(_granted, _notGranted));
    }

    /// <summary>
    /// The plain double has no notion of an anonymous caller - it answers from its key list
    /// alone. That difference is asserted here on purpose, so the boundary is recorded
    /// instead of discovered later: the plain double must not be used to "prove" anything
    /// about an unauthenticated request, and the admin case shows the same gap even for
    /// <see cref="TestPermissionService.Admin"/>.
    /// </summary>
    [Fact]
    public async Task TestPermissionService_CannotExpressAnAnonymousCaller_SoItCannotProveOne()
    {
        var real = RealService(authenticated: false, admin: false);
        var standIn = new TestPermissionService(_granted);
        var realAdmin = RealService(authenticated: false, admin: true);

        Assert.False(await real.HasAsync(_granted));
        Assert.True(await standIn.HasAsync(_granted));

        Assert.False(await realAdmin.HasAsync(_granted));
        Assert.True(await TestPermissionService.Admin.HasAsync(_granted));
    }

    /// <summary>
    /// A signed-in user with no <c>NameIdentifier</c> claim makes the real service look up
    /// an empty permission set. A standIn that granted keys anyway would let a test pass on
    /// behaviour the production service cannot produce.
    /// </summary>
    [Fact]
    public async Task TheRealService_RefusesEverything_WhenTheUserHasNoIdentifierClaim()
    {
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity([new Claim(ClaimTypes.Name, "no-id")], "Test", ClaimTypes.Name, ClaimTypes.Role)),
            },
        };
        using var db = new AppDbContext(_options);
        var real = new PermissionService(db, http);

        Assert.False(await real.HasAsync(_granted));
        Assert.False(await real.HasAnyAsync(_granted, _alsoGranted));
    }

    /// <summary>
    /// A principal can carry the <c>Admin</c> role claim while being unauthenticated -
    /// <see cref="ClaimsIdentity.IsInRole"/> does not require an authentication type, and
    /// <see cref="ClaimsIdentity.IsAuthenticated"/> does not require a role. Swapping the two
    /// guards in <see cref="PermissionService"/> is therefore a real behaviour change, not a
    /// reorder of two equivalent checks, and this is the only case in the suite that can see it.
    /// The check must be the authentication one.
    /// </summary>
    [Fact]
    public async Task TheRealService_RefusesAnUnauthenticatedCaller_EvenWhenItCarriesTheAdminRoleClaim()
    {
        var forged = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], authenticationType: null)),
            },
        };
        using var db = new AppDbContext(_options);
        var real = new PermissionService(db, forged);
        var standIn = new AuthzTestPermissionService { IsAuthenticated = false, IsAdmin = true };

        // IsInRole is true and IsAuthenticated is false, which is exactly the combination that
        // makes the guard order observable.
        Assert.True(forged.HttpContext!.User.IsInRole("Admin"));
        Assert.False(forged.HttpContext.User.Identity?.IsAuthenticated);
        Assert.True(real.IsAdmin);

        Assert.False(await real.HasAsync(_granted));
        Assert.False(await real.HasAnyAsync(_granted, _alsoGranted));
        Assert.False(await standIn.HasAsync(_granted));
    }

    [Fact]
    public async Task GetKeysAsync_ReturnsTheSameRows_FromBothDoubles_AndTheRealService()
    {
        using var db = new AppDbContext(_options);
        var real = new PermissionService(db, Http());
        var authzDouble = new AuthzTestPermissionService(_granted, _alsoGranted);
        var plainDouble = new TestPermissionService(_granted, _alsoGranted);

        var expected = new List<string> { _alsoGranted, _granted };
        Assert.Equal(expected, await real.GetKeysAsync(_userId));
        Assert.Equal(expected, await authzDouble.GetKeysAsync(_userId));
        Assert.Equal(expected, await plainDouble.GetKeysAsync(_userId));
    }

    private static async Task AssertSameDecisionsAsync(
        IPermissionService real,
        IPermissionService standIn,
        bool authenticated,
        bool admin)
    {
        foreach (var key in new[] { _granted, _alsoGranted, _notGranted, "Reports.Export" })
        {
            Assert.True(
                await real.HasAsync(key) == await standIn.HasAsync(key),
                $"HasAsync({key}) يفترق عند (authenticated: {authenticated}, admin: {admin}).");
        }

        Assert.Equal(await real.HasAnyAsync(_granted, _notGranted), await standIn.HasAnyAsync(_granted, _notGranted));
        Assert.Equal(await real.HasAnyAsync(_notGranted, "Reports.Export"), await standIn.HasAnyAsync(_notGranted, "Reports.Export"));
        Assert.Equal(await real.HasAnyAsync(), await standIn.HasAnyAsync());

        Assert.Equal(real.IsAdmin, standIn.IsAdmin);

        // The guard that decides everything else, checked directly so a future edit that
        // moves the authentication check below the admin check is caught here.
        if (!authenticated)
        {
            Assert.False(await standIn.HasAsync(_granted));
            Assert.False(await standIn.HasAnyAsync(_granted));
        }
    }

    private PermissionService RealService(bool authenticated, bool admin)
    {
        var db = new AppDbContext(_options);
        _contexts.Add(db);
        return new PermissionService(db, Http(authenticated, admin));
    }

    private static IHttpContextAccessor Http(bool authenticated = true, bool admin = false, bool withId = true)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "tester") };
        if (withId)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, _userId));
        }

        if (admin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        var identity = authenticated
            ? new ClaimsIdentity(claims, "Test", ClaimTypes.Name, ClaimTypes.Role)
            : new ClaimsIdentity();

        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
    }
}
