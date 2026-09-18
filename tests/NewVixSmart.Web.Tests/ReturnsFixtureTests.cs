using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class ReturnsFixtureTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public ReturnsFixtureTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static async Task<(int itemId, int custId, int supId)> SeedAsync(AppDbContext db)
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
        var supplier = new Supplier { Name = "مورد اختبار" };
        db.Customers.Add(customer);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        return (item.Id, customer.Id, supplier.Id);
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
            ("3001", "الأرباح المحتجزة", GLAccountType.Equity, NormalBalance.Credit),
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
            ("5101", "مرتجعات المبيعات", GLAccountType.Expense, NormalBalance.Debit),
            ("5102", "مرتجعات المشتريات", GLAccountType.Expense, NormalBalance.Debit)
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.SaveChanges();
    }

    // ---------- Draft workflow (sale returns) ----------

    [Fact]
    public async Task SaleReturn_Draft_DoesNotTouchStockOrGL()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var ret = new SaleReturn { CustomerId = custId, ReturnDate = new DateTime(2026, 3, 1) };
        var (ok, err, id) = await svc.CreateSaleReturnDraftAsync(ret,
            new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 3, Count = 0, UnitPrice = 80 } }, "test");

        Assert.True(ok);
        Assert.Null(err);
        Assert.True(id > 0);
        Assert.Equal(ReturnStatus.Draft, (await db.SaleReturns.SingleAsync(r => r.Id == id)).Status);
        Assert.Equal(100, db.Items.Single().CurrentQuantity);
        Assert.Equal(0, await db.StockMovements.CountAsync(m => m.DocumentType == DocumentType.SaleReturn));
        Assert.Equal(0, await db.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task PostSaleReturn_RestoresStockAtOriginalCost()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var saleInv = new SaleInvoice { CustomerId = custId };
        await svc.CreateSaleAsync(saleInv, new List<SaleInvoiceItem> { new() { ItemId = itemId, Quantity = 10, Count = 0, UnitPrice = 80 } }, "test");

        var delivery = new DeliveryOrder { SaleInvoiceId = saleInv.Id, DeliveryDate = new DateTime(2026, 3, 2) };
        var (dOk, dErr) = await svc.CreateDeliveryOrderAsync(delivery, new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 10, Count = 0 } }, "test");
        Assert.True(dOk, dErr);
        var (dlvOk, dlvErr) = await svc.DeliverDeliveryOrderAsync(delivery.Id, "test");
        Assert.True(dlvOk, dlvErr);

        var ret = new SaleReturn { CustomerId = custId, SaleInvoiceId = saleInv.Id, ReturnDate = new DateTime(2026, 3, 2) };
        var (ok, _, id) = await svc.CreateSaleReturnDraftAsync(ret,
            new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok);

        var (posted, postErr) = await svc.PostSaleReturnAsync(id, "poster");

        Assert.True(posted);
        Assert.Null(postErr);

        var saved = await db.SaleReturns.SingleAsync(r => r.Id == id);
        Assert.Equal(ReturnStatus.Posted, saved.Status);
        Assert.Equal("poster", saved.PostedBy);
        Assert.NotNull(saved.PostedAt);

        Assert.Equal(94, db.Items.Single().CurrentQuantity);
        Assert.Single(await db.StockMovements.Where(m => m.DocumentType == DocumentType.SaleReturn).ToListAsync());

        var layer = await db.StockLayers.SingleAsync(l => l.ItemId == itemId);
        Assert.Equal(4, layer.RemainingQty);
        Assert.Equal(30, layer.UnitCost);
    }

    [Fact]
    public async Task PostSaleReturn_InClosedYear_IsRejected_BeforeAnySideEffect()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        db.FiscalPeriods.Add(new FiscalPeriod { Year = 2026, IsClosed = true });
        await db.SaveChangesAsync();
        var svc = new InventoryService(db);

        var ret = new SaleReturn { CustomerId = custId, ReturnDate = new DateTime(2026, 5, 1) };
        var (ok, _, id) = await svc.CreateSaleReturnDraftAsync(ret,
            new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok);

        var (posted, err) = await svc.PostSaleReturnAsync(id, "test");

        Assert.False(posted);
        Assert.Contains("مغلقة", err);
        Assert.Equal(100, db.Items.Single().CurrentQuantity);
        Assert.Equal(0, await db.StockMovements.CountAsync(m => m.DocumentType == DocumentType.SaleReturn));
        Assert.Equal(0, await db.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task CreateSaleReturnAsync_InClosedYear_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        db.FiscalPeriods.Add(new FiscalPeriod { Year = 2026, IsClosed = true });
        await db.SaveChangesAsync();
        var svc = new InventoryService(db);

        var ret = new SaleReturn { CustomerId = custId, ReturnDate = new DateTime(2026, 5, 1) };
        var (ok, err) = await svc.CreateSaleReturnAsync(ret,
            new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 80 } }, "test");

        Assert.False(ok);
        Assert.Contains("مغلقة", err);
        Assert.Equal(100, db.Items.Single().CurrentQuantity);
        Assert.Equal(0, await db.StockMovements.CountAsync());
        Assert.Equal(0, await db.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task PostSaleReturn_Twice_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var saleInv = new SaleInvoice { CustomerId = custId };
        await svc.CreateSaleAsync(saleInv, new List<SaleInvoiceItem> { new() { ItemId = itemId, Quantity = 10, Count = 0, UnitPrice = 80 } }, "test");

        var delivery = new DeliveryOrder { SaleInvoiceId = saleInv.Id, DeliveryDate = new DateTime(2026, 3, 2) };
        var (dOk, dErr) = await svc.CreateDeliveryOrderAsync(delivery, new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 10, Count = 0 } }, "test");
        Assert.True(dOk, dErr);
        var (dlvOk, dlvErr) = await svc.DeliverDeliveryOrderAsync(delivery.Id, "test");
        Assert.True(dlvOk, dlvErr);

        var ret = new SaleReturn { CustomerId = custId, SaleInvoiceId = saleInv.Id, ReturnDate = new DateTime(2026, 3, 2) };
        var (ok, _, id) = await svc.CreateSaleReturnDraftAsync(ret,
            new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok);

        var (posted, _) = await svc.PostSaleReturnAsync(id, "test");
        Assert.True(posted);

        var (second, err) = await svc.PostSaleReturnAsync(id, "test");
        Assert.False(second);
        Assert.Contains("بالفعل", err);
    }

    // ---------- Draft workflow (purchase returns) ----------

    [Fact]
    public async Task PostPurchaseReturn_ReducesStock_AtFifoCcr()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var purchaseInv = new PurchaseInvoice { SupplierId = supId };
        await svc.CreatePurchaseAsync(purchaseInv, new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 20, Count = 0, UnitPrice = 45 } }, "test");

        var ret = new PurchaseReturn { SupplierId = supId, PurchaseInvoiceId = purchaseInv.Id, ReturnDate = new DateTime(2026, 3, 1) };
        var (ok, _, id) = await svc.CreatePurchaseReturnDraftAsync(ret,
            new List<PurchaseReturnItem> { new() { ItemId = itemId, Quantity = 5, Count = 0, UnitPrice = 45 } }, "test");
        Assert.True(ok);

        var (posted, postErr) = await svc.PostPurchaseReturnAsync(id, "test");

        Assert.True(posted);
        Assert.Null(postErr);
        Assert.Equal(115, db.Items.Single().CurrentQuantity);

        var saved = await db.PurchaseReturns.SingleAsync(r => r.Id == id);
        Assert.Equal(ReturnStatus.Posted, saved.Status);
        Assert.Equal("test", saved.PostedBy);
        Assert.NotNull(saved.PostedAt);

        var layer = await db.StockLayers.SingleAsync(l => l.ItemId == itemId);
        Assert.Equal(15, layer.RemainingQty);
    }

    [Fact]
    public async Task PostPurchaseReturn_InClosedYear_IsRejected_BeforeAnySideEffect()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        db.FiscalPeriods.Add(new FiscalPeriod { Year = 2026, IsClosed = true });
        await db.SaveChangesAsync();
        var svc = new InventoryService(db);

        var ret = new PurchaseReturn { SupplierId = supId, ReturnDate = new DateTime(2026, 5, 1) };
        var (ok, _, id) = await svc.CreatePurchaseReturnDraftAsync(ret,
            new List<PurchaseReturnItem> { new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 45 } }, "test");
        Assert.True(ok);

        var (posted, err) = await svc.PostPurchaseReturnAsync(id, "test");

        Assert.False(posted);
        Assert.Contains("مغلقة", err);
        Assert.Equal(100, db.Items.Single().CurrentQuantity);
        Assert.Equal(0, await db.StockMovements.CountAsync(m => m.DocumentType == DocumentType.PurchaseReturn));
        Assert.Equal(0, await db.JournalEntries.CountAsync());
    }

    // ---------- FX-aware GL postings ----------

    [Fact]
    public async Task RecordSaleReturnWithCost_PostsBalancedEntry_ContraAndInventoryLegs()
    {
        using var db = CreateContext();
        SeedChart(db);
        var accounting = new AccountingService(db);

        await accounting.RecordSaleReturnWithCostAsync(new DateTime(2026, 2, 2), 9, 1, 100m, 40m, null, null, "test");

        var entry = await db.JournalEntries.SingleAsync(e => e.Source == JournalSource.SaleReturn && e.SourceId == 9);
        var lines = await db.JournalEntryLines.Where(l => l.JournalEntryId == entry.Id).ToListAsync();

        Assert.Equal(4, lines.Count);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
        Assert.True(entry.IsPosted);

        var a5101 = await db.GLAccounts.SingleAsync(a => a.Code == "5101");
        var a1200 = await db.GLAccounts.SingleAsync(a => a.Code == "1200");
        var a1300 = await db.GLAccounts.SingleAsync(a => a.Code == "1300");
        var a5000 = await db.GLAccounts.SingleAsync(a => a.Code == "5000");
        Assert.Equal(100m, lines.Where(l => l.AccountId == a5101.Id).Sum(l => l.Debit));
        Assert.Equal(100m, lines.Where(l => l.AccountId == a1200.Id).Sum(l => l.Credit));
        Assert.Equal(40m, lines.Where(l => l.AccountId == a1300.Id).Sum(l => l.Debit));
        Assert.Equal(40m, lines.Where(l => l.AccountId == a5000.Id).Sum(l => l.Credit));
    }

    [Fact]
    public async Task RecordPurchaseReturnWithCost_PostsBalancedEntry_ContraAndInventoryLegs()
    {
        using var db = CreateContext();
        SeedChart(db);
        var accounting = new AccountingService(db);

        await accounting.RecordPurchaseReturnWithCostAsync(new DateTime(2026, 2, 2), 7, 1, 80m, 30m, null, null, "test");

        var entry = await db.JournalEntries.SingleAsync(e => e.Source == JournalSource.PurchaseReturn && e.SourceId == 7);
        var lines = await db.JournalEntryLines.Where(l => l.JournalEntryId == entry.Id).ToListAsync();

        Assert.Equal(4, lines.Count);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
        Assert.True(entry.IsPosted);

        var a2000 = await db.GLAccounts.SingleAsync(a => a.Code == "2000");
        var a5102 = await db.GLAccounts.SingleAsync(a => a.Code == "5102");
        var a1300 = await db.GLAccounts.SingleAsync(a => a.Code == "1300");
        var a5000 = await db.GLAccounts.SingleAsync(a => a.Code == "5000");
        Assert.Equal(80m, lines.Where(l => l.AccountId == a2000.Id).Sum(l => l.Debit));
        Assert.Equal(80m, lines.Where(l => l.AccountId == a5102.Id).Sum(l => l.Credit));
        Assert.Equal(30m, lines.Where(l => l.AccountId == a1300.Id).Sum(l => l.Credit));
        Assert.Equal(30m, lines.Where(l => l.AccountId == a5000.Id).Sum(l => l.Debit));
    }

    [Fact]
    public async Task RecordSaleReturnWithCost_FxConvertsDocumentValue_ToLocal()
    {
        using var db = CreateContext();
        SeedChart(db);
        var accounting = new AccountingService(db);

        await accounting.RecordSaleReturnWithCostAsync(new DateTime(2026, 2, 3), 10, 1, 100m, 40m, null, 0.9m, "test");

        var entry = await db.JournalEntries.SingleAsync(e => e.Source == JournalSource.SaleReturn && e.SourceId == 10);
        var lines = await db.JournalEntryLines.Where(l => l.JournalEntryId == entry.Id).ToListAsync();
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));

        var a5101 = await db.GLAccounts.SingleAsync(a => a.Code == "5101");
        var a1200 = await db.GLAccounts.SingleAsync(a => a.Code == "1200");
        var a1300 = await db.GLAccounts.SingleAsync(a => a.Code == "1300");
        Assert.Equal(90m, lines.Where(l => l.AccountId == a5101.Id).Sum(l => l.Debit));
        Assert.Equal(90m, lines.Where(l => l.AccountId == a1200.Id).Sum(l => l.Credit));
        Assert.Equal(40m, lines.Where(l => l.AccountId == a1300.Id).Sum(l => l.Debit));
    }

    // ---------- Negative quantity rejection (N-7) ----------

    [Fact]
    public async Task SaleReturnDraft_NegativeQuantity_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var ret = new SaleReturn { CustomerId = custId, ReturnDate = new DateTime(2026, 3, 1) };
        var (ok, err, _) = await svc.CreateSaleReturnDraftAsync(ret,
            new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = -3, Count = 0, UnitPrice = 80 } }, "test");

        Assert.False(ok);
        Assert.Contains("سالبة", err);
        Assert.Equal(0, await db.SaleReturns.CountAsync());
        Assert.Equal(100, db.Items.Single().CurrentQuantity);
        Assert.Equal(0, await db.StockMovements.CountAsync());
    }

    [Fact]
    public async Task PurchaseReturnDraft_NegativeCount_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var ret = new PurchaseReturn { SupplierId = supId, ReturnDate = new DateTime(2026, 3, 1) };
        var (ok, err, _) = await svc.CreatePurchaseReturnDraftAsync(ret,
            new List<PurchaseReturnItem> { new() { ItemId = itemId, Quantity = 0, Count = -2, UnitPrice = 45 } }, "test");

        Assert.False(ok);
        Assert.Contains("سالبة", err);
        Assert.Equal(0, await db.PurchaseReturns.CountAsync());
        Assert.Equal(100, db.Items.Single().CurrentQuantity);
    }

    [Fact]
    public async Task PostSaleReturn_NegativeQuantityStoredDraft_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var ret = new SaleReturn { CustomerId = custId, ReturnDate = new DateTime(2026, 3, 2) };
        var (ok, _, id) = await svc.CreateSaleReturnDraftAsync(ret,
            new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok);

        var saved = await db.SaleReturnItems.SingleAsync();
        saved.Quantity = -2;
        await db.SaveChangesAsync();

        var (posted, err) = await svc.PostSaleReturnAsync(id, "test");
        Assert.False(posted);
        Assert.Contains("سالبة", err);
        Assert.Equal(100, db.Items.Single().CurrentQuantity);
        Assert.Equal(ReturnStatus.Draft, (await db.SaleReturns.SingleAsync()).Status);
    }
}