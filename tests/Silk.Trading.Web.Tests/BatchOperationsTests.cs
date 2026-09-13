using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Models.Core;
using Silk.Trading.Web.Models.Sales;
using Silk.Trading.Web.Models.Stock;
using Silk.Trading.Web.Services;
using Xunit;

namespace Silk.Trading.Web.Tests;

public sealed class BatchOperationsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public BatchOperationsTests()
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
            ("1000", "النقد / الصندوق", GLAccountType.Asset, NormalBalance.Debit),
            ("1200", "المدينون (العملاء)", GLAccountType.Asset, NormalBalance.Debit),
            ("1300", "المخزون", GLAccountType.Asset, NormalBalance.Debit),
            ("2000", "الدائنون (الموردون)", GLAccountType.Liability, NormalBalance.Credit),
            ("3000", "رأس المال", GLAccountType.Equity, NormalBalance.Credit),
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.SaveChanges();
    }

    private async Task<(Item item1, Item item2, int customerId)> SeedAsync(AppDbContext db)
    {
        var cat = new ItemCategory { Name = "تصنيف اختبار" };
        var type = new ItemType { Name = "نوع اختبار" };
        var unit = new Unit { Name = "قطعة" };
        db.ItemCategories.Add(cat);
        db.ItemTypes.Add(type);
        db.Units.Add(unit);

        var item1 = new Item
        {
            Name = "صنف 1", Category = cat, ItemType = type, CountUnit = unit, QuantityUnit = unit,
            PurchasePrice = 50, SalePrice = 80, CurrentCount = 100, CurrentQuantity = 100
        };
        var item2 = new Item
        {
            Name = "صنف 2", Category = cat, ItemType = type, CountUnit = unit, QuantityUnit = unit,
            PurchasePrice = 50, SalePrice = 80, CurrentCount = 100, CurrentQuantity = 100
        };
        db.Items.AddRange(item1, item2);

        var customer = new Customer { Name = "عميل اختبار" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        return (item1, item2, customer.Id);
    }

    private static BatchService CreateBatchService(AppDbContext db, bool withAccounting = true)
    {
        var accounting = withAccounting ? new AccountingService(db) : null;
        return new BatchService(db, new InventoryService(db, accounting));
    }

    // ---------- مبيعات جماعية ----------

    [Fact]
    public async Task RunSalesBatch_CreatesAllInvoices_DecrementsStock_PostsGl_And_UniqueNumbers()
    {
        using var db = CreateContext();
        var (item1, _, customerId) = await SeedAsync(db);
        var svc = CreateBatchService(db);

        var request = new BatchSalesBatchRequest(
            CustomerId: customerId,
            InvoiceDate: new DateTime(2026, 3, 1),
            PaymentTerms: InvoicePaymentTerms.OnReceipt,
            CurrencyId: null,
            ExchangeRate: 1m,
            Discount: 0, Discount2: null, Discount3: null,
            Tax: 0, Notes: "دفعة اختبار",
            Invoices: [
                new BatchSalesInvoice([new BatchSalesLine(item1.Id, Quantity: 10, Count: 0, UnitPrice: 50, Discount: 0)]),
                new BatchSalesInvoice([new BatchSalesLine(item1.Id, Quantity: 10, Count: 0, UnitPrice: 50, Discount: 0)])
            ]);

        var result = await svc.RunSalesBatchAsync(request, "tester");

        Assert.Equal(2, result.SuccessCount);
        Assert.Equal(0, result.FailCount);
        Assert.All(result.Results, r => Assert.True(r.Success));
        Assert.All(result.Results, r => Assert.StartsWith("SI-", r.DocumentNumber));

        Assert.Equal(2, await db.SaleInvoices.CountAsync());
        var numbers = await db.SaleInvoices.Select(s => s.InvoiceNumber).ToListAsync();
        Assert.Equal(2, numbers.Distinct().Count());

        Assert.Equal(80, db.Items.Single(i => i.Id == item1.Id).CurrentQuantity);

        Assert.Equal(2, await db.JournalEntries.CountAsync(e => e.Source == JournalSource.SaleInvoice));
        Assert.Equal(2, await db.JournalEntries.CountAsync(e => e.Source == JournalSource.Receipt));

        Assert.Equal(2, await db.StockMovements.CountAsync());
        Assert.All(await db.StockMovements.ToListAsync(), m => Assert.Equal(DocumentType.SaleInvoice, m.DocumentType));

        Assert.All(await db.SaleInvoices.ToListAsync(), s => Assert.Equal(500m, s.TotalAmount));
    }

    [Fact]
    public async Task RunSalesBatch_OneInvoiceFailsOnStock_OthersStillSucceed()
    {
        using var db = CreateContext();
        var (item1, _, customerId) = await SeedAsync(db);
        var svc = CreateBatchService(db);

        var request = new BatchSalesBatchRequest(
            CustomerId: customerId,
            InvoiceDate: DateTime.Today,
            PaymentTerms: InvoicePaymentTerms.OnReceipt,
            CurrencyId: null, ExchangeRate: 1m,
            Discount: 0, Discount2: null, Discount3: null, Tax: 0, Notes: null,
            Invoices: [
                new BatchSalesInvoice([new BatchSalesLine(item1.Id, Quantity: 150, Count: 0, UnitPrice: 50, Discount: 0)]),
                new BatchSalesInvoice([new BatchSalesLine(item1.Id, Quantity: 10, Count: 0, UnitPrice: 50, Discount: 0)])
            ]);

        var result = await svc.RunSalesBatchAsync(request, "tester");

        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(1, result.FailCount);
        Assert.False(result.Results[0].Success);
        Assert.True(result.Results[1].Success);
        Assert.NotNull(result.Results[0].Error);

        Assert.Single(await db.SaleInvoices.ToListAsync());
        Assert.Equal(90, db.Items.Single(i => i.Id == item1.Id).CurrentQuantity);
        Assert.Equal(10 * 50m, (await db.SaleInvoices.SingleAsync()).TotalAmount);
        Assert.Equal(2, await db.JournalEntries.CountAsync());
        Assert.Single(await db.StockMovements.ToListAsync());
    }

    // ---------- جرد جماعي ----------

    [Fact]
    public async Task RunAdjustmentBatch_MultipleItems_UpdatesStock_PostsGlOnIncrease_FifoLayer()
    {
        using var db = CreateContext();
        var (item1, item2, _) = await SeedAsync(db);
        var svc = CreateBatchService(db);

        var request = new BatchAdjustmentRequest(
            AdjustDate: new DateTime(2026, 3, 1),
            Reason: "جرد دوري",
            Lines: [
                new BatchAdjustmentLine(item1.Id, NewCount: 100, NewQuantity: 150),
                new BatchAdjustmentLine(item2.Id, NewCount: 100, NewQuantity: 40)
            ]);

        var result = await svc.RunAdjustmentBatchAsync(request, "tester");

        Assert.Equal(2, result.SuccessCount);
        Assert.Equal(0, result.FailCount);
        Assert.All(result.Results, r => Assert.StartsWith("ADJ-", r.DocumentNumber));

        Assert.Equal(2, await db.InventoryAdjustments.CountAsync());
        Assert.Equal(150, db.Items.Single(i => i.Id == item1.Id).CurrentQuantity);
        Assert.Equal(40, db.Items.Single(i => i.Id == item2.Id).CurrentQuantity);

        // زيادة فقط تنشئ طبقة FIFO (50 وحدة بتكلفة 50) وتُرحّل قيد افتتاحي
        var layer = await db.StockLayers.SingleAsync();
        Assert.Equal(item1.Id, layer.ItemId);
        Assert.Equal(50, layer.Qty);
        Assert.Equal(50, layer.RemainingQty);
        Assert.Equal(50, layer.UnitCost);

        Assert.Equal(2, await db.JournalEntries.CountAsync());

        var increaseEntry = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceId == item1.Id);
        Assert.Equal(JournalSource.OpeningStock, increaseEntry.Source);
        Assert.Equal(2, increaseEntry.Lines.Count);
        var inventoryLine = increaseEntry.Lines.Single(l => l.Debit > 0);
        Assert.Equal(50 * 50m, inventoryLine.Debit);
        Assert.Equal("1300", (await db.GLAccounts.SingleAsync(a => a.Id == inventoryLine.AccountId)).Code);

        var writeDownEntry = await db.JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceId == item2.Id);
        Assert.Equal(JournalSource.OpeningStock, writeDownEntry.Source);
        Assert.Equal(2, writeDownEntry.Lines.Count);
        var writeDownDebit = writeDownEntry.Lines.Single(l => l.Debit > 0);
        Assert.Equal(60 * 50m, writeDownDebit.Debit);
        Assert.Equal("3000", (await db.GLAccounts.SingleAsync(a => a.Id == writeDownDebit.AccountId)).Code);

        var movements = await db.StockMovements.OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.Equal(MovementType.In, movements[0].Type);
        Assert.Equal(MovementType.Out, movements[1].Type);
        Assert.Equal(50, movements[0].Quantity);
        Assert.Equal(-60, movements[1].Quantity);
    }

    // ---------- أذونات الوحدة ----------

    [Fact]
    public void BatchPermissionKeys_AreRegistered_WithDefaults()
    {
        Assert.Equal(["SalesCreate", "AdjustmentCreate"], PermissionCatalog.ActionsFor("Batch"));
        Assert.Contains(PermissionCatalog.Modules, m => m.Key == "Batch");

        var accountant = PermissionDefaults.DefaultsFor("Accountant");
        Assert.Contains("Batch.SalesCreate", accountant);
        Assert.Contains("Batch.AdjustmentCreate", accountant);

        var warehouse = PermissionDefaults.DefaultsFor("Warehouse");
        Assert.Contains("Batch.AdjustmentCreate", warehouse);
        Assert.DoesNotContain("Batch.SalesCreate", warehouse);
    }

    // ---------- حالات فارغة ----------

    [Fact]
    public async Task RunBatches_NoValidLines_ReturnFailureResultSets()
    {
        using var db = CreateContext();
        var svc = CreateBatchService(db);

        var salesResult = await svc.RunSalesBatchAsync(
            new BatchSalesBatchRequest(1, DateTime.Today, InvoicePaymentTerms.OnReceipt,
                null, 1m, 0, null, null, 0, null,
                [new BatchSalesInvoice([new BatchSalesLine(0, 0, 0, 0, 0)])]),
            "tester");

        Assert.Equal(0, salesResult.SuccessCount);
        Assert.Equal(1, salesResult.FailCount);
        Assert.NotNull(salesResult.Results[0].Error);

        var adjResult = await svc.RunAdjustmentBatchAsync(
            new BatchAdjustmentRequest(DateTime.Today, null, [new BatchAdjustmentLine(0, 0, 0)]),
            "tester");

        Assert.Equal(0, adjResult.SuccessCount);
        Assert.Equal(1, adjResult.FailCount);
        Assert.NotNull(adjResult.Results[0].Error);

        Assert.Equal(0, await db.SaleInvoices.CountAsync());
        Assert.Equal(0, await db.InventoryAdjustments.CountAsync());
    }
}