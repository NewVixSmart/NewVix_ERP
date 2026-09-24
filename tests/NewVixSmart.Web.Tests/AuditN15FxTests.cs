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

public sealed class AuditN15FxTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AuditN15FxTests()
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
            ("1100", "البنك", GLAccountType.Asset, NormalBalance.Debit),
            ("1200", "المدينون (العملاء)", GLAccountType.Asset, NormalBalance.Debit),
            ("1300", "المخزون", GLAccountType.Asset, NormalBalance.Debit),
            ("2000", "الدائنون (الموردون)", GLAccountType.Liability, NormalBalance.Credit),
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("4400", "خسائر فروقات العملة (عملة أجنبية)", GLAccountType.Expense, NormalBalance.Debit),
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
            ("8400", "أرباح فروقات العملة (عملة أجنبية)", GLAccountType.Revenue, NormalBalance.Credit),
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.SaveChanges();
    }

    private async Task SeedCurrenciesAsync(AppDbContext db)
    {
        db.Currencies.Add(new Currency { Code = "SDG", Name = "جنيه سوداني", Symbol = "ج.س", ExchangeRate = 1m, IsBase = true, IsActive = true });
        db.Currencies.Add(new Currency { Code = "USD", Name = "دولار أمريكي", Symbol = "$", ExchangeRate = 500m, IsBase = false, IsActive = true });
        await db.SaveChangesAsync();
    }

    private async Task<int> SeedCustomerAsync(AppDbContext db)
    {
        db.Customers.Add(new Customer { Name = "عميل اختبار" });
        await db.SaveChangesAsync();
        return (await db.Customers.SingleAsync()).Id;
    }

    private async Task<int> SeedSupplierAsync(AppDbContext db)
    {
        db.Suppliers.Add(new Supplier { Name = "مورد اختبار" });
        await db.SaveChangesAsync();
        return (await db.Suppliers.SingleAsync()).Id;
    }

    private static async Task<SaleInvoice> SeedBaseSaleInvoiceAsync(AppDbContext db, int customerId, decimal net, DateTime date)
    {
        var inv = new SaleInvoice
        {
            InvoiceNumber = $"SI-N15-{Guid.NewGuid():N}".Substring(0, 16),
            CustomerId = customerId,
            NetAmount = net,
            TotalAmount = net,
            InvoiceDate = date,
            CreatedAt = DateTime.UtcNow
        };
        db.SaleInvoices.Add(inv);
        await db.SaveChangesAsync();
        db.DeliveryOrders.Add(new DeliveryOrder
        {
            DeliveryNumber = $"DLV-N15-{inv.Id}",
            SaleInvoiceId = inv.Id,
            CustomerId = customerId,
            DeliveryDate = date,
            Status = DeliveryOrderStatus.Delivered,
            DeliveredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return inv;
    }

    private static async Task<Models.Purchases.PurchaseInvoice> SeedBasePurchaseInvoiceAsync(AppDbContext db, int supplierId, decimal net, DateTime date)
    {
        var inv = new Models.Purchases.PurchaseInvoice
        {
            InvoiceNumber = $"PI-N15-{Guid.NewGuid():N}".Substring(0, 16),
            SupplierId = supplierId,
            NetAmount = net,
            TotalAmount = net,
            InvoiceDate = date,
            CreatedAt = DateTime.UtcNow
        };
        db.PurchaseInvoices.Add(inv);
        await db.SaveChangesAsync();
        return inv;
    }

    // (1) Foreign payment settles a base (local-currency) invoice entirely in base units:
    //     100 USD @500 = 50,000 SDG against a 50,000 SDG invoice, no phantom FX, no unit mixing.
    [Fact]
    public async Task ForeignPayment_SettlesBaseInvoice_Exactly_InBaseUnits()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedCurrenciesAsync(db);
        var cust = await SeedCustomerAsync(db);
        await SeedBaseSaleInvoiceAsync(db, cust, 50000m, DateTime.Today.AddDays(-5));
        var svc = new PaymentService(db, new AccountingService(db));

        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        var (ok, error, payment) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-N15-1",
            Type = PaymentType.Receipt,
            CustomerId = cust,
            CurrencyId = usd.Id,
            ExchangeRate = 500m,
            Amount = 100m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "test");

        Assert.True(ok, error);
        Assert.Equal(50000m, payment!.BaseAmount);
        var inv = await db.SaleInvoices.SingleAsync();
        Assert.Equal(50000m, inv.PaidAmount);
        Assert.True(inv.IsPaid);
        Assert.Equal(0m, inv.PaidAmount - inv.NetAmount);

        var allocation = await db.SalePaymentAllocations.SingleAsync();
        Assert.Equal(50000m, allocation.AllocatedBaseAmount);
        Assert.Equal(0m, allocation.FxGain);
        Assert.Equal(0m, allocation.FxLoss);
        Assert.Equal(500m, allocation.ExchangeRateAtSettlement);

        var lines = await db.JournalEntryLines.Include(l => l.Account).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Account!.Code == "1000" && l.Debit == 50000m);
        Assert.Contains(lines, l => l.Account!.Code == "1200" && l.Credit == 50000m);
        Assert.False(await db.JournalEntryLines.Include(l => l.Account).AnyAsync(l => l.Account!.Code == "8400"));
        Assert.False(await db.JournalEntryLines.Include(l => l.Account).AnyAsync(l => l.Account!.Code == "4400"));
    }

    // (2) Partial foreign payment against a base invoice leaves the remaining balance owed.
    [Fact]
    public async Task ForeignPayment_PartiallySettlesBaseInvoice()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedCurrenciesAsync(db);
        var cust = await SeedCustomerAsync(db);
        await SeedBaseSaleInvoiceAsync(db, cust, 50000m, DateTime.Today.AddDays(-5));
        var svc = new PaymentService(db, new AccountingService(db));

        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        var (ok, _, _) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-N15-2",
            Type = PaymentType.Receipt,
            CustomerId = cust,
            CurrencyId = usd.Id,
            ExchangeRate = 500m,
            Amount = 50m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "test");

        Assert.True(ok);
        var inv = await db.SaleInvoices.SingleAsync();
        Assert.Equal(25000m, inv.PaidAmount);
        Assert.False(inv.IsPaid);
        Assert.Equal(25000m, (await db.SalePaymentAllocations.SingleAsync()).AllocatedBaseAmount);
    }

    // (3) Foreign overpayment against a base invoice is rejected and fully rolled back.
    [Fact]
    public async Task ForeignPayment_OnBaseInvoice_OverPayment_Rejected_NoSideEffects()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedCurrenciesAsync(db);
        var cust = await SeedCustomerAsync(db);
        await SeedBaseSaleInvoiceAsync(db, cust, 50000m, DateTime.Today.AddDays(-5));
        var svc = new PaymentService(db, new AccountingService(db));

        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        var (ok, error, payment) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-N15-3",
            Type = PaymentType.Receipt,
            CustomerId = cust,
            CurrencyId = usd.Id,
            ExchangeRate = 500m,
            Amount = 110m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "test");

        Assert.False(ok);
        Assert.Contains("المبلغ أكبر من إجمالي المستحق", error);
        Assert.Null(payment);
        Assert.Equal(0, await db.Payments.CountAsync());
        Assert.Equal(0, await db.SalePaymentAllocations.CountAsync() + await db.PurchasePaymentAllocations.CountAsync());
        Assert.Equal(0, await db.JournalEntries.CountAsync());
        var inv = await db.SaleInvoices.SingleAsync();
        Assert.Equal(0m, inv.PaidAmount);
        Assert.False(inv.IsPaid);
    }

    // (4) Foreign payment spreads across multiple base invoices oldest-first.
    [Fact]
    public async Task ForeignPayment_SettlesMultipleBaseInvoices_Fifo()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedCurrenciesAsync(db);
        var cust = await SeedCustomerAsync(db);
        await SeedBaseSaleInvoiceAsync(db, cust, 30000m, DateTime.Today.AddDays(-10));
        await SeedBaseSaleInvoiceAsync(db, cust, 20000m, DateTime.Today.AddDays(-3));
        var svc = new PaymentService(db, new AccountingService(db));

        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        var (ok, _, _) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-N15-4",
            Type = PaymentType.Receipt,
            CustomerId = cust,
            CurrencyId = usd.Id,
            ExchangeRate = 500m,
            Amount = 100m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "test");

        Assert.True(ok);
        var invoices = await db.SaleInvoices.OrderBy(i => i.InvoiceDate).ToListAsync();
        Assert.Equal(30000m, invoices[0].PaidAmount);
        Assert.Equal(20000m, invoices[1].PaidAmount);
        Assert.All(invoices, i => Assert.True(i.IsPaid));
        var allocations = await db.SalePaymentAllocations.OrderBy(a => a.SaleInvoiceId).ToListAsync();
        Assert.Equal(2, allocations.Count);
        Assert.Equal(30000m, allocations[0].AllocatedBaseAmount);
        Assert.Equal(20000m, allocations[1].AllocatedBaseAmount);
    }

    // (5) Foreign disbursement settles a base purchase invoice (mirror of the receipt path).
    [Fact]
    public async Task ForeignDisbursement_SettlesBasePurchaseInvoice_InBaseUnits()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedCurrenciesAsync(db);
        var sup = await SeedSupplierAsync(db);
        await SeedBasePurchaseInvoiceAsync(db, sup, 4000m, DateTime.Today.AddDays(-5));
        var svc = new PaymentService(db, new AccountingService(db));

        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        var (ok, error, payment) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-N15-5",
            Type = PaymentType.Disbursement,
            SupplierId = sup,
            CurrencyId = usd.Id,
            ExchangeRate = 500m,
            Amount = 8m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "test");

        Assert.True(ok, error);
        Assert.Equal(4000m, payment!.BaseAmount);
        var inv = await db.PurchaseInvoices.SingleAsync();
        Assert.Equal(4000m, inv.PaidAmount);
        Assert.True(inv.IsPaid);

        var allocation = await db.PurchasePaymentAllocations.SingleAsync();
        Assert.Equal(4000m, allocation.AllocatedBaseAmount);
        Assert.Equal(0m, allocation.FxGain);
        Assert.Equal(0m, allocation.FxLoss);

        var lines = await db.JournalEntryLines.Include(l => l.Account).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Account!.Code == "2000" && l.Debit == 4000m);
        Assert.Contains(lines, l => l.Account!.Code == "1000" && l.Credit == 4000m);
    }

    // (6) End-to-end foreign invoice lifecycle: create the sale (posts value + COGS at base),
    //     then settle at a higher rate (posts the FX gain and clears the receivable).
    [Fact]
    public async Task ForeignInvoice_Lifecycle_CreateThenSettle_AtHigherRate()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedCurrenciesAsync(db);
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
            PurchasePrice = 40,
            SalePrice = 80,
            CurrentCount = 100,
            CurrentQuantity = 100
        };
        db.Items.Add(item);
        var customer = new Customer { Name = "عميل اختبار" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var invDate = DateTime.Today.AddDays(-5);
        db.StockLayers.Add(new StockLayer { ItemId = item.Id, Qty = 10, Count = 0, UnitCost = 40m, RemainingQty = 10, RemainingCount = 0, DateReceived = invDate, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var accounting = new AccountingService(db);
        var inventory = new InventoryService(db, accounting);
        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        var (ok, err) = await inventory.CreateSaleAsync(new SaleInvoice { CustomerId = customer.Id, CurrencyId = usd.Id, ExchangeRate = 500m, InvoiceDate = invDate, PaymentTerms = InvoicePaymentTerms.Net30 },
            new List<SaleInvoiceItem> { new() { ItemId = item.Id, Quantity = 4, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok, err);
        Assert.Equal(0, await db.JournalEntries.CountAsync()); // invoice alone posts nothing

        var delivery = new DeliveryOrder { SaleInvoiceId = (await db.SaleInvoices.SingleAsync()).Id, DeliveryDate = invDate };
        var (dOk, dErr) = await inventory.CreateDeliveryOrderAsync(delivery, new List<DeliveryOrderItem> { new() { ItemId = item.Id, Quantity = 4, Count = 0 } }, "test");
        Assert.True(dOk, dErr);
        var (dlvOk, dlvErr) = await inventory.DeliverDeliveryOrderAsync(delivery.Id, "test");
        Assert.True(dlvOk, dlvErr);

        var saleEntry = await db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account).SingleAsync();
        Assert.Equal(320m, (await db.SaleInvoices.SingleAsync()).NetAmount);
        Assert.Contains(saleEntry.Lines, l => l.Account!.Code == "1200" && l.Debit == 160000m);
        Assert.Contains(saleEntry.Lines, l => l.Account!.Code == "4000" && l.Credit == 160000m);
        Assert.Contains(saleEntry.Lines, l => l.Account!.Code == "5000" && l.Debit == 160m);
        Assert.Contains(saleEntry.Lines, l => l.Account!.Code == "1300" && l.Credit == 160m);

        var paySvc = new PaymentService(db, accounting);
        var (ok2, err2, payment) = await paySvc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-N15-6",
            Type = PaymentType.Receipt,
            CustomerId = customer.Id,
            CurrencyId = usd.Id,
            ExchangeRate = 520m,
            Amount = 320m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "test");
        Assert.True(ok2, err2);
        Assert.Equal(320m, payment!.Amount);
        var settled = await db.SaleInvoices.SingleAsync();
        Assert.Equal(320m, settled.PaidAmount);
        Assert.True(settled.IsPaid);

        var allocation = await db.SalePaymentAllocations.SingleAsync();
        Assert.Equal(160000m, allocation.AllocatedBaseAmount);
        Assert.Equal(6400m, allocation.FxGain);
        Assert.Equal(0m, allocation.FxLoss);
        Assert.Equal(520m, allocation.ExchangeRateAtSettlement);

        var entries = await db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account).ToListAsync();
        Assert.Equal(2, entries.Count);
        var settleEntry = entries.Single(e => e.Source == JournalSource.Receipt);
        Assert.Contains(settleEntry.Lines, l => l.Account!.Code == "1000" && l.Debit == 166400m);
        Assert.Contains(settleEntry.Lines, l => l.Account!.Code == "1200" && l.Credit == 160000m);
        Assert.Contains(settleEntry.Lines, l => l.Account!.Code == "8400" && l.Credit == 6400m);
        Assert.Null(settleEntry.Lines.FirstOrDefault(l => l.Account!.Code == "4400"));

        var allLines = entries.SelectMany(e => e.Lines).ToList();
        Assert.Equal(allLines.Sum(l => l.Debit), allLines.Sum(l => l.Credit));
        var arNet = allLines.Where(l => l.Account!.Code == "1200").Sum(l => l.Debit) - allLines.Where(l => l.Account!.Code == "1200").Sum(l => l.Credit);
        Assert.Equal(0m, arNet);
    }
}