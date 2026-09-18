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

        db.SaleInvoices.Add(SaleInvoice(customerId, "S-OR", today.AddDays(-100), 250, 0, due: null));
        await db.SaveChangesAsync();

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

        var svc = new ReportService(db, new FinancialReportService(db));
        var bytes = await svc.ExportAgingXlsxAsync();

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        Assert.Equal(0x50, bytes[0]);
        Assert.Equal(0x4B, bytes[1]);
    }
}
