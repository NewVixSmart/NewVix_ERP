using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NewVixSmart.Web.Controllers;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Sales;
using NewVixSmart.Web.ViewModels.Stock;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// The end-to-end consequence of the precision policy: a fourth-decimal quantity and a third-decimal
/// unit price survive the whole path from the operator's keystroke to the stored row, and the
/// delivered-settled rule is finally asked about the real figure.
/// <para>
/// This is the complement of <see cref="PrecisionColumnMetadataTests"/> and
/// <see cref="PrecisionRoundTripTests"/>. Neither of those can fail if the application rounds a value
/// on its way in: the column is declared <c>decimal(20,4)</c> and still receives 99.99, and a bare
/// round-trip never asks the controller or the services what they did to the figure first. Only a test
/// that drives the real controller and the real services, and then reads back what the business logic
/// itself computed, can catch a throttle on the path.
/// </para>
/// <para>
/// Every expected figure is written out literally rather than derived from <c>DecimalPrecision</c>: a
/// test that took its expectations from the same constants the production code rounds to would agree
/// with any width, including the old two-decimal one.
/// </para>
/// </summary>
public sealed class PrecisionPathDeliveryTests : IDisposable
{
    /// <summary>
    /// The exact message both input screens put in TempData when a figure is finer than the grid. Copied
    /// literally, so a change to the wording of the refusal shows up here as a test failure rather than
    /// passing silently.
    /// </summary>
    internal const string QuantityStepError =
        "الكمية والعدد يجب أن تكونا بأربع خانات عشرية كحدٍّ أقصى — أصغر خطوة يمكن تسجيلها هي 0.0001";

    /// <summary>The case the migration exists to support: ordered 100, issued 99.9957 — a shortfall of
    /// fifty-three ten-thousandths of a unit.</summary>
    internal const decimal OrderedQuantity = 100m;

    internal const decimal DeliveredQuantity = 99.9957m;

    /// <summary>A third-decimal unit price, at the width <c>SalesOrderItem.UnitPrice</c> declares.</summary>
    internal const decimal UnitPriceValue = 12.345m;

    /// <summary>The layer cost used where a test needs stock to move.</summary>
    private const decimal _layerCost = 60m;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public PrecisionPathDeliveryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    /// <summary>
    /// Renders a figure the way the column does, so that an assertion distinguishes 99.9957 from a
    /// 99.99 that happens to be equal under some other comparison. <see cref="decimal"/> equality
    /// already distinguishes them — 99.9957m != 99.99m — but the stored string is what a store that
    /// truncates rather than rounds would hand back, and a value read as text would show it.
    /// </summary>
    internal static string Stored(decimal value) => value.ToString(CultureInfo.InvariantCulture);

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

    private static async Task<(int ItemId, int CustomerId)> SeedItemAsync(
        AppDbContext db, decimal quantity = OrderedQuantity)
    {
        var unit = new Unit { Name = "قطعة" };
        db.Units.Add(unit);
        db.Items.Add(new Item
        {
            Name = "صنف الدقة",
            Category = new ItemCategory { Name = "تصنيف الدقة" },
            ItemType = new ItemType { Name = "نوع الدقة" },
            CountUnit = unit,
            QuantityUnit = unit,
            PurchasePrice = 50m,
            SalePrice = 80m,
            CurrentQuantity = quantity,
            CurrentCount = 0m
        });
        db.Customers.Add(new Customer { Name = "عميل الدقة" });
        await db.SaveChangesAsync();

        return (db.Items.Single().Id, db.Customers.Single().Id);
    }

