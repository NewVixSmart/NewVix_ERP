using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class PublicIdTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public PublicIdTests()
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
    public async Task New_Documents_Get_Distinct_NonEmpty_PublicIds()
    {
        using var db = CreateContext();
        var customer = new Customer { Name = "عميل المعرّفات العامة" };
        db.Customers.Add(customer);
        var itemType = new ItemType { Name = "نوع عام" };
        var category = new ItemCategory { Name = "تصنيف عام" };
        db.ItemTypes.Add(itemType);
        db.ItemCategories.Add(category);
        await db.SaveChangesAsync();

        db.Payments.AddRange(
            new Payment { ReceiptNumber = "PAY-10001", Type = PaymentType.Receipt, CustomerId = customer.Id, Amount = 10m, Method = PaymentMethod.Cash },
            new Payment { ReceiptNumber = "PAY-10002", Type = PaymentType.Receipt, CustomerId = customer.Id, Amount = 20m, Method = PaymentMethod.Cash });
        db.SaleInvoices.AddRange(
            new SaleInvoice
            {
                InvoiceNumber = "SI-10001",
                CustomerId = customer.Id,
                InvoiceDate = DateTime.Today,
                TotalAmount = 1000m,
                NetAmount = 1000m,
                PaidAmount = 0m,
                IsPaid = false
            },
            new SaleInvoice
            {
                InvoiceNumber = "SI-10002",
                CustomerId = customer.Id,
                InvoiceDate = DateTime.Today,
                TotalAmount = 2000m,
                NetAmount = 2000m,
                PaidAmount = 0m,
                IsPaid = false
            });
        db.Items.Add(new Item { Name = "صنف معرّف عام", Code = "ITM-10001", ItemTypeId = itemType.Id, CategoryId = category.Id });
        await db.SaveChangesAsync();

        var paymentIds = await db.Payments.Select(p => p.PublicId).ToListAsync();
        var invoiceIds = await db.SaleInvoices.Select(s => s.PublicId).ToListAsync();
        var itemId = await db.Items.Select(i => i.PublicId).SingleAsync();

        Assert.All(paymentIds, id => Assert.NotEqual(Guid.Empty, id));
        Assert.All(invoiceIds, id => Assert.NotEqual(Guid.Empty, id));
        Assert.NotEqual(Guid.Empty, itemId);
        Assert.Equal(2, paymentIds.Distinct().Count());
        Assert.Equal(2, invoiceIds.Distinct().Count());
    }

    [Fact]
    public async Task Duplicate_PublicId_Violates_Unique_Index()
    {
        using var db = CreateContext();
        var customer = new Customer { Name = "عميل الفهرس الفريد" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var same = Guid.NewGuid();
        db.SaleInvoices.AddRange(
            new SaleInvoice
            {
                InvoiceNumber = "SI-20001",
                CustomerId = customer.Id,
                InvoiceDate = DateTime.Today,
                TotalAmount = 1m,
                NetAmount = 1m,
                PaidAmount = 0m,
                IsPaid = false,
                PublicId = same
            },
            new SaleInvoice
            {
                InvoiceNumber = "SI-20002",
                CustomerId = customer.Id,
                InvoiceDate = DateTime.Today,
                TotalAmount = 2m,
                NetAmount = 2m,
                PaidAmount = 0m,
                IsPaid = false,
                PublicId = same
            });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
