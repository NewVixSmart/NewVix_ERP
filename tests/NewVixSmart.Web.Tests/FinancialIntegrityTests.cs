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
            ("5102", "مرتجعات المشتريات", GLAccountType.Expense, NormalBalance.Debit),
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.SaveChanges();
    }

    private static decimal DebitOf(JournalEntry entry, string code)
        => entry.Lines.Where(l => l.Account!.Code == code).Sum(l => l.Debit);

    private static decimal CreditOf(JournalEntry entry, string code)
        => entry.Lines.Where(l => l.Account!.Code == code).Sum(l => l.Credit);

    private static void AssertBalanced(JournalEntry entry)
    {
        Assert.Equal(decimal.Round(entry.Lines.Sum(l => l.Debit), 2), decimal.Round(entry.Lines.Sum(l => l.Credit), 2));
    }

    private static async Task<(bool Ok, string? Error)> DeliverAsync(AppDbContext db, SaleInvoice invoice, int itemId, decimal quantity)
    {
        var svc = new InventoryService(db, new AccountingService(db));
        var delivery = new DeliveryOrder { SaleInvoiceId = invoice.Id, DeliveryDate = DateTime.Today };
        var (ok, err) = await svc.CreateDeliveryOrderAsync(delivery,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = quantity, Count = 0 } }, "test");
        if (!ok) return (false, err);
        return await svc.DeliverDeliveryOrderAsync(delivery.Id, "test");
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
    public async Task Aging_PartialDelivery_OutstandingMatchesArControl()
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
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 1 } }, "test");
        Assert.True(dOk, dErr);
        var (dlvOk, dlvErr) = await inventory.DeliverDeliveryOrderAsync(delivery.Id, "test");
        Assert.True(dlvOk, dlvErr);

        decimal arControl = await db.JournalEntryLines
            .Include(l => l.Account)
            .Where(l => l.Account!.Code == "1200")
            .SumAsync(l => l.Debit - l.Credit);
        Assert.Equal(95m, arControl);

        var svc = new ReportService(db, new FinancialReportService(db));
        var vm = await svc.AgingAsync();
        var row = Assert.Single(vm.Receivables);
        Assert.Equal(95m, row.Total);
        Assert.Equal(95m, vm.ArTotal);
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

    private static async Task<(int itemId, int supplierId)> PurchaseReturnSeedAsync(AppDbContext db)
    {
        var unit = new Unit { Name = "قطعة مرتجع شراء" };
        var item = new Item
        {
            Name = "صنف مرتجع شراء",
            Category = new ItemCategory { Name = "تصنيف مرتجع شراء" },
            ItemType = new ItemType { Name = "نوع مرتجع شراء" },
            CountUnit = unit,
            QuantityUnit = unit,
            PurchasePrice = 50,
            SalePrice = 90,
            CurrentCount = 20m,
            CurrentQuantity = 20m
        };
        var supplier = new Supplier { Name = "مورد مرتجع" };
        db.Items.Add(item);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return (item.Id, supplier.Id);
    }

    // Invoice booked (delivery) as Dr 1200 net / Cr 4000 (net - tax) / Cr 2055 tax.
    // Invoice: 2 x 100 = 200 gross, header discount 20, tax 10 -> net 190.
    // Returning 1 of 2 units is f = 100/200 = 0.5, so the exact mirror is
    //   Cr 1200 = 0.5 * 190 = 95, Dr 5101 = 0.5 * (190 - 10) = 90, Dr 2055 = 0.5 * 10 = 5.
    // The old behaviour credited AR with the raw returned gross (100) and debited the contra
    // revenue with 95, so the 20 header discount was never reversed.
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
        Assert.Equal(90m, DebitOf(entry, "5101"));
        Assert.Equal(5m, DebitOf(entry, "2055"));
        Assert.Equal(95m, CreditOf(entry, "1200"));
        Assert.Equal(40m, DebitOf(entry, "1300"));
        Assert.Equal(40m, CreditOf(entry, "5000"));
        Assert.Equal(0m, CreditOf(entry, "5101"));
        Assert.Equal(0m, DebitOf(entry, "1200"));
        AssertBalanced(entry);

        // The reversal is the exact inverse of the delivery booking, leaving the undelivered
        // half of the invoice on the ledger: AR 95, revenue 90, tax 5.
        var delivered = await db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account)
            .SingleAsync(e => e.Source == JournalSource.SaleDeliveryOrder);
        Assert.Equal(95m, DebitOf(delivered, "1200") - CreditOf(entry, "1200"));
        Assert.Equal(90m, CreditOf(delivered, "4000") - DebitOf(entry, "5101"));
        Assert.Equal(5m, CreditOf(delivered, "2055") - DebitOf(entry, "2055"));
    }

    [Fact]
    public async Task SaleReturn_InvoiceWithoutDiscountOrTax_PostsUnchangedGrossAmount()
    {
        using var db = CreateContext();
        var (itemId, customerId) = await SeedItemAndCustomerAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        // 2 x 100 = 200, no header discount, no tax -> net 200. Proration must be a no-op.
        var invoice = new SaleInvoice { CustomerId = customerId, InvoiceDate = DateTime.Today, PaymentTerms = InvoicePaymentTerms.Net30 };
        var (ok, err) = await inventory.CreateSaleAsync(invoice, new List<SaleInvoiceItem>
        {
            new() { ItemId = itemId, Quantity = 2, UnitPrice = 100 }
        }, "test");
        Assert.True(ok, err);

        var (dlvOk, dlvErr) = await DeliverAsync(db, invoice, itemId, 2);
        Assert.True(dlvOk, dlvErr);

        var saleReturn = new SaleReturn { SaleInvoiceId = invoice.Id, CustomerId = customerId, ReturnDate = DateTime.Today };
        var (rOk, rErr) = await inventory.CreateSaleReturnAsync(saleReturn,
            new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 1, UnitPrice = 100 } }, "test");
        Assert.True(rOk, rErr);

        var saved = await db.SaleReturns.Include(r => r.Items).SingleAsync();
        Assert.Equal(100m, saved.TotalAmount);

        var entry = await db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account)
            .SingleAsync(e => e.Source == JournalSource.SaleReturn);
        Assert.Equal(100m, DebitOf(entry, "5101"));
        Assert.Equal(100m, CreditOf(entry, "1200"));
        Assert.Equal(40m, DebitOf(entry, "1300"));
        Assert.Equal(40m, CreditOf(entry, "5000"));
        Assert.DoesNotContain(entry.Lines, l => l.Account!.Code == "2055");
        AssertBalanced(entry);
    }

    [Fact]
    public async Task SaleReturn_PartialQuantity_ProratesHeaderDiscountAndTax()
    {
        using var db = CreateContext();
        var (itemId, customerId) = await SeedItemAndCustomerAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        // 3 x 99.99 = 299.97 gross, header discount 0.01, tax 7.47 -> net 307.43.
        // Returning 1 of 3 units is f = 99.99 / 299.97 = 1/3, so:
        //   net    = 307.43 / 3 = 102.4766... -> 102.48
        //   tax    =   7.47 / 3 =   2.49
        //   contra = 102.48 - 2.49 = 99.99 and the AR leg absorbs the 0.01 rounding drift.
        var invoice = new SaleInvoice { CustomerId = customerId, InvoiceDate = DateTime.Today, PaymentTerms = InvoicePaymentTerms.Net30 };
        var (ok, err) = await inventory.CreateSaleAsync(invoice, new List<SaleInvoiceItem>
        {
            new() { ItemId = itemId, Quantity = 3, UnitPrice = 99.99m }
        }, "test");
        Assert.True(ok, err);
        var created = await db.SaleInvoices.SingleAsync();
        created.Discount = 0.01m;
        created.Tax = 7.47m;
        created.NetAmount = 307.43m;
        await db.SaveChangesAsync();

        var (dlvOk, dlvErr) = await DeliverAsync(db, invoice, itemId, 3);
        Assert.True(dlvOk, dlvErr);

        var saleReturn = new SaleReturn { SaleInvoiceId = invoice.Id, CustomerId = customerId, ReturnDate = DateTime.Today };
        var (rOk, rErr) = await inventory.CreateSaleReturnAsync(saleReturn,
            new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 1 } }, "test");
        Assert.True(rOk, rErr);

        var entry = await db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account)
            .SingleAsync(e => e.Source == JournalSource.SaleReturn);
        Assert.Equal(99.99m, DebitOf(entry, "5101"));
        Assert.Equal(2.49m, DebitOf(entry, "2055"));
        Assert.Equal(102.48m, CreditOf(entry, "1200"));
        Assert.Equal(40m, DebitOf(entry, "1300"));
        Assert.Equal(40m, CreditOf(entry, "5000"));
        AssertBalanced(entry);

        var delivered = await db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account)
            .SingleAsync(e => e.Source == JournalSource.SaleDeliveryOrder);
        Assert.Equal(307.43m, DebitOf(delivered, "1200"));
        Assert.Equal(299.96m, CreditOf(delivered, "4000"));
        Assert.Equal(7.47m, CreditOf(delivered, "2055"));
    }

    [Fact]
    public async Task PurchaseReturn_InvoiceWithHeaderDiscount_ReversesNetProportionally()
    {
        using var db = CreateContext();
        var (itemId, supplierId) = await PurchaseReturnSeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        // 2 x 50 = 100 gross, header discount 20, tax 10 -> net 90, booked Dr 1300 90 / Cr 2000 90.
        // Returning 1 of 2 units is f = 50 / 100 = 0.5, so the supplier credit is 0.5 * 90 = 45.
        var invoice = new PurchaseInvoice
        {
            SupplierId = supplierId,
            InvoiceDate = DateTime.Today,
            PaymentTerms = InvoicePaymentTerms.Net30,
            Discount = 20m,
            Tax = 10m
        };
        var (ok, err) = await inventory.CreatePurchaseAsync(invoice, new List<PurchaseInvoiceItem>
        {
            new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 50 }
        }, "test");
        Assert.True(ok, err);
        Assert.Equal(100m, invoice.TotalAmount);
        Assert.Equal(90m, invoice.NetAmount);

        var purchaseReturn = new PurchaseReturn
        {
            PurchaseInvoiceId = invoice.Id,
            SupplierId = supplierId,
            ReturnDate = DateTime.Today
        };
        var (rOk, rErr) = await inventory.CreatePurchaseReturnAsync(purchaseReturn,
            new List<PurchaseReturnItem> { new() { ItemId = itemId, Quantity = 1, Count = 0 } }, "test");
        Assert.True(rOk, rErr);

        var entry = await db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account)
            .SingleAsync(e => e.Source == JournalSource.PurchaseReturn);
        // The purchase invoice is booked without a separate tax leg, so the whole 0.5 * net = 45
        // relieves the payable and contra-purchases. The old raw-gross behaviour released 50
        // and never reversed the 20 header discount.
        Assert.Equal(45m, DebitOf(entry, "2000"));
        Assert.Equal(45m, CreditOf(entry, "5102"));
        Assert.Equal(50m, CreditOf(entry, "1300"));
        Assert.Equal(50m, DebitOf(entry, "5000"));
        AssertBalanced(entry);

        var booked = await db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account)
            .SingleAsync(e => e.Source == JournalSource.PurchaseInvoice);
        Assert.Equal(90m, CreditOf(booked, "2000"));
        Assert.Equal(45m, CreditOf(booked, "2000") - DebitOf(entry, "2000"));
    }

    [Fact]
    public async Task PurchaseReturn_InvoiceWithoutDiscountOrTax_PostsUnchangedGrossAmount()
    {
        using var db = CreateContext();
        var (itemId, supplierId) = await PurchaseReturnSeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        // 2 x 50 = 100, no header discount, no tax -> net 100. Proration must be a no-op.
        var invoice = new PurchaseInvoice
        {
            SupplierId = supplierId,
            InvoiceDate = DateTime.Today,
            PaymentTerms = InvoicePaymentTerms.Net30
        };
        var (ok, err) = await inventory.CreatePurchaseAsync(invoice, new List<PurchaseInvoiceItem>
        {
            new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 50 }
        }, "test");
        Assert.True(ok, err);
        Assert.Equal(100m, invoice.NetAmount);

        var purchaseReturn = new PurchaseReturn
        {
            PurchaseInvoiceId = invoice.Id,
            SupplierId = supplierId,
            ReturnDate = DateTime.Today
        };
        var (rOk, rErr) = await inventory.CreatePurchaseReturnAsync(purchaseReturn,
            new List<PurchaseReturnItem> { new() { ItemId = itemId, Quantity = 1, Count = 0 } }, "test");
        Assert.True(rOk, rErr);

        var saved = await db.PurchaseReturns.Include(r => r.Items).SingleAsync();
        Assert.Equal(50m, saved.TotalAmount);

        var entry = await db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account)
            .SingleAsync(e => e.Source == JournalSource.PurchaseReturn);
        Assert.Equal(50m, DebitOf(entry, "2000"));
        Assert.Equal(50m, CreditOf(entry, "5102"));
        Assert.Equal(50m, CreditOf(entry, "1300"));
        Assert.Equal(50m, DebitOf(entry, "5000"));
        AssertBalanced(entry);
    }

    [Fact]
    public async Task PurchaseReturn_PartialQuantity_ProratesHeaderDiscount()
    {
        using var db = CreateContext();
        var (itemId, supplierId) = await PurchaseReturnSeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        // 3 x 33.33 = 99.99 gross, header discount 0.03, no tax -> net 99.96.
        // Returning 1 of 3 units is f = 33.33 / 99.99 = 1/3, so the supplier credit is
        // 99.96 / 3 = 33.32 — the old raw-gross behaviour released 33.33, one piastre too much.
        var invoice = new PurchaseInvoice
        {
            SupplierId = supplierId,
            InvoiceDate = DateTime.Today,
            PaymentTerms = InvoicePaymentTerms.Net30,
            Discount = 0.03m
        };
        var (ok, err) = await inventory.CreatePurchaseAsync(invoice, new List<PurchaseInvoiceItem>
        {
            new() { ItemId = itemId, Quantity = 3, Count = 0, UnitPrice = 33.33m }
        }, "test");
        Assert.True(ok, err);
        Assert.Equal(99.99m, invoice.TotalAmount);
        Assert.Equal(99.96m, invoice.NetAmount);

        var purchaseReturn = new PurchaseReturn
        {
            PurchaseInvoiceId = invoice.Id,
            SupplierId = supplierId,
            ReturnDate = DateTime.Today
        };
        var (rOk, rErr) = await inventory.CreatePurchaseReturnAsync(purchaseReturn,
            new List<PurchaseReturnItem> { new() { ItemId = itemId, Quantity = 1, Count = 0 } }, "test");
        Assert.True(rOk, rErr);

        var entry = await db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account)
            .SingleAsync(e => e.Source == JournalSource.PurchaseReturn);
        Assert.Equal(33.32m, DebitOf(entry, "2000"));
        Assert.Equal(33.32m, CreditOf(entry, "5102"));
        Assert.Equal(33.33m, CreditOf(entry, "1300"));
        Assert.Equal(33.33m, DebitOf(entry, "5000"));
        AssertBalanced(entry);
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