    private static async Task SeedLayerAsync(AppDbContext db, int itemId, decimal qty, decimal unitCost,
        int? warehouseId = null)
    {
        db.StockLayers.Add(new StockLayer
        {
            ItemId = itemId,
            WarehouseId = warehouseId,
            Qty = qty,
            Count = 0m,
            UnitCost = unitCost,
            CountCost = unitCost,
            DateReceived = new DateTime(2026, 1, 1),
            RemainingQty = qty,
            RemainingCount = 0m,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<int> CreateApprovedOrderAsync(
        AppDbContext db, int itemId, int customerId, decimal quantity, decimal unitPrice, decimal count = 0m)
    {
        var orders = new SalesOrdersService(db, new InventoryService(db));
        var order = new SalesOrder { CustomerId = customerId, OrderDate = DateTime.Today };
        var (ok, error) = await orders.CreateOrderAsync(order,
            [new SalesOrderItem { ItemId = itemId, Quantity = quantity, Count = count, UnitPrice = unitPrice }],
            "tester");
        Assert.True(ok, error);

        var stored = await db.SalesOrders.FindAsync(order.Id);
        stored!.Status = SalesOrderStatus.Approved;
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static async Task<DeliveryOrder> CreateNoteAsync(
        AppDbContext db, InventoryService inventory, int orderId, int itemId, decimal quantity)
    {
        var (ok, error, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            [new DeliveryOrderItem { ItemId = itemId, Quantity = quantity }], "tester");
        Assert.True(ok, error);
        return note!;
    }

    private static async Task<DeliveryIssue> CreateIssueAsync(
        AppDbContext db, InventoryService inventory, int noteId, int orderId, int itemId, decimal quantity)
    {
        var orderLineId = await db.SalesOrderItems
            .Where(i => i.SalesOrderId == orderId && i.ItemId == itemId)
            .Select(i => (int?)i.Id).SingleAsync();
        var (ok, error, issue) = await inventory.CreateDeliveryIssueAsync(noteId,
            [new DeliveryIssueItem { ItemId = itemId, Quantity = quantity, SalesOrderItemId = orderLineId }],
            "tester");
        Assert.True(ok, error);
        return issue!;
    }

    /// <summary>
    /// Drives the real <see cref="DeliveryIssuesController.Create(DeliveryIssueViewModel)"/> action with
    /// the posted view model — no direct service call — so the controller's own input check is on the
    /// path under test. It opens its own context, as a request would, and hands back what the action
    /// returned together with the error it put in TempData, so a refusal can be told from a write.
    /// </summary>
    private async Task<(IActionResult Result, string? Error)> PostIssueAsync(
        int noteId, decimal quantity, decimal count = 0m)
    {
        using var db = CreateContext();
        var note = await db.DeliveryOrders.AsNoTracking()
            .Include(d => d.Items)
            .SingleAsync(d => d.Id == noteId);
        var line = note.Items.Single();

        // The order line the posted item belongs to, exactly as the raise-an-issue screen posts it: it
        // is what carries the delivery back to SalesOrderItem.DeliveredQty when the issue is posted.
        var orderLineId = note.SalesOrderId.HasValue
            ? await db.SalesOrderItems.AsNoTracking()
                .Where(i => i.SalesOrderId == note.SalesOrderId && i.ItemId == line.ItemId)
                .Select(i => (int?)i.Id).SingleAsync()
            : null;

        var controller = new DeliveryIssuesController(db, new InventoryService(db));
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "tester")], "TestAuth"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        var tempData = new TempDataDictionary(http, new FakeTempDataProvider());
        controller.TempData = tempData;

        var result = await controller.Create(new DeliveryIssueViewModel
        {
            Issue = new DeliveryIssue
            {
                DeliveryOrderId = note.Id,
                CustomerId = note.CustomerId,
                IssueDate = note.DeliveryDate
            },
            Items =
            [
                new DeliveryIssueItem
                {
                    ItemId = line.ItemId,
                    DeliveryOrderItemId = line.Id,
                    SalesOrderItemId = orderLineId,
                    Quantity = quantity,
                    Count = count
                }
            ]
        });

        return (result, tempData["Error"] as string);
    }

    private static decimal DebitOf(JournalEntry entry, string code, Dictionary<int, string> codes)
        => entry.Lines.Where(l => codes[l.AccountId] == code).Sum(l => l.Debit);

    private static decimal CreditOf(JournalEntry entry, string code, Dictionary<int, string> codes)
        => entry.Lines.Where(l => codes[l.AccountId] == code).Sum(l => l.Credit);

    private sealed class FakeTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }

    /// <summary>
    /// The test that matters. A delivery of 99.9957 against an ordered 100, posted through the real
    /// controller and then posted to the ledger, must be stored as 99.9957 and must still read as an open
    /// line.
    /// <para>
    /// Two defects hide behind this one assertion. The controller rounded the posted quantity to two
    /// decimals, so 99.9957 became 100.00 — an order silently reported as fully delivered — or 99.99, and
    /// the balance was never storable at all. And once it is stored, the settled rule has to be asked
    /// about the real figure: 100 − 99.9957 is 0.0043, which is eighty-six times the 0.00005 tolerance
    /// and so leaves the line open, whereas the old two-decimal figures were 0.00 and 0.01, on opposite
    /// sides of a boundary that had nothing to do with the delivery.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Fourth_decimal_delivery_through_the_controller_is_stored_and_reported_open()
    {
        using var db = CreateContext();
        var (itemId, customerId) = await SeedItemAsync(db);
        await SeedLayerAsync(db, itemId, OrderedQuantity, _layerCost);
        var orderId = await CreateApprovedOrderAsync(db, itemId, customerId, OrderedQuantity, UnitPriceValue);
        var inventory = new InventoryService(db);
        var note = await CreateNoteAsync(db, inventory, orderId, itemId, OrderedQuantity);

        Assert.IsType<RedirectToActionResult>((await PostIssueAsync(note.Id, DeliveredQuantity)).Result);

        var issue = await db.DeliveryIssues.AsNoTracking().SingleAsync();
        Assert.Equal(DeliveryIssueStatus.Draft, issue.Status);

        using (var verify = CreateContext())
        {
            var stored = await verify.DeliveryIssueItems.AsNoTracking().SingleAsync();
            Assert.Equal(DeliveredQuantity, stored.Quantity);
            Assert.Equal(Stored(DeliveredQuantity), Stored(stored.Quantity));
            // Non-vacuity: the old code stored one of these two figures instead.
            Assert.NotEqual(decimal.Round(DeliveredQuantity, 2), stored.Quantity);
            Assert.NotEqual(OrderedQuantity, stored.Quantity);
        }

        Assert.True((await inventory.IssueDeliveryAsync(issue.Id, "tester")).Success);

        using (var verify = CreateContext())
        {
            var stored = await verify.DeliveryIssueItems.AsNoTracking().SingleAsync();
            Assert.Equal(DeliveredQuantity, stored.Quantity);
            Assert.Equal(Stored(DeliveredQuantity), Stored(stored.Quantity));

            var orderLine = await verify.SalesOrderItems.AsNoTracking().SingleAsync();
            Assert.Equal(DeliveredQuantity, orderLine.DeliveredQty);
            Assert.Equal(Stored(DeliveredQuantity), Stored(orderLine.DeliveredQty));
        }

        // The same shared predicate the raise-an-issue screen asks reports the line as still open.
        using (var verify = CreateContext())
        {
            var reloaded = await verify.DeliveryOrders
                .Include(d => d.Items)
                .Include(d => d.Issues).ThenInclude(i => i.Items)
                .AsNoTracking()
                .SingleAsync(d => d.Id == note.Id);

            Assert.Equal(DeliveryOrderStatus.PartiallyIssued, reloaded.Status);
            Assert.True(DeliveryOpenLines.HasOutstandingLines(reloaded),
                "A 0.0043 shortfall is a real balance, so the note must still offer itself for issuing.");
            Assert.Equal(0.0043m, OrderedQuantity - DeliveredQuantity);
            Assert.True(DeliveryOpenLines.HasOpenQuantity(OrderedQuantity, 0m, DeliveredQuantity, 0m));
            Assert.False(DeliveryOpenLines.IsLineSettled(OrderedQuantity, 0m, DeliveredQuantity, 0m));
        }
    }

    /// <summary>
    /// The complement of the test above: a delivery that really is complete closes the line. Without
    /// this, the previous test would also pass if the code refused to settle anything at all.
    /// </summary>
    [Fact]
    public async Task An_exactly_complete_delivery_settles_the_line()
    {
        using var db = CreateContext();
        var (itemId, customerId) = await SeedItemAsync(db);
        await SeedLayerAsync(db, itemId, OrderedQuantity, _layerCost);
        var orderId = await CreateApprovedOrderAsync(db, itemId, customerId, OrderedQuantity, UnitPriceValue);
        var inventory = new InventoryService(db);
        var note = await CreateNoteAsync(db, inventory, orderId, itemId, OrderedQuantity);

        Assert.IsType<RedirectToActionResult>((await PostIssueAsync(note.Id, OrderedQuantity)).Result);

        var issue = await db.DeliveryIssues.AsNoTracking().SingleAsync();
        Assert.True((await inventory.IssueDeliveryAsync(issue.Id, "tester")).Success);

        using var verify = CreateContext();
        var reloaded = await verify.DeliveryOrders
            .Include(d => d.Items)
            .Include(d => d.Issues).ThenInclude(i => i.Items)
            .AsNoTracking()
            .SingleAsync(d => d.Id == note.Id);

        Assert.Equal(DeliveryOrderStatus.Delivered, reloaded.Status);
        Assert.False(DeliveryOpenLines.HasOutstandingLines(reloaded));
        Assert.True(DeliveryOpenLines.IsFullyIssued(reloaded));
    }

    /// <summary>
    /// The same rule on the reservation screen: a figure the operator typed that is not already on the
    /// 0.0001 grid is refused and the Arabic step error is surfaced. The two controllers share the rule
    /// but not the constant — neither may borrow <see cref="DeliveryOpenLines.QuantityTolerance"/>, whose
    /// half-step window belongs to the settled-line boundary.
    /// </summary>
    [Theory]
    [InlineData(99.99575)]
    [InlineData(99.99574)]
    [InlineData(99.99576)]
    [InlineData(0.00005)]
    public async Task An_off_grid_reservation_is_refused_on_the_reservation_screen_too(decimal posted)
    {
        using var db = CreateContext();
        var (itemId, customerId) = await SeedItemAsync(db, quantity: 200m);
        var orderId = await CreateApprovedOrderAsync(db, itemId, customerId, 200m, UnitPriceValue);

        var controller = new StockReservationsController(db, new StockReservationsService(db));
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "tester")], "TestAuth"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        var tempData = new TempDataDictionary(http, new FakeTempDataProvider());
        controller.TempData = tempData;

