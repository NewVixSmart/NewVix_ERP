using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class ReturnPostingGuardTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public ReturnPostingGuardTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static async Task<(int itemId, int custId)> SeedAsync(AppDbContext db)
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
            PurchasePrice = 30,
            SalePrice = 80,
            CurrentCount = 100,
            CurrentQuantity = 100
        };
        db.Items.Add(item);

        var customer = new Customer { Name = "عميل اختبار" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        return (item.Id, customer.Id);
    }

    private static void SeedChart(AppDbContext db)
    {
        (string Code, string Name, GLAccountType Type, NormalBalance Normal)[] accounts =
        {
            ("1000", "النقد", GLAccountType.Asset, NormalBalance.Debit),
            ("1100", "البنوك", GLAccountType.Asset, NormalBalance.Debit),
            ("1200", "المدينون", GLAccountType.Asset, NormalBalance.Debit),
            ("1300", "المخزون", GLAccountType.Asset, NormalBalance.Debit),
            ("2000", "الدائنون", GLAccountType.Liability, NormalBalance.Credit),
            ("3000", "رأس المال", GLAccountType.Equity, NormalBalance.Credit),
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
            ("5101", "مرتجعات المبيعات", GLAccountType.Expense, NormalBalance.Debit),
            ("5102", "مرتجعات المشتريات", GLAccountType.Expense, NormalBalance.Debit),
            ("5200", "فروق الجرد", GLAccountType.Expense, NormalBalance.Debit)
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.SaveChanges();
    }

    [Fact]
    public async Task Adjustment_BackDatedIncrease_PostsVarianceOnAdjustmentDate()
    {
        using var db = CreateContext();
        SeedChart(db);
        var (itemId, _) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        var adjustment = new InventoryAdjustment
        {
            ItemId = itemId,
            NewCount = 120m,
            NewQuantity = 120m,
            AdjustmentDate = new DateTime(2026, 4, 15)
        };

        var (ok, error) = await inventory.CreateAdjustmentAsync(adjustment, "tester");

        Assert.True(ok, error);
        var entry = await db.JournalEntries.SingleAsync(e => e.Source == JournalSource.InventoryAdjustment);
        Assert.Equal(new DateTime(2026, 4, 15), entry.Date);
        Assert.NotEqual(DateTime.UtcNow.Date, entry.Date.Date);

        var movement = await db.StockMovements.SingleAsync(m => m.DocumentType == DocumentType.Adjustment);
        Assert.Equal(adjustment.AdjustmentDate, movement.MovementDate);
    }

    [Fact]
    public async Task Adjustment_BackDatedDecrease_PostsWriteDownOnAdjustmentDate()
    {
        using var db = CreateContext();
        SeedChart(db);
        var (itemId, _) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        var adjustment = new InventoryAdjustment
        {
            ItemId = itemId,
            NewCount = 80m,
            NewQuantity = 80m,
            AdjustmentDate = new DateTime(2026, 6, 10)
        };

        var (ok, error) = await inventory.CreateAdjustmentAsync(adjustment, "tester");

        Assert.True(ok, error);
        var entry = await db.JournalEntries.SingleAsync(e => e.Source == JournalSource.InventoryAdjustment);
        Assert.Equal(new DateTime(2026, 6, 10), entry.Date);
        Assert.NotEqual(DateTime.UtcNow.Date, entry.Date.Date);
    }

    [Fact]
    public async Task StockJournal_OnClosedAdjustmentYear_IsRejected()
    {
        using var db = CreateContext();
        SeedChart(db);
        db.FiscalPeriods.Add(new FiscalPeriod { Year = 2026, IsClosed = true });
        await db.SaveChangesAsync();
        var accounting = new AccountingService(db);
        var closedDate = new DateTime(2026, 5, 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            accounting.RecordOpeningStockAsync(1, 10m, 0m, 20m, "tester", date: closedDate));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            accounting.RecordStockWriteDownAsync(1, 10m, 0m, 20m, "tester", date: closedDate));

        Assert.Equal(0, await db.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task CreateDeliveryIssue_OnInvoiceBackedNote_IsRejectedAndCreatesNoIssue()
    {
        using var db = CreateContext();
        SeedChart(db);
        var (itemId, custId) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        var invoice = new SaleInvoice { CustomerId = custId };
        var (invoiceOk, invoiceErr) = await inventory.CreateSaleAsync(invoice,
            new List<SaleInvoiceItem> { new() { ItemId = itemId, Quantity = 5, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(invoiceOk, invoiceErr);

        var note = new DeliveryOrder { SaleInvoiceId = invoice.Id, DeliveryDate = new DateTime(2026, 3, 2) };
        var (noteOk, noteErr) = await inventory.CreateDeliveryOrderAsync(note,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 5, Count = 0 } }, "test");
        Assert.True(noteOk, noteErr);
        Assert.Equal(invoice.Id, (await db.DeliveryOrders.SingleAsync()).SaleInvoiceId);

        var (ok, error, issue) = await inventory.CreateDeliveryIssueAsync(note.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 2, Count = 0 }
        }, "test");

        Assert.False(ok);
        Assert.Null(issue);
        Assert.Contains("فاتورة بيع", error);
        Assert.Equal(0, await db.DeliveryIssues.CountAsync());
        Assert.Equal(DeliveryOrderStatus.Draft, (await db.DeliveryOrders.SingleAsync()).Status);
    }

    [Fact]
    public async Task CreateDeliveryIssue_OnPlainNote_IsStillAllowed()
    {
        using var db = CreateContext();
        SeedChart(db);
        var (itemId, custId) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        var (ok, error, note) = await inventory.CreateSalesDeliveryNoteAsync(null, null, custId,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 4, Count = 0 } }, "test",
            new DateTime(2026, 3, 2));
        Assert.True(ok, error);

        var (issueOk, issueErr, issue) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 2, Count = 0 }
        }, "test");

        Assert.True(issueOk, issueErr);
        Assert.NotNull(issue);
        Assert.Equal(DeliveryIssueStatus.Draft, issue!.Status);
        Assert.Single(await db.DeliveryIssues.ToListAsync());
    }
}
