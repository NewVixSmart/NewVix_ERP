using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Models.Core;
using Silk.Trading.Web.Models.Purchases;
using Silk.Trading.Web.Models.Sales;
using Silk.Trading.Web.Models.Stock;
using Silk.Trading.Web.Services;
using Xunit;

namespace Silk.Trading.Web.Tests;

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
    public async Task Sale_WithBranch_PostsJournalEntry_WithBranchId()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId, supId) = await SeedSaleAsync(db);
        var accounting = new AccountingService(db);
        var svc = new InventoryService(db, accounting);

        var branch = new Branch { Code = "BR-T", Name = "فرع اختبار", IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var invoice = new SaleInvoice { CustomerId = custId };
        var (ok, _) = await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem> { QtyLine(itemId, 3, 50) }, "test", branch.Id);

        Assert.True(ok);
        Assert.Equal(branch.Id, (await db.SaleInvoices.SingleAsync()).BranchId);
        var entries = await db.JournalEntries.ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(branch.Id, e.BranchId));
        Assert.All(await db.JournalEntryLines.ToListAsync(), l => Assert.Equal(branch.Id, l.BranchId));
    }

    [Fact]
    public async Task Sale_NoBranch_PostsJournalEntry_WithNullBranchId()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId, supId) = await SeedSaleAsync(db);
        var accounting = new AccountingService(db);
        var svc = new InventoryService(db, accounting);

        var invoice = new SaleInvoice { CustomerId = custId };
        var (ok, _) = await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem> { QtyLine(itemId, 2, 50) }, "test");

        Assert.True(ok);
        Assert.Null((await db.SaleInvoices.SingleAsync()).BranchId);
        var entries = await db.JournalEntries.ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Null(e.BranchId));
    }

    // ---------- Branch CRUD ----------

    [Fact]
    public async Task Sale_WithLayers_AndAccounting_PostsCogs_AtBaseCost()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedCurrenciesAsync(db);
        var (itemId, custId, supId) = await SeedSaleAsync(db);
        db.StockLayers.Add(new StockLayer { ItemId = itemId, Qty = 10, Count = 0, UnitCost = 40m, RemainingQty = 10, RemainingCount = 0, DateReceived = new DateTime(2026, 1, 1), CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var accounting = new AccountingService(db);
        var svc = new InventoryService(db, accounting);
        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        var invoice = new SaleInvoice { CustomerId = custId, CurrencyId = usd.Id, ExchangeRate = 500m };

        var (ok, err) = await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem> { QtyLine(itemId, 4, 80) }, "test");
        Assert.True(ok);
        Assert.Equal(6, (await db.StockLayers.SingleAsync()).RemainingQty);

        var entry = await db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account).SingleAsync(e => e.Source == JournalSource.SaleInvoice);
        Assert.Equal(4, entry.Lines.Count);
        Assert.Equal(entry.Lines.Sum(l => l.Debit), entry.Lines.Sum(l => l.Credit));
        // value 4 × 80 = 320 USD @500 = 160,000 ; COGS 4 × 40 = 160 base cost (layers are base currency)
        Assert.Contains(entry.Lines, l => l.Account!.Code == "1200" && l.Debit == 160000m);
        Assert.Contains(entry.Lines, l => l.Account!.Code == "4000" && l.Credit == 160000m);
        Assert.Contains(entry.Lines, l => l.Account!.Code == "5000" && l.Debit == 160m);
        Assert.Contains(entry.Lines, l => l.Account!.Code == "1300" && l.Credit == 160m);

        // OnReceipt auto-payment posts a cash receipt at the invoice's base value
        var receiptEntry = await db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account).SingleAsync(e => e.Source == JournalSource.Receipt);
        Assert.Equal(2, receiptEntry.Lines.Count);
        Assert.Contains(receiptEntry.Lines, l => l.Account!.Code == "1000" && l.Debit == 160000m);
        Assert.Contains(receiptEntry.Lines, l => l.Account!.Code == "1200" && l.Credit == 160000m);
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

    // ---------- Shipments ----------

    [Fact]
    public async Task Shipment_CreateWithLinkedSale_AndEditStatus()
    {
        using var db = CreateContext();
        var (itemId, custId, supId) = await SeedSaleAsync(db);
        var svc = new InventoryService(db);
        var sale = new SaleInvoice { CustomerId = custId };
        await svc.CreateSaleAsync(sale, new List<SaleInvoiceItem> { QtyLine(itemId, 1, 50) }, "test");

        var shipment = new Shipment
        {
            ShipmentNumber = "SHP-TEST-001",
            InvoiceType = ShipmentInvoiceType.Sale,
            SaleInvoiceId = sale.Id,
            CustomerId = custId,
            Carrier = "DHL",
            TrackingNumber = "TRK123",
            ShipDate = DateTime.Today,
            Status = ShipmentStatus.Preparing,
            CreatedBy = "test",
            CreatedAt = DateTime.UtcNow
        };
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync();

        var saved = await db.Shipments.Include(s => s.SaleInvoice).SingleAsync(s => s.ShipmentNumber == "SHP-TEST-001");
        Assert.Equal(ShipmentInvoiceType.Sale, saved.InvoiceType);
        Assert.Equal(sale.Id, saved.SaleInvoiceId);
        Assert.Equal("DHL", saved.Carrier);

        saved.Status = ShipmentStatus.Delivered;
        saved.TrackingNumber = "TRK456";
        await db.SaveChangesAsync();

        var updated = await db.Shipments.SingleAsync();
        Assert.Equal(ShipmentStatus.Delivered, updated.Status);
        Assert.Equal("TRK456", updated.TrackingNumber);
    }

    [Fact]
    public void Shipment_StatusEnum_DisplayNamesAreArabic()
    {
        void HasDisplay<TEnum>(TEnum value, string expected)
        {
            var member = typeof(TEnum).GetMember(value!.ToString()!)[0]!;
            var attr = (System.ComponentModel.DataAnnotations.DisplayAttribute)member
                .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false)[0];
            Assert.Equal(expected, attr.Name);
        }

        HasDisplay(ShipmentStatus.Preparing, "قيد التحضير");
        HasDisplay(ShipmentStatus.Delivered, "تم التسليم");
        HasDisplay(ShipmentInvoiceType.Sale, "بيع");
        HasDisplay(ShipmentInvoiceType.Purchase, "شراء");
    }
}
