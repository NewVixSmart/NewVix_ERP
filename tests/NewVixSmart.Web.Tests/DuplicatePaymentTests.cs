using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class DuplicatePaymentTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public DuplicatePaymentTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static async Task<AppDbContext> SeedCustomerWithDeliveredInvoiceAsync(DbContextOptions<AppDbContext> options)
    {
        var db = new AppDbContext(options);
        db.GLAccounts.AddRange(
            new GLAccount { Code = "1000", Name = "النقدية" },
            new GLAccount { Code = "1100", Name = "البنوك" },
            new GLAccount { Code = "1200", Name = "ذمم العملاء" },
            new GLAccount { Code = "1300", Name = "المخزون" },
            new GLAccount { Code = "2000", Name = "الدائنون" },
            new GLAccount { Code = "2055", Name = "ضريبة القيمة المضافة" },
            new GLAccount { Code = "3000", Name = "رأس المال" },
            new GLAccount { Code = "4000", Name = "إيرادات المبيعات" },
            new GLAccount { Code = "5000", Name = "تكلفة المبيعات" },
            new GLAccount { Code = "5101", Name = "مردودات المبيعات" },
            new GLAccount { Code = "5102", Name = "مردودات المشتريات" });
        var customer = new Customer { Name = "عميل الدفعات المكررة" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var invoice = new SaleInvoice
        {
            InvoiceNumber = "SI-DUP-300",
            CustomerId = customer.Id,
            InvoiceDate = DateTime.Today,
            TotalAmount = 1000m,
            NetAmount = 1000m,
            PaidAmount = 0m,
            IsPaid = false
        };
        db.SaleInvoices.Add(invoice);
        await db.SaveChangesAsync();

        db.DeliveryOrders.Add(new DeliveryOrder
        {
            DeliveryNumber = $"DLV-DUP-{invoice.Id}",
            SaleInvoiceId = invoice.Id,
            CustomerId = customer.Id,
            DeliveryDate = DateTime.Today,
            Status = DeliveryOrderStatus.Delivered
        });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Identical_Submission_Within_Window_Is_Rejected_With_Friendly_Error()
    {
        using var db = await SeedCustomerWithDeliveredInvoiceAsync(_options);
        var customerId = await db.Customers.Select(c => c.Id).SingleAsync();
        var svc = new PaymentService(db, new AccountingService(db));

        var (ok1, e1, _) = await svc.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = customerId,
            Amount = 100m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "tester");
        Assert.True(ok1, e1);

        var (ok2, e2, p2) = await svc.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = customerId,
            Amount = 100m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "tester");
        Assert.False(ok2);
        Assert.Null(p2);
        Assert.Contains("مطابقة", e2);
    }

    [Fact]
    public async Task Different_Amount_Or_Party_Is_Not_Blocked()
    {
        using var db = await SeedCustomerWithDeliveredInvoiceAsync(_options);
        var customerId = await db.Customers.Select(c => c.Id).SingleAsync();

        var other = new Customer { Name = "عميل آخر للدفعات" };
        db.Customers.Add(other);
        await db.SaveChangesAsync();

        var otherInvoice = new SaleInvoice
        {
            InvoiceNumber = "SI-DUP-301",
            CustomerId = other.Id,
            InvoiceDate = DateTime.Today,
            TotalAmount = 1000m,
            NetAmount = 1000m,
            PaidAmount = 0m,
            IsPaid = false
        };
        db.SaleInvoices.Add(otherInvoice);
        await db.SaveChangesAsync();
        db.DeliveryOrders.Add(new DeliveryOrder
        {
            DeliveryNumber = $"DLV-DUP-{otherInvoice.Id}",
            SaleInvoiceId = otherInvoice.Id,
            CustomerId = other.Id,
            DeliveryDate = DateTime.Today,
            Status = DeliveryOrderStatus.Delivered
        });
        await db.SaveChangesAsync();

        var svc = new PaymentService(db, new AccountingService(db));
        var (ok1, e1, _) = await svc.CreatePaymentAsync(new Payment { Type = PaymentType.Receipt, CustomerId = customerId, Amount = 100m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today }, "tester");
        var (ok2, e2, _) = await svc.CreatePaymentAsync(new Payment { Type = PaymentType.Receipt, CustomerId = customerId, Amount = 250m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today }, "tester");
        var (ok3, e3, _) = await svc.CreatePaymentAsync(new Payment { Type = PaymentType.Receipt, CustomerId = other.Id, Amount = 100m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today }, "tester");

        Assert.True(ok1, e1);
        Assert.True(ok2, e2);
        Assert.True(ok3, e3);
    }

    [Fact]
    public async Task Database_Unique_Index_Rejects_Duplicate_DedupeKey()
    {
        using var db = CreateContext();
        var customer = new Customer { Name = "عميل قيد فريد" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        db.Payments.Add(new Payment
        {
            ReceiptNumber = "PAY-DUP-1",
            Type = PaymentType.Receipt,
            CustomerId = customer.Id,
            Amount = 50m,
            Method = PaymentMethod.Cash,
            DedupeKey = "dup-key-x"
        });
        db.Payments.Add(new Payment
        {
            ReceiptNumber = "PAY-DUP-2",
            Type = PaymentType.Receipt,
            CustomerId = customer.Id,
            Amount = 50m,
            Method = PaymentMethod.Cash,
            DedupeKey = "dup-key-x"
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.NotNull(ex.InnerException);

        var indexNames = await db.Database.SqlQueryRaw<string>(
            "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'Payments'").ToListAsync();
        Assert.Contains(indexNames, n => n != null && n.Contains("DedupeKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Payment_NextDay_SameAmount_Is_Not_Blocked()
    {
        using var db = await SeedCustomerWithDeliveredInvoiceAsync(_options);
        var customerId = await db.Customers.Select(c => c.Id).SingleAsync();
        var svc = new PaymentService(db, new AccountingService(db));

        var (ok1, e1, _) = await svc.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = customerId,
            Amount = 100m,
            Method = PaymentMethod.Cash,
            PaymentDate = new DateTime(2026, 3, 1)
        }, "tester");
        var (ok2, e2, _) = await svc.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = customerId,
            Amount = 100m,
            Method = PaymentMethod.Cash,
            PaymentDate = new DateTime(2026, 3, 2)
        }, "tester");

        Assert.True(ok1, e1);
        Assert.True(ok2, e2);
    }

    [Fact]
    public async Task SameDay_SameAmount_DifferentMethod_Is_Not_Blocked()
    {
        using var db = await SeedCustomerWithDeliveredInvoiceAsync(_options);
        var customerId = await db.Customers.Select(c => c.Id).SingleAsync();
        var svc = new PaymentService(db, new AccountingService(db));

        var (ok1, e1, _) = await svc.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = customerId,
            Amount = 100m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "tester");
        var (ok2, e2, _) = await svc.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = customerId,
            Amount = 100m,
            Method = PaymentMethod.BankTransfer,
            PaymentDate = DateTime.Today
        }, "tester");

        Assert.True(ok1, e1);
        Assert.True(ok2, e2);
    }
}