        var result = await controller.Create(new StockReservationViewModel
        {
            Reservation = new StockReservation { CustomerId = customerId },
            StandaloneItems =
            [
                new StockReservationLine
                {
                    ItemId = itemId,
                    Quantity = posted
                }
            ]
        });

        Assert.IsType<ViewResult>(result);
        Assert.Equal(QuantityStepError, tempData["Error"] as string);
        Assert.False(await db.StockReservationLines.AnyAsync());
    }

    /// <summary>
    /// A figure the operator typed that is not already on the 0.0001 grid is refused, and nothing is
    /// written: no issue, no movement on the order line, and the Arabic step error in TempData.
    /// <para>
    /// The interesting cases are the ones a tolerance would wave through. <c>99.99575</c> is the exact
    /// midpoint, one twentieth of a hundredth off the grid; <c>99.99574</c> and <c>99.99576</c> sit just
    /// either side of it. Under the previous half-step window all three were accepted and silently
    /// rewritten to 99.9958 or 99.9957 — the store then reported a delivery nobody made. The check has
    /// to be exact, so all three are refused.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(99.99575)]   // exactly half a step off the grid — the old inclusive boundary
    [InlineData(99.99574)]   // four ten-thousandths below the midpoint
    [InlineData(99.99576)]   // four ten-thousandths above the midpoint
    [InlineData(99.995749)]  // finer than a hundredth in the last digit only
    [InlineData(99.995755)]
    [InlineData(0.00005)]    // a single half-step at the scale of the smallest figures
    [InlineData(100.00004)]  // a big figure still off the grid
    public async Task An_off_grid_quantity_is_refused_and_nothing_is_written(decimal posted)
    {
        using var db = CreateContext();
        var (itemId, customerId) = await SeedItemAsync(db, quantity: 200m);
        var orderId = await CreateApprovedOrderAsync(db, itemId, customerId, 200m, UnitPriceValue);
        var inventory = new InventoryService(db);
        var note = await CreateNoteAsync(db, inventory, orderId, itemId, 200m);

        var (result, error) = await PostIssueAsync(note.Id, posted);

        Assert.IsType<ViewResult>(result);
        Assert.Equal(QuantityStepError, error);

        using var verify = CreateContext();
        Assert.False(await verify.DeliveryIssues.AnyAsync(), "a refused posting must not create an issue");
        var orderLine = await verify.SalesOrderItems.AsNoTracking().SingleAsync();
        Assert.Equal(0m, orderLine.DeliveredQty);
    }

    /// <summary>
    /// A figure that is already on the grid is accepted and stored exactly as typed — including the
    /// ones a <c>double</c>-based check would get wrong, which is why the arithmetic is
    /// <see cref="decimal"/>.
    /// </summary>
    [Theory]
    [InlineData(99.9957)]
    [InlineData(99.995)]
    [InlineData(100)]
    [InlineData(0.0001)]   // the smallest figure the column can show
    [InlineData(0.0003)]   // (double)0.0003 * 10000 is 2.9999999999999996, not 3
    [InlineData(0.0006)]
    [InlineData(1.2345)]
    public async Task An_on_grid_quantity_is_accepted_and_stored_exactly(decimal posted)
    {
        using var db = CreateContext();
        var (itemId, customerId) = await SeedItemAsync(db, quantity: 200m);
        var orderId = await CreateApprovedOrderAsync(db, itemId, customerId, 200m, UnitPriceValue);
        var inventory = new InventoryService(db);
        var note = await CreateNoteAsync(db, inventory, orderId, itemId, 200m);

        Assert.IsType<RedirectToActionResult>((await PostIssueAsync(note.Id, posted)).Result);

        using var verify = CreateContext();
        var stored = await verify.DeliveryIssueItems.AsNoTracking().SingleAsync();
        Assert.Equal(posted, stored.Quantity);
    }

    /// <summary>
    /// The reason the check is written in <see cref="decimal"/> and not <see cref="double"/>.
    /// <para>
    /// <c>decimal</c> is base ten, so every multiple of 0.0001 — <c>0.0001</c> itself included — is
    /// exactly representable, and comparing the typed figure against its 4-decimal rounding is an exact
    /// divisibility test. <c>double</c> is base two: to test divisibility by the step you would multiply
    /// by 10000, and <c>(double)0.0003 * 10000</c> is 2.9999999999999996 rather than 3. Of the first
    /// four thousand four-decimal values on the grid, 558 fail that way, and a check built on it would
    /// refuse quantities this store can hold exactly.
    /// </para>
    /// </summary>
    [Fact]
    public void The_grid_check_holds_only_in_decimal_arithmetic()
    {
        // The figures are on the grid, and stay there in decimal.
        Assert.Equal(0.0003m, 0.0001m * 3m);
        Assert.Equal(0.0003m, decimal.Round(0.0003m, 4));
        Assert.Equal(0.0006m, 0.0001m * 6m);
        Assert.Equal(0.0006m, decimal.Round(0.0006m, 4));

        // The same figures are not on it in double, and would fail a divisibility test written there.
        foreach (var value in new[] { 0.0003d, 0.0006d, 0.0012d, 0.0024d })
        {
            var steps = value * 10000d;
            Assert.NotEqual(Math.Round(steps), steps);
        }
    }

    /// <summary>
    /// The settled-line boundary is a different question and is deliberately left where it was. A
    /// physical line counts as finished within half a step of its ordered quantity, because that
    /// shortfall can only have come out of arithmetic that has already been through a lossy step. This
    /// test pins that constant, so a future reader cannot "fix" the input check by borrowing it.
    /// </summary>
    [Fact]
    public void The_settled_line_tolerance_is_untouched_by_the_exact_input_check()
    {
        Assert.Equal(0.00005m, DeliveryOpenLines.QuantityTolerance);
    }

    /// <summary>
    /// The count dimension of the same column, and the reason the controller quantises both: a note sold
    /// by count has the same 0.0001 grid on its count, and a count that was rounded to a whole number
    /// would close a line that is still short.
    /// </summary>
    [Fact]
    public async Task A_posted_count_keeps_its_fourth_decimal_on_a_count_driven_line()
    {
        using var db = CreateContext();
        var (itemId, customerId) = await SeedItemAsync(db, quantity: 0m);
        db.Items.Single().CurrentCount = 200m;
        await db.SaveChangesAsync();

        var orderId = await CreateApprovedOrderAsync(db, itemId, customerId, 0m, UnitPriceValue, count: 200m);
        var inventory = new InventoryService(db);
        var (ok, error, note) = await inventory.CreateSalesDeliveryNoteAsync(orderId, null, null,
            [new DeliveryOrderItem { ItemId = itemId, Count = 200m }], "tester");
        Assert.True(ok, error);

        Assert.IsType<RedirectToActionResult>((await PostIssueAsync(note!.Id, 0m, 199.9957m)).Result);

        using var verify = CreateContext();
        var stored = await verify.DeliveryIssueItems.AsNoTracking().SingleAsync();
        Assert.Equal(0m, stored.Quantity);
        Assert.Equal(199.9957m, stored.Count);
        Assert.Equal(Stored(199.9957m), Stored(stored.Count));
        Assert.NotEqual(200m, stored.Count);
    }

    /// <summary>
    /// A fourth-decimal quantity on the reservation path, end to end: the residual a reservation is
    /// raised for keeps its fourth decimal, and consuming it writes that same figure to
    /// <c>ConsumedQuantity</c> instead of a rounded one.
    /// <para>
    /// The old behaviour rounded the need to a hundredth before asking whether the reservation was
    /// already spent, so a 99.9957 balance was matched against 100.00: the reservation closed one
    /// hundredth early, and the settled rule was handed an answer computed from a number that no longer
    /// existed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Reservation_residual_and_consumption_keep_the_fourth_decimal()
    {
        using var db = CreateContext();
        var (itemId, customerId) = await SeedItemAsync(db, quantity: 150m);

        var reservations = new StockReservationsService(db);
        var inventory = new InventoryService(db, null, reservations);

        // Order 100 units and deliver 0.0004 of them, so the residual is 99.9996 rather than the 100.00
        // a two-decimal figure would leave.
        var orderId = await CreateApprovedOrderAsync(db, itemId, customerId, 100m, UnitPriceValue);
        var note = await CreateNoteAsync(db, inventory, orderId, itemId, 100m);
        Assert.IsType<RedirectToActionResult>((await PostIssueAsync(note.Id, 0.0004m)).Result);
        var issue = await db.DeliveryIssues.AsNoTracking().SingleAsync();
        Assert.True((await inventory.IssueDeliveryAsync(issue.Id, "tester")).Success);

        var (reserved, reserveError) = await reservations.ReserveOrderAsync(orderId, "tester");
        Assert.True(reserved, reserveError);

        var line = await db.StockReservationLines.AsNoTracking().SingleAsync();
        Assert.Equal(99.9996m, line.Quantity);
        Assert.Equal(Stored(99.9996m), Stored(line.Quantity));
        Assert.NotEqual(100m, line.Quantity);

        var orderLineId = await db.SalesOrderItems
            .Where(i => i.SalesOrderId == orderId).Select(i => i.Id).SingleAsync();
        var (consumed, consumeError) = await reservations.ConsumeForIssuesAsync(
            [new DeliveryIssueItemLine(itemId, orderLineId, 99.9996m, 0m)], customerId, DateTime.Today);
        Assert.True(consumed, consumeError);

        var after = await db.StockReservationLines.AsNoTracking().SingleAsync();
        Assert.Equal(99.9996m, after.ConsumedQuantity);
        Assert.Equal(Stored(99.9996m), Stored(after.ConsumedQuantity));
        Assert.Equal(StockReservationStatus.Consumed,
            (await db.StockReservations.AsNoTracking().SingleAsync()).Status);
    }

    /// <summary>
    /// A third-decimal unit price survives the whole delivery-to-invoice path. It used to be rounded to
    /// 12.35 on the way in, which silently repriced every line computed from it.
    /// </summary>
    [Fact]
    public async Task Third_decimal_unit_price_survives_the_delivery_to_invoice_path()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, customerId) = await SeedItemAsync(db);
        await SeedLayerAsync(db, itemId, OrderedQuantity, _layerCost);

        var orderId = await CreateApprovedOrderAsync(db, itemId, customerId, OrderedQuantity, UnitPriceValue);
        var reservations = new StockReservationsService(db);
        var accounting = new AccountingService(db);
        var inventory = new InventoryService(db, accounting, reservations);
        var note = await CreateNoteAsync(db, inventory, orderId, itemId, OrderedQuantity);

        Assert.IsType<RedirectToActionResult>((await PostIssueAsync(note.Id, DeliveredQuantity)).Result);
        var issue = await db.DeliveryIssues.AsNoTracking().SingleAsync();
        Assert.True((await inventory.IssueDeliveryAsync(issue.Id, "tester")).Success);

        var invoicing = new DeliveriesInvoicingService(db, inventory, accounting);
        var (ok, error, invoice) = await invoicing.CreateInvoiceFromIssuesAsync(
            [issue.Id],
            new SaleInvoice { CustomerId = customerId, InvoiceDate = new DateTime(2026, 5, 4) },
            "tester");
        Assert.True(ok, error);

        var line = invoice!.Items.Single();
        Assert.Equal(UnitPriceValue, line.UnitPrice);
        Assert.Equal(Stored(UnitPriceValue), Stored(line.UnitPrice));
        Assert.NotEqual(decimal.Round(UnitPriceValue, 2), line.UnitPrice);

        // The invoiced quantity is the fourth-decimal figure, summed once and rounded to the quantity
        // grid — not a two-decimal approximation of it.
        Assert.Equal(DeliveredQuantity, line.Quantity);
        Assert.Equal(Stored(DeliveredQuantity), Stored(line.Quantity));

        // And the order knows exactly how much of itself is still to come.
        var orderLine = await db.SalesOrderItems.AsNoTracking().SingleAsync();
        Assert.Equal(DeliveredQuantity, orderLine.InvoicedQty);
        Assert.Equal(OrderedQuantity - DeliveredQuantity, orderLine.PendingQty);
    }

    /// <summary>
    /// The price-consistency guard still bites at the third decimal. Two delivery notes that bill
    /// genuinely different third-decimal prices are refused.
    /// <para>
    /// This is the guard at the top of the invoice builder, and it is exactly what quantising the price
    /// to a hundredth would have defeated: the two prices would be rounded onto the same value, the
    /// comparison would find them equal, and a delivery note billing a different price than the order
    /// would pass unnoticed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Two_delivery_notes_with_different_third_decimal_prices_are_still_refused()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, customerId) = await SeedItemAsync(db, quantity: 500m);
        await SeedLayerAsync(db, itemId, 500m, _layerCost);

        var reservations = new StockReservationsService(db);
        var accounting = new AccountingService(db);
        var inventory = new InventoryService(db, accounting, reservations);

        // Two orders, one customer, same item, prices differing only in the third decimal. The pair is
        // chosen so the two are equal once rounded to a hundredth — 12.344 and 12.345 are both 12.34
        // at two decimals — so the guard can only fire if the comparison happens at the column's own
        // width. A pair like 12.345 and 12.346 would straddle a rounding boundary and agree by luck
        // even with the throttle back in place, which would make this test pass for the wrong reason.
        var firstOrder = await CreateApprovedOrderAsync(db, itemId, customerId, 100m, 12.344m);
        var secondOrder = await CreateApprovedOrderAsync(db, itemId, customerId, 100m, 12.345m);
        Assert.Equal(decimal.Round(12.344m, 2), decimal.Round(12.345m, 2));
        Assert.True((await reservations.ReserveOrderAsync(firstOrder, "tester")).Success);
        Assert.True((await reservations.ReserveOrderAsync(secondOrder, "tester")).Success);

        var firstNote = await CreateNoteAsync(db, inventory, firstOrder, itemId, 10m);
        var firstIssue = await CreateIssueAsync(db, inventory, firstNote.Id, firstOrder, itemId, 10m);
        Assert.True((await inventory.IssueDeliveryAsync(firstIssue.Id, "tester")).Success);

        var secondNote = await CreateNoteAsync(db, inventory, secondOrder, itemId, 10m);
        var secondIssue = await CreateIssueAsync(db, inventory, secondNote.Id, secondOrder, itemId, 10m);
        Assert.True((await inventory.IssueDeliveryAsync(secondIssue.Id, "tester")).Success);

        var invoicing = new DeliveriesInvoicingService(db, inventory, accounting);
        var (ok, error, invoice) = await invoicing.CreateInvoiceFromIssuesAsync(
            [firstIssue.Id, secondIssue.Id],
            new SaleInvoice { CustomerId = customerId, InvoiceDate = new DateTime(2026, 5, 4) },
            "tester");

        Assert.False(ok, "12.344 and 12.345 are different prices; quantising them to a hundredth "
            + "before comparing would have let them agree and billed whichever one came first.");
        Assert.Contains("اختلف سعر الصنف", error);
        Assert.Null(invoice);
        Assert.Equal(0, await db.SaleInvoices.CountAsync());
    }

    /// <summary>
    /// The money regression guard, at the point where an amount becomes an accounting fact. The operands
    /// carry a fourth and a third decimal; every figure the ledger records is still on the piastre.
    /// <para>
    /// 99.9957 × 12.345 is 1234.4469165, and with 30 of tax the receivable is 1264.4469165. Both are
    /// recorded as 1234.45 and 1264.45, the tax stays whole, and each entry still balances — so
    /// widening the quantity and the price did not widen the ledger.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Money_on_the_touched_paths_is_still_recorded_on_the_piastre()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, customerId) = await SeedItemAsync(db);
        await SeedLayerAsync(db, itemId, OrderedQuantity, _layerCost);

        var orderId = await CreateApprovedOrderAsync(db, itemId, customerId, OrderedQuantity, UnitPriceValue);
        var reservations = new StockReservationsService(db);
        var accounting = new AccountingService(db);
        var inventory = new InventoryService(db, accounting, reservations);
        var note = await CreateNoteAsync(db, inventory, orderId, itemId, OrderedQuantity);

        Assert.IsType<RedirectToActionResult>((await PostIssueAsync(note.Id, DeliveredQuantity)).Result);
        var issue = await db.DeliveryIssues.AsNoTracking().SingleAsync();
        Assert.True((await inventory.IssueDeliveryAsync(issue.Id, "tester")).Success);

        var invoicing = new DeliveriesInvoicingService(db, inventory, accounting);
        var (ok, error, _) = await invoicing.CreateInvoiceFromIssuesAsync(
            [issue.Id],
            new SaleInvoice { CustomerId = customerId, InvoiceDate = new DateTime(2026, 5, 4), Tax = 30m },
            "tester");
        Assert.True(ok, error);

        // Non-vacuity: the raw product really does carry more than two decimals, so the figures below
        // are the result of rounding and not of an operand that happened to be whole.
        Assert.Equal(1234.4469165m, DeliveredQuantity * UnitPriceValue);
        Assert.Equal(5999.742m, DeliveredQuantity * _layerCost);

        var entries = await db.JournalEntries.Include(e => e.Lines).ToListAsync();
        var revenue = entries.Single(e => e.Source == JournalSource.SaleInvoice);
        var cost = entries.Single(e => e.Source == JournalSource.SaleDeliveryIssue);
        var codes = await db.GLAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Code);

        // Dr 1200 1264.45 / Cr 4000 1234.45 / Cr 2055 30.00
        Assert.Equal(1264.45m, DebitOf(revenue, "1200", codes));
        Assert.Equal(1234.45m, CreditOf(revenue, "4000", codes));
        Assert.Equal(30m, CreditOf(revenue, "2055", codes));
        Assert.Equal(revenue.Lines.Sum(l => l.Debit), revenue.Lines.Sum(l => l.Credit));

        // Dr 5000 5999.74 / Cr 1300 5999.74 — 99.9957 units at 60.00, on the piastre.
        Assert.Equal(5999.74m, DebitOf(cost, "5000", codes));
        Assert.Equal(5999.74m, CreditOf(cost, "1300", codes));
        Assert.Equal(cost.Lines.Sum(l => l.Debit), cost.Lines.Sum(l => l.Credit));

        foreach (var entry in entries)
        {
            foreach (var journalLine in entry.Lines)
            {
                Assert.Equal(Stored(decimal.Round(journalLine.Debit, 2)), Stored(journalLine.Debit));
                Assert.Equal(Stored(decimal.Round(journalLine.Credit, 2)), Stored(journalLine.Credit));
            }
        }
    }

    /// <summary>
    /// A purchase price reaches the stock layer at the six-decimal cost width, not at a price width and
    /// certainly not at two. The layer is the record every later valuation reads, so a third decimal
    /// discarded here is a cost that never reconciles.
    /// </summary>
    [Fact]
    public async Task A_purchase_price_reaches_the_stock_layer_at_the_six_decimal_cost_width()
    {
        using var db = CreateContext();
        var (itemId, _) = await SeedItemAsync(db, quantity: 0m);
        var supplier = new Supplier { Name = "مورد الدقة" };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var inventory = new InventoryService(db);
        var (ok, error) = await inventory.CreatePurchaseAsync(
            new PurchaseInvoice { SupplierId = supplier.Id, InvoiceDate = new DateTime(2026, 2, 1) },
            [new PurchaseInvoiceItem { ItemId = itemId, Quantity = 4m, Count = 0m, UnitPrice = UnitPriceValue }],
            "tester");
        Assert.True(ok, error);

        var layer = await db.StockLayers.AsNoTracking().SingleAsync();
        Assert.Equal(UnitPriceValue, layer.UnitCost);
        Assert.Equal(Stored(UnitPriceValue), Stored(layer.UnitCost));
        Assert.NotEqual(decimal.Round(UnitPriceValue, 2), layer.UnitCost);
    }

    /// <summary>
    /// The derived per-unit cost on a transfer keeps more than two decimals. A layer cost that is not on
    /// a hundredth grid produces a per-unit cost that is not on one either, and forcing it onto two is
    /// what made the layers on either side of a transfer stop adding back to the cost that left.
    /// </summary>
    [Fact]
    public async Task A_derived_transfer_unit_cost_keeps_more_than_two_decimals()
    {
        using var db = CreateContext();
        var (itemId, _) = await SeedItemAsync(db, quantity: 3m);
        var (source, target) = await SeedWarehousesAsync(db, "PP");
        // 3 units of stock: a per-unit cost of 3.333333, which no hundredth grid can hold.
        await SeedLayerAsync(db, itemId, 3m, 3.333333m, source);

        var inventory = new InventoryService(db);
        var (ok, error) = await inventory.CreateTransferAsync(
            new StockTransfer { SourceWarehouseId = source, TargetWarehouseId = target, TransferDate = DateTime.UtcNow },
            [new StockTransferItem { ItemId = itemId, Quantity = 3m, Count = 0m }], "tester");
        Assert.True(ok, error);

        var moved = await db.StockTransferItems.AsNoTracking().SingleAsync();
        Assert.Equal(3.333333m, moved.UnitCost);
        Assert.Equal(Stored(3.333333m), Stored(moved.UnitCost));
        Assert.NotEqual(3.33m, moved.UnitCost);

        // And the cost arrived in the target warehouse at the same width.
        var targetLayer = await db.StockLayers.AsNoTracking().SingleAsync(l => l.WarehouseId == target);
        Assert.Equal(3.333333m, targetLayer.UnitCost);
        Assert.Equal(0m, (await db.StockLayers.AsNoTracking().SingleAsync(l => l.WarehouseId == source)).RemainingQty);
    }

    /// <summary>
    /// A transfer line that asks for nothing is refused cleanly. It never reaches the division the
    /// per-unit cost is derived from, and the source warehouse is left as it was.
    /// </summary>
    [Fact]
    public async Task A_zero_quantity_transfer_is_refused_cleanly_rather_than_throwing()
    {
        using var db = CreateContext();
        var (itemId, _) = await SeedItemAsync(db, quantity: 3m);
        var (source, target) = await SeedWarehousesAsync(db, "PPZ");
        await SeedLayerAsync(db, itemId, 3m, 3.333333m, source);

        var inventory = new InventoryService(db);
        var (ok, error) = await inventory.CreateTransferAsync(
            new StockTransfer { SourceWarehouseId = source, TargetWarehouseId = target, TransferDate = DateTime.UtcNow },
            [new StockTransferItem { ItemId = itemId, Quantity = 0m, Count = 0m }], "tester");

        Assert.False(ok);
        Assert.Contains("صنف واحد على الأقل", error);
        Assert.Equal(0, await db.StockTransfers.CountAsync());
        Assert.Equal(3m, (await db.StockLayers.AsNoTracking().SingleAsync()).RemainingQty);
    }

    private static async Task<(int SourceId, int TargetId)> SeedWarehousesAsync(AppDbContext db, string tag)
    {
        var source = new Warehouse { Code = $"{tag}-SRC", Name = $"مصدر {tag}", IsActive = true };
        var target = new Warehouse { Code = $"{tag}-DST", Name = $"هدف {tag}", IsActive = true };
        db.Warehouses.AddRange(source, target);
        await db.SaveChangesAsync();
        return (source.Id, target.Id);
    }
}

