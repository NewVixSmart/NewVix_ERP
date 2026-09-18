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

public sealed class MilestoneM8aTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public MilestoneM8aTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
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
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.SaveChanges();
    }

    private async Task SeedCurrenciesAsync(AppDbContext db, bool baseSdg = true)
    {
        db.Currencies.Add(new Currency { Code = "SDG", Name = "جنيه سوداني", Symbol = "ج.س", ExchangeRate = 1m, IsBase = baseSdg, IsActive = true });
        db.Currencies.Add(new Currency { Code = "USD", Name = "دولار أمريكي", Symbol = "$", ExchangeRate = 500m, IsBase = false, IsActive = true });
        await db.SaveChangesAsync();
    }

    private async Task<(int itemId, int custId, int supId)> SeedSaleAsync(AppDbContext db)
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
            PurchasePrice = 50,
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

    private static SaleInvoiceItem QtyLine(int itemId, decimal qty, decimal price) => new()
    {
        ItemId = itemId, Quantity = qty, Count = 0, UnitPrice = price
    };

    // ---------- Currencies ----------

    [Fact]
    public void Currency_Seed_BaseFlag_SingleTrue()
    {
        using var db = CreateContext();
        db.Currencies.Add(new Currency { Code = "SDG", Name = "جنيه", ExchangeRate = 1m, IsBase = true });
        db.Currencies.Add(new Currency { Code = "USD", Name = "دولار", ExchangeRate = 500m, IsBase = false });
        db.SaveChanges();
        Assert.Equal(1, db.Currencies.Count(c => c.IsBase));
        Assert.Equal(2, db.Currencies.Count());
    }

    [Fact]
    public async Task Sale_WithForeignCurrency_SnapshotsCurrencyAndRate_BaseNetAmountUnchanged()
    {
        using var db = CreateContext();
        await SeedCurrenciesAsync(db);
        var (itemId, custId, supId) = await SeedSaleAsync(db);
        var svc = new InventoryService(db);

        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        var invoice = new SaleInvoice
        {
            CustomerId = custId,
            CurrencyId = usd.Id,
            ExchangeRate = 500m,
            Discount = 10,
            Tax = 5
        };
        var lines = new List<SaleInvoiceItem> { QtyLine(itemId, 10, 50) };

        var (ok, _) = await svc.CreateSaleAsync(invoice, lines, "test");

        Assert.True(ok);
        var saved = await db.SaleInvoices.SingleAsync();
        Assert.Equal(usd.Id, saved.CurrencyId);
        Assert.Equal(500m, saved.ExchangeRate);
        Assert.Equal(500m, saved.TotalAmount);
        Assert.Equal(500 - 10 + 5, saved.NetAmount); // still base currency math
    }

    [Fact]
    public async Task Sale_NoCurrencyOrDefaultBase_StoresNullAndRateNull_WhenNotSet()
    {
        using var db = CreateContext();
        var (itemId, custId, supId) = await SeedSaleAsync(db);
        var svc = new InventoryService(db);

        var invoice = new SaleInvoice { CustomerId = custId };
        var (ok, _) = await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem> { QtyLine(itemId, 5, 50) }, "test");

        Assert.True(ok);
        var saved = await db.SaleInvoices.SingleAsync();
        Assert.Null(saved.CurrencyId);
        Assert.Null(saved.ExchangeRate);
        Assert.Equal(250m, saved.NetAmount);
    }

    // ---------- Branch + GL ----------

    [Fact]
    public async Task Sale_WithBranch_PostsJournalEntry_AtDelivery_WithBranchId()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId, supId) = await SeedSaleAsync(db);
        var accounting = new AccountingService(db);
        var svc = new InventoryService(db);

        var branch = new Branch { Code = "BR-T", Name = "فرع اختبار", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var invoice = new SaleInvoice { CustomerId = custId };
        var (ok, _) = await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem> { QtyLine(itemId, 3, 50) }, "test", branch.Id);

        Assert.True(ok);
        Assert.Equal(branch.Id, (await db.SaleInvoices.SingleAsync()).BranchId);
        Assert.Equal(0, await db.JournalEntries.CountAsync());

        var posting = new InventoryService(db, accounting);
        var delivery = new DeliveryOrder { SaleInvoiceId = invoice.Id, DeliveryDate = new DateTime(2026, 9, 1) };
        var (dOk, dErr) = await posting.CreateDeliveryOrderAsync(delivery, new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 3 } }, "test");
        Assert.True(dOk);
        Assert.Null(dErr);
        Assert.StartsWith("DLV-", delivery.DeliveryNumber);

        var (dlvOk, dlvErr) = await posting.DeliverDeliveryOrderAsync(delivery.Id, "test", branch.Id);
        Assert.True(dlvOk);
        Assert.Null(dlvErr);

        var entries = await db.JournalEntries.ToListAsync();
        Assert.Single(entries);
        Assert.Equal(JournalSource.SaleDeliveryOrder, entries[0].Source);
        Assert.Equal(branch.Id, entries[0].BranchId);
        Assert.All(await db.JournalEntryLines.ToListAsync(), l => Assert.Equal(branch.Id, l.BranchId));
    }

    [Fact]
    public async Task Sale_NoBranch_PostsJournalEntry_AtDelivery_WithNullBranchId()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId, supId) = await SeedSaleAsync(db);
        var accounting = new AccountingService(db);
        var svc = new InventoryService(db);

        var invoice = new SaleInvoice { CustomerId = custId };
        var (ok, _) = await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem> { QtyLine(itemId, 2, 50) }, "test");

        Assert.True(ok);
        Assert.Null((await db.SaleInvoices.SingleAsync()).BranchId);

        var posting = new InventoryService(db, accounting);
        var delivery = new DeliveryOrder { SaleInvoiceId = invoice.Id, DeliveryDate = new DateTime(2026, 9, 1) };
        var (dOk, _) = await posting.CreateDeliveryOrderAsync(delivery, new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 2 } }, "test");
        Assert.True(dOk);
        var (dlvOk, _) = await posting.DeliverDeliveryOrderAsync(delivery.Id, "test");
        Assert.True(dlvOk);

        var entries = await db.JournalEntries.ToListAsync();
        Assert.Single(entries);
        Assert.Equal(JournalSource.SaleDeliveryOrder, entries[0].Source);
        Assert.Null(entries[0].BranchId);
        Assert.All(await db.JournalEntryLines.ToListAsync(), l => Assert.Null(l.BranchId));
    }

    // ---------- Branch CRUD ----------

    [Fact]
    public async Task Sale_WithLayers_AndAccounting_PostsCogs_AtDelivery_AtBaseCost()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedCurrenciesAsync(db);
        var (itemId, custId, supId) = await SeedSaleAsync(db);
        db.StockLayers.Add(new StockLayer { ItemId = itemId, Qty = 10, Count = 0, UnitCost = 40m, RemainingQty = 10, RemainingCount = 0, DateReceived = new DateTime(2026, 1, 1), CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var svc = new InventoryService(db);
        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        var invoice = new SaleInvoice { CustomerId = custId, CurrencyId = usd.Id, ExchangeRate = 500m };

        var (ok, err) = await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem> { QtyLine(itemId, 4, 80) }, "test");
        Assert.True(ok);
        Assert.Equal(10, (await db.StockLayers.SingleAsync()).RemainingQty);
        Assert.Equal(0, await db.JournalEntries.CountAsync());

        var posting = new InventoryService(db, new AccountingService(db));
        var delivery = new DeliveryOrder { SaleInvoiceId = invoice.Id, DeliveryDate = new DateTime(2026, 9, 2) };
        var (dOk, _) = await posting.CreateDeliveryOrderAsync(delivery, new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 4 } }, "test");
        Assert.True(dOk);
        var (dlvOk, _) = await posting.DeliverDeliveryOrderAsync(delivery.Id, "test");
        Assert.True(dlvOk);

        Assert.Equal(6, (await db.StockLayers.SingleAsync()).RemainingQty);

        var entry = await db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account).SingleAsync();
        Assert.Equal(JournalSource.SaleDeliveryOrder, entry.Source);
        Assert.Equal(4, entry.Lines.Count);
        Assert.Equal(entry.Lines.Sum(l => l.Debit), entry.Lines.Sum(l => l.Credit));
        // value 4 × 80 = 320 USD @500 = 160,000 ; COGS 4 × 40 = 160 base cost (layers are base currency)
        Assert.Contains(entry.Lines, l => l.Account!.Code == "1200" && l.Debit == 160000m);
        Assert.Contains(entry.Lines, l => l.Account!.Code == "4000" && l.Credit == 160000m);
        Assert.Contains(entry.Lines, l => l.Account!.Code == "5000" && l.Debit == 160m);
        Assert.Contains(entry.Lines, l => l.Account!.Code == "1300" && l.Credit == 160m);

        // no auto-payment at invoice or delivery phase; the customer balance is settled via Payments
        Assert.Equal(0, await db.JournalEntries.CountAsync(e => e.Source == JournalSource.Receipt));
        Assert.Equal(0, await db.Payments.CountAsync());
    }

    [Fact]
    public async Task Branch_CreateUpdateDelete()
    {
        using var db = CreateContext();
        var branch = new Branch { Code = "BR-01", Name = "فرع 1", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        branch.Name = "الفرع المحدث";
        branch.Address = "الخرطوم";
        await db.SaveChangesAsync();

        var saved = await db.Branches.SingleAsync();
        Assert.Equal("الفرع المحدث", saved.Name);
        Assert.Equal("الخرطوم", saved.Address);

        db.Branches.Remove(saved);
        await db.SaveChangesAsync();
        Assert.Equal(0, await db.Branches.CountAsync());
    }

    [Fact]
    public async Task Branch_InUse_CanBeToggledInactive_NotDeleted()
    {
        using var db = CreateContext();
        await SeedCurrenciesAsync(db);
        var branch = new Branch { Code = "BR-X", Name = "فرع مستخدم", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var (itemId, custId, supId) = await SeedSaleAsync(db);
        var svc = new InventoryService(db);
        var invoice = new SaleInvoice { CustomerId = custId };
        await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem> { QtyLine(itemId, 2, 50) }, "test", branch.Id);
        Assert.Equal(branch.Id, (await db.SaleInvoices.SingleAsync()).BranchId);

        // in-use branch cannot be physically deleted; marking inactive is the safe path
        var used = await db.Branches.SingleAsync();
        used.IsActive = false;
        await db.SaveChangesAsync();
        Assert.False((await db.Branches.SingleAsync()).IsActive);
    }

    // ---------- Delivery Orders ----------

    [Fact]
    public async Task DeliveryOrder_CreateAndDeliver_WithLinkedSale()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId, supId) = await SeedSaleAsync(db);
        db.StockLayers.Add(new StockLayer { ItemId = itemId, Qty = 5, Count = 0, UnitCost = 40m, RemainingQty = 5, RemainingCount = 0, DateReceived = new DateTime(2026, 1, 1), CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var svc = new InventoryService(db);
        var sale = new SaleInvoice { CustomerId = custId };
        var (ok, _) = await svc.CreateSaleAsync(sale, new List<SaleInvoiceItem> { QtyLine(itemId, 1, 50) }, "test");
        Assert.True(ok);

        var posting = new InventoryService(db, new AccountingService(db));
        var delivery = new DeliveryOrder
        {
            SaleInvoiceId = sale.Id,
            Carrier = "DHL",
            TrackingNumber = "TRK123",
            DeliveryDate = new DateTime(2026, 9, 3)
        };
        var (dOk, dErr) = await posting.CreateDeliveryOrderAsync(delivery, new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 1 } }, "test");
        Assert.True(dOk);
        Assert.Null(dErr);

        var saved = await db.DeliveryOrders.Include(d => d.SaleInvoice).SingleAsync();
        Assert.Equal(sale.Id, saved.SaleInvoiceId);
        Assert.Equal(custId, saved.CustomerId);
        Assert.Equal("DHL", saved.Carrier);
        Assert.Equal(DeliveryOrderStatus.Draft, saved.Status);
        Assert.StartsWith("DLV-", saved.DeliveryNumber);

        var (dlvOk, dlvErr) = await posting.DeliverDeliveryOrderAsync(saved.Id, "test");
        Assert.True(dlvOk);
        Assert.Null(dlvErr);
        Assert.Single(await db.StockMovements.Where(m => m.DocumentType == DocumentType.SaleDeliveryOrder).ToListAsync());

        var updated = await db.DeliveryOrders.SingleAsync();
        Assert.Equal(DeliveryOrderStatus.Delivered, updated.Status);
        Assert.Equal("test", updated.DeliveredBy);
        Assert.NotNull(updated.DeliveredAt);
        Assert.Equal(4, (await db.StockLayers.SingleAsync()).RemainingQty);
    }

    [Fact]
    public async Task DeliveryOrder_OverQuantity_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId, supId) = await SeedSaleAsync(db);
        var svc = new InventoryService(db);
        var sale = new SaleInvoice { CustomerId = custId };
        var (ok, _) = await svc.CreateSaleAsync(sale, new List<SaleInvoiceItem> { QtyLine(itemId, 2, 50) }, "test");
        Assert.True(ok);

        var posting = new InventoryService(db);
        var delivery = new DeliveryOrder { SaleInvoiceId = sale.Id, DeliveryDate = DateTime.Today };
        var (dOk, dErr) = await posting.CreateDeliveryOrderAsync(delivery, new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 3 } }, "test");
        Assert.False(dOk);
        Assert.Contains("متبقي", dErr);
        Assert.Equal(0, await db.DeliveryOrders.CountAsync());
    }

    [Fact]
    public async Task DeliveryOrder_CancelDraft_ThenCreateAgainReusesRemaining()
    {
        using var db = CreateContext();
        var (itemId, custId, supId) = await SeedSaleAsync(db);
        var svc = new InventoryService(db);
        var sale = new SaleInvoice { CustomerId = custId };
        var (ok, _) = await svc.CreateSaleAsync(sale, new List<SaleInvoiceItem> { QtyLine(itemId, 2, 50) }, "test");
        Assert.True(ok);

        var posting = new InventoryService(db);
        var delivery = new DeliveryOrder { SaleInvoiceId = sale.Id, DeliveryDate = DateTime.Today };
        var (dOk, _) = await posting.CreateDeliveryOrderAsync(delivery, new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 2 } }, "test");
        Assert.True(dOk);
        Assert.Equal(DeliveryOrderStatus.Draft, (await db.DeliveryOrders.SingleAsync()).Status);

        var (cOk, cErr) = await posting.CancelDeliveryOrderAsync(delivery.Id, "test");
        Assert.True(cOk);
        Assert.Null(cErr);
        Assert.Equal(DeliveryOrderStatus.Cancelled, (await db.DeliveryOrders.SingleAsync()).Status);

        var second = new DeliveryOrder { SaleInvoiceId = sale.Id, DeliveryDate = DateTime.Today };
        var (sOk, sErr) = await posting.CreateDeliveryOrderAsync(second, new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 2 } }, "test");
        Assert.True(sOk);
        Assert.Null(sErr);
    }

    [Fact]
    public void DeliveryOrder_StatusEnum_DisplayNamesAreArabic()
    {
        void HasDisplay<TEnum>(TEnum value, string expected)
        {
            var member = typeof(TEnum).GetMember(value!.ToString()!)[0]!;
            var attr = (System.ComponentModel.DataAnnotations.DisplayAttribute)member
                .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false)[0];
            Assert.Equal(expected, attr.Name);
        }

        HasDisplay(DeliveryOrderStatus.Draft, "مسودة");
        HasDisplay(DeliveryOrderStatus.Delivered, "تم التسليم");
        HasDisplay(DeliveryOrderStatus.Cancelled, "ملغي");
    }
}
