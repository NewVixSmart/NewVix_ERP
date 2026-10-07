using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class SalesReservationDeliveryFlowTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public SalesReservationDeliveryFlowTests()
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
            new GLAccount { Code = "1200", Name = "ذمم العملاء" },
            new GLAccount { Code = "1300", Name = "المخزون" },
            new GLAccount { Code = "2055", Name = "ضريبة القيمة المضافة" },
            new GLAccount { Code = "4000", Name = "إيرادات المبيعات" },
            new GLAccount { Code = "5000", Name = "تكلفة المبيعات" },
            new GLAccount { Code = "5101", Name = "مردودات المبيعات" });
    }

    private static async Task<(int itemId, int custId)> SeedAsync(AppDbContext db, decimal qty = 100m, decimal count = 0m)
    {
        var cat = new ItemCategory { Name = "تصنيف حجز" };
        var type = new ItemType { Name = "نوع حجز" };
        var unit = new Unit { Name = "قطعة" };
        db.ItemCategories.Add(cat);
        db.ItemTypes.Add(type);
        db.Units.Add(unit);

        db.Items.Add(new Item
        {
            Name = "صنف حجز",
            Category = cat,
            ItemType = type,
            CountUnit = unit,
            QuantityUnit = unit,
            PurchasePrice = 50,
            SalePrice = 80,
            CurrentCount = count,
            CurrentQuantity = qty
        });
        db.Customers.Add(new Customer { Name = "عميل حجز" });
        await db.SaveChangesAsync();

        var item = await db.Items.SingleAsync();
        var customer = await db.Customers.SingleAsync();
        return (item.Id, customer.Id);
    }

    private async Task<int> CreateApprovedOrderAsync(AppDbContext db, int itemId, int custId, decimal qty, decimal unitPrice = 80m)
    {
        var orders = new SalesOrdersService(db, new InventoryService(db, new AccountingService(db)));
        var order = new SalesOrder { CustomerId = custId, OrderDate = DateTime.Today };
        var (ok, err) = await orders.CreateOrderAsync(order,
            new List<SalesOrderItem> { new() { ItemId = itemId, Quantity = qty, UnitPrice = unitPrice } }, "test");
        Assert.True(ok, err);
        var found = await db.SalesOrders.FindAsync(order.Id);
        found!.Status = SalesOrderStatus.Approved;
        await db.SaveChangesAsync();
        return order.Id;
    }

    [Fact]
    public async Task ReserveOrderAsync_DoesNotDeductStock_AndBlocksDoubleReservation()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var reservations = new StockReservationsService(db);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 30m);

        var (ok, err) = await reservations.ReserveOrderAsync(orderId, "tester");
        Assert.True(ok, err);

        var item = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal(100m, item.CurrentQuantity);
        Assert.Equal(0m, item.CurrentCount);
        Assert.Equal(30m, item.ReservedQuantity);
        Assert.Equal(70m, item.AvailableQuantity);

        var reservation = await db.StockReservations.Include(r => r.Items).SingleAsync();
        Assert.Equal(StockReservationStatus.Active, reservation.Status);
        Assert.Equal(orderId, reservation.SalesOrderId);
        Assert.Equal(30m, reservation.Items.Single().Quantity);
        Assert.StartsWith("RSV-", reservation.ReservationNumber);

        var orderLine = await db.SalesOrderItems.AsNoTracking().SingleAsync(i => i.SalesOrderId == orderId);
        Assert.Equal(30m, orderLine.ReservedQty);
        Assert.Equal(30m, orderLine.PendingQty);

        var (ok2, err2) = await reservations.ReserveOrderAsync(orderId, "tester");
        Assert.False(ok2);
        Assert.Contains("يوجد حجز", err2);
    }

    [Fact]
    public async Task ReserveOrderAsync_Shortage_IsRejected_AndLeavesAggregatesUntouched()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db, qty: 10m);
        var reservations = new StockReservationsService(db);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 25m);

        var (ok, err) = await reservations.ReserveOrderAsync(orderId, "tester");
        Assert.False(ok);
        Assert.Contains("غير كافٍ", err);

        var item = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal(0m, item.ReservedQuantity);
        Assert.Equal(10m, item.CurrentQuantity);
        Assert.Equal(0, await db.StockReservations.CountAsync());
    }

    [Fact]
    public async Task ReserveOrderAsync_OnlyForApprovedOrder()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var reservations = new StockReservationsService(db);
        var orders = new SalesOrdersService(db, new InventoryService(db, new AccountingService(db)));
        var draft = new SalesOrder { CustomerId = custId };
        var (ok, _) = await orders.CreateOrderAsync(draft,
            new List<SalesOrderItem> { new() { ItemId = itemId, Quantity = 5m, UnitPrice = 80 } }, "test");
        Assert.True(ok);

        var (reserved, err) = await reservations.ReserveOrderAsync(draft.Id, "tester");
        Assert.False(reserved);
        Assert.Contains("معتمد", err);
    }

    [Fact]
    public async Task CreateStandaloneAsync_WorksWithoutSalesOrderOrCustomer()
    {
        using var db = CreateContext();
        var (itemId, _) = await SeedAsync(db);
        var reservations = new StockReservationsService(db);

        var header = new StockReservation { Reason = "حجز يدوي لعميل نقدي" };
        var (ok, err, reservation) = await reservations.CreateStandaloneAsync(header, new List<StockReservationLine>
        {
            new() { ItemId = itemId, Quantity = 12m }
        }, "tester");

        Assert.True(ok, err);
        Assert.NotNull(reservation);
        Assert.Null(reservation!.SalesOrderId);
        Assert.Null(reservation.CustomerId);
        Assert.Equal(StockReservationStatus.Active, reservation.Status);

        var item = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal(12m, item.ReservedQuantity);
        Assert.Equal(88m, item.AvailableQuantity);
        Assert.Equal(100m, item.CurrentQuantity);
    }

    [Fact]
    public async Task CreateStandaloneAsync_DuplicateItem_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, _) = await SeedAsync(db);
        var reservations = new StockReservationsService(db);

        var (ok, err, _) = await reservations.CreateStandaloneAsync(new StockReservation(), new List<StockReservationLine>
        {
            new() { ItemId = itemId, Quantity = 2m },
            new() { ItemId = itemId, Count = 3m }
        }, "tester");

        Assert.False(ok);
        Assert.Contains("نفسه", err);
    }

    [Fact]
    public async Task ReleaseAsync_ReturnsAvailability_AndClearsOrderCounters()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var reservations = new StockReservationsService(db);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 40m);

        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);
        var reservation = await db.StockReservations.SingleAsync();

        var (ok, err) = await reservations.ReleaseAsync(reservation.Id, "tester");
        Assert.True(ok, err);

        var item = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal(0m, item.ReservedQuantity);
        Assert.Equal(100m, item.AvailableQuantity);

        var reloaded = await db.StockReservations.AsNoTracking().SingleAsync();
        Assert.Equal(StockReservationStatus.Released, reloaded.Status);
        Assert.Equal("tester", reloaded.ReleasedBy);
        Assert.NotNull(reloaded.ReleasedAt);

        var orderLine = await db.SalesOrderItems.AsNoTracking().SingleAsync(i => i.SalesOrderId == orderId);
        Assert.Equal(0m, orderLine.ReservedQty);
    }

    [Fact]
    public async Task ReleaseForOrderAsync_CancelsReservationOfCancelledOrder()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var reservations = new StockReservationsService(db);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 15m);

        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);
        await reservations.ReleaseForOrderAsync(orderId, "tester");

        var item = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal(0m, item.ReservedQuantity);
        var reloaded = await db.StockReservations.AsNoTracking().SingleAsync();
        Assert.Equal(StockReservationStatus.Cancelled, reloaded.Status);
    }

    [Fact]
    public async Task ConsumeForIssuesAsync_PartiallyConsumesAndRecalculates()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var reservations = new StockReservationsService(db);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 50m);
        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);

        var orderLineId = await db.SalesOrderItems.Where(i => i.SalesOrderId == orderId).Select(i => i.Id).SingleAsync();
        var (ok, err) = await reservations.ConsumeForIssuesAsync(
            new[] { new DeliveryIssueItemLine(itemId, orderLineId, 20m, 0m) }, custId, DateTime.Today);

        Assert.True(ok, err);

        var item = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal(30m, item.ReservedQuantity);
        Assert.Equal(70m, item.AvailableQuantity);
        Assert.Equal(100m, item.CurrentQuantity);

        var line = await db.StockReservationLines.AsNoTracking().SingleAsync();
        Assert.Equal(20m, line.ConsumedQuantity);
        Assert.Equal(30m, line.RemainingQuantity);
        var reservation = await db.StockReservations.AsNoTracking().SingleAsync();
        Assert.Equal(StockReservationStatus.PartiallyConsumed, reservation.Status);

        var orderLine = await db.SalesOrderItems.AsNoTracking().SingleAsync(i => i.SalesOrderId == orderId);
        Assert.Equal(30m, orderLine.ReservedQty);
    }

    [Fact]
    public async Task ConsumeForIssuesAsync_FullConsumption_ClosesReservation()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var reservations = new StockReservationsService(db);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 10m);
        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);

        var orderLineId = await db.SalesOrderItems.Where(i => i.SalesOrderId == orderId).Select(i => i.Id).SingleAsync();
        var (ok, _) = await reservations.ConsumeForIssuesAsync(
            new[] { new DeliveryIssueItemLine(itemId, orderLineId, 4m, 0m) }, custId, DateTime.Today);
        Assert.True(ok);
        var (ok2, _) = await reservations.ConsumeForIssuesAsync(
            new[] { new DeliveryIssueItemLine(itemId, orderLineId, 6m, 0m) }, custId, DateTime.Today);
        Assert.True(ok2);

        var reservation = await db.StockReservations.AsNoTracking().SingleAsync();
        Assert.Equal(StockReservationStatus.Consumed, reservation.Status);
        var item = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal(0m, item.ReservedQuantity);
        Assert.Equal(100m, item.AvailableQuantity);
    }

    [Fact]
    public async Task ReleaseAsync_AfterConsumption_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var reservations = new StockReservationsService(db);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 10m);
        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);

        var reservation = await db.StockReservations.SingleAsync();
        var orderLineId = await db.SalesOrderItems.Where(i => i.SalesOrderId == orderId).Select(i => i.Id).SingleAsync();
        await reservations.ConsumeForIssuesAsync(
            new[] { new DeliveryIssueItemLine(itemId, orderLineId, 1m, 0m) }, custId, DateTime.Today);

        var (ok, err) = await reservations.ReleaseAsync(reservation.Id, "tester");
        Assert.False(ok);
        Assert.Contains("استهلاك", err);
    }

    [Fact]
    public async Task RecalculateItemReservationsAsync_RepairsDrift()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var reservations = new StockReservationsService(db);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 20m);
        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);

        var item = await db.Items.FirstAsync(i => i.Id == itemId);
        item.ReservedQuantity = 999m;
        await db.SaveChangesAsync();

        await reservations.RecalculateItemReservationsAsync(itemId);

        var fixedItem = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal(20m, fixedItem.ReservedQuantity);
    }

    [Fact]
    public async Task GetAvailabilityAsync_ReportsReservedAndAvailable()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db, qty: 50m, count: 8m);
        var reservations = new StockReservationsService(db);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 20m);
        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);

        var availability = await reservations.GetAvailabilityAsync(itemId);
        Assert.NotNull(availability);
        Assert.Equal(50m, availability!.Value.CurrentQuantity);
        Assert.Equal(20m, availability.Value.ReservedQuantity);
        Assert.Equal(30m, availability.Value.AvailableQuantity);
        Assert.Equal(8m, availability.Value.AvailableCount);

        var list = await reservations.GetAvailabilityAsync(new[] { itemId, 99999 });
        Assert.Single(list);
    }

    [Fact]
    public async Task ApproveOrder_ReservesStock_AndCancel_ReleasesIt()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var reservations = new StockReservationsService(db);
        var orders = new SalesOrdersService(db, new InventoryService(db, new AccountingService(db)), reservations);

        var order = new SalesOrder { CustomerId = custId };
        var (created, _) = await orders.CreateOrderAsync(order,
            new List<SalesOrderItem> { new() { ItemId = itemId, Quantity = 18m, UnitPrice = 80 } }, "tester");
        Assert.True(created);

        var (approved, approveError) = await orders.ApproveOrderAsync(order.Id);
        Assert.True(approved, approveError);
        Assert.Equal(SalesOrderStatus.Approved, (await db.SalesOrders.FindAsync(order.Id))!.Status);

        var item = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal(18m, item.ReservedQuantity);
        Assert.Equal(100m, item.CurrentQuantity);

        var (cancelled, cancelError) = await orders.CancelOrderAsync(order.Id);
        Assert.True(cancelled, cancelError);
        var afterCancel = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal(0m, afterCancel.ReservedQuantity);
        Assert.Equal(StockReservationStatus.Cancelled,
            (await db.StockReservations.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task ApproveOrder_Shortage_DoesNotApprove()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db, qty: 5m);
        var orders = new SalesOrdersService(db, new InventoryService(db, new AccountingService(db)));
        var order = new SalesOrder { CustomerId = custId };
        await orders.CreateOrderAsync(order,
            new List<SalesOrderItem> { new() { ItemId = itemId, Quantity = 9m, UnitPrice = 80 } }, "tester");

        var (approved, error) = await orders.ApproveOrderAsync(order.Id);
        Assert.False(approved);
        Assert.Contains("غير كافٍ", error);
        Assert.Equal(SalesOrderStatus.Draft, (await db.SalesOrders.FindAsync(order.Id))!.Status);
        Assert.Equal(0, await db.StockReservations.CountAsync());
    }

    [Fact]
    public async Task CreateSalesDeliveryNote_FromApprovedOrder_CapsAtOrderQuantity()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var reservations = new StockReservationsService(db);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 25m);
        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);

        var (ok, err, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 10m } }, "tester");
        Assert.True(ok, err);
        Assert.NotNull(note);
        Assert.Equal(orderId, note!.SalesOrderId);
        Assert.Equal(custId, note.CustomerId);
        Assert.Null(note.SaleInvoiceId);
        Assert.Equal(DeliveryOrderStatus.Draft, note.Status);
        Assert.StartsWith("DLV-", note.DeliveryNumber);
        Assert.True(note.IsOrderBacked);

        var (overOk, overErr, _) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 20m } }, "tester");
        Assert.False(overOk);
        Assert.Contains("أكبر من المتبقي", overErr);
    }

    [Fact]
    public async Task CreateSalesDeliveryNote_Standalone_RequiresCustomerOnly()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        var (ok, err, note) = await inventory.CreateSalesDeliveryNoteAsync(null, null, custId,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Count = 3m } }, "tester");
        Assert.True(ok, err);
        Assert.NotNull(note);
        Assert.Null(note!.SalesOrderId);
        Assert.Null(note.SaleInvoiceId);
        Assert.False(note.IsOrderBacked);
        Assert.Equal(3m, note.Items.Single().Count);

        var (noCustomer, customerError, _) = await inventory.CreateSalesDeliveryNoteAsync(null, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 1m } }, "tester");
        Assert.False(noCustomer);
        Assert.Contains("العميل", customerError);
    }

    [Fact]
    public async Task CreateSalesDeliveryNote_DraftOrder_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var orders = new SalesOrdersService(db, inventory);
        var order = new SalesOrder { CustomerId = custId };
        await orders.CreateOrderAsync(order,
            new List<SalesOrderItem> { new() { ItemId = itemId, Quantity = 2m, UnitPrice = 80 } }, "tester");

        var (ok, err, _) = await inventory.CreateSalesDeliveryNoteAsync(order.Id, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 1m } }, "tester");
        Assert.False(ok);
        Assert.Contains("مسودة", err);
    }

    [Fact]
    public async Task IssueDelivery_DeductsStock_CostsOnly_AndConsumesReservation()
    {
        using var db = CreateContext();
        var accounting = new AccountingService(db);
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedAsync(db);
        db.StockLayers.Add(new StockLayer
        {
            ItemId = itemId,
            Qty = 100,
            Count = 0,
            UnitCost = 60m,
            RemainingQty = 100,
            RemainingCount = 0,
            DateReceived = new DateTime(2026, 1, 1),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var reservations = new StockReservationsService(db);
        var inventory = new InventoryService(db, accounting, reservations);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 40m);
        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);

        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 40m } }, "tester");
        var orderLineId = await db.SalesOrderItems.Where(i => i.SalesOrderId == orderId).Select(i => i.Id).SingleAsync();

        var (created, createError, issue) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 15m, SalesOrderItemId = orderLineId }
        }, "tester");
        Assert.True(created, createError);
        Assert.StartsWith("ISS-", issue!.IssueNumber);
        Assert.Equal(orderId, issue.SalesOrderId);
        Assert.Equal(DeliveryIssueStatus.Draft, issue.Status);
        Assert.True(issue.Items.Single().DeliveryOrderItemId > 0);

        var (issued, issueError) = await inventory.IssueDeliveryAsync(issue.Id, "tester");
        Assert.True(issued, issueError);

        var item = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal(85m, item.CurrentQuantity);
        Assert.Equal(25m, item.ReservedQuantity);
        Assert.Equal(60m, item.AvailableQuantity);
        Assert.Equal(85m, (await db.StockLayers.AsNoTracking().SingleAsync()).RemainingQty);

        var reloaded = await db.DeliveryIssues.AsNoTracking().SingleAsync();
        Assert.Equal(DeliveryIssueStatus.Issued, reloaded.Status);
        Assert.Equal("tester", reloaded.IssuedBy);
        Assert.NotNull(reloaded.IssuedAt);

        var delivery = await db.DeliveryOrders.AsNoTracking().SingleAsync();
        Assert.Equal(DeliveryOrderStatus.PartiallyIssued, delivery.Status);

        var orderLine = await db.SalesOrderItems.AsNoTracking().SingleAsync(i => i.SalesOrderId == orderId);
        Assert.Equal(15m, orderLine.DeliveredQty);
        Assert.Equal(25m, orderLine.PendingQty);
        Assert.Equal(25m, orderLine.ReservedQty);

        var movement = await db.StockMovements.AsNoTracking().SingleAsync();
        Assert.Equal(DocumentType.SalesDeliveryIssue, movement.DocumentType);
        Assert.Equal(reloaded.IssueNumber, movement.DocumentNumber);

        var entry = await db.JournalEntries.Include(e => e.Lines).SingleAsync();
        Assert.Equal(JournalSource.SaleDeliveryIssue, entry.Source);
        Assert.Equal(15m * 60m, entry.Lines.Where(l => l.Debit > 0).Sum(l => l.Debit));
        Assert.Equal(15m * 60m, entry.Lines.Where(l => l.Credit > 0).Sum(l => l.Credit));
        Assert.Equal(2, entry.Lines.Count);
    }

    [Fact]
    public async Task IssueDelivery_ExceedingAvailableStock_IsRejected_AndKeepsReservation()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db, qty: 30m);
        var reservations = new StockReservationsService(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 20m);
        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);

        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 20m } }, "tester");
        var (_, _, issue) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 20m }
        }, "tester");

        var other = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal(10m, other.AvailableQuantity);

        var tracked = await db.Items.FirstAsync(i => i.Id == itemId);
        tracked.CurrentQuantity = 15m;
        await db.SaveChangesAsync();

        var (ok, err) = await inventory.IssueDeliveryAsync(issue!.Id, "tester");
        Assert.False(ok);
        Assert.Contains("غير كافٍ", err);
        Assert.Equal(DeliveryIssueStatus.Draft, (await db.DeliveryIssues.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(0, await db.StockMovements.CountAsync());
        Assert.Equal(20m, (await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId)).ReservedQuantity);
    }

    [Fact]
    public async Task IssueDelivery_ReservationOfOtherOrder_CannotBeConsumed()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedAsync(db, qty: 40m);
        var secondCustomer = new Customer { Name = "عميل ثانٍ" };
        db.Customers.Add(secondCustomer);
        await db.SaveChangesAsync();

        var reservations = new StockReservationsService(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var firstOrderId = await CreateApprovedOrderAsync(db, itemId, custId, 10m);
        var secondOrderId = await CreateApprovedOrderAsync(db, itemId, secondCustomer.Id, 10m);
        Assert.True((await reservations.ReserveOrderAsync(firstOrderId, "tester")).Success);
        Assert.True((await reservations.ReserveOrderAsync(secondOrderId, "tester")).Success);

        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(firstOrderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 10m } }, "tester");
        var (_, _, issue) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 10m }
        }, "tester");

        Assert.True((await inventory.IssueDeliveryAsync(issue!.Id, "tester")).Success);
        var item = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal(30m, item.CurrentQuantity);
        Assert.Equal(10m, item.ReservedQuantity);
        Assert.Equal(
            StockReservationStatus.Consumed,
            (await db.StockReservations.AsNoTracking()
                .SingleAsync(r => r.SalesOrderId == firstOrderId)).Status);
        Assert.Equal(
            StockReservationStatus.Active,
            (await db.StockReservations.AsNoTracking()
                .SingleAsync(r => r.SalesOrderId == secondOrderId)).Status);
    }

    [Fact]
    public async Task IssueDelivery_Twice_IsRejected()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 5m);
        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 5m } }, "tester");
        var (_, _, issue) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 5m }
        }, "tester");

        Assert.True((await inventory.IssueDeliveryAsync(issue!.Id, "tester")).Success);
        var (again, againError) = await inventory.IssueDeliveryAsync(issue!.Id, "tester");
        Assert.False(again);
        Assert.Contains("مرحّل", againError);
        Assert.Equal(95m, (await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId)).CurrentQuantity);
    }

    [Fact]
    public async Task CreateDeliveryIssue_ExceedingNoteQuantity_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 6m);
        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 6m } }, "tester");

        var (ok, err, issue) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 9m }
        }, "tester");

        Assert.False(ok);
        Assert.Null(issue);
        Assert.Contains("أكبر من المتبقي", err);
        Assert.Equal(0, await db.DeliveryIssues.CountAsync());
    }

    [Fact]
    public async Task CreateDeliveryIssue_TwoIssues_CannotExceedNoteQuantity()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 10m);
        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 10m } }, "tester");

        var (firstOk, firstErr, _) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 6m }
        }, "tester");
        Assert.True(firstOk, firstErr);

        var (secondOk, secondErr, _) = await inventory.CreateDeliveryIssueAsync(note.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 6m }
        }, "tester");
        Assert.False(secondOk);
        Assert.Contains("أكبر من المتبقي", secondErr);

        var (fitsOk, fitsErr, _) = await inventory.CreateDeliveryIssueAsync(note.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 4m }
        }, "tester");
        Assert.True(fitsOk, fitsErr);
    }

    [Fact]
    public async Task FullIssue_MarksDeliveryDelivered_AndBlocksOrderCancellation()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedAsync(db);
        var reservations = new StockReservationsService(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var orders = new SalesOrdersService(db, inventory, reservations);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 7m);
        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);

        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 7m } }, "tester");
        var (_, _, issue) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 7m }
        }, "tester");
        Assert.True((await inventory.IssueDeliveryAsync(issue!.Id, "tester")).Success);

        Assert.Equal(DeliveryOrderStatus.Delivered,
            (await db.DeliveryOrders.AsNoTracking().SingleAsync()).Status);

        var (cancelled, cancelError) = await orders.CancelOrderAsync(orderId);
        Assert.False(cancelled);
        Assert.Contains("تم تسليم", cancelError);
    }

    [Fact]
    public async Task CancelDeliveryIssue_KeepsStockAndReservation()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var reservations = new StockReservationsService(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 9m);
        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);

        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 9m } }, "tester");
        var (_, _, issue) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 4m }
        }, "tester");

        var (ok, err) = await inventory.CancelDeliveryIssueAsync(issue!.Id, "tester");
        Assert.True(ok, err);
        Assert.Equal(DeliveryIssueStatus.Cancelled, (await db.DeliveryIssues.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(100m, (await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId)).CurrentQuantity);
        Assert.Equal(9m, (await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId)).ReservedQuantity);
        Assert.Equal(DeliveryOrderStatus.Draft, (await db.DeliveryOrders.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task LegacyDeliver_IsRejected_ForOrderBackedNote()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 5m);
        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 5m } }, "tester");

        var (ok, err) = await inventory.DeliverDeliveryOrderAsync(note!.Id, "tester");
        Assert.False(ok);
        Assert.Contains("أمر التسليم", err);
    }

    [Fact]
    public async Task CancelDeliveryNote_WithIssuedIssue_IsRejected()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 5m);
        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 5m } }, "tester");
        var (_, _, issue) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 2m }
        }, "tester");
        Assert.True((await inventory.IssueDeliveryAsync(issue!.Id, "tester")).Success);

        var (ok, err) = await inventory.CancelDeliveryOrderAsync(note!.Id, "tester");
        Assert.False(ok);
        Assert.Contains("ألغِ أمر التسليم", err);
    }

    [Fact]
    public async Task InvoiceFromIssues_PostsRevenueOnly_AndLinksIssues()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedAsync(db);
        db.StockLayers.Add(new StockLayer
        {
            ItemId = itemId,
            Qty = 100,
            Count = 0,
            UnitCost = 60m,
            RemainingQty = 100,
            RemainingCount = 0,
            DateReceived = new DateTime(2026, 1, 1),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var reservations = new StockReservationsService(db);
        var accounting = new AccountingService(db);
        var inventory = new InventoryService(db, accounting, reservations);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 50m, unitPrice: 100m);
        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);

        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 50m } }, "tester");
        var orderLineId = await db.SalesOrderItems.Where(i => i.SalesOrderId == orderId).Select(i => i.Id).SingleAsync();
        var (_, _, issue) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 20m, SalesOrderItemId = orderLineId }
        }, "tester");
        Assert.True((await inventory.IssueDeliveryAsync(issue!.Id, "tester")).Success);

        var invoicing = new DeliveriesInvoicingService(db, inventory, accounting);
        var (ok, err, invoice) = await invoicing.CreateInvoiceFromIssuesAsync(
            new[] { issue!.Id },
            new SaleInvoice { CustomerId = custId, InvoiceDate = new DateTime(2026, 5, 4), Tax = 30m },
            "tester");

        Assert.True(ok, err);
        Assert.NotNull(invoice);
        Assert.Equal(SalesPostingMode.AtInvoice, invoice!.PostingMode);
        Assert.Equal(orderId, invoice.SalesOrderId);
        var line = invoice.Items.Single();
        Assert.Equal(20m, line.Quantity);
        Assert.Equal(100m, line.UnitPrice);
        Assert.Equal(2000m, line.Total);
        Assert.Equal(30m, invoice.Tax);
        Assert.Equal(2030m, invoice.NetAmount);

        Assert.Equal(invoice.Id, (await db.DeliveryIssues.AsNoTracking().SingleAsync()).SaleInvoiceId);

        var orderLine = await db.SalesOrderItems.AsNoTracking().SingleAsync(i => i.SalesOrderId == orderId);
        Assert.Equal(20m, orderLine.InvoicedQty);
        Assert.Equal(0m, orderLine.UninvoicedQty);
        Assert.Equal(20m, orderLine.DeliveredQty);
        Assert.Equal(30m, orderLine.PendingQty);
        Assert.Equal(SalesOrderStatus.PartiallyInvoiced,
            (await db.SalesOrders.AsNoTracking().SingleAsync()).Status);

        var entries = await db.JournalEntries.Include(e => e.Lines).OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(2, entries.Count);
        var cost = entries.Single(e => e.Source == JournalSource.SaleDeliveryIssue);
        Assert.Equal(20m * 60m, cost.Lines.Sum(l => l.Debit));
        Assert.Equal(20m * 60m, cost.Lines.Single(l => l.Credit > 0).Credit);

        var revenue = entries.Single(e => e.Source == JournalSource.SaleInvoice);
        Assert.Equal(3, revenue.Lines.Count);
        Assert.Equal(2030m, revenue.Lines.Single(l => l.Debit > 0).Debit);
        var credits = revenue.Lines.Where(l => l.Credit > 0).Select(l => l.Credit).OrderByDescending(c => c).ToList();
        Assert.Equal(new[] { 2000m, 30m }, credits);
        Assert.Equal(credits.Sum(), revenue.Lines.Sum(l => l.Debit));

        var itemAfter = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal(80m, itemAfter.CurrentQuantity);
        Assert.Equal(0, await db.StockMovements.CountAsync(m => m.DocumentType == DocumentType.SaleInvoice));
    }

    [Fact]
    public async Task InvoiceFromIssues_FullyDiscountedInvoice_PostsCostOnlyAndNoRevenue()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedAsync(db);
        db.StockLayers.Add(new StockLayer
        {
            ItemId = itemId,
            Qty = 100,
            Count = 0,
            UnitCost = 60m,
            RemainingQty = 100,
            RemainingCount = 0,
            DateReceived = new DateTime(2026, 1, 1),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var reservations = new StockReservationsService(db);
        var accounting = new AccountingService(db);
        var inventory = new InventoryService(db, accounting, reservations);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 50m, unitPrice: 100m);
        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);

        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 50m } }, "tester");
        var orderLineId = await db.SalesOrderItems.Where(i => i.SalesOrderId == orderId).Select(i => i.Id).SingleAsync();
        var (_, _, issue) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 20m, SalesOrderItemId = orderLineId }
        }, "tester");
        Assert.True((await inventory.IssueDeliveryAsync(issue!.Id, "tester")).Success);

        // A 100% invoice-level discount is a legitimate case (samples, promotions). The invoice
        // nets to zero, so there is no receivable and no revenue to recognise - but the goods
        // still leave stock at cost, so the delivery cost entry must post.
        var invoicing = new DeliveriesInvoicingService(db, inventory, accounting);
        var (ok, err, invoice) = await invoicing.CreateInvoiceFromIssuesAsync(
            new[] { issue!.Id },
            new SaleInvoice
            {
                CustomerId = custId,
                InvoiceDate = new DateTime(2026, 5, 4),
                Tax = 0m,
                Discount = 2000m
            },
            "tester");

        Assert.True(ok, err);
        Assert.NotNull(invoice);
        Assert.Equal(2000m, invoice!.TotalAmount);
        Assert.Equal(0m, invoice.NetAmount);
        Assert.Equal(invoice.Id, (await db.DeliveryIssues.AsNoTracking().SingleAsync()).SaleInvoiceId);

        var entries = await db.JournalEntries.Include(e => e.Lines).ToListAsync();
        var cost = Assert.Single(entries);
        Assert.Equal(JournalSource.SaleDeliveryIssue, cost.Source);
        Assert.Equal(20m * 60m, cost.Lines.Sum(l => l.Debit));
        Assert.Equal(cost.Lines.Sum(l => l.Debit), cost.Lines.Sum(l => l.Credit));
        Assert.DoesNotContain(entries, e => e.Source == JournalSource.SaleInvoice);

        var itemAfter = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal(80m, itemAfter.CurrentQuantity);
    }

    [Fact]
    public async Task InvoiceFromIssues_TwoIssuesOneInvoice_CollapsesLines()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedAsync(db);
        var reservations = new StockReservationsService(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var invoicing = new DeliveriesInvoicingService(db, inventory, new AccountingService(db));

        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 40m, unitPrice: 70m);
        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);
        var orderLineId = await db.SalesOrderItems.Where(i => i.SalesOrderId == orderId).Select(i => i.Id).SingleAsync();

        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 40m } }, "tester");
        var (_, _, first) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 10m, SalesOrderItemId = orderLineId }
        }, "tester");
        var (_, _, second) = await inventory.CreateDeliveryIssueAsync(note.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 15m, SalesOrderItemId = orderLineId }
        }, "tester");
        Assert.True((await inventory.IssueDeliveryAsync(first!.Id, "tester")).Success);
        Assert.True((await inventory.IssueDeliveryAsync(second!.Id, "tester")).Success);

        var (ok, err, invoice) = await invoicing.CreateInvoiceFromIssuesAsync(
            new[] { first.Id, second.Id },
            new SaleInvoice { CustomerId = custId, InvoiceDate = new DateTime(2026, 5, 4) }, "tester");

        Assert.True(ok, err);
        var line = invoice!.Items.Single();
        Assert.Equal(25m, line.Quantity);
        Assert.Equal(70m, line.UnitPrice);
        Assert.Equal(1750m, invoice.NetAmount);
        Assert.All(await db.DeliveryIssues.AsNoTracking().ToListAsync(),
            i => Assert.Equal(invoice.Id, i.SaleInvoiceId));
        Assert.Equal(25m, (await db.SalesOrderItems.AsNoTracking().SingleAsync()).InvoicedQty);
    }

    [Fact]
    public async Task InvoiceFromIssues_FullDelivery_ClosesOrder()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedAsync(db);
        var reservations = new StockReservationsService(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var invoicing = new DeliveriesInvoicingService(db, inventory, new AccountingService(db));

        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 10m, unitPrice: 55m);
        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);
        var orderLineId = await db.SalesOrderItems.Where(i => i.SalesOrderId == orderId).Select(i => i.Id).SingleAsync();
        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 10m } }, "tester");
        var (_, _, issue) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 10m, SalesOrderItemId = orderLineId }
        }, "tester");
        Assert.True((await inventory.IssueDeliveryAsync(issue!.Id, "tester")).Success);

        var (ok, err, _) = await invoicing.CreateInvoiceFromIssuesAsync(new[] { issue!.Id },
            new SaleInvoice { CustomerId = custId, InvoiceDate = new DateTime(2026, 5, 4) }, "tester");
        Assert.True(ok, err);

        Assert.Equal(SalesOrderStatus.Invoiced, (await db.SalesOrders.AsNoTracking().SingleAsync()).Status);
        var orderLine = await db.SalesOrderItems.AsNoTracking().SingleAsync();
        Assert.Equal(0m, orderLine.UninvoicedQty);
        Assert.Equal(0m, orderLine.PendingQty);
    }

    [Fact]
    public async Task InvoiceFromIssues_StandaloneUsesItemSalePrice()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var invoicing = new DeliveriesInvoicingService(db, inventory, new AccountingService(db));

        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(null, null, custId,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 3m } }, "tester");
        var (_, _, issue) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 3m }
        }, "tester");
        Assert.True((await inventory.IssueDeliveryAsync(issue!.Id, "tester")).Success);

        var (ok, err, invoice) = await invoicing.CreateInvoiceFromIssuesAsync(new[] { issue!.Id },
            new SaleInvoice { CustomerId = custId, InvoiceDate = new DateTime(2026, 5, 4) }, "tester");
        Assert.True(ok, err);
        Assert.Null(invoice!.SalesOrderId);
        Assert.Equal(80m, invoice.Items.Single().UnitPrice);
        Assert.Equal(240m, invoice.NetAmount);
    }

    [Fact]
    public async Task InvoiceFromIssues_RejectsDraftOrAlreadyInvoicedIssues()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var invoicing = new DeliveriesInvoicingService(db, inventory, new AccountingService(db));

        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(null, null, custId,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 2m } }, "tester");
        var (_, _, draft) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 2m }
        }, "tester");

        var (draftOk, draftErr, _) = await invoicing.CreateInvoiceFromIssuesAsync(new[] { draft!.Id },
            new SaleInvoice { CustomerId = custId, InvoiceDate = new DateTime(2026, 5, 4) }, "tester");
        Assert.False(draftOk);
        Assert.Contains("المرحّلة فقط", draftErr);

        Assert.True((await inventory.IssueDeliveryAsync(draft.Id, "tester")).Success);
        Assert.True((await invoicing.CreateInvoiceFromIssuesAsync(new[] { draft.Id },
            new SaleInvoice { CustomerId = custId, InvoiceDate = new DateTime(2026, 5, 4) }, "tester")).Success);

        var (againOk, againErr, _) = await invoicing.CreateInvoiceFromIssuesAsync(new[] { draft.Id },
            new SaleInvoice { CustomerId = custId, InvoiceDate = new DateTime(2026, 5, 5) }, "tester");
        Assert.False(againOk);
        Assert.Contains("مفوتر بالفعل", againErr);
        Assert.Equal(1, await db.SaleInvoices.CountAsync());
    }

    [Fact]
    public async Task InvoiceFromIssues_RejectsMixedCustomers()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedAsync(db);
        var other = new Customer { Name = "عميل آخر" };
        db.Customers.Add(other);
        await db.SaveChangesAsync();

        var inventory = new InventoryService(db, new AccountingService(db));
        var invoicing = new DeliveriesInvoicingService(db, inventory, new AccountingService(db));

        var (_, _, firstNote) = await inventory.CreateSalesDeliveryNoteAsync(null, null, custId,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 1m } }, "tester");
        var (_, _, first) = await inventory.CreateDeliveryIssueAsync(firstNote!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 1m }
        }, "tester");
        Assert.True((await inventory.IssueDeliveryAsync(first!.Id, "tester")).Success);

        var (_, _, secondNote) = await inventory.CreateSalesDeliveryNoteAsync(null, null, other.Id,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 1m } }, "tester");
        var (_, _, second) = await inventory.CreateDeliveryIssueAsync(secondNote!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 1m }
        }, "tester");
        Assert.True((await inventory.IssueDeliveryAsync(second!.Id, "tester")).Success);

        var (ok, err, _) = await invoicing.CreateInvoiceFromIssuesAsync(new[] { first.Id, second.Id },
            new SaleInvoice { CustomerId = custId, InvoiceDate = new DateTime(2026, 5, 4) }, "tester");
        Assert.False(ok);
        Assert.Contains("عميل واحد", err);
    }

    [Fact]
    public async Task InvoiceOutstandingDeliveries_RequiresIssuedIssue()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var invoicing = new DeliveriesInvoicingService(db, inventory, new AccountingService(db));
        var orders = new SalesOrdersService(db, inventory, reservations: null, invoicing);

        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 12m, unitPrice: 90m);
        var (emptyOk, emptyErr, _) = await orders.InvoiceOutstandingDeliveriesAsync(orderId, "tester");
        Assert.False(emptyOk);
        Assert.Contains("لا توجد تسليمات غير مفوترة", emptyErr);

        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 12m } }, "tester");
        var orderLineId = await db.SalesOrderItems.Where(i => i.SalesOrderId == orderId).Select(i => i.Id).SingleAsync();
        var (_, _, issue) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 5m, SalesOrderItemId = orderLineId }
        }, "tester");
        Assert.True((await inventory.IssueDeliveryAsync(issue!.Id, "tester")).Success);

        var (ok, err, invoice) = await orders.InvoiceOutstandingDeliveriesAsync(orderId, "tester");
        Assert.True(ok, err);
        Assert.Equal(5m, invoice!.Items.Single().Quantity);
        Assert.Equal(90m, invoice.Items.Single().UnitPrice);
        Assert.Equal(orderId, invoice.SalesOrderId);
    }

    [Fact]
    public async Task IssueDelivery_UsesOwnReservation_WhenStockFullyReserved()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedAsync(db, qty: 40m);
        var reservations = new StockReservationsService(db);
        var inventory = new InventoryService(db, null, new AccountingService(db), reservations);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 40m); Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);

        var item = await db.Items.SingleAsync();
        Assert.Equal(0m, item.AvailableQuantity);
        Assert.Equal(40m, item.ReservedQuantity);

        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 40m } }, "tester");
        var orderLineId = await db.SalesOrderItems.Where(i => i.SalesOrderId == orderId).Select(i => i.Id).SingleAsync();
        var (_, _, issue) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 25m, SalesOrderItemId = orderLineId }
        }, "tester");

        var (ok, err) = await inventory.IssueDeliveryAsync(issue!.Id, "tester");
        Assert.True(ok, err);
        var after = await db.Items.AsNoTracking().SingleAsync();
        Assert.Equal(15m, after.CurrentQuantity);
        Assert.Equal(15m, after.ReservedQuantity);
        Assert.Equal(0m, after.AvailableQuantity);
    }

    [Fact]
    public async Task IssueDelivery_CannotUseOtherOrdersReservation()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var reservations = new StockReservationsService(db);
        var inventory = new InventoryService(db, null, new AccountingService(db), reservations);

        var reserving = await CreateApprovedOrderAsync(db, itemId, custId, 70m);
        Assert.True((await reservations.ReserveOrderAsync(reserving, "tester")).Success);
        var availableNow = (await db.Items.AsNoTracking().SingleAsync()).AvailableQuantity;
        Assert.Equal(30m, availableNow);

        var other = await CreateApprovedOrderAsync(db, itemId, custId, 50m);
        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(other, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 50m } }, "tester");
        var orderLineId = await db.SalesOrderItems.Where(i => i.SalesOrderId == other).Select(i => i.Id).SingleAsync();
        var (_, _, issue) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 50m, SalesOrderItemId = orderLineId }
        }, "tester");

        var (ok, err) = await inventory.IssueDeliveryAsync(issue!.Id, "tester");
        Assert.False(ok);
        Assert.Contains("الرصيد المتاح غير كافٍ", err);
        Assert.Equal(100m, (await db.Items.AsNoTracking().SingleAsync()).CurrentQuantity);
    }

    [Fact]
    public async Task DraftIssue_DoesNotCountAsDelivered()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedAsync(db);
        var reservations = new StockReservationsService(db);
        var inventory = new InventoryService(db, null, new AccountingService(db), reservations);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 20m);
        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);
        var orderLineId = await db.SalesOrderItems.Where(i => i.SalesOrderId == orderId).Select(i => i.Id).SingleAsync();

        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 20m } }, "tester");
        var (_, _, draft) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 5m, SalesOrderItemId = orderLineId }
        }, "tester");
        Assert.NotNull(draft);

        Assert.Equal(DeliveryOrderStatus.Draft, (await db.DeliveryOrders.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(100m, (await db.Items.AsNoTracking().SingleAsync()).CurrentQuantity);

        var (_, _, second) = await inventory.CreateDeliveryIssueAsync(note.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 15m, SalesOrderItemId = orderLineId }
        }, "tester");
        Assert.True((await inventory.IssueDeliveryAsync(second!.Id, "tester")).Success);
        var delivery = await db.DeliveryOrders.AsNoTracking().SingleAsync();
        Assert.Equal(DeliveryOrderStatus.PartiallyIssued, delivery.Status);
    }

    [Fact]
    public async Task CreateNote_FromOrder_UsesPendingLinesOnly()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var reservations = new StockReservationsService(db);
        var inventory = new InventoryService(db, null, new AccountingService(db), reservations);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 30m);
        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);

        var (firstOk, firstErr, first) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 20m } }, "tester");
        Assert.True(firstOk, firstErr);
        Assert.Equal(20m, first!.Items.Single().Quantity);

        var (ok, err, _) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 25m } }, "tester");
        Assert.False(ok);
        Assert.Contains("أكبر من المتبقي في أمر البيع", err);

        var (secondOk, secondErr, second) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 10m } }, "tester");
        Assert.True(secondOk, secondErr);
        Assert.NotNull(second);

        var notes = await db.DeliveryOrders.AsNoTracking().Include(d => d.Items)
            .Where(d => d.SalesOrderId == orderId).ToListAsync();
        Assert.Equal(2, notes.Count);
        Assert.Equal(30m, notes.Sum(n => n.Items.Sum(i => i.Quantity)));
    }

    [Fact]
    public async Task CreateNote_FreeForm_RequiresCustomer()
    {
        using var db = CreateContext();
        var (itemId, _) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        var (noCustomer, err, _) = await inventory.CreateSalesDeliveryNoteAsync(null, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 1m } }, "tester");
        Assert.False(noCustomer);
        Assert.Contains("يرجى اختيار العميل", err);

        var customer = await db.Customers.AsNoTracking().FirstAsync();
        var (ok, _, note) = await inventory.CreateSalesDeliveryNoteAsync(null, null, customer.Id,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 3m } }, "tester");
        Assert.True(ok);
        Assert.Null(note!.SalesOrderId);
        Assert.Null(note.SaleInvoiceId);
    }

    [Fact]
    public async Task LegacyDirectSale_StaysAtDelivery()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));

        var invoice = new SaleInvoice { CustomerId = custId, InvoiceDate = new DateTime(2026, 5, 4) };
        var (ok, err) = await inventory.CreateSaleAsync(invoice, new List<SaleInvoiceItem>
        {
            new() { ItemId = itemId, Quantity = 5m, UnitPrice = 80m }
        }, "tester");

        Assert.True(ok, err);
        var stored = await db.SaleInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(SalesPostingMode.AtDelivery, stored.PostingMode);
        Assert.Equal(SalesPostingMode.AtDelivery, invoice.PostingMode);
    }

    [Fact]
    public async Task LegacyInvoiceFromOrder_IsBlocked_WhenDeliveryNoteExists()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var orders = new SalesOrdersService(db, inventory);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 3m);
        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 3m } }, "tester");

        var (ok, err) = await orders.CreateInvoiceFromOrderAsync(orderId, "tester");
        Assert.False(ok);
        Assert.Contains("أوامر التسليم", err);
        Assert.Equal(0, await db.SaleInvoices.CountAsync());
    }

    [Fact]
    public async Task CreateDeliveryIssue_PersistsIssueDateCarrierAndTracking()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var inventory = new InventoryService(db, new AccountingService(db));
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 4m);
        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 4m } }, "tester");

        var issueDate = new DateTime(2026, 7, 9);
        var (ok, err, issue) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 4m }
        }, "tester", issueDate, "ملاحظة Bowman", "شركة الشحن", "TRK-9911");

        Assert.True(ok, err);
        Assert.NotNull(issue);
        Assert.Equal(issueDate, issue!.IssueDate);
        Assert.Equal("شركة الشحن", issue.Carrier);
        Assert.Equal("TRK-9911", issue.TrackingNumber);
        Assert.Equal("ملاحظة Bowman", issue.Notes);

        var stored = await db.DeliveryIssues.AsNoTracking().SingleAsync(i => i.Id == issue.Id);
        Assert.Equal(issueDate, stored.IssueDate);
        Assert.Equal("شركة الشحن", stored.Carrier);
        Assert.Equal("TRK-9911", stored.TrackingNumber);
        Assert.Equal("ملاحظة Bowman", stored.Notes);
    }

    [Fact]
    public async Task ReturnAgainstAtInvoiceInvoice_MirrorsGrossRevenueAndReleasesCost()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedAsync(db);
        db.StockLayers.Add(new StockLayer
        {
            ItemId = itemId,
            Qty = 100,
            Count = 0,
            UnitCost = 60m,
            RemainingQty = 100,
            RemainingCount = 0,
            DateReceived = new DateTime(2026, 1, 1),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var reservations = new StockReservationsService(db);
        var accounting = new AccountingService(db);
        var inventory = new InventoryService(db, accounting, reservations);
        var orderId = await CreateApprovedOrderAsync(db, itemId, custId, 50m, unitPrice: 100m);
        Assert.True((await reservations.ReserveOrderAsync(orderId, "tester")).Success);

        var (_, _, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 50m } }, "tester");
        var orderLineId = await db.SalesOrderItems.Where(i => i.SalesOrderId == orderId).Select(i => i.Id).SingleAsync();
        var (_, _, issue) = await inventory.CreateDeliveryIssueAsync(note!.Id, new List<DeliveryIssueItem>
        {
            new() { ItemId = itemId, Quantity = 20m, SalesOrderItemId = orderLineId }
        }, "tester");
        Assert.True((await inventory.IssueDeliveryAsync(issue!.Id, "tester")).Success);

        var invoicing = new DeliveriesInvoicingService(db, inventory, accounting);
        var (invOk, invErr, invoice) = await invoicing.CreateInvoiceFromIssuesAsync(
            new[] { issue!.Id },
            new SaleInvoice { CustomerId = custId, InvoiceDate = new DateTime(2026, 5, 4), Tax = 30m },
            "tester");
        Assert.True(invOk, invErr);
        Assert.Equal(SalesPostingMode.AtInvoice, invoice!.PostingMode);
        Assert.Equal(2030m, invoice.NetAmount);

        // مرتجع نصي (10 من أصل 20) يجب أن ينعكس تناسبياً: 1000 contra + 15 ضريبة
        var invoiceLine = invoice.Items.Single();
        var saleReturn = new SaleReturn
        {
            ReturnNumber = "SR-1",
            CustomerId = custId,
            SaleInvoiceId = invoice.Id,
            ReturnDate = new DateTime(2026, 5, 20),
            TotalAmount = 1000m,
            Reason = "مرتجع جزئي",
            CreatedBy = "tester",
            Items = new List<SaleReturnItem>
            {
                new() { ItemId = itemId, Quantity = 10m, UnitPrice = invoiceLine.UnitPrice }
            }
        };

        var (retOk, retErr) = await inventory.CreateSaleReturnAsync(saleReturn, saleReturn.Items.ToList(), "tester");
        Assert.True(retOk, retErr);
        Assert.Equal(ReturnStatus.Posted, saleReturn.Status);

        var entries = await db.JournalEntries.Include(e => e.Lines).OrderBy(e => e.Id).ToListAsync();
        var mirrored = entries.Single(e => e.Source == JournalSource.SaleReturn);
        Assert.Equal(saleReturn.Id, mirrored.SourceId);

        var codes = await db.GLAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Code);
        var debit = new Dictionary<string, decimal>();
        var credit = new Dictionary<string, decimal>();
        foreach (var line in mirrored.Lines)
        {
            var code = codes[line.AccountId];
            debit[code] = debit.TryGetValue(code, out var d) ? d + line.Debit : line.Debit;
            credit[code] = credit.TryGetValue(code, out var c) ? c + line.Credit : line.Credit;
        }

        Assert.Equal(1000m, debit["5101"]);
        Assert.Equal(15m, debit["2055"]);
        Assert.Equal(1015m, credit["1200"]);
        Assert.Equal(600m, debit["1300"]);
        Assert.Equal(600m, credit["5000"]);
        Assert.Equal(mirrored.Lines.Sum(l => l.Debit), mirrored.Lines.Sum(l => l.Credit));

        // المرتجع يعيد 10 من أصل 20 مسلَّمة، فيعود المخزون فقط ولا يمس عدّادات الفوترة
        var itemAfter = await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        Assert.Equal(90m, itemAfter.CurrentQuantity);
        var orderLineAfter = await db.SalesOrderItems.AsNoTracking().SingleAsync(i => i.Id == orderLineId);
        Assert.Equal(20m, orderLineAfter.InvoicedQty);
        Assert.Equal(20m, orderLineAfter.DeliveredQty);
    }
}
