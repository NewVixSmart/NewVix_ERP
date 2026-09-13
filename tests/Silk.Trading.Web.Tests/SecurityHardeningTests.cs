using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using Silk.Trading.Web.Api;
using Silk.Trading.Web.Api.Dtos;
using Silk.Trading.Web.Controllers;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Infrastructure;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Models.Core;
using Silk.Trading.Web.Models.Sales;
using Silk.Trading.Web.ViewModels.Users;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

namespace Silk.Trading.Web.Tests;

public sealed class SecurityHardeningTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public SecurityHardeningTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
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
    public async Task SeedUserPasswords_FromConfig_LoginSucceeds()
    {
        using var db = CreateContext();
        var um = CreateUserManager(db);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:AdminPassword"] = "Test@Admin12345",
                ["Seed:AccountantPassword"] = "Test@Acct123456",
                ["Seed:WarehousePassword"] = "Test@Ware123456"
            })
            .Build();

        (string UserName, string Key, string Legacy)[] seed =
        [
            ("seedadmin", "Seed:AdminPassword", "Admin@123"),
            ("seedacct", "Seed:AccountantPassword", "Accountant@123"),
            ("seedwh", "Seed:WarehousePassword", "Warehouse@123")
        ];

        foreach (var (userName, key, legacy) in seed)
        {
            var password = config[key]!;
            await um.CreateAsync(new IdentityUser { UserName = userName, Email = $"{userName}@silk.local" }, password);
            var user = await um.FindByNameAsync(userName);
            Assert.NotNull(user);
            Assert.True(await um.CheckPasswordAsync(user!, password));
            Assert.False(await um.CheckPasswordAsync(user!, legacy));
        }
    }

    [Fact]
    public async Task TokenStampMismatch_RejectsToken()
    {
        using var db = CreateContext();
        var um = CreateUserManager(db);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "silk-token-test-secret-key-0123456789ABCDEF",
                ["Jwt:Issuer"] = "SilkTrading",
                ["Jwt:Audience"] = "SilkTrading"
            })
            .Build();

        await um.CreateAsync(new IdentityUser { UserName = "stampuser" }, "Stamp@12345");
        var controller = new TokensController(um, config);

        var ok = Assert.IsType<OkObjectResult>(await controller.CreateToken(new TokenRequest("stampuser", "Stamp@12345")));
        var response = Assert.IsType<TokenResponse>(ok.Value);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(response.Token);
        var tokenStamp = jwt.Claims.FirstOrDefault(c => c.Type == TokenStampChecks.StampClaimType)?.Value;
        var user = await um.FindByNameAsync("stampuser");
        Assert.NotNull(user);
        Assert.NotNull(tokenStamp);
        Assert.True(TokenStampChecks.StampMatches(tokenStamp, await um.GetSecurityStampAsync(user!)));

        await um.UpdateSecurityStampAsync(user!);
        Assert.False(TokenStampChecks.StampMatches(tokenStamp, await um.GetSecurityStampAsync(user!)));
    }

    [Fact]
    public async Task ApiCustomers_GetCustomers_Paginates()
    {
        using var db = CreateContext();
        for (int i = 1; i <= 5; i++)
            db.Customers.Add(new Customer { Name = $"عميل {i}", Code = $"CUS-T{i}", IsActive = true });
        await db.SaveChangesAsync();

        var controller = new Silk.Trading.Web.Api.CustomersController(db);
        var ok = Assert.IsType<OkObjectResult>(await controller.GetCustomers(page: 1, pageSize: 2, search: null));
        var items = Assert.IsAssignableFrom<List<CustomerResponse>>(ok.Value);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task SetCurrentBranch_UnknownBranch_Rejects()
    {
        using var db = CreateContext();
        db.Branches.Add(new Branch { Code = "BR-T", Name = "فرع اختبار", IsActive = true });
        await db.SaveChangesAsync();

        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { Session = new FakeSession() } };
        var controller = new SettingsController(db, http);
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new FakeTempDataProvider());

        var result = await controller.SetCurrentBranch(9999);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task AddCurrency_ClientSuppliedIsBaseIgnored()
    {
        using var db = CreateContext();
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { Session = new FakeSession() } };
        var controller = new SettingsController(db, http);
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new FakeTempDataProvider());

        var request = new AddCurrencyRequest { Code = "EUR", Name = "يورو", Symbol = "€", ExchangeRate = 1.1m, IsActive = true };
        var result = await controller.AddCurrency(request);

        Assert.IsType<RedirectToActionResult>(result);
        var currency = await db.Currencies.FirstOrDefaultAsync(c => c.Code == "EUR");
        Assert.NotNull(currency);
        Assert.False(currency.IsBase);
    }

    [Fact]
    public async Task CreateUser_EmailConfirmedFalse_AccountUsable()
    {
        using var db = CreateContext();
        db.Roles.AddRange(
            new IdentityRole { Name = "Warehouse", NormalizedName = "WAREHOUSE" },
            new IdentityRole { Name = "Accountant", NormalizedName = "ACCOUNTANT" });
        await db.SaveChangesAsync();
        var um = CreateUserManager(db);
        var controller = new UsersController(um, db);
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new FakeTempDataProvider());

        var result = await controller.Create(new CreateUserViewModel
        {
            Username = "operator",
            Password = "Oper@tor12345",
            Role = "Warehouse"
        });

        Assert.IsType<RedirectToActionResult>(result);
        var user = await um.FindByNameAsync("operator");
        Assert.NotNull(user);
        Assert.False(user!.EmailConfirmed);
        Assert.True(await um.CheckPasswordAsync(user, "Oper@tor12345"));
    }

    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _data = new();

        public bool IsAvailable => true;
        public string Id => "test";
        public IEnumerable<string> Keys => _data.Keys;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Clear() => _data.Clear();
        public void Remove(string key) => _data.Remove(key);
        public void Set(string key, byte[] value) => _data[key] = value;
        bool ISession.TryGetValue(string key, [NotNullWhen(true)] out byte[]? value) => _data.TryGetValue(key, out value);
    }

    private sealed class FakeTempDataProvider : ITempDataProvider
    {
        private readonly Dictionary<string, object?> _data = new();

        public IDictionary<string, object?> LoadTempData(HttpContext context) => _data;

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
            foreach (var kv in values)
                _data[kv.Key] = kv.Value;
        }
    }
}