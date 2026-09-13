using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Models.Core;
using Silk.Trading.Web.Models.Purchases;
using Silk.Trading.Web.Models.Sales;
using Silk.Trading.Web.Services;
using Xunit;

namespace Silk.Trading.Web.Tests;

public sealed class CashFlowTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public CashFlowTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static Payment Payment(string number, DateTime date, PaymentType type, decimal amount, PaymentMethod method = PaymentMethod.Cash)
        => new()
        {
            ReceiptNumber = number,
            PaymentDate = date,
            Type = type,
            Method = method,
            Amount = amount,
            BaseAmount = amount
        };

    [Fact]
    public async Task CashFlow_ComputesOpeningPeriodAndClosing()
    {
        using var db = CreateContext();
        var today = DateTime.Today;

        db.Payments.AddRange(
            Payment("P-OPEN-R", today.AddDays(-40), PaymentType.Receipt, 100m),
            Payment("P-OPEN-D", today.AddDays(-35), PaymentType.Disbursement, 50m),
            Payment("P-PER-R1", today.AddDays(-10), PaymentType.Receipt, 200m, PaymentMethod.Cash),
            Payment("P-PER-D", today.AddDays(-5), PaymentType.Disbursement, 80m, PaymentMethod.BankTransfer),
            Payment("P-PER-R2", today, PaymentType.Receipt, 30m, PaymentMethod.Cash)
        );
        await db.SaveChangesAsync();

        var svc = new ReportService(db, new FinancialReportService(db));
        var vm = await svc.CashFlowAsync(today.AddDays(-15), today);

        Assert.Equal(50m, vm.OpeningBalance);
        Assert.Equal(230m, vm.TotalReceipts);
        Assert.Equal(80m, vm.TotalDisbursements);
        Assert.Equal(150m, vm.NetCashFlow);
        Assert.Equal(200m, vm.ClosingBalance);
        Assert.Equal(3, vm.Payments.Count);

        var cash = Assert.Single(vm.ByMethod, m => m.Method == PaymentMethod.Cash);
        Assert.Equal(230m, cash.Receipts);
        Assert.Equal(0m, cash.Disbursements);
        var bank = Assert.Single(vm.ByMethod, m => m.Method == PaymentMethod.BankTransfer);
        Assert.Equal(80m, bank.Disbursements);
    }

    [Fact]
    public async Task CashFlow_NoPayments_ZeroBalances()
    {
        using var db = CreateContext();
        await db.SaveChangesAsync();

        var svc = new ReportService(db, new FinancialReportService(db));
        var vm = await svc.CashFlowAsync(DateTime.Today.AddDays(-7), DateTime.Today);

        Assert.Equal(0m, vm.OpeningBalance);
        Assert.Equal(0m, vm.TotalReceipts);
        Assert.Equal(0m, vm.TotalDisbursements);
        Assert.Equal(0m, vm.ClosingBalance);
        Assert.Empty(vm.ByMethod);
        Assert.Empty(vm.Payments);
    }

    [Fact]
    public async Task ExportCashFlowXlsx_ProducesValidWorkbook()
    {
        using var db = CreateContext();
        var today = DateTime.Today;
        db.Payments.AddRange(
            Payment("P-1", today.AddDays(-3), PaymentType.Receipt, 500m),
            Payment("P-2", today, PaymentType.Disbursement, 120m)
        );
        await db.SaveChangesAsync();

        var svc = new ReportService(db, new FinancialReportService(db));
        var bytes = await svc.ExportCashFlowXlsxAsync(today.AddDays(-7), today);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        Assert.Equal(0x50, bytes[0]);
        Assert.Equal(0x4B, bytes[1]);
    }

    [Fact]
    public async Task CustomerStatement_ComputesRunningClosingBalance()
    {
        using var db = CreateContext();
        var customer = new Customer { Name = "عميل كشف" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        db.Customers.First().OpeningBalance = 500m;
        db.SaleInvoices.Add(new SaleInvoice
        {
            InvoiceNumber = "S-1",
            CustomerId = customer.Id,
            InvoiceDate = DateTime.Today.AddDays(-10),
            TotalAmount = 300m,
            NetAmount = 300m
        });
        db.SaleReturns.Add(new SaleReturn
        {
            ReturnNumber = "R-1",
            CustomerId = customer.Id,
            ReturnDate = DateTime.Today.AddDays(-5),
            TotalAmount = 50m,
            Status = ReturnStatus.Posted
        });
        var pay1 = Payment("PAY-1", DateTime.Today.AddDays(-2), PaymentType.Receipt, 120m);
        pay1.CustomerId = customer.Id;
        db.Payments.Add(pay1);
        await db.SaveChangesAsync();

        var svc = new ReportService(db, new FinancialReportService(db));
        var bytes = await svc.ExportCustomerStatementXlsxAsync(customer.Id);

        Assert.True(bytes.Length > 0);
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(1);
        Assert.Equal("كشف حساب — عميل كشف", ws.Cell(1, 1).GetString());
        Assert.Equal((double)630m, ws.Cell(8, 5).GetDouble());
    }

    [Fact]
    public async Task SupplierStatement_RunningBalance_ReducesWithPayments()
    {
        using var db = CreateContext();
        var supplier = new Supplier { Name = "مورد كشف" };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        db.Suppliers.First().OpeningBalance = 100m;
        db.PurchaseInvoices.Add(new PurchaseInvoice
        {
            InvoiceNumber = "PU-1",
            SupplierId = supplier.Id,
            InvoiceDate = DateTime.Today.AddDays(-10),
            TotalAmount = 400m,
            NetAmount = 400m
        });
        db.PurchaseReturns.Add(new PurchaseReturn
        {
            ReturnNumber = "PR-1",
            SupplierId = supplier.Id,
            ReturnDate = DateTime.Today.AddDays(-5),
            TotalAmount = 60m,
            Status = ReturnStatus.Posted
        });
        var pay2 = Payment("PAY-2", DateTime.Today.AddDays(-1), PaymentType.Disbursement, 140m);
        pay2.SupplierId = supplier.Id;
        db.Payments.Add(pay2);
        await db.SaveChangesAsync();

        var svc = new ReportService(db, new FinancialReportService(db));
        var bytes = await svc.ExportSupplierStatementXlsxAsync(supplier.Id);

        Assert.True(bytes.Length > 0);
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(1);
        Assert.Equal("كشف حساب — مورد كشف", ws.Cell(1, 1).GetString());
        Assert.Equal((double)300m, ws.Cell(8, 5).GetDouble());
    }

    [Fact]
    public async Task Statement_UnknownParty_ReturnsEmpty()
    {
        using var db = CreateContext();
        var svc = new ReportService(db, new FinancialReportService(db));
        Assert.Empty(await svc.ExportCustomerStatementXlsxAsync(9999));
        Assert.Empty(await svc.ExportSupplierStatementXlsxAsync(9999));
    }
}