/// <summary>
/// Binds the figures these tests store to the widths the schema declares for them.
/// <para>
/// The exhaustive width check is
/// <see cref="PrecisionColumnMetadataTests.Every_mapped_decimal_column_has_the_expected_store_type"/>:
/// it already asserts the store type of every mapped decimal in the model, and it is not repeated
/// here. What is asserted here is the narrower thing it cannot see — that the literals in this file
/// fit the columns they are written to. If a fixture were later raised to a fifth decimal, or a cost to
/// a seventh, this fails with the column named, instead of the fixture failing as an unexplained
/// equality or silently storing more than the store can hold.
/// </para>
/// <para>
/// SQLite does not enforce decimal scale, so the widths are read from the SQL Server model with a
/// connection string that is never opened, in the same style as
/// <see cref="PrecisionColumnMetadataTests"/>.
/// </para>
/// </summary>
public sealed class PrecisionPathScaleFitsFixtureTests
{
    private const string _unreachableServer =
        "Server=precision-path-probe;Database=PrecisionPathProbe;Integrated Security=True;TrustServerCertificate=True";

    private static IModel SqlServerModel
    {
        get
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_unreachableServer).Options;
            using var context = new AppDbContext(options);
            return context.Model;
        }
    }

    /// <summary>The figures this file writes, each against the column it is written to.</summary>
    public static TheoryData<string, decimal> StoredFigures() => new()
    {
        { "DeliveryIssueItem.Quantity", PrecisionPathDeliveryTests.DeliveredQuantity },
        { "DeliveryIssueItem.Count", 199.9957m },
        { "SalesOrderItem.DeliveredQty", PrecisionPathDeliveryTests.DeliveredQuantity },
        { "SalesOrderItem.InvoicedQty", PrecisionPathDeliveryTests.DeliveredQuantity },
        { "StockReservationLine.Quantity", 99.9996m },
        { "StockReservationLine.ConsumedQuantity", 99.9996m },
        { "SaleInvoiceItem.Quantity", PrecisionPathDeliveryTests.DeliveredQuantity },
        { "SalesOrderItem.UnitPrice", PrecisionPathDeliveryTests.UnitPriceValue },
        { "SaleInvoiceItem.UnitPrice", PrecisionPathDeliveryTests.UnitPriceValue },
        { "PurchaseInvoiceItem.UnitPrice", PrecisionPathDeliveryTests.UnitPriceValue },
        { "StockLayer.UnitCost", 3.333333m },
        { "StockTransferItem.UnitCost", 3.333333m },
    };

    [Theory]
    [MemberData(nameof(StoredFigures))]
    public void The_figure_a_test_stores_fits_the_declared_width_of_its_column(string column, decimal value)
    {
        var dot = column.IndexOf('.');
        var entity = Assert.Single(SqlServerModel.GetEntityTypes(), t => t.ClrType.Name == column[..dot]);
        var scale = Assert.IsType<int>(entity.FindProperty(column[(dot + 1)..])!.GetScale());

        // Count the decimals the figure itself carries, so 100 and 12.345 are not treated as four-digit
        // and three-digit figures respectively.
        var figuresOwnScale = (decimal.GetBits(value)[3] >> 16) & 0xFF;
        Assert.True(figuresOwnScale <= scale,
            $"{column} is decimal(18,{scale}) but the test stores {PrecisionPathDeliveryTests.Stored(value)}, which carries "
            + $"{figuresOwnScale} decimals.");
    }
}
