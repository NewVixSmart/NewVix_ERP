using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Silk.Trading.Web.Api;
using Silk.Trading.Web.Api.Dtos;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Infrastructure;
using Xunit;

namespace Silk.Trading.Web.Tests;

public sealed class IdentityAndTokenTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IConfiguration _config;

    public IdentityAndTokenTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "silk-token-test-secret-key-0123456789ABCDEF",
                ["Jwt:Issuer"] = "SilkTrading",
                ["Jwt:Audience"] = "SilkTrading"
            })
            .Build();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static UserManager<IdentityUser> CreateUserManager(AppDbContext db)
    {
        var options = new OptionsWrapper<IdentityOptions>(new IdentityOptions());
        IdentityOptionsFactory.ApplyDefaults(options.Value);

        var store = new UserStore<IdentityUser>(db);
        return new UserManager<IdentityUser>(
            store,
            options,
            new PasswordHasher<IdentityUser>(),
            new[] { new UserValidator<IdentityUser>() },
            new[] { new PasswordValidator<IdentityUser>() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<IdentityUser>>.Instance);
    }

    [Fact]
    public async Task PasswordPolicy_RejectsWeak_AcceptsStrong()
    {
        using var db = CreateContext();
        var um = CreateUserManager(db);

        var weak = new IdentityUser { UserName = "weakuser" };
        var weakResult = await um.CreateAsync(weak, "abc123");
        Assert.False(weakResult.Succeeded);
        Assert.Contains(weakResult.Errors,
            e => e.Code.Contains("Password", StringComparison.OrdinalIgnoreCase));

        var strong = new IdentityUser { UserName = "stronguser" };
        var strongResult = await um.CreateAsync(strong, "Str0ng@123");
        Assert.True(strongResult.Succeeded);
    }

    [Fact]
    public async Task TokenEndpoint_ValidCredentials_ReturnsToken()
    {
        using var db = CreateContext();
        var um = CreateUserManager(db);
        await um.CreateAsync(new IdentityUser { UserName = "apiuser" }, "Api@123456");

        var controller = new TokensController(um, _config);

        var result = await controller.CreateToken(new TokenRequest("apiuser", "Api@123456"));

        var ok = Assert.IsAssignableFrom<OkObjectResult>(result);
        var response = Assert.IsAssignableFrom<TokenResponse>(ok.Value);
        Assert.False(string.IsNullOrEmpty(response.Token));
        Assert.True(response.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task TokenEndpoint_WrongPassword_FiveFails_LocksOut()
    {
        using var db = CreateContext();
        var um = CreateUserManager(db);
        await um.CreateAsync(new IdentityUser { UserName = "brutus" }, "Brut@123456");

        var controller = new TokensController(um, _config);

        for (int i = 0; i < 5; i++)
        {
            var bad = await controller.CreateToken(new TokenRequest("brutus", "Wrong@1"));
            Assert.IsAssignableFrom<UnauthorizedObjectResult>(bad);
        }

        var lockedUser = await um.FindByNameAsync("brutus");
        Assert.True(lockedUser is not null);
        Assert.True(await um.IsLockedOutAsync(lockedUser));

        var good = await controller.CreateToken(new TokenRequest("brutus", "Brut@123456"));
        Assert.IsAssignableFrom<UnauthorizedObjectResult>(good);
    }
}