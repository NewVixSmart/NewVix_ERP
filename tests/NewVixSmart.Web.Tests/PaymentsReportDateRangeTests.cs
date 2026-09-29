using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// The payments report used to filter with <c>PaymentDate &lt;= to.Value.Date</c>. PaymentDate
/// carries a time component, so the upper bound sat at midnight and every receipt taken on the
/// end date fell outside the range - the user records a payment and it is missing from the very
/// report that should show it. The bound has to be exclusive at the next midnight.
/// </summary>
public sealed class PaymentsReportDateRangeTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public PaymentsReportDateRangeTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    [Fact]
    public async Task Report_IncludesPaymentRecordedOnTheEndDate_AtAnEveningHour()
    {
        using var db = CreateContext();
        var cust = new Customer { Name = "عميل التقرير" };
        db.Customers.Add(cust);
        await db.SaveChangesAsync();

        // 21:45 on the end date - the case an inclusive midnight bound threw away.
        var endDate = new DateTime(2026, 9, 27);
        db.Payments.Add(new Payment
        {
            ReceiptNumber = "PAY-LATE",
            Type = PaymentType.Receipt,
            CustomerId = cust.Id,
            PaymentDate = endDate.AddHours(21).AddMinutes(45),
            Method = PaymentMethod.Cash,
            Amount = 617.28m,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = new ReportService(db, new FinancialReportService(db));
        var vm = await svc.PaymentsReportAsync(endDate.AddDays(-10), endDate);

        var payment = Assert.Single(vm.Payments);
        Assert.Equal("PAY-LATE", payment.ReceiptNumber);
        Assert.Equal(1, vm.ReceiptCount);
        Assert.Equal(617.28m, vm.TotalReceipts);
        Assert.Equal(617.28m, vm.Balance);
    }

    [Fact]
    public async Task Report_ExcludesPaymentOneDayAfterTheEndDate()
    {
        using var db = CreateContext();
        var cust = new Customer { Name = "عميل التقرير" };
        db.Customers.Add(cust);
        await db.SaveChangesAsync();

        var endDate = new DateTime(2026, 9, 27);
        db.Payments.Add(new Payment
        {
            ReceiptNumber = "PAY-AFTER",
            Type = PaymentType.Receipt,
            CustomerId = cust.Id,
            PaymentDate = endDate.AddDays(1).AddHours(9),
            Method = PaymentMethod.Cash,
            Amount = 100m,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = new ReportService(db, new FinancialReportService(db));
        var vm = await svc.PaymentsReportAsync(endDate.AddDays(-10), endDate);

        Assert.Empty(vm.Payments);
        Assert.Equal(0m, vm.TotalReceipts);
    }

    [Fact]
    public async Task Report_SplitsReceiptsAndDisbursementsAcrossTheSameRange()
    {
        using var db = CreateContext();
        var cust = new Customer { Name = "عميل التقرير" };
        var supp = new Supplier { Name = "مورد التقرير" };
        db.Customers.Add(cust);
        db.Suppliers.Add(supp);
        await db.SaveChangesAsync();

        var day = new DateTime(2026, 9, 15);
        db.Payments.AddRange(
            new Payment
            {
                ReceiptNumber = "PAY-R",
                Type = PaymentType.Receipt,
                CustomerId = cust.Id,
                PaymentDate = day.AddHours(10),
                Method = PaymentMethod.Cash,
                Amount = 1000m,
                CreatedAt = DateTime.UtcNow
            },
            new Payment
            {
                ReceiptNumber = "PAY-D",
                Type = PaymentType.Disbursement,
                SupplierId = supp.Id,
                PaymentDate = day.AddHours(16),
                Method = PaymentMethod.Cash,
                Amount = 400m,
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var svc = new ReportService(db, new FinancialReportService(db));
        var vm = await svc.PaymentsReportAsync(day, day);

        Assert.Equal(2, vm.Payments.Count);
        Assert.Equal(1, vm.ReceiptCount);
        Assert.Equal(1, vm.DisbursementCount);
        Assert.Equal(1000m, vm.TotalReceipts);
        Assert.Equal(400m, vm.TotalDisbursements);
        Assert.Equal(600m, vm.Balance);
    }
}
