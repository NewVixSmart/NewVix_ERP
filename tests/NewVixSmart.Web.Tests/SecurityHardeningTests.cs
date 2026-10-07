using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
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
using NewVixSmart.Web.Api;
using NewVixSmart.Web.Api.Dtos;
using NewVixSmart.Web.Controllers;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Infrastructure;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Core;
using NewVixSmart.Web.ViewModels.Users;
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
        {
            db.Customers.Add(new Customer { Name = $"عميل {i}", Code = $"CUS-T{i}", IsActive = true });
        }

        await db.SaveChangesAsync();

        var controller = new NewVixSmart.Web.Api.CustomersController(db);
        var ok = Assert.IsType<OkObjectResult>(await controller.GetCustomers(page: 1, pageSize: 2, search: null));
        var items = Assert.IsAssignableFrom<List<CustomerResponse>>(ok.Value);
        Assert.Equal(2, items.Count);
    }

    /// <summary>
    /// رفضُ حفظ إعدادات الطباعة إذا تغيّرت المحفوظة بعد العرض. الاختبارُ الحركيّ: البصمةُ
    /// المرسودة تُنتَج من حالةٍ ثمانية، ثم تُغيَّر القاعدةُ، فيجب أن يرفض <c>SavePrinting</c>
    /// ولا يستدعي خدمةَ الطباعة أصلًا — فالدليلُ على الرفض هو غيابُ الكتابة.
    /// </summary>
    [Fact]
    public async Task SavePrinting_RefusesWhenStoredLayoutsDivergeFromTheRenderedFingerprint()
    {
        using var db = CreateContext();
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { Session = new FakeSession() } };
        var print = new RecordingPrintSettingsService();
        var controller = new SettingsController(db, http, new FakeBrandingService(), print, new SetWriteGate(db));
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new FakeTempDataProvider());

        db.SystemSettings.Add(new SystemSetting { Key = "PrintStudio.sales_invoice.PageSize", Value = "A4" });
        await db.SaveChangesAsync();

        var printing = Assert.IsType<ViewResult>(await controller.Printing());
        var rendered = (string)printing.ViewData["PrintFingerprint"]!;
        Assert.NotEmpty(rendered);

        // مديرٌ آخر يحفظ تخطيطًا مختلفًا بعد أن عُرضت الصفحة.
        db.SystemSettings.Single(s => s.Key == "PrintStudio.sales_invoice.PageSize").Value = "A5";
        await db.SaveChangesAsync();

        var result = await controller.SavePrinting(
            PrintGroup.SalesInvoice,
            new PrintLayoutOptions { PageSize = PrintPageSize.A4 },
            applyToAll: false,
            printFingerprint: rendered);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(0, print.SaveCount);

        // وقبل كلّ هذا: لو طابقت البصمة لَما رُفض شيء، وإلا كان الحارسُ يرفض بلا سبب.
        var matching = await controller.SavePrinting(
            PrintGroup.SalesInvoice,
            new PrintLayoutOptions { PageSize = PrintPageSize.A4 },
            applyToAll: false,
            printFingerprint: await RecomputeAsync(db));
        Assert.IsType<RedirectToActionResult>(matching);
        Assert.Equal(1, print.SaveCount);
    }

    /// <summary>
    /// البصمةُ الغائبةُ ليست إذنًا للكتابة. فالنموذجُ الوحيدُ الذي ينشر إلى
    /// <c>SavePrinting</c> يحملها دومًا، فغيابُها يعني أن الصفحةَ المعروضةَ قديمةٌ أو أن
    /// الحقلَ طُوي؛ والحالان يستوجبان الرفض. وإلا لكان الحارسُ يُغلقُ على مَن يحمل
    /// البصمةَ الصحيحةَ ويفتحُ لمن لا يحمل شيئًا، فتصبح الحمايةُ اختياريةً لمن يريد.
    /// </summary>
    [Fact]
    public async Task SavePrinting_RefusesWhenTheFingerprintIsAbsent()
    {
        using var db = CreateContext();
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { Session = new FakeSession() } };
        var print = new RecordingPrintSettingsService();
        var controller = new SettingsController(db, http, new FakeBrandingService(), print, new SetWriteGate(db));
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new FakeTempDataProvider());

        db.SystemSettings.Add(new SystemSetting { Key = "PrintStudio.sales_invoice.PageSize", Value = "A4" });
        await db.SaveChangesAsync();

        var printing = Assert.IsType<ViewResult>(await controller.Printing());
        Assert.NotEmpty((string)printing.ViewData["PrintFingerprint"]!);

        var result = await controller.SavePrinting(
            PrintGroup.SalesInvoice,
            new PrintLayoutOptions { PageSize = PrintPageSize.A5 },
            applyToAll: false,
            printFingerprint: null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(0, print.SaveCount);
        Assert.NotNull(controller.TempData["Error"]);
    }

    /// <summary>
    /// تُحسب البصمةُ نفسُها المستخدَمة في الخادم، حتى لا يعتمد الاختبار على تنفيذٍ خاصٍّ به.
    /// </summary>
    private static Task<string> RecomputeAsync(AppDbContext db)
    {
        var rows = db.SystemSettings.AsNoTracking()
            .Where(s => s.Key.StartsWith("PrintStudio.") || s.Key.StartsWith("Print."))
            .OrderBy(s => s.Key)
            .Select(s => new { s.Key, s.Value })
            .ToList();
        var payload = string.Join("\n", rows.Select(r => $"{r.Key}={r.Value ?? string.Empty}"));
        return Task.FromResult(Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload))));
    }

    /// <summary>يحصي المراتِ التي حُفظت فعلًا، فدليلُ الرفض غيابُ العدد لا مجرّدُ التوجيه.</summary>
    private sealed class RecordingPrintSettingsService : FakePrintSettingsService
    {
        public int SaveCount { get; private set; }

        public override Task SaveLayoutAsync(PrintGroup group, PrintLayoutOptions options)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SetCurrentBranch_UnknownBranch_Rejects()
    {
        using var db = CreateContext();
        db.Branches.Add(new Branch { Code = "BR-T", Name = "فرع اختبار", IsActive = true });
        await db.SaveChangesAsync();

        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { Session = new FakeSession() } };
        var controller = new SettingsController(db, http, new FakeBrandingService(), new FakePrintSettingsService(), new SetWriteGate(db));
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
        var controller = new UsersController(um, db, new SetWriteGate(db));
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
            {
                _data[kv.Key] = kv.Value;
            }
        }
    }


    /// <summary>
    /// ترتيبُ الفحصِ في <c>SaveBranding</c> ليس ترتيبًا شكليًّا. المفتاحُ غير الموجود يجعل
    /// <c>SetSettingAsync</c> تحفظ وحدَها، و<c>SaveChanges</c> يشمل كلَّ المتتبَّع — منه
    /// تعديلُ <c>CompanyProfile</c> المحمَّل قبل قليل. فلو سُبق وضعُ الرمزِ بنداءاتِ
    /// المظهر لَكُتبت هذه القيمُ قبل الفحص، ولما بلغ <c>catch</c> الاستثناءَ قطّ.
    /// <para>
    /// والاختبارُ حقيقيٌّ لا نصّيٌّ: يُرسل رمزًا قديمًا بعد تعديل زميل، ومفتاحُ مظهرٍ
    /// غيرِ موجودٍ يُجبر المسارَ على الحفظ المبكر. والنتيجةُ لا تُشترَط أن ترمِي استثناءً —
    /// المطلوبُ وحدَه ألّا يكتب سطرًا واحدًا.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SaveBranding_RefusesStaleBrandingEvenWhenAThemeKeyIsMissing()
    {
        using var db = CreateContext();
        db.CompanyProfiles.Add(new CompanyProfile { CompanyName = "الاسم الأصلي", Tagline = "الشعار الأصلي" });
        await db.SaveChangesAsync();

        // لا مفاتيحَ مظهرٍ إطلاقًا، وهذا هو ما يجعل `SetSettingAsync` تحفظ وحدَها أوّلَ مرّة.
        Assert.Empty(db.SystemSettings);

        // SQLite لا يولّد `rowversion`، فالرمزُ هنا يأتي من محرّكٍ حقيقيّ وحده. نُحاكي
        // ما يفعله ذلك المحرّك بقيمتين مختلفتين: واحدةٌ في الصفّ وأخرى مُرسَلةٌ قديمة.
        // المعروضُ للاختبار هو الشرطُ نفسُه: أن يُوضع الرمزُ القديم في شرط الحفظ.
        //
        // و`x'..'` حرفيٌّ BLOB في SQLite. لو كُتبت القيمة hexً نصًّا لخُزِّنت TEXT
        // فلم تساوِ بايتًا أبدًا، فرفض الحفظُ كلَّ مرّة — ويمرّ الاختبارُ والحارسُ غائب.
        var stale = new byte[] { 0xCC, 0xDD };
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE [CompanyProfiles] SET [RowVersion] = x'00AABB'");

        // ولا بدّ من نسيان المتتبِّع: لولا ذلك حمل `SaveBranding` الكيانَ قادمًا بالرمز
        // الفارغ، فيرفضه الحفظُ لتعارضٍ مع تحديثٍ آخر لا صلة له بالرمز المُرسَل.
        db.ChangeTracker.Clear();

        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { Session = new FakeSession() } };
        var controller = new SettingsController(db, http, new FakeBrandingService(), new FakePrintSettingsService(), new SetWriteGate(db));
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new FakeTempDataProvider());

        using var verify = CreateContext();
        var result = await controller.SaveBranding(new BrandingViewModel
        {
            CompanyName = "اسمي أنا",
            Tagline = "شعارّي أنا",
            Primary = "#112233",
            Accent = "#112233",
            SidebarBg = "#112233",
            PageBg = "#112233",
            RowVersion = stale
        });

        Assert.IsType<RedirectToActionResult>(result);

        // لم يكتب سطرٌ واحد: لا الاسمُ ولا أيُّ مفتاحِ مظهر.
        var settled = await verify.CompanyProfiles.AsNoTracking().SingleAsync();
        Assert.Equal("الاسم الأصلي", settled.CompanyName);
        Assert.Equal("الشعار الأصلي", settled.Tagline);
        Assert.Empty(await verify.SystemSettings.AsNoTracking().ToListAsync());
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

    private class FakePrintSettingsService : IPrintSettingsService
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

        /// <summary>
        /// <c>virtual</c> ليعمل به <see cref="RecordingPrintSettingsService"/>؛ فالإخفاءُ مع
        /// <c>new</c> لا يُخترق عبر الاستدعاء من <c>SettingsController</c>-interface، إذ يبقى
        /// التنفيذُ الأصلي هو الذي يُنفَّذ.
        /// </summary>
        public virtual Task SaveLayoutAsync(PrintGroup group, PrintLayoutOptions options) => Task.CompletedTask;
        public Task<PrintLayoutOptions> GetPreviewLayoutAsync(PrintGroup group, string? state)
        {
            if (PrintSettingsService.DecodeState(state) is { } layout)
            {
                return Task.FromResult(layout);
            }

            return Task.FromResult(new PrintLayoutOptions());
        }
        public void Invalidate() { }
    }
}
