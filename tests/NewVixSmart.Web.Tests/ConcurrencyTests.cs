using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class ConcurrencyTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public ConcurrencyTests()
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
    public async Task DuplicateReceipt_WithinTwoMinuteWindow_IsRejected()
    {
        using var db = CreateContext();
        var customer = new Customer { Name = "عميل الدفع المكرر" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        db.SaleInvoices.Add(new SaleInvoice
        {
            InvoiceNumber = "S-DUP-1",
            CustomerId = customer.Id,
            InvoiceDate = DateTime.Today,
            PaymentTerms = InvoicePaymentTerms.OnReceipt,
            DueDate = DateTime.Today,
            TotalAmount = 1000m,
            NetAmount = 1000m,
            PaidAmount = 0m,
            IsPaid = false
        });
        await db.SaveChangesAsync();

        var svc = new PaymentService(db);

        var first = new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = customer.Id,
            Amount = 500m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        };
        var (ok1, _, _) = await svc.CreatePaymentAsync(first, "tester");
        Assert.True(ok1);

        var second = new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = customer.Id,
            Amount = 500m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        };
        var (ok2, error2, _) = await svc.CreatePaymentAsync(second, "tester");
        Assert.False(ok2);
        Assert.Contains("مطابقة", error2);
    }

    [Fact]
    public async Task StaleContext_WritesSuccessful_OnSqlite()
    {
        using var seed = CreateContext();
        var unit = new Unit { Name = "قطعة تعارض" };
        var item = new Item
        {
            Name = "صنف تعارض",
            Category = new ItemCategory { Name = "تصنيف تعارض" },
            ItemType = new ItemType { Name = "نوع تعارض" },
            CountUnit = unit,
            QuantityUnit = unit,
            CurrentCount = 100m,
            CurrentQuantity = 100m
        };
        seed.Items.Add(item);
        await seed.SaveChangesAsync();

        using var db1 = CreateContext();
        using var db2 = CreateContext();
        var tracked1 = await db1.Items.SingleAsync(i => i.Id == item.Id);
        var tracked2 = await db2.Items.SingleAsync(i => i.Id == item.Id);

        tracked1.CurrentQuantity += 10m;
        await db1.SaveChangesAsync();

        tracked2.CurrentQuantity += 20m;
        await db2.SaveChangesAsync();

        using var verify = CreateContext();
        var final = await verify.Items.SingleAsync(i => i.Id == item.Id);
        Assert.Equal(120m, final.CurrentQuantity);
    }

    [Fact]
    public async Task SequentialPayments_ReceiveDistinctNumbers()
    {
        using var db = CreateContext();
        var customer = new Customer { Name = "عميل الأرقام" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        db.SaleInvoices.Add(new SaleInvoice
        {
            InvoiceNumber = "S-NUM-1",
            CustomerId = customer.Id,
            InvoiceDate = DateTime.Today,
            PaymentTerms = InvoicePaymentTerms.OnReceipt,
            DueDate = DateTime.Today,
            TotalAmount = 1000m,
            NetAmount = 1000m,
            PaidAmount = 0m,
            IsPaid = false
        });
        await db.SaveChangesAsync();

        var svc = new PaymentService(db);

        var (ok1, _, p1) = await svc.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = customer.Id,
            Amount = 100m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "tester");
        Assert.True(ok1);

        var (ok2, _, p2) = await svc.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = customer.Id,
            Amount = 200m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "tester");
        Assert.True(ok2);
        Assert.NotNull(p1);
        Assert.NotNull(p2);
        Assert.NotEqual(p1.ReceiptNumber, p2.ReceiptNumber);
    }
}