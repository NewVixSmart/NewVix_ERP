using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class AgingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AgingTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static async Task<(int customerId, int supplierId)> SeedPartiesAsync(AppDbContext db)
    {
var customer = new Customer { Name = "عميل أ" };
        var supplier = new Supplier { Name = "مورد ب" };
        db.Customers.Add(customer);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return (customer.Id, supplier.Id);
    }

    private static SaleInvoice SaleInvoice(int customerId, string number, DateTime date, decimal net, decimal paid, DateTime? due = null) => new()
    {
        InvoiceNumber = number,
        CustomerId = customerId,
        InvoiceDate = date,
        PaymentTerms = InvoicePaymentTerms.OnReceipt,
        DueDate = due,
        TotalAmount = net,
        NetAmount = net,
        PaidAmount = paid,
        IsPaid = paid >= net
    };

private static PurchaseInvoice PurchaseInvoice(int supplierId, string number, DateTime date, decimal net, decimal paid, DateTime? due = null) => new()
    {
        InvoiceNumber = number,
        SupplierId = supplierId,
        InvoiceDate = date,
        DueDate = due,
        TotalAmount = net,
        NetAmount = net,
        PaidAmount = paid,
        IsPaid = paid >= net
    };

    private static async Task MarkDeliveredAsync(AppDbContext db, int invoiceId, int customerId, string number)
    {
        db.DeliveryOrders.Add(new DeliveryOrder
        {
            DeliveryNumber = number,
            SaleInvoiceId = invoiceId,
            CustomerId = customerId,
            DeliveryDate = DateTime.Today,
            Status = DeliveryOrderStatus.Delivered,
            DeliveredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Aging_Buckets_Customers_ByDueDate()
    {
        using var db = CreateContext();
        var (customerId, _) = await SeedPartiesAsync(db);
        var today = DateTime.Today;

        db.SaleInvoices.AddRange(
            SaleInvoice(customerId, "S-CUR", today.AddDays(-5), 100, 0, due: today.AddDays(10)),
            SaleInvoice(customerId, "S-30", today.AddDays(-20), 200, 0, due: today.AddDays(-10)),
            SaleInvoice(customerId, "S-60", today.AddDays(-100), 300, 0, due: today.AddDays(-45)),
            SaleInvoice(customerId, "S-90", today.AddDays(-110), 400, 0, due: today.AddDays(-75)),
            SaleInvoice(customerId, "S-120", today.AddDays(-240), 500, 0, due: today.AddDays(-120)),
            SaleInvoice(customerId, "S-PAID", today.AddDays(-30), 600, 600, due: today.AddDays(-15))
        );
        await db.SaveChangesAsync();

        foreach (var inv in db.SaleInvoices.ToList())
            await MarkDeliveredAsync(db, inv.Id, customerId, $"DLV-{inv.InvoiceNumber}");

        var svc = new ReportService(db, new FinancialReportService(db));
        var vm = await svc.AgingAsync();

        var row = Assert.Single(vm.Receivables);
        Assert.Equal("عميل أ", row.PartyName);
        Assert.Equal(100m, row.Current);
        Assert.Equal(200m, row.Days1To30);
        Assert.Equal(300m, row.Days31To60);
        Assert.Equal(400m, row.Days61To90);
        Assert.Equal(500m, row.Days90Plus);
        Assert.Equal(1500m, row.Total);
        Assert.Equal(1500m, vm.ArTotal);
        Assert.Equal(1400m, vm.ArOverdue);
    }

    [Fact]
    public async Task Aging_OnReceipt_Invoice_Uses_InvoiceDateAsDue()
    {
        using var db = CreateContext();
        var (customerId, _) = await SeedPartiesAsync(db);
        var today = DateTime.Today;

var sOr = SaleInvoice(customerId, "S-OR", today.AddDays(-100), 250, 0, due: null);
        db.SaleInvoices.Add(sOr);
        await db.SaveChangesAsync();
        await MarkDeliveredAsync(db, sOr.Id, customerId, "DLV-S-OR");

        var svc = new ReportService(db, new FinancialReportService(db));
var vm = await svc.AgingAsync();

        var row = Assert.Single(vm.Receivables);
        Assert.Equal(250m, row.Days90Plus);
    }

    [Fact]
    public async Task Aging_Suppliers_And_FullyPaid_Excluded()
    {
        using var db = CreateContext();
        var (customerId, supplierId) = await SeedPartiesAsync(db);
        var today = DateTime.Today;

        db.PurchaseInvoices.AddRange(
            PurchaseInvoice(supplierId, "P-PAID", today.AddDays(-60), 700, 700, due: today.AddDays(-30)),
            PurchaseInvoice(supplierId, "P-OVER", today.AddDays(-60), 800, 0, due: today.AddDays(-5)),
            PurchaseInvoice(supplierId, "P-FUT", today.AddDays(-60), 900, 0, due: today.AddDays(3))
        );
db.SaleInvoices.AddRange(
            SaleInvoice(customerId, "S-PAID2", today.AddDays(-40), 200, 200, due: today.AddDays(-10)),
            SaleInvoice(customerId, "S-FUT2", today.AddDays(-40), 300, 0, due: today.AddDays(2))
        );
        await db.SaveChangesAsync();

        foreach (var inv in db.SaleInvoices.Where(s => s.CustomerId == customerId).ToList())
            await MarkDeliveredAsync(db, inv.Id, customerId, $"DLV-{inv.InvoiceNumber}");

        var svc = new ReportService(db, new FinancialReportService(db));
        var vm = await svc.AgingAsync();

        Assert.DoesNotContain(vm.Payables, r => r.Total == 700);
        var payable = Assert.Single(vm.Payables);
        Assert.Equal("مورد ب", payable.PartyName);
        Assert.Equal(900m, payable.Current);
        Assert.Equal(800m, payable.Days1To30);
        Assert.Equal(1700m, vm.ApTotal);

        var receivable = Assert.Single(vm.Receivables);
        Assert.Equal(300m, receivable.Current);
        Assert.Equal(300m, vm.ArTotal);
    }

    [Fact]
    public async Task Aging_NoOpenInvoices_EmptyReport()
    {
        using var db = CreateContext();
        await SeedPartiesAsync(db);
        await db.SaveChangesAsync();

        var svc = new ReportService(db, new FinancialReportService(db));
        var vm = await svc.AgingAsync();

        Assert.Empty(vm.Receivables);
        Assert.Empty(vm.Payables);
        Assert.Equal(0m, vm.ArTotal);
        Assert.Equal(0m, vm.ApTotal);
    }

    [Fact]
    public async Task ExportAgingXlsx_ProducesValidWorkbook()
    {
        using var db = CreateContext();
        var (customerId, supplierId) = await SeedPartiesAsync(db);
        var today = DateTime.Today;

db.SaleInvoices.Add(SaleInvoice(customerId, "S-X", today.AddDays(-15), 100, 0, due: today.AddDays(-10)));
        db.PurchaseInvoices.Add(PurchaseInvoice(supplierId, "P-X", today.AddDays(-15), 150, 0, due: today.AddDays(-10)));
        await db.SaveChangesAsync();
        var sx = await db.SaleInvoices.SingleAsync(s => s.InvoiceNumber == "S-X");
        await MarkDeliveredAsync(db, sx.Id, customerId, "DLV-S-X");

var svc = new ReportService(db, new FinancialReportService(db));
        var bytes = await svc.ExportAgingXlsxAsync();

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        Assert.Equal(0x50, bytes[0]);
        Assert.Equal(0x4B, bytes[1]);
    }

    [Fact]
    public async Task Dashboard_DueAlerts_ConvertsForeignCurrenciesToBase()
    {
        using var db = CreateContext();
        var (customerId, _) = await SeedPartiesAsync(db);

        db.Currencies.Add(new Currency { Code = "EUR", Name = "يورو", Symbol = "€", ExchangeRate = 3m, IsBase = false });
        await db.SaveChangesAsync();
        var currency = await db.Currencies.SingleAsync(c => c.Code == "EUR");

        var today = DateTime.Today;
        db.SaleInvoices.Add(new SaleInvoice
        {
            InvoiceNumber = "S-FX",
            CustomerId = customerId,
            InvoiceDate = today.AddDays(-20),
            DueDate = today.AddDays(-10),
            CurrencyId = currency.Id,
            ExchangeRate = 3m,
            TotalAmount = 100m,
            NetAmount = 100m,
            PaidAmount = 0m
        });
        await db.SaveChangesAsync();

        var vm = await new DashboardService(db).GetDashboardAsync();

        Assert.Equal(1, vm.OverdueReceivableCount);
        Assert.Equal(300m, vm.OverdueReceivableTotal); // 100 × 3, not a raw mixed-currency 100
    }

    [Fact]
    public async Task Aging_Includes_OpeningBalance_InOldestBucket()
    {
        using var db = CreateContext();
        var customer = new Customer { Name = "عميل افتتاحي", OpeningBalance = 200m };
        var supplier = new Supplier { Name = "مورد افتتاحي", OpeningBalance = 150m };
        db.Customers.Add(customer);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var today = DateTime.Today;
        db.SaleInvoices.Add(SaleInvoice(customer.Id, "S-OB", today.AddDays(-20), 100, 0, due: today.AddDays(-10)));
        db.PurchaseInvoices.Add(PurchaseInvoice(supplier.Id, "P-OB", today.AddDays(-20), 120, 0, due: today.AddDays(-10)));
        await db.SaveChangesAsync();
        var sx = await db.SaleInvoices.SingleAsync(s => s.InvoiceNumber == "S-OB");
        await MarkDeliveredAsync(db, sx.Id, customer.Id, "DLV-S-OB");

        var svc = new ReportService(db, new FinancialReportService(db));
        var vm = await svc.AgingAsync();

        var ar = Assert.Single(vm.Receivables);
        Assert.Equal("عميل افتتاحي", ar.PartyName);
        Assert.Equal(100m, ar.Days1To30);
        Assert.Equal(200m, ar.Days90Plus);
        Assert.Equal(300m, ar.Total);

        var ap = Assert.Single(vm.Payables);
        Assert.Equal("مورد افتتاحي", ap.PartyName);
        Assert.Equal(120m, ap.Days1To30);
        Assert.Equal(150m, ap.Days90Plus);
        Assert.Equal(270m, ap.Total);
    }

    [Fact]
    public async Task Aging_StandaloneReturn_CreditRemainder_IsSurfacedNotDropped()
    {
        using var db = CreateContext();
        var customer = new Customer { Name = "عميل دائن" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        db.SaleReturns.Add(new SaleReturn
        {
            ReturnNumber = "SR-CR1",
            CustomerId = customer.Id,
            Status = ReturnStatus.Posted,
            TotalAmount = 120m,
            ReturnDate = DateTime.Today.AddDays(-3)
        });
        await db.SaveChangesAsync();

        var svc = new ReportService(db, new FinancialReportService(db));
        var vm = await svc.AgingAsync();

        var row = Assert.Single(vm.Receivables);
        Assert.Equal(-120m, row.Total);
        Assert.Equal(-120m, row.Days1To30);
        Assert.Equal(-120m, vm.ArTotal);
    }

    [Fact]
    public async Task Aging_ZeroNetPosition_Excluded_ButPureCreditRemainderSurfaced()
    {
        using var db = CreateContext();
        var customer = new Customer { Name = "عميل صفري" };
        var supplier = new Supplier { Name = "مورد دائن" };
        db.Customers.Add(customer);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var today = DateTime.Today;
        var inv = SaleInvoice(customer.Id, "S-ZERO", today.AddDays(-10), 100, 0, due: today.AddDays(-5));
        db.SaleInvoices.Add(inv);
        await db.SaveChangesAsync();
        await MarkDeliveredAsync(db, inv.Id, customer.Id, "DLV-S-ZERO");

        db.SaleReturns.Add(new SaleReturn
        {
            ReturnNumber = "SR-Z",
            CustomerId = customer.Id,
            SaleInvoiceId = inv.Id,
            Status = ReturnStatus.Posted,
            TotalAmount = 100m,
            ReturnDate = today.AddDays(-2)
        });
        db.PurchaseReturns.Add(new PurchaseReturn
        {
            ReturnNumber = "PR-CR",
            SupplierId = supplier.Id,
            Status = ReturnStatus.Posted,
            TotalAmount = 60m,
            ReturnDate = today.AddDays(-4)
        });
        await db.SaveChangesAsync();

        var svc = new ReportService(db, new FinancialReportService(db));
        var vm = await svc.AgingAsync();

        Assert.Empty(vm.Receivables);
        var payable = Assert.Single(vm.Payables);
        Assert.Equal("مورد دائن", payable.PartyName);
        Assert.Equal(-60m, payable.Total);
        Assert.Equal(-60m, vm.ApTotal);
    }

    [Fact]
    public async Task DashboardReport_Overdue_UsesInvoiceDate_WhenNoDueDate()
    {
        using var db = CreateContext();
        var (customerId, _) = await SeedPartiesAsync(db);
        var today = DateTime.Today;

        db.SaleInvoices.Add(SaleInvoice(customerId, "S-NOD", today.AddDays(-40), 150, 0, due: null));
        await db.SaveChangesAsync();

        var svc = new ReportService(db, new FinancialReportService(db));
        var vm = await svc.GetDashboardAsync();

        var row = Assert.Single(vm.OverdueReceivables);
        Assert.Equal(today.AddDays(-40), row.DueDate);
        Assert.Equal(150m, row.NetAmount);
    }

    [Fact]
    public async Task Aging_FxSettlementAtDifferentRate_OutstandingReconcilesToArControl()
    {
        using var db = CreateContext();
        foreach (var (code, name, type, normal) in new (string, string, GLAccountType, NormalBalance)[]
        {
            ("1000", "النقد / الصندوق", GLAccountType.Asset, NormalBalance.Debit),
            ("1200", "المدينون (العملاء)", GLAccountType.Asset, NormalBalance.Debit),
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("8400", "أرباح فروقات العملة (عملة أجنبية)", GLAccountType.Revenue, NormalBalance.Credit),
        })
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.Currencies.Add(new Currency { Code = "USD", Name = "دولار أمريكي", Symbol = "$", ExchangeRate = 48.5m, IsBase = false, IsActive = true });
        var (customerId, _) = await SeedPartiesAsync(db);
        await db.SaveChangesAsync();

        var invoice = SaleInvoice(customerId, "S-FX", DateTime.Today.AddDays(-5), 100, 0, due: DateTime.Today.AddDays(10));
        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        invoice.CurrencyId = usd.Id;
        invoice.ExchangeRate = 48.5m;
        db.SaleInvoices.Add(invoice);
        await db.SaveChangesAsync();
        await MarkDeliveredAsync(db, invoice.Id, customerId, "DLV-S-FX");

        await new AccountingService(db).RecordSaleDeliveryAsync(
            DateTime.Today.AddDays(-5), customerId, 4850m, 0m, usd.Id, 48.5m, "test", deliveryId: invoice.Id);

        var payments = new PaymentService(db, new AccountingService(db));
        var (ok, err, _) = await payments.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = customerId,
            Amount = 50m,
            CurrencyId = usd.Id,
            ExchangeRate = 50m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "test");
        Assert.True(ok, err);

        var alloc = await db.SalePaymentAllocations.SingleAsync();
        Assert.Equal(2425m, alloc.AllocatedBaseAmount);
        Assert.Equal(75m, alloc.FxGain);

        var svc = new ReportService(db, new FinancialReportService(db));
        var vm = await svc.AgingAsync();

        decimal arControl = await db.JournalEntryLines
            .Include(l => l.Account)
            .Where(l => l.Account!.Code == "1200")
            .SumAsync(l => l.Debit - l.Credit);
        Assert.Equal(2425m, arControl);

        var row = Assert.Single(vm.Receivables);
        Assert.Equal(2425m, row.Total);
        Assert.Equal(2425m, vm.ArTotal);
    }
}
