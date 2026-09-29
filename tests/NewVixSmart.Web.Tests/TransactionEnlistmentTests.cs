using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// يغطي العمليات التي صارت تقبل <c>beginOwnTransaction: false</c> فتضم إلى معاملة المستدعي:
/// متى تفشل الخطوة الأخيرة يجب أن تعود القاعدة كما كانت تمامًا، ومتى لا توجد معاملة قائمة
/// يجب أن ترفض العملية أن تعمل بلا حماية بدل أن تكتب نصف وحدة ذرية.
/// </summary>
public sealed class TransactionEnlistmentTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly LastStepFailureInterceptor _interceptor = new();

    public TransactionEnlistmentTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_interceptor)
            .Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    /// <summary>
    /// يفشل عمدًا لحظة حفظ جدول بعينه: ترحيل القيد آخر خطوة دائمة في الدفعة والمرتجعات
    /// والجرد، وحفظ أذن التحويل آخر خطوة فيه، وإنشاء الحجز آخر خطوة في اعتماد الأمر.
    /// </summary>
    private sealed class LastStepFailureInterceptor : SaveChangesInterceptor
    {
        public const string FailureMessage = "فشلت الخطوة الأخيرة عمدًا";
        public Type? FailWhenAdded { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (FailWhenAdded is not null && eventData.Context!.ChangeTracker.Entries()
                    .Any(e => e.State == EntityState.Added && e.Entity.GetType() == FailWhenAdded))
                throw new InvalidOperationException(FailureMessage);

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static void SeedChart(AppDbContext db)
    {
        db.GLAccounts.AddRange(
            new GLAccount { Code = "1000", Name = "النقدية", Type = GLAccountType.Asset, NormalBalance = NormalBalance.Debit, IsActive = true },
            new GLAccount { Code = "1200", Name = "ذمم العملاء", Type = GLAccountType.Asset, NormalBalance = NormalBalance.Debit, IsActive = true },
            new GLAccount { Code = "1300", Name = "المخزون", Type = GLAccountType.Asset, NormalBalance = NormalBalance.Debit, IsActive = true },
            new GLAccount { Code = "2000", Name = "الدائنون", Type = GLAccountType.Liability, NormalBalance = NormalBalance.Credit, IsActive = true },
            new GLAccount { Code = "2055", Name = "ضريبة القيمة المضافة", Type = GLAccountType.Liability, NormalBalance = NormalBalance.Credit, IsActive = true },
            new GLAccount { Code = "3000", Name = "رأس المال", Type = GLAccountType.Equity, NormalBalance = NormalBalance.Credit, IsActive = true },
            new GLAccount { Code = "4000", Name = "إيرادات المبيعات", Type = GLAccountType.Revenue, NormalBalance = NormalBalance.Credit, IsActive = true },
            new GLAccount { Code = "5000", Name = "تكلفة المبيعات", Type = GLAccountType.Expense, NormalBalance = NormalBalance.Debit, IsActive = true },
            new GLAccount { Code = "5101", Name = "مردودات المبيعات", Type = GLAccountType.Expense, NormalBalance = NormalBalance.Debit, IsActive = true },
            new GLAccount { Code = "5102", Name = "مردودات المشتريات", Type = GLAccountType.Expense, NormalBalance = NormalBalance.Debit, IsActive = true });
        db.SaveChanges();
    }

    private static async Task<(int ItemId, int CustomerId, int SupplierId, int SourceWarehouseId, int TargetWarehouseId)> SeedAsync(
        AppDbContext db)
    {
        var unit = new Unit { Name = "قطعة" };
        var category = new ItemCategory { Name = "تصنيف معاملات" };
        var itemType = new ItemType { Name = "نوع معاملات" };
        db.ItemCategories.Add(category);
        db.ItemTypes.Add(itemType);
        db.Units.Add(unit);
        db.Items.Add(new Item
        {
            Name = "صنف معاملات",
            Category = category,
            ItemType = itemType,
            CountUnit = unit,
            QuantityUnit = unit,
            PurchasePrice = 25m,
            SalePrice = 80m,
            CurrentQuantity = 100m,
            CurrentCount = 0m
        });
        db.Customers.Add(new Customer { Name = "عميل معاملات" });
        db.Suppliers.Add(new Supplier { Name = "مورد معاملات" });
        var source = new Warehouse { Code = "WH-SRC", Name = "مخزن المصدر", IsActive = true };
        var target = new Warehouse { Code = "WH-DST", Name = "مخزن الهدف", IsActive = true };
        db.Warehouses.AddRange(source, target);
        await db.SaveChangesAsync();

        db.StockLayers.Add(new StockLayer
        {
            ItemId = (await db.Items.SingleAsync()).Id,
            WarehouseId = source.Id,
            Qty = 100m,
            Count = 0m,
            UnitCost = 25m,
            CountCost = 25m,
            RemainingQty = 100m,
            RemainingCount = 0m,
            DateReceived = new DateTime(2026, 1, 1),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return ((await db.Items.SingleAsync()).Id,
            (await db.Customers.SingleAsync()).Id,
            (await db.Suppliers.SingleAsync()).Id,
            source.Id,
            target.Id);
    }

    private static async Task<int> CreateDeliveredInvoiceAsync(AppDbContext db, int customerId, decimal amount)
    {
        var invoice = new SaleInvoice
        {
            CustomerId = customerId,
            InvoiceDate = DateTime.Today,
            PaymentTerms = InvoicePaymentTerms.OpenTerm,
            TotalAmount = amount,
            NetAmount = amount,
            PaidAmount = 0m,
            IsPaid = false
        };
        db.SaleInvoices.Add(invoice);
        await db.SaveChangesAsync();
        db.DeliveryOrders.Add(new DeliveryOrder
        {
            DeliveryNumber = $"DLV-{invoice.Id}",
            SaleInvoiceId = invoice.Id,
            CustomerId = customerId,
            DeliveryDate = DateTime.Today,
            Status = DeliveryOrderStatus.Delivered,
            DeliveredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return invoice.Id;
    }

    private static async Task<int> CreateDraftOrderAsync(AppDbContext db, int customerId, int itemId, decimal quantity)
    {
        var orders = new SalesOrdersService(db, new InventoryService(db));
        var order = new SalesOrder { CustomerId = customerId, OrderDate = DateTime.Today };
        var (created, error) = await orders.CreateOrderAsync(order, new List<SalesOrderItem>
        {
            new() { ItemId = itemId, Quantity = quantity, UnitPrice = 80m }
        }, "tester");
        Assert.True(created, error);
        return order.Id;
    }

    // ---------- (1) معاملة المستدعي: فشل الخطوة الأخيرة لا يترك أثرًا ----------

    [Fact]
    public async Task Ambient_ReturnWithJournalFailure_LeavesNoOrphanRows()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        await using var tx = await db.Database.BeginTransactionAsync();
        _interceptor.FailWhenAdded = typeof(JournalEntry);

        var returned = new SaleReturn { CustomerId = seed.CustomerId, ReturnDate = DateTime.Today };
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.CreateSaleReturnAsync(
            returned,
            new List<SaleReturnItem> { new() { ItemId = seed.ItemId, Quantity = 2m, UnitPrice = 80m } },
            "tester", beginOwnTransaction: false));

        Assert.Contains(LastStepFailureInterceptor.FailureMessage, failure.Message);
        await tx.RollbackAsync();
        db.ChangeTracker.Clear();

        using var verify = CreateContext();
        Assert.Equal(0, await verify.SaleReturns.CountAsync());
        Assert.Equal(0, await verify.SaleReturnItems.CountAsync());
        Assert.Equal(0, await verify.StockMovements.CountAsync());
        Assert.Equal(0, await verify.JournalEntries.CountAsync());
        Assert.Equal(100m, (await verify.Items.SingleAsync()).CurrentQuantity);
        Assert.Equal(100m, (await verify.StockLayers.SingleAsync()).RemainingQty);
    }

    [Fact]
    public async Task Ambient_PurchaseReturnWithJournalFailure_LeavesNoOrphanRows()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        await using var tx = await db.Database.BeginTransactionAsync();
        _interceptor.FailWhenAdded = typeof(JournalEntry);

        var returned = new PurchaseReturn { SupplierId = seed.SupplierId, ReturnDate = DateTime.Today };
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.CreatePurchaseReturnAsync(
            returned,
            new List<PurchaseReturnItem> { new() { ItemId = seed.ItemId, Quantity = 5m, UnitPrice = 25m } },
            "tester", beginOwnTransaction: false));

        Assert.Contains(LastStepFailureInterceptor.FailureMessage, failure.Message);
        await tx.RollbackAsync();
        db.ChangeTracker.Clear();

        using var verify = CreateContext();
        Assert.Equal(0, await verify.PurchaseReturns.CountAsync());
        Assert.Equal(0, await verify.PurchaseReturnItems.CountAsync());
        Assert.Equal(0, await verify.StockMovements.CountAsync());
        Assert.Equal(0, await verify.JournalEntries.CountAsync());
        Assert.Equal(100m, (await verify.Items.SingleAsync()).CurrentQuantity);
        Assert.Equal(100m, (await verify.StockLayers.SingleAsync()).RemainingQty);
    }

    [Fact]
    public async Task Ambient_AdjustmentWithJournalFailure_LeavesStockUnchanged()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        await using var tx = await db.Database.BeginTransactionAsync();
        _interceptor.FailWhenAdded = typeof(JournalEntry);

        var adjustment = new InventoryAdjustment
        {
            ItemId = seed.ItemId,
            NewCount = 0m,
            NewQuantity = 110m,
            AdjustmentDate = DateTime.Today
        };
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => inventory.CreateAdjustmentAsync(adjustment, "tester", beginOwnTransaction: false));

        Assert.Contains(LastStepFailureInterceptor.FailureMessage, failure.Message);
        await tx.RollbackAsync();
        db.ChangeTracker.Clear();

        using var verify = CreateContext();
        Assert.Equal(0, await verify.InventoryAdjustments.CountAsync());
        Assert.Equal(0, await verify.StockMovements.CountAsync());
        Assert.Equal(0, await verify.JournalEntries.CountAsync());
        Assert.Equal(100m, (await verify.Items.SingleAsync()).CurrentQuantity);
        Assert.Equal(100m, (await verify.StockLayers.SingleAsync()).RemainingQty);
    }

    [Fact]
    public async Task Ambient_TransferFailure_LeavesNoBleedFromSourceWarehouse()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        await using var tx = await db.Database.BeginTransactionAsync();
        _interceptor.FailWhenAdded = typeof(StockTransfer);

        var transfer = new StockTransfer
        {
            SourceWarehouseId = seed.SourceWarehouseId,
            TargetWarehouseId = seed.TargetWarehouseId,
            TransferDate = DateTime.Today
        };
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.CreateTransferAsync(transfer,
            new List<StockTransferItem> { new() { ItemId = seed.ItemId, Quantity = 12m } }, "tester",
            beginOwnTransaction: false));

        Assert.Contains(LastStepFailureInterceptor.FailureMessage, failure.Message);
        await tx.RollbackAsync();
        db.ChangeTracker.Clear();

        using var verify = CreateContext();
        Assert.Equal(0, await verify.StockTransfers.CountAsync());
        Assert.Equal(0, await verify.StockTransferItems.CountAsync());
        var layers = await verify.StockLayers.AsNoTracking().ToListAsync();
        Assert.Single(layers);
        Assert.Equal(seed.SourceWarehouseId, layers[0].WarehouseId);
        Assert.Equal(100m, layers[0].RemainingQty);
        Assert.Equal(0, await verify.StockLayers.CountAsync(l => l.WarehouseId == seed.TargetWarehouseId));
    }

    [Fact]
    public async Task Ambient_PaymentWithJournalFailure_LeavesNoPaymentOrAllocation()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        await CreateDeliveredInvoiceAsync(db, seed.CustomerId, 1000m);
        var payments = new PaymentService(db, new AccountingService(db));

        await using var tx = await db.Database.BeginTransactionAsync();
        _interceptor.FailWhenAdded = typeof(JournalEntry);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => payments.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = seed.CustomerId,
            Amount = 500m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "tester", beginOwnTransaction: false));

        Assert.Contains(LastStepFailureInterceptor.FailureMessage, failure.Message);
        await tx.RollbackAsync();
        db.ChangeTracker.Clear();

        using var verify = CreateContext();
        Assert.Equal(0, await verify.Payments.CountAsync());
        Assert.Equal(0, await verify.SalePaymentAllocations.CountAsync());
        Assert.Equal(0, await verify.JournalEntries.CountAsync());
        Assert.Equal(0m, (await verify.SaleInvoices.SingleAsync()).PaidAmount);
    }

    [Fact]
    public async Task Ambient_ApprovalWithReservationFailure_LeavesOrderDraft()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        var orderId = await CreateDraftOrderAsync(db, seed.CustomerId, seed.ItemId, 10m);
        var inventory = new InventoryService(db, new AccountingService(db));
        var orders = new SalesOrdersService(db, inventory);

        await using var tx = await db.Database.BeginTransactionAsync();
        _interceptor.FailWhenAdded = typeof(StockReservation);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => orders.ApproveOrderAsync(orderId, beginOwnTransaction: false));

        Assert.Contains(LastStepFailureInterceptor.FailureMessage, failure.Message);
        await tx.RollbackAsync();
        db.ChangeTracker.Clear();

        using var verify = CreateContext();
        Assert.Equal(SalesOrderStatus.Draft, (await verify.SalesOrders.FindAsync(orderId))!.Status);
        Assert.Equal(0, await verify.StockReservations.CountAsync());
        Assert.Equal(0m, (await verify.Items.SingleAsync()).ReservedQuantity);
        Assert.Equal(0m, (await verify.SalesOrderItems.SingleAsync()).ReservedQty);
        Assert.Equal(0, await verify.JournalEntries.CountAsync());
    }

    // ---------- (2) بلا معاملة قائمة: ترفض العملية أن تعمل بلا حماية ----------

    [Fact]
    public async Task NoAmbient_SaleReturn_ThrowsAndWritesNothing()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.CreateSaleReturnAsync(
            new SaleReturn { CustomerId = seed.CustomerId, ReturnDate = DateTime.Today },
            new List<SaleReturnItem> { new() { ItemId = seed.ItemId, Quantity = 2m, UnitPrice = 80m } },
            "tester", beginOwnTransaction: false));

        Assert.Contains("دون معاملة قائمة", failure.Message);
        using var verify = CreateContext();
        Assert.Equal(0, await verify.SaleReturns.CountAsync());
        Assert.Equal(0, await verify.StockMovements.CountAsync());
        Assert.Equal(0, await verify.JournalEntries.CountAsync());
        Assert.Equal(100m, (await verify.Items.SingleAsync()).CurrentQuantity);
    }

    [Fact]
    public async Task NoAmbient_PurchaseReturn_ThrowsAndWritesNothing()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.CreatePurchaseReturnAsync(
            new PurchaseReturn { SupplierId = seed.SupplierId, ReturnDate = DateTime.Today },
            new List<PurchaseReturnItem> { new() { ItemId = seed.ItemId, Quantity = 5m, UnitPrice = 25m } },
            "tester", beginOwnTransaction: false));

        Assert.Contains("دون معاملة قائمة", failure.Message);
        using var verify = CreateContext();
        Assert.Equal(0, await verify.PurchaseReturns.CountAsync());
        Assert.Equal(0, await verify.StockMovements.CountAsync());
        Assert.Equal(0, await verify.JournalEntries.CountAsync());
        Assert.Equal(100m, (await verify.Items.SingleAsync()).CurrentQuantity);
    }

    [Fact]
    public async Task NoAmbient_Adjustment_ThrowsAndWritesNothing()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.CreateAdjustmentAsync(
            new InventoryAdjustment { ItemId = seed.ItemId, NewCount = 0m, NewQuantity = 110m, AdjustmentDate = DateTime.Today },
            "tester", beginOwnTransaction: false));

        Assert.Contains("دون معاملة قائمة", failure.Message);
        using var verify = CreateContext();
        Assert.Equal(0, await verify.InventoryAdjustments.CountAsync());
        Assert.Equal(0, await verify.StockMovements.CountAsync());
        Assert.Equal(100m, (await verify.Items.SingleAsync()).CurrentQuantity);
    }

    [Fact]
    public async Task NoAmbient_Transfer_ThrowsAndWritesNothing()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.CreateTransferAsync(
            new StockTransfer
            {
                SourceWarehouseId = seed.SourceWarehouseId,
                TargetWarehouseId = seed.TargetWarehouseId,
                TransferDate = DateTime.Today
            },
            new List<StockTransferItem> { new() { ItemId = seed.ItemId, Quantity = 5m } }, "tester",
            beginOwnTransaction: false));

        Assert.Contains("دون معاملة قائمة", failure.Message);
        using var verify = CreateContext();
        Assert.Equal(0, await verify.StockTransfers.CountAsync());
        Assert.Equal(100m, (await verify.StockLayers.SingleAsync()).RemainingQty);
    }

    [Fact]
    public async Task NoAmbient_Payment_ThrowsAndWritesNothing()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        await CreateDeliveredInvoiceAsync(db, seed.CustomerId, 1000m);
        var payments = new PaymentService(db, new AccountingService(db));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => payments.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = seed.CustomerId,
            Amount = 500m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "tester", beginOwnTransaction: false));

        Assert.Contains("دون معاملة قائمة", failure.Message);
        using var verify = CreateContext();
        Assert.Equal(0, await verify.Payments.CountAsync());
        Assert.Equal(0, await verify.SalePaymentAllocations.CountAsync());
        Assert.Equal(0, await verify.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task NoAmbient_Approval_ThrowsAndLeavesOrderDraft()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        var orderId = await CreateDraftOrderAsync(db, seed.CustomerId, seed.ItemId, 10m);
        var orders = new SalesOrdersService(db, new InventoryService(db, new AccountingService(db)));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => orders.ApproveOrderAsync(orderId, beginOwnTransaction: false));

        Assert.Contains("دون معاملة قائمة", failure.Message);
        using var verify = CreateContext();
        Assert.Equal(SalesOrderStatus.Draft, (await verify.SalesOrders.FindAsync(orderId))!.Status);
        Assert.Equal(0, await verify.StockReservations.CountAsync());
    }

    // ---------- (3) المسار الافتراضي لم يتغيّر ----------

    [Fact]
    public async Task DefaultTransaction_SaleReturn_PostsEverything()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        var (ok, error) = await inventory.CreateSaleReturnAsync(
            new SaleReturn { CustomerId = seed.CustomerId, ReturnDate = DateTime.Today },
            new List<SaleReturnItem> { new() { ItemId = seed.ItemId, Quantity = 2m, UnitPrice = 80m } }, "tester");

        Assert.True(ok, error);
        Assert.Equal(ReturnStatus.Posted, (await db.SaleReturns.SingleAsync()).Status);
        Assert.Equal(1, await db.JournalEntries.CountAsync(e => e.Source == JournalSource.SaleReturn));
        Assert.Equal(1, await db.StockMovements.CountAsync(m => m.DocumentType == DocumentType.SaleReturn));
        Assert.Equal(102m, (await db.Items.SingleAsync()).CurrentQuantity);
    }

    [Fact]
    public async Task DefaultTransaction_PurchaseReturn_PostsEverything()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        var (ok, error) = await inventory.CreatePurchaseReturnAsync(
            new PurchaseReturn { SupplierId = seed.SupplierId, ReturnDate = DateTime.Today },
            new List<PurchaseReturnItem> { new() { ItemId = seed.ItemId, Quantity = 5m, UnitPrice = 25m } }, "tester");

        Assert.True(ok, error);
        Assert.Equal(ReturnStatus.Posted, (await db.PurchaseReturns.SingleAsync()).Status);
        Assert.Equal(1, await db.JournalEntries.CountAsync(e => e.Source == JournalSource.PurchaseReturn));
        Assert.Equal(1, await db.StockMovements.CountAsync(m => m.DocumentType == DocumentType.PurchaseReturn));
        Assert.Equal(95m, (await db.Items.SingleAsync()).CurrentQuantity);
    }

    [Fact]
    public async Task DefaultTransaction_Adjustment_PostsEverything()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        var (ok, error) = await inventory.CreateAdjustmentAsync(
            new InventoryAdjustment { ItemId = seed.ItemId, NewCount = 0m, NewQuantity = 110m, AdjustmentDate = DateTime.Today },
            "tester");

        Assert.True(ok, error);
        Assert.Equal(1, await db.InventoryAdjustments.CountAsync());
        Assert.Equal(1, await db.JournalEntries.CountAsync());
        Assert.Equal(1, await db.StockMovements.CountAsync(m => m.DocumentType == DocumentType.Adjustment));
        Assert.Equal(110m, (await db.Items.SingleAsync()).CurrentQuantity);
    }

    [Fact]
    public async Task DefaultTransaction_Transfer_MovesStock()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        var (ok, error) = await inventory.CreateTransferAsync(
            new StockTransfer
            {
                SourceWarehouseId = seed.SourceWarehouseId,
                TargetWarehouseId = seed.TargetWarehouseId,
                TransferDate = DateTime.Today
            },
            new List<StockTransferItem> { new() { ItemId = seed.ItemId, Quantity = 12m } }, "tester");

        Assert.True(ok, error);
        Assert.Equal(1, await db.StockTransfers.CountAsync());
        var source = await db.StockLayers.AsNoTracking().SingleAsync(l => l.WarehouseId == seed.SourceWarehouseId);
        var target = await db.StockLayers.AsNoTracking().SingleAsync(l => l.WarehouseId == seed.TargetWarehouseId);
        Assert.Equal(88m, source.RemainingQty);
        Assert.Equal(12m, target.RemainingQty);
        Assert.Equal(100m, (await db.Items.SingleAsync()).CurrentQuantity);
    }

    [Fact]
    public async Task DefaultTransaction_Payment_PostsPaymentAllocationAndJournal()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        var invoiceId = await CreateDeliveredInvoiceAsync(db, seed.CustomerId, 1000m);
        var payments = new PaymentService(db, new AccountingService(db));

        var (ok, error, payment) = await payments.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = seed.CustomerId,
            Amount = 400m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "tester");

        Assert.True(ok, error);
        Assert.NotNull(payment);
        Assert.Equal(1, await db.Payments.CountAsync());
        Assert.Equal(400m, (await db.SalePaymentAllocations.AsNoTracking().SingleAsync(a => a.SaleInvoiceId == invoiceId)).AllocatedAmount);
        Assert.Equal(1, await db.JournalEntries.CountAsync(e => e.Source == JournalSource.Receipt));
        Assert.Equal(400m, (await db.SaleInvoices.FindAsync(invoiceId))!.PaidAmount);
    }

    [Fact]
    public async Task DefaultTransaction_Approval_ApprovesAndReservesOnce()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        var orderId = await CreateDraftOrderAsync(db, seed.CustomerId, seed.ItemId, 12m);
        var orders = new SalesOrdersService(db, new InventoryService(db, new AccountingService(db)));

        var (ok, error) = await orders.ApproveOrderAsync(orderId);

        Assert.True(ok, error);
        Assert.Equal(SalesOrderStatus.Approved, (await db.SalesOrders.FindAsync(orderId))!.Status);
        Assert.Equal(1, await db.StockReservations.CountAsync(r => r.SalesOrderId == orderId));
        Assert.Equal(12m, (await db.Items.SingleAsync()).ReservedQuantity);
        Assert.Equal(88m, (await db.Items.SingleAsync()).AvailableQuantity);
    }

    // ---------- (4) القيد الذي يمرّره المستدعي يُحفظ كما هو ----------

    [Fact]
    public async Task PostAsync_SuppliedEntryNumber_IsKeptInsteadOfGenerated()
    {
        using var db = CreateContext();
        SeedChart(db);
        var seed = await SeedAsync(db);
        await CreateDeliveredInvoiceAsync(db, seed.CustomerId, 1000m);
        var accounting = new AccountingService(db);

        await accounting.PostAsync(JournalSource.Receipt, 1, DateTime.Today, "قبض بم رقم مستورد",
            new[] { new JournalLine("1200", 250m, 0m, "مدينون"), new JournalLine("1000", 0m, 250m, "نقدية") },
            "tester", entryNumber: "JV-IMPORT-0007");

        var entry = await db.JournalEntries.SingleAsync();
        Assert.Equal("JV-IMPORT-0007", entry.EntryNumber);

        await accounting.PostAsync(JournalSource.Receipt, 2, DateTime.Today, "قبض autogenerated",
            new[] { new JournalLine("1200", 100m, 0m, "مدينون"), new JournalLine("1000", 0m, 100m, "نقدية") },
            "tester");

        Assert.NotEqual("JV-IMPORT-0007", (await db.JournalEntries.AsNoTracking().SingleAsync(e => e.Id != entry.Id)).EntryNumber);
    }
}
