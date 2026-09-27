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
using NewVixSmart.Web.Api;
using NewVixSmart.Web.Api.Dtos;
using NewVixSmart.Web.Controllers;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Infrastructure;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Users;
using NewVixSmart.Web.ViewModels.Core;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

namespace NewVixSmart.Web.Tests;

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
            await um.CreateAsync(new IdentityUser { UserName = userName, Email = $"{userName}@vix.local" }, password);
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
                ["Jwt:Key"] = "vix-token-test-secret-key-0123456789ABCDEF",
                ["Jwt:Issuer"] = "NewVixSmart",
                ["Jwt:Audience"] = "NewVixSmart"
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

        var controller = new NewVixSmart.Web.Api.CustomersController(db);
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
        var controller = new SettingsController(db, http, new FakeBrandingService(), new FakePrintSettingsService());
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new FakeTempDataProvider());

        var result = await controller.SetCurrentBranch(9999);
        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task SettingsController_ExposesNoCurrencyMutationActions()
    {
        // The ledger is single-currency (EGP), so the former mass-assignment hole in
        // AddCurrency is gone with the feature: there must be no action left to reach.
        var actions = typeof(SettingsController)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(m => m.Name)
            .Where(n => n.Contains("Currency", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(actions.Count == 0,
            $"SettingsController must not expose currency actions but declares: {string.Join(", ", actions)}");
        await Task.CompletedTask;
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

    private sealed class FakeBrandingService : IBrandingService
    {
        public PalettePreset[] Presets => [];
        public Task<BrandingData> LoadAsync() => Task.FromResult(new BrandingData());
        public Task<CompanyProfile> GetProfileAsync() => Task.FromResult(new CompanyProfile());
        public Task<BrandingTheme> GetThemeAsync() => Task.FromResult(new BrandingTheme());
        public string RenderThemeCss(BrandingTheme theme) => string.Empty;
        public void Invalidate() { }
    }

    private sealed class FakePrintSettingsService : IPrintSettingsService
    {
        public Task<PrintSettingsViewModel> LoadAsync() => Task.FromResult(new PrintSettingsViewModel());
        public bool ShowLogo(PrintGroup group) => true;
        public bool ShowCompanyName(PrintGroup group) => true;
        public bool ShowTagline(PrintGroup group) => true;
        public bool ShowCompanyContact(PrintGroup group) => true;
        public bool ShowFooter(PrintGroup group) => true;
        public bool ShowBarcode(PrintGroup group) => true;
        public bool ShowUnitPrice(PrintGroup group) => true;
        public bool ShowDiscountColumn(PrintGroup group) => true;
        public double FontScale(PrintGroup group) => 1.0;
        public string PaperMargin(PrintGroup group) => "normal";
        public Task<PrintLayoutOptions> GetLayoutAsync(PrintGroup group) => Task.FromResult(new PrintLayoutOptions());
        public Task SaveLayoutAsync(PrintGroup group, PrintLayoutOptions options) => Task.CompletedTask;
        public Task<PrintLayoutOptions> GetPreviewLayoutAsync(PrintGroup group, string? state)
        {
            if (PrintSettingsService.DecodeState(state) is { } layout) return Task.FromResult(layout);
            return Task.FromResult(new PrintLayoutOptions());
        }
        public void Invalidate() { }
    }
}