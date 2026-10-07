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

    private static void SeedChartOfAccounts(AppDbContext db)
    {
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
    }

    private static async Task MarkDeliveredAsync(AppDbContext db, int invoiceId, int customerId)
    {
        db.DeliveryOrders.Add(new DeliveryOrder
        {
            DeliveryNumber = $"DLV-{invoiceId}",
            SaleInvoiceId = invoiceId,
            CustomerId = customerId,
            DeliveryDate = DateTime.Today,
            Status = DeliveryOrderStatus.Delivered,
            DeliveredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task DuplicateReceipt_WithinTwoMinuteWindow_IsRejected()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
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
        await MarkDeliveredAsync(db, (await db.SaleInvoices.SingleAsync()).Id, customer.Id);

        var svc = new PaymentService(db, new AccountingService(db));

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

    /// <summary>
    /// كان هذا الاختبار يسمّي نفسه <c>StaleContext_WritesSuccessful_OnSqlite</c> ويثبت أن
    /// الكتابة القديمة تُحفظ. هذا ليس وصفًا لسلوك SQLite فحسب، بل محمّل كأنه مقبول - والحقيقة
    /// أن لـ<code>[Timestamp]</code> عقدًا واحدًا: كتابة قديمة <b>تُرفض</b>. SQLite لا يولّد
    /// <c>rowversion</c> ولا يفحصه، فالعقد غير قابل للإثبات على هذا المزوّد. الاختبار الجديد
    /// يثبت الحدّ بدل أن يثبِّت العيب، ويثبت أيضًا أن هناك اختبارًا آخر يغطّي العقد فعلًا
    /// <see cref="TheRealStaleWriteAssertion_IsNotRemoved"/> في {@link MigrationChainSqlServerTests}.
    /// </summary>
    [Fact]
    public async Task StaleWrite_IsNotRejected_OnSqlite_SoThisProviderCannotProveTheConcurrencyContract()
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

        // الفجوة بأعينها: لا رمز نسخ على السطر، فليس هناك ما يُقارَن به.
        Assert.Null(tracked1.RowVersion);
        Assert.Null(tracked2.RowVersion);

        tracked1.CurrentQuantity += 10m;
        await db1.SaveChangesAsync();

        tracked2.CurrentQuantity += 20m;
        await db2.SaveChangesAsync();

        // النتيجة: الكتابة القديمة طمست الكتابة الأحدث بلا أي ملاحظة. هذا سلوك SQLite
        // الموثَّق هنا عمدًا - لو اختفى هذا السطر فقد عاد الاختبار إلى الادّعاء بأن الضياع
        // مقبول، وهذا عكس المقصود.
        using var verify = CreateContext();
        var final = await verify.Items.SingleAsync(i => i.Id == item.Id);
        Assert.Equal(120m, final.CurrentQuantity);
    }

    /// <summary>
    /// يمنع أن يتحوّل «العقد غير قابل للإثبات هنا» إلى ثقب صامت: إن حُذف اختبار المحرّك
    /// الحقيقي أو تحوّل إلى <c>void</c> لا <c>Task</c>، يفشل هذا الاختبار.
    /// </summary>
    [Fact]
    public void TheRealStaleWriteAssertion_IsNotRemoved()
    {
        var method = typeof(MigrationChainSqlServerTests)
            .GetMethod(nameof(MigrationChainSqlServerTests.RowVersion_IsGeneratedByTheEngine_AndAStaleWriteIsRejected));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
        Assert.Contains(method.GetCustomAttributes(inherit: true), a => a is SqlServerFactAttribute);
    }

    [Fact]
    public async Task SequentialPayments_ReceiveDistinctNumbers()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
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
        await MarkDeliveredAsync(db, (await db.SaleInvoices.SingleAsync()).Id, customer.Id);

        var svc = new PaymentService(db, new AccountingService(db));

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

    [Fact]
    public async Task SaleOrder_CannotBeInvoicedTwice()
    {
        using var db = CreateContext();
        var unit = new Unit { Name = "قطعة فوترة" };
        var item = new Item
        {
            Name = "صنف فوترة",
            Category = new ItemCategory { Name = "تصنيف فوترة" },
            ItemType = new ItemType { Name = "نوع فوترة" },
            CountUnit = unit,
            QuantityUnit = unit,
            CurrentCount = 50m,
            CurrentQuantity = 50m
        };
        var customer = new Customer { Name = "عميل الفوترة" };
        db.Items.Add(item);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var orders = new SalesOrdersService(db, new InventoryService(db, new AccountingService(db)));
        var order = new SalesOrder { CustomerId = customer.Id, OrderDate = DateTime.Today };
        var (okCreate, _) = await orders.CreateOrderAsync(order, new List<SalesOrderItem>
        {
            new() { ItemId = item.Id, Quantity = 5, Count = 5, UnitPrice = 60 }
        }, "tester");
        Assert.True(okCreate);
        var (okApprove, _) = await orders.ApproveOrderAsync(order.Id);
        Assert.True(okApprove);

        var (ok1, err1) = await orders.CreateInvoiceFromOrderAsync(order.Id, "tester");
        Assert.True(ok1, err1);
        Assert.Single(await db.SaleInvoices.ToListAsync());
        var invoice = await db.SaleInvoices.SingleAsync();
        Assert.Equal(order.Id, invoice.SalesOrderId);
        Assert.Equal(SalesOrderStatus.Invoiced, (await db.SalesOrders.FindAsync(order.Id))!.Status);

        using var verify = CreateContext();
        var again = new SalesOrdersService(verify, new InventoryService(verify, new AccountingService(verify)));
        var (ok2, err2) = await again.CreateInvoiceFromOrderAsync(order.Id, "tester");
        Assert.False(ok2);
        Assert.Contains("بالفعل", err2);
        Assert.Single(await verify.SaleInvoices.ToListAsync());
        Assert.Equal(5m, (await verify.SalesOrderItems.SingleAsync()).InvoicedQty);
    }

    [Fact]
    public async Task PurchaseOrder_CannotBeInvoicedTwice()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var unit = new Unit { Name = "قطعة فاتورة شراء" };
        var item = new Item
        {
            Name = "صنف فاتورة شراء",
            Category = new ItemCategory { Name = "تصنيف فاتورة شراء" },
            ItemType = new ItemType { Name = "نوع فاتورة شراء" },
            CountUnit = unit,
            QuantityUnit = unit,
            CurrentCount = 0m,
            CurrentQuantity = 0m
        };
        var supplier = new Supplier { Name = "مورد الفوترة" };
        db.Items.Add(item);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var proc = new ProcurementService(db, new InventoryService(db, new AccountingService(db)));
        var order = new PurchaseOrder { SupplierId = supplier.Id, OrderDate = DateTime.Today };
        var (okCreate, _) = await proc.CreateOrderAsync(order, new List<PurchaseOrderItem>
        {
            new() { ItemId = item.Id, Quantity = 8, Count = 8, UnitPrice = 40 }
        }, "tester");
        Assert.True(okCreate);
        var (okApprove, _) = await proc.ApproveOrderAsync(order.Id);
        Assert.True(okApprove);
        var orderItem = await db.PurchaseOrderItems.SingleAsync();
        var (okReceive, _) = await proc.ReceiveOrderLineAsync(order.Id, orderItem.Id, 8, 8);
        Assert.True(okReceive);

        var (ok1, err1) = await proc.CreateInvoiceFromOrderAsync(order.Id, "tester");
        Assert.True(ok1, err1);
        Assert.Single(await db.PurchaseInvoices.ToListAsync());
        var invoice = await db.PurchaseInvoices.SingleAsync();
        Assert.Equal(order.Id, invoice.PurchaseOrderId);

        var (ok2, err2) = await proc.CreateInvoiceFromOrderAsync(order.Id, "tester");
        Assert.False(ok2);
        Assert.Contains("مرة", err2);
        Assert.Single(await db.PurchaseInvoices.ToListAsync());
    }

    [Fact]
    public async Task SaleInvoice_MultipleInvoicesPerSalesOrder_AreAllowed()
    {
        using var db = CreateContext();
        var customer = new Customer { Name = "عميل الفواتير الجزئية" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var order = new SalesOrder { CustomerId = customer.Id, OrderDate = DateTime.Today, Status = SalesOrderStatus.Approved };
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync();

        db.SaleInvoices.Add(new SaleInvoice
        {
            InvoiceNumber = "SI-PART-1",
            CustomerId = customer.Id,
            InvoiceDate = DateTime.Today,
            PaymentTerms = InvoicePaymentTerms.OpenTerm,
            TotalAmount = 100,
            NetAmount = 100,
            PaidAmount = 0,
            IsPaid = false,
            SalesOrderId = order.Id
        });
        await db.SaveChangesAsync();

        using var second = CreateContext();
        second.SaleInvoices.Add(new SaleInvoice
        {
            InvoiceNumber = "SI-PART-2",
            CustomerId = customer.Id,
            InvoiceDate = DateTime.Today,
            PaymentTerms = InvoicePaymentTerms.OpenTerm,
            TotalAmount = 50,
            NetAmount = 50,
            PaidAmount = 0,
            IsPaid = false,
            SalesOrderId = order.Id
        });
        await second.SaveChangesAsync();

        // Partial invoicing: the same sales order may carry several invoices (one per
        // delivered batch), so the former unique index is intentionally gone. The guard
        // against double invoicing now lives on the delivery issue, not on the order.
        Assert.Equal(2, await db.SaleInvoices.CountAsync(s => s.SalesOrderId == order.Id));
    }

    [Fact]
    public async Task PurchaseInvoice_UniquePurchaseOrderIndex_RejectsDuplicateDirectInsert()
    {
        using var db = CreateContext();
        var supplier = new Supplier { Name = "مورد الفهرس الفريد" };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var order = new PurchaseOrder { SupplierId = supplier.Id, OrderDate = DateTime.Today, Status = PurchaseOrderStatus.Approved };
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync();

        db.PurchaseInvoices.Add(new PurchaseInvoice
        {
            InvoiceNumber = "PO-UNIQ-1",
            SupplierId = supplier.Id,
            InvoiceDate = DateTime.Today,
            TotalAmount = 100,
            NetAmount = 100,
            PaidAmount = 0,
            IsPaid = false,
            PurchaseOrderId = order.Id
        });
        await db.SaveChangesAsync();

        using var second = CreateContext();
        second.PurchaseInvoices.Add(new PurchaseInvoice
        {
            InvoiceNumber = "PO-UNIQ-2",
            SupplierId = supplier.Id,
            InvoiceDate = DateTime.Today,
            TotalAmount = 100,
            NetAmount = 100,
            PaidAmount = 0,
            IsPaid = false,
            PurchaseOrderId = order.Id
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        Assert.Single(await db.PurchaseInvoices.Where(p => p.PurchaseOrderId == order.Id).ToListAsync());
    }
}
