using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class InventoryVarianceAccountingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public InventoryVarianceAccountingTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private sealed record ResolvedLine(string Code, decimal Debit, decimal Credit);

    private static void SeedChartOfAccounts(AppDbContext db)
    {
        (string Code, string Name, GLAccountType Type, NormalBalance Normal)[] accounts =
        {
            ("1000", "النقدية", GLAccountType.Asset, NormalBalance.Debit),
            ("1200", "ذمم العملاء", GLAccountType.Asset, NormalBalance.Debit),
            ("1300", "المخزون", GLAccountType.Asset, NormalBalance.Debit),
            ("2000", "الدائنون", GLAccountType.Liability, NormalBalance.Credit),
            ("3000", "رأس المال", GLAccountType.Equity, NormalBalance.Credit),
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("5000", "تكلفة المبيعات", GLAccountType.Expense, NormalBalance.Debit),
            ("5200", "فروق الجرد", GLAccountType.Expense, NormalBalance.Debit)
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.SaveChanges();
    }

    private static async Task<int> SeedItemAsync(AppDbContext db, decimal purchasePrice)
    {
        var cat = new ItemCategory { Name = "تصنيف اختبار" };
        var type = new ItemType { Name = "نوع اختبار" };
        var unit = new Unit { Name = "قطعة" };
        db.ItemCategories.Add(cat);
        db.ItemTypes.Add(type);
        db.Units.Add(unit);

        var item = new Item
        {
            Name = "صنف اختبار",
            Category = cat,
            ItemType = type,
            CountUnit = unit,
            QuantityUnit = unit,
            PurchasePrice = purchasePrice,
            SalePrice = 80,
            CurrentCount = 100,
            CurrentQuantity = 0
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private static async Task<List<ResolvedLine>> ResolveLinesAsync(AppDbContext db, JournalEntry entry)
    {
        var codeById = (await db.GLAccounts.AsNoTracking().ToListAsync()).ToDictionary(a => a.Id, a => a.Code);
        return entry.Lines.Select(l => new ResolvedLine(codeById[l.AccountId], l.Debit, l.Credit)).ToList();
    }

    // ---------- Write-down → Dr 5200 / Cr 1300 ----------

    [Fact]
    public async Task RecordStockWriteDownAsync_PostsDebit5200AndCredit1300()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var itemId = await SeedItemAsync(db, 30m);
        var accounting = new AccountingService(db);

        await accounting.RecordStockWriteDownAsync(itemId, 12m, 0m, 25m, "tester");

        Assert.Equal(1, await db.JournalEntries.CountAsync());
        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        Assert.Equal(JournalSource.InventoryAdjustment, entry.Source);
        Assert.Equal(itemId, entry.SourceId);
        Assert.Equal(2, entry.Lines.Count);

        var lines = await ResolveLinesAsync(db, entry);
        var debit = Assert.Single(lines, l => l.Debit > 0);
        var credit = Assert.Single(lines, l => l.Credit > 0);
        Assert.Equal("5200", debit.Code);
        Assert.Equal(300m, debit.Debit);
        Assert.Equal("1300", credit.Code);
        Assert.Equal(300m, credit.Credit);
        Assert.Equal(0m, debit.Credit);
        Assert.Equal(0m, credit.Debit);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
        Assert.StartsWith("GL-", entry.EntryNumber);
        Assert.False(string.IsNullOrWhiteSpace(entry.Description));
    }

    // ---------- Variance up → Dr 1300 / Cr 5200 ----------

    [Fact]
    public async Task RecordStockVarianceUpAsync_PostsDebit1300AndCredit5200()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var itemId = await SeedItemAsync(db, 30m);
        var accounting = new AccountingService(db);

        await accounting.RecordStockVarianceUpAsync(itemId, 12m, 0m, 25m, "tester");

        Assert.Equal(1, await db.JournalEntries.CountAsync());
        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        Assert.Equal(JournalSource.InventoryAdjustment, entry.Source);
        Assert.Equal(itemId, entry.SourceId);
        Assert.Equal(2, entry.Lines.Count);

        var lines = await ResolveLinesAsync(db, entry);
        var debit = Assert.Single(lines, l => l.Debit > 0);
        var credit = Assert.Single(lines, l => l.Credit > 0);
        Assert.Equal("1300", debit.Code);
        Assert.Equal(300m, debit.Debit);
        Assert.Equal("5200", credit.Code);
        Assert.Equal(300m, credit.Credit);
        Assert.Equal(0m, debit.Credit);
        Assert.Equal(0m, credit.Debit);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
        Assert.StartsWith("GL-", entry.EntryNumber);
        Assert.False(string.IsNullOrWhiteSpace(entry.Description));
    }

    // ---------- Regression guard: the variance is P&L, never equity ----------

    [Fact]
    public async Task VarianceDoesNotTouchEquityAccount3000()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var itemId = await SeedItemAsync(db, 30m);
        var accounting = new AccountingService(db);

        await accounting.RecordStockWriteDownAsync(itemId, 12m, 0m, 25m, "tester");
        await accounting.RecordStockVarianceUpAsync(itemId, 12m, 0m, 25m, "tester");

        Assert.Equal(2, await db.JournalEntries.CountAsync());

        foreach (var entry in await db.JournalEntries.Include(e => e.Lines).ToListAsync())
        {
            var touched = await ResolveLinesAsync(db, entry);
            Assert.DoesNotContain(touched, l => l.Code == "3000");
        }
    }

    [Fact]
    public async Task VarianceUp_PostsToInventoryAdjustmentSource_NotOpeningStock()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var itemId = await SeedItemAsync(db, 30m);
        var accounting = new AccountingService(db);

        await accounting.RecordStockVarianceUpAsync(itemId, 12m, 0m, 25m, "tester");

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        Assert.Equal(JournalSource.InventoryAdjustment, entry.Source);
        Assert.Empty(await db.JournalEntries.Where(e => e.Source == JournalSource.OpeningStock).ToListAsync());
    }

    // ---------- Zero amounts never post ----------

    [Fact]
    public async Task ZeroAmountVariance_PostsNothing()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var itemId = await SeedItemAsync(db, 30m);
        var accounting = new AccountingService(db);

        await accounting.RecordStockWriteDownAsync(itemId, 0m, 0m, 25m, "tester");
        Assert.Empty(await db.JournalEntries.ToListAsync());

        await accounting.RecordStockVarianceUpAsync(itemId, 0m, 0m, 25m, "tester");
        Assert.Empty(await db.JournalEntries.ToListAsync());

        await accounting.RecordStockWriteDownAsync(itemId, 5m, 0m, 0m, "tester");
        Assert.Empty(await db.JournalEntries.ToListAsync());

        await accounting.RecordStockVarianceUpAsync(itemId, 5m, 0m, 0m, "tester");
        Assert.Empty(await db.JournalEntries.ToListAsync());
        Assert.Empty(await db.JournalEntryLines.ToListAsync());
    }

    // ---------- InventoryService routes each direction to the right legs ----------

    [Fact]
    public async Task CreateAdjustmentAsync_Increase_PostsVarianceNotOpeningStock()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var itemId = await SeedItemAsync(db, 30m);
        var inventory = new InventoryService(db, new AccountingService(db));

        var adjustment = new InventoryAdjustment
        {
            ItemId = itemId,
            NewCount = 120m,
            NewQuantity = 0m,
            AdjustmentDate = new DateTime(2026, 4, 15)
        };

        var (ok, error) = await inventory.CreateAdjustmentAsync(adjustment, "tester");

        Assert.True(ok, error);
        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        Assert.Equal(JournalSource.InventoryAdjustment, entry.Source);
        Assert.Equal(new DateTime(2026, 4, 15), entry.Date);

        var lines = await ResolveLinesAsync(db, entry);
        var debit = Assert.Single(lines, l => l.Debit > 0);
        var credit = Assert.Single(lines, l => l.Credit > 0);
        Assert.Equal("1300", debit.Code);
        Assert.Equal(600m, debit.Debit);
        Assert.Equal("5200", credit.Code);
        Assert.Equal(600m, credit.Credit);
        Assert.Empty(await db.JournalEntries.Where(e => e.Source == JournalSource.OpeningStock).ToListAsync());
    }

    [Fact]
    public async Task CreateAdjustmentAsync_Decrease_PostsWriteDownTo5200()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var itemId = await SeedItemAsync(db, 30m);
        var inventory = new InventoryService(db, new AccountingService(db));

        var adjustment = new InventoryAdjustment
        {
            ItemId = itemId,
            NewCount = 80m,
            NewQuantity = 0m,
            AdjustmentDate = new DateTime(2026, 6, 10)
        };

        var (ok, error) = await inventory.CreateAdjustmentAsync(adjustment, "tester");

        Assert.True(ok, error);
        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        Assert.Equal(JournalSource.InventoryAdjustment, entry.Source);
        Assert.Equal(new DateTime(2026, 6, 10), entry.Date);

        var lines = await ResolveLinesAsync(db, entry);
        var debit = Assert.Single(lines, l => l.Debit > 0);
        var credit = Assert.Single(lines, l => l.Credit > 0);
        Assert.Equal("5200", debit.Code);
        Assert.Equal(600m, debit.Debit);
        Assert.Equal("1300", credit.Code);
        Assert.Equal(600m, credit.Credit);
        Assert.DoesNotContain(lines, l => l.Code == "3000");
    }

    // ---------- Double entry ----------

    [Fact]
    public async Task VarianceEntry_IsBalanced()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var itemId = await SeedItemAsync(db, 30m);
        var accounting = new AccountingService(db);

        await accounting.RecordStockWriteDownAsync(itemId, 12m, 0m, 25m, "tester");
        await accounting.RecordStockVarianceUpAsync(itemId, 12m, 0m, 25m, "tester");
        // count يُسعَّر مثل الكمية، فالخصم =(qty > 0 ? qty : count) * cost
        await accounting.RecordStockVarianceUpAsync(itemId, 0m, 12m, 25m, "tester");

        var entries = await db.JournalEntries.Include(e => e.Lines).OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(3, entries.Count);

        foreach (var entry in entries)
        {
            Assert.NotEmpty(entry.Lines);
            Assert.Equal(entry.Lines.Sum(l => l.Debit), entry.Lines.Sum(l => l.Credit));
            Assert.Equal(300m, entry.Lines.Sum(l => l.Debit));
            Assert.Equal(300m, entry.Lines.Sum(l => l.Credit));
        }
    }
}
