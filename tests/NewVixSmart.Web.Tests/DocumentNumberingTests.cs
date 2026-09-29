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

public sealed class DocumentNumberingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public DocumentNumberingTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static string TodayPrefix() => DateTime.Now.ToString("yyyyMMdd");

    [Fact]
    public async Task Payment_Numbering_Advances_Past_Committed_Max_Not_Count()
    {
        using var db = CreateContext();
        var customer = new Customer { Name = "عميل ترقيم المدفوعات" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        db.Payments.AddRange(
            new Payment { ReceiptNumber = "PAY-00001", Type = PaymentType.Receipt, CustomerId = customer.Id, Amount = 10m, Method = PaymentMethod.Cash },
            new Payment { ReceiptNumber = "PAY-00005", Type = PaymentType.Receipt, CustomerId = customer.Id, Amount = 10m, Method = PaymentMethod.Cash });
        await db.SaveChangesAsync();

        var invoice = new SaleInvoice
        {
            InvoiceNumber = "SI-NUM-100",
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
            DeliveryNumber = $"DLV-NUM-{invoice.Id}",
            SaleInvoiceId = invoice.Id,
            CustomerId = customer.Id,
            DeliveryDate = DateTime.Today,
            Status = DeliveryOrderStatus.Delivered
        });
        await db.SaveChangesAsync();

        var svc = new PaymentService(db);
        var (ok, error, payment) = await svc.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = customer.Id,
            Amount = 100m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "tester");

        Assert.True(ok, error);
        Assert.NotNull(payment);
        Assert.Equal("PAY-00006", payment.ReceiptNumber);
    }

    [Fact]
    public async Task Payment_Sequential_Creates_Use_Strictly_Increasing_Numbers()
    {
        using var db = CreateContext();
        var customer = new Customer { Name = "عميل التسلسل" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var invoice = new SaleInvoice
        {
            InvoiceNumber = "SI-SEQ-200",
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
            DeliveryNumber = $"DLV-SEQ-{invoice.Id}",
            SaleInvoiceId = invoice.Id,
            CustomerId = customer.Id,
            DeliveryDate = DateTime.Today,
            Status = DeliveryOrderStatus.Delivered
        });
        await db.SaveChangesAsync();

        var svc = new PaymentService(db);
        var (ok1, e1, p1) = await svc.CreatePaymentAsync(new Payment { Type = PaymentType.Receipt, CustomerId = customer.Id, Amount = 100m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today }, "tester");
        var (ok2, e2, p2) = await svc.CreatePaymentAsync(new Payment { Type = PaymentType.Receipt, CustomerId = customer.Id, Amount = 200m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today }, "tester");
        Assert.True(ok1 && ok2, $"{e1} | {e2}");
        Assert.NotNull(p1);
        Assert.NotNull(p2);
        Assert.NotEqual(p1.ReceiptNumber, p2.ReceiptNumber);
        Assert.True(p2.ReceiptNumber.CompareTo(p1.ReceiptNumber) > 0, "أرقام الإيصالات يجب أن تتزايد");
    }

    [Fact]
    public async Task SalesOrder_Numbering_Advances_Past_Committed_Max()
    {
        using var db = CreateContext();
        var customer = new Customer { Name = "عميل أوامر البيع" };
        db.Customers.Add(customer);
        var unit = new Unit { Name = "قطعة أوامر" };
        var item = new Item
        {
            Name = "صنف أوامر البيع",
            Category = new ItemCategory { Name = "تصنيف أوامر" },
            ItemType = new ItemType { Name = "نوع أوامر" },
            CountUnit = unit,
            QuantityUnit = unit,
            IsActive = true
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        db.SalesOrders.Add(new SalesOrder
        {
            OrderNumber = $"SO-{TodayPrefix()}-005",
            CustomerId = customer.Id
        });
        await db.SaveChangesAsync();

        var inventory = new InventoryService(db, new AccountingService(db));
        var svc = new SalesOrdersService(db, inventory);
        var order = new SalesOrder { CustomerId = customer.Id };
        var (ok, error) = await svc.CreateOrderAsync(order, new List<SalesOrderItem>
        {
            new SalesOrderItem { ItemId = item.Id, Quantity = 1, Count = 1, UnitPrice = 100m }
        }, "tester");

        Assert.True(ok, error);
        Assert.Equal($"SO-{TodayPrefix()}-006", order.OrderNumber);
    }

    [Fact]
    public async Task PurchaseOrder_Numbering_Advances_Past_Committed_Max()
    {
        using var db = CreateContext();
        var supplier = new Supplier { Name = "مورد أوامر الشراء" };
        db.Suppliers.Add(supplier);
        var unit = new Unit { Name = "قطعة مشتريات" };
        var item = new Item
        {
            Name = "صنف أوامر الشراء",
            Category = new ItemCategory { Name = "تصنيف مشتريات" },
            ItemType = new ItemType { Name = "نوع مشتريات" },
            CountUnit = unit,
            QuantityUnit = unit,
            IsActive = true
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        db.PurchaseOrders.Add(new PurchaseOrder
        {
            OrderNumber = $"PRC-{TodayPrefix()}-005",
            SupplierId = supplier.Id
        });
        await db.SaveChangesAsync();

        var inventory = new InventoryService(db, new AccountingService(db));
        var svc = new ProcurementService(db, inventory);
        var order = new PurchaseOrder { SupplierId = supplier.Id };
        var (ok, error) = await svc.CreateOrderAsync(order, new List<PurchaseOrderItem>
        {
            new PurchaseOrderItem { ItemId = item.Id, Quantity = 1, Count = 1, UnitPrice = 100m }
        }, "tester");

        Assert.True(ok, error);
        Assert.Equal($"PRC-{TodayPrefix()}-006", order.OrderNumber);
    }
}
