using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class JournalEntryLineInvariantTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public JournalEntryLineInvariantTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
        SeedChartOfAccounts(db);
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static void SeedChartOfAccounts(AppDbContext db)
    {
        (string Code, string Name, GLAccountType Type, NormalBalance Normal)[] accounts =
        {
            ("1000", "الصندوق", GLAccountType.Asset, NormalBalance.Debit),
            ("1200", "حسابات العملاء", GLAccountType.Asset, NormalBalance.Debit),
            ("1300", "المخزون", GLAccountType.Asset, NormalBalance.Debit),
            ("2000", "حسابات الموردين", GLAccountType.Liability, NormalBalance.Credit),
            ("2055", "ضريبة القيمة المضافة", GLAccountType.Liability, NormalBalance.Credit),
            ("3000", "رأس المال", GLAccountType.Equity, NormalBalance.Credit),
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("5000", "تكلفة البضاعة المباعة", GLAccountType.Expense, NormalBalance.Debit),
            ("5101", "مرتجعات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("5102", "مرتجعات المشتريات", GLAccountType.Expense, NormalBalance.Debit),
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.SaveChanges();
    }

    private static void AssertNothingPersisted(AppDbContext db)
    {
        Assert.Empty(db.JournalEntries.ToList());
        Assert.Empty(db.JournalEntryLines.ToList());
    }

    [Theory]
    [InlineData(100, 50)]
    [InlineData(50, 100)]
    public async Task PostAsync_LineWithBothSidesPositive_IsRejected_AndNothingPersists(decimal debit, decimal credit)
    {
        using var db = CreateContext();
        var svc = new AccountingService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PostAsync(
            JournalSource.Import, 1, new DateTime(2026, 3, 10), "اختبار",
            new[] { new JournalLine("1000", debit, credit) }, "test"));

        Assert.Contains("كل سطر في القيد يجب أن يكون مدينًا أو دائنًا وليس كلاهما", ex.Message);
        AssertNothingPersisted(db);
    }

    [Fact]
    public async Task PostAsync_LineWithNeitherSide_IsRejected_AndNothingPersists()
    {
        using var db = CreateContext();
        var svc = new AccountingService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PostAsync(
            JournalSource.Import, 1, new DateTime(2026, 3, 10), "اختبار",
            new[] { new JournalLine("1000", 0m, 0m) }, "test"));

        Assert.Contains("كل سطر في القيد يجب أن يكون مدينًا أو دائنًا وليس كلاهما", ex.Message);
        AssertNothingPersisted(db);
    }

    [Fact]
    public async Task PostAsync_NegativeAmount_IsRejected_AndNothingPersists()
    {
        using var db = CreateContext();
        var svc = new AccountingService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PostAsync(
            JournalSource.Import, 1, new DateTime(2026, 3, 10), "اختبار",
            new[] { new JournalLine("1000", 100m, -50m) }, "test"));

        Assert.Contains("لا يمكن أن يكون المبلغ سالبًا", ex.Message);
        AssertNothingPersisted(db);
    }

    [Fact]
    public async Task PostAsync_UnknownAccountCode_IsRejected_AndNothingPersists()
    {
        using var db = CreateContext();
        var svc = new AccountingService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PostAsync(
            JournalSource.Import, 1, new DateTime(2026, 3, 10), "اختبار",
            new[] { new JournalLine("9999", 100m, 0m), new JournalLine("4000", 0m, 100m) }, "test"));

        Assert.Contains("9999", ex.Message);
        AssertNothingPersisted(db);
    }

    [Fact]
    public async Task PostAsync_EmptyLines_IsRejected_AndNothingPersists()
    {
        using var db = CreateContext();
        var svc = new AccountingService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PostAsync(
            JournalSource.Import, 1, new DateTime(2026, 3, 10), "اختبار",
            Array.Empty<JournalLine>(), "test"));

        Assert.Contains("لا يمكن إنشاء قيد بلا أسطر", ex.Message);
        AssertNothingPersisted(db);
    }

    [Fact]
    public async Task PostAsync_UnbalancedEntry_IsRejected_AndNothingPersists()
    {
        using var db = CreateContext();
        var svc = new AccountingService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PostAsync(
            JournalSource.Import, 1, new DateTime(2026, 3, 10), "اختبار",
            new[] { new JournalLine("1000", 100m, 0m), new JournalLine("4000", 0m, 99m) }, "test"));

        Assert.Contains("مجموع المدين لا يساوي مجموع الدائن في القيد", ex.Message);
        AssertNothingPersisted(db);
    }

    [Fact]
    public async Task PostAsync_BalancedEntry_IsAccepted_AndPersistsBalancedLines()
    {
        using var db = CreateContext();
        var svc = new AccountingService(db);

        await svc.PostAsync(JournalSource.Import, 1, new DateTime(2026, 3, 10), "اختبار",
            new[] { new JournalLine("1000", 100m, 0m), new JournalLine("4000", 0m, 100m) }, "test");

        var entry = Assert.Single(db.JournalEntries.ToList());
        var lines = db.JournalEntryLines.ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal(l.JournalEntryId, entry.Id));
        Assert.Equal(100m, decimal.Round(lines.Sum(l => l.Debit), 2));
        Assert.Equal(decimal.Round(lines.Sum(l => l.Debit), 2), decimal.Round(lines.Sum(l => l.Credit), 2));
    }

    private static async Task<int> SeedEntryAsync(AppDbContext db)
    {
        var entry = new JournalEntry
        {
            EntryNumber = "TEST-" + Guid.NewGuid().ToString("N"),
            Date = new DateTime(2026, 3, 10),
            Description = "اختبار قيد السطر",
            Source = JournalSource.Import,
            SourceId = 1,
            IsPosted = true
        };
        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private static void AddLine(AppDbContext db, int entryId, decimal debit, decimal credit)
    {
        var accountId = db.GLAccounts.Single(a => a.Code == "1000").Id;
        db.JournalEntryLines.Add(new JournalEntryLine
        {
            JournalEntryId = entryId,
            AccountId = accountId,
            Debit = debit,
            Credit = credit
        });
    }

    [Fact]
    public async Task JournalEntryLine_NegativeDebit_IsRejectedByDatabase()
    {
        using var db = CreateContext();
        var entryId = await SeedEntryAsync(db);
        AddLine(db, entryId, -10m, 0m);

        await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
        Assert.Empty(db.JournalEntryLines.ToList());
    }

    [Fact]
    public async Task JournalEntryLine_NegativeCredit_IsRejectedByDatabase()
    {
        using var db = CreateContext();
        var entryId = await SeedEntryAsync(db);
        AddLine(db, entryId, 0m, -10m);

        await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
        Assert.Empty(db.JournalEntryLines.ToList());
    }

    [Fact]
    public async Task JournalEntryLine_ZeroOnBothSides_IsRejectedByDatabase()
    {
        using var db = CreateContext();
        var entryId = await SeedEntryAsync(db);
        AddLine(db, entryId, 0m, 0m);

        await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
        Assert.Empty(db.JournalEntryLines.ToList());
    }

    [Fact]
    public async Task JournalEntryLine_BothSidesPositive_IsRejectedByDatabase()
    {
        using var db = CreateContext();
        var entryId = await SeedEntryAsync(db);
        AddLine(db, entryId, 50m, 50m);

        await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
        Assert.Empty(db.JournalEntryLines.ToList());
    }

    [Fact]
    public async Task JournalEntryLine_OneSidedPositive_IsAcceptedByDatabase()
    {
        using var db = CreateContext();
        var entryId = await SeedEntryAsync(db);
        AddLine(db, entryId, 25m, 0m);

        await db.SaveChangesAsync();
        Assert.Single(db.JournalEntryLines.ToList());
    }
}
