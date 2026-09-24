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

public sealed class FinancialIntegrityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public FinancialIntegrityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
        SeedChartOfAccounts(db);
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static void SeedChartOfAccounts(AppDbContext db)
    {
        (string Code, string Name, GLAccountType Type, NormalBalance Normal)[] accounts =
        {
            ("1000", "النقد", GLAccountType.Asset, NormalBalance.Debit),
            ("1200", "المدينون", GLAccountType.Asset, NormalBalance.Debit),
            ("1300", "المخزون", GLAccountType.Asset, NormalBalance.Debit),
            ("2000", "الدائنون", GLAccountType.Liability, NormalBalance.Credit),
            ("2055", "الضريبة مستحقة", GLAccountType.Liability, NormalBalance.Credit),
            ("3000", "رأس المال", GLAccountType.Equity, NormalBalance.Credit),
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
            ("5101", "مرتجعات البيع", GLAccountType.Revenue, NormalBalance.Credit),
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.SaveChanges();
    }

    private static async Task<(int itemId, int customerId)> SeedItemAndCustomerAsync(AppDbContext db)
    {
        var unit = new Unit { Name = "قطعة" };
        var item = new Item
        {
            Name = "صنف مالي",
            Category = new ItemCategory { Name = "تصنيف مالي" },
            ItemType = new ItemType { Name = "نوع مالي" },
            CountUnit = unit,
            QuantityUnit = unit,
            PurchasePrice = 40,
            SalePrice = 100,
            CurrentCount = 20m,
            CurrentQuantity = 20m
        };
        var customer = new Customer { Name = "عميل مالي" };
        db.Items.Add(item);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return (item.Id, customer.Id);
    }

    [Fact]
    public async Task Delivery_Posts_ApportionedNet_NotGross_Revenue()
    {
        using var db = CreateContext();
        var (itemId, customerId) = await SeedItemAndCustomerAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        var invoice = new SaleInvoice { CustomerId = customerId, InvoiceDate = DateTime.Today, PaymentTerms = InvoicePaymentTerms.Net30 };
        var (ok, err) = await inventory.CreateSaleAsync(invoice, new List<SaleInvoiceItem>
        {
            new() { ItemId = itemId, Quantity = 2, UnitPrice = 100 }
        }, "test");
        Assert.True(ok, err);
        Assert.Equal(200m, invoice.TotalAmount);
        var created = await db.SaleInvoices.SingleAsync();
        created.Discount = 20m;
        created.Tax = 10m;
        created.NetAmount = 190m;
        await db.SaveChangesAsync();
        Assert.Equal(190m, (await db.SaleInvoices.SingleAsync()).NetAmount);

        var delivery = new DeliveryOrder { SaleInvoiceId = invoice.Id, DeliveryDate = DateTime.Today };
        var (dOk, dErr) = await inventory.CreateDeliveryOrderAsync(delivery,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 1 } }, "test");
        Assert.True(dOk, dErr);
        var (dlvOk, dlvErr) = await inventory.DeliverDeliveryOrderAsync(delivery.Id, "test");
        Assert.True(dlvOk, dlvErr);

        var entry = await db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account)
            .SingleAsync(e => e.Source == JournalSource.SaleDeliveryOrder);
        var arLine = entry.Lines.Single(l => l.Account!.Code == "1200");
        var revLine = entry.Lines.Single(l => l.Account!.Code == "4000");
        var taxLine = entry.Lines.Single(l => l.Account!.Code == "2055");
        Assert.Equal(95m, arLine.Debit);
        Assert.Equal(90m, revLine.Credit);
        Assert.Equal(5m, taxLine.Credit);
        Assert.Equal(arLine.Debit, revLine.Credit + taxLine.Credit);
    }

    [Fact]
    public async Task Receipt_OnUndeliveredInvoice_IsRejected_UntilDelivery()
    {
        using var db = CreateContext();
        var (itemId, customerId) = await SeedItemAndCustomerAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var payments = new PaymentService(db, new AccountingService(db));

        var invoice = new SaleInvoice { CustomerId = customerId, InvoiceDate = DateTime.Today, PaymentTerms = InvoicePaymentTerms.Net30 };
        var (ok, err) = await inventory.CreateSaleAsync(invoice, new List<SaleInvoiceItem>
        {
            new() { ItemId = itemId, Quantity = 2, UnitPrice = 100 }
        }, "test");
        Assert.True(ok, err);

        var (pOk, pErr, _) = await payments.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = customerId,
            Amount = 200m,
            PaymentDate = DateTime.Today,
            Method = PaymentMethod.Cash
        }, "test");
        Assert.False(pOk);
        Assert.Contains("أكبر من إجمالي المستحق", pErr);
        Assert.Equal(0m, (await db.SaleInvoices.SingleAsync()).PaidAmount);
        Assert.Empty(await db.SalePaymentAllocations.ToListAsync());
        Assert.Empty(await db.PurchasePaymentAllocations.ToListAsync());

        var delivery = new DeliveryOrder { SaleInvoiceId = invoice.Id, DeliveryDate = DateTime.Today };
        var (dOk, _) = await inventory.CreateDeliveryOrderAsync(delivery,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 2 } }, "test");
        Assert.True(dOk);
        var (dlvOk, _) = await inventory.DeliverDeliveryOrderAsync(delivery.Id, "test");
        Assert.True(dlvOk);

        var (p2Ok, p2Err, _) = await payments.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = customerId,
            Amount = 200m,
            PaymentDate = DateTime.Today,
            Method = PaymentMethod.Cash
        }, "test");
        Assert.True(p2Ok, p2Err);
        var settled = await db.SaleInvoices.SingleAsync();
        Assert.Equal(200m, settled.PaidAmount);
        Assert.True(settled.IsPaid);
    }

    [Fact]
    public async Task SequentialReceives_Accumulate_WithoutLostUpdate()
    {
        using var db = CreateContext();
        var (itemId, supplierId) = await ProcurementSeedAsync(db);
        var proc = new ProcurementService(db, new InventoryService(db));

        var order = new PurchaseOrder { SupplierId = supplierId, OrderDate = DateTime.Today };
        var (ok, _) = await proc.CreateOrderAsync(order, new List<PurchaseOrderItem>
        {
            new() { ItemId = itemId, Quantity = 20, Count = 20, UnitPrice = 40 }
        }, "test");
        Assert.True(ok);
        await proc.ApproveOrderAsync(order.Id);

        using var second = CreateContext();
        var proc2 = new ProcurementService(second, new InventoryService(second));
        var (ok1, err1) = await proc.ReceiveOrderLineAsync(order.Id, (await db.PurchaseOrderItems.SingleAsync()).Id, 7, 7);
        Assert.True(ok1, err1);
        var (ok2, err2) = await proc2.ReceiveOrderLineAsync(order.Id, (await second.PurchaseOrderItems.SingleAsync()).Id, 7, 7);
        Assert.True(ok2, err2);

        using var verify = CreateContext();
        Assert.Equal(14m, (await verify.PurchaseOrderItems.SingleAsync()).ReceivedQty);
        Assert.Equal(14m, (await verify.PurchaseOrderItems.SingleAsync()).ReceivedCount);
    }

    [Fact]
    public async Task OverReceipt_BeyondQuantityIsRejected()
    {
        using var db = CreateContext();
        var (itemId, supplierId) = await ProcurementSeedAsync(db);
        var proc = new ProcurementService(db, new InventoryService(db));

        var order = new PurchaseOrder { SupplierId = supplierId, OrderDate = DateTime.Today };
        var (ok, _) = await proc.CreateOrderAsync(order, new List<PurchaseOrderItem>
        {
            new() { ItemId = itemId, Quantity = 10, Count = 10, UnitPrice = 40 }
        }, "test");
        Assert.True(ok);
        await proc.ApproveOrderAsync(order.Id);

        var (ok1, err1) = await proc.ReceiveOrderLineAsync(order.Id, (await db.PurchaseOrderItems.SingleAsync()).Id, 5, 5);
        Assert.True(ok1, err1);
        var (ok2, err2) = await proc.ReceiveOrderLineAsync(order.Id, (await db.PurchaseOrderItems.SingleAsync()).Id, 6, 6);
        Assert.False(ok2);
        Assert.Contains("أكبر من الكمية المطلوبة", err2);
        Assert.Equal(5m, (await db.PurchaseOrderItems.SingleAsync()).ReceivedQty);
    }

    private static async Task<(int itemId, int supplierId)> ProcurementSeedAsync(AppDbContext db)
    {
        var unit = new Unit { Name = "قطعة شراء" };
        var item = new Item
        {
            Name = "صنف شراء مالي",
            Category = new ItemCategory { Name = "تصنيف شراء مالي" },
            ItemType = new ItemType { Name = "نوع شراء مالي" },
            CountUnit = unit,
            QuantityUnit = unit,
            PurchasePrice = 40,
            CurrentCount = 0m,
            CurrentQuantity = 0m
        };
        var supplier = new Supplier { Name = "مورد مالي" };
        db.Items.Add(item);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return (item.Id, supplier.Id);
    }

    [Fact]
    public async Task SaleReturn_WithTax_PostsValueAndTaxReversal()
    {
        using var db = CreateContext();
        var (itemId, customerId) = await SeedItemAndCustomerAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        var invoice = new SaleInvoice { CustomerId = customerId, InvoiceDate = DateTime.Today, PaymentTerms = InvoicePaymentTerms.Net30 };
        var (ok, err) = await inventory.CreateSaleAsync(invoice, new List<SaleInvoiceItem>
        {
            new() { ItemId = itemId, Quantity = 2, UnitPrice = 100 }
        }, "test");
        Assert.True(ok, err);
        var created = await db.SaleInvoices.SingleAsync();
        created.Discount = 20m;
        created.Tax = 10m;
        created.NetAmount = 190m;
        await db.SaveChangesAsync();

        var delivery = new DeliveryOrder { SaleInvoiceId = invoice.Id, DeliveryDate = DateTime.Today };
        var (dOk, dErr) = await inventory.CreateDeliveryOrderAsync(delivery,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 2 } }, "test");
        Assert.True(dOk, dErr);
        var (dlvOk, dlvErr) = await inventory.DeliverDeliveryOrderAsync(delivery.Id, "test");
        Assert.True(dlvOk, dlvErr);

        var saleReturn = new SaleReturn { SaleInvoiceId = invoice.Id, CustomerId = customerId, ReturnDate = DateTime.Today };
        var (rOk, rErr) = await inventory.CreateSaleReturnAsync(saleReturn,
            new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 1 } }, "test");
        Assert.True(rOk, rErr);

        var entry = await db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account)
            .SingleAsync(e => e.Source == JournalSource.SaleReturn);
        var htmlLine = entry.Lines.Single(l => l.Account!.Code == "5101");
        var taxLine = entry.Lines.Single(l => l.Account!.Code == "2055");
        var arLine = entry.Lines.Single(l => l.Account!.Code == "1200");
        var stockLine = entry.Lines.Single(l => l.Account!.Code == "1300");
        var coLine = entry.Lines.Single(l => l.Account!.Code == "5000");
        Assert.Equal(95m, htmlLine.Debit);
        Assert.Equal(5m, taxLine.Debit);
        Assert.Equal(100m, arLine.Credit);
        Assert.Equal(40m, stockLine.Debit);
        Assert.Equal(40m, coLine.Credit);
        Assert.True(entry.Lines.Sum(l => l.Debit) == entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task Purchase_PostsStockMovement_WithInvoiceDocumentId()
    {
        using var db = CreateContext();
        var (itemId, _) = await SeedItemAndCustomerAsync(db);
        var supplier = new Supplier { Name = "مورد شراء مالي" };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        var inventory = new InventoryService(db);

        var invoice = new PurchaseInvoice { SupplierId = supplier.Id, InvoiceDate = DateTime.Today };
        var (ok, err) = await inventory.CreatePurchaseAsync(invoice, new List<PurchaseInvoiceItem>
        {
            new() { ItemId = itemId, Quantity = 5, Count = 0, UnitPrice = 40 }
        }, "test");
        Assert.True(ok, err);

        var movement = await db.StockMovements.SingleAsync(m => m.DocumentType == DocumentType.PurchaseInvoice);
        Assert.Equal(invoice.Id, movement.DocumentId);
        Assert.Equal(invoice.InvoiceNumber, movement.DocumentNumber);
        Assert.Equal(25m, (await db.Items.SingleAsync(i => i.Id == itemId)).CurrentQuantity);
    }

    [Fact]
    public async Task Adjustment_Delete_WithPostedOpeningJournal_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, _) = await SeedItemAndCustomerAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        var adjustment = new InventoryAdjustment { ItemId = itemId, NewCount = 0, NewQuantity = 25, AdjustmentDate = DateTime.Today };
        var (cOk, cErr) = await inventory.CreateAdjustmentAsync(adjustment, "test");
        Assert.True(cOk, cErr);
        Assert.True(await db.JournalEntries.AnyAsync(j => j.Source == JournalSource.OpeningStock && j.SourceId == itemId));

        var (dOk, dErr) = await inventory.DeleteAdjustmentAsync(adjustment.Id, "test");
        Assert.False(dOk);
        Assert.Contains("رُحّلت إلى قيود اليومية", dErr);
        Assert.Equal(1, await db.InventoryAdjustments.CountAsync(a => a.Id == adjustment.Id));
        Assert.Equal(25m, (await db.Items.SingleAsync(i => i.Id == itemId)).CurrentQuantity);
    }
}