using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class ProcurementServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public ProcurementServiceTests()
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
            ("3000", "رأس المال", GLAccountType.Equity, NormalBalance.Credit),
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.SaveChanges();
    }

    private async Task<(int itemId, int supId)> SeedAsync(AppDbContext db)
    {
        var cat = new ItemCategory { Name = "تصنيف شراء" };
        var type = new ItemType { Name = "نوع شراء" };
        var unit = new Unit { Name = "قطعة" };
        db.ItemCategories.Add(cat);
        db.ItemTypes.Add(type);
        db.Units.Add(unit);

        var item = new Item
        {
            Name = "صنف شراء",
            Category = cat,
            ItemType = type,
            CountUnit = unit,
            QuantityUnit = unit,
            PurchasePrice = 40,
            SalePrice = 60,
            CurrentCount = 0,
            CurrentQuantity = 0
        };
        db.Items.Add(item);

        var supplier = new Supplier { Name = "مورد شراء" };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        return (item.Id, supplier.Id);
    }

    private (ProcurementService proc, InventoryService inv) Services(AppDbContext db)
    {
        var accounting = new AccountingService(db);
        var inv = new InventoryService(db, accounting);
        var proc = new ProcurementService(db, inv);
        return (proc, inv);
    }

    private async Task<int> CreateApprovedOrderAsync(AppDbContext db, int itemId, int supId, decimal qty)
    {
        var (proc, _) = Services(db);
        var order = new PurchaseOrder { SupplierId = supId, OrderDate = DateTime.Today };
        var items = new List<PurchaseOrderItem> { new() { ItemId = itemId, Quantity = qty, Count = qty, UnitPrice = 40 } };
        var (ok, _) = await proc.CreateOrderAsync(order, items, "test");
        Assert.True(ok);

        var (ok2, _) = await proc.ApproveOrderAsync(order.Id);
        Assert.True(ok2);
        return order.Id;
    }

    [Fact]
    public async Task CreateOrder_CreatesDraft_AndApprove()
    {
        using var db = CreateContext();
        var (itemId, supId) = await SeedAsync(db);
        var (proc, _) = Services(db);

        var order = new PurchaseOrder { SupplierId = supId };
        var (ok, _) = await proc.CreateOrderAsync(order, new List<PurchaseOrderItem> { new() { ItemId = itemId, Quantity = 5, Count = 5, UnitPrice = 40 } }, "test");

        Assert.True(ok);
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
        Assert.StartsWith("PRC-", order.OrderNumber);
        Assert.Single(await db.PurchaseOrders.ToListAsync());
        Assert.Single(await db.PurchaseOrderItems.ToListAsync());

        var (ok2, _) = await proc.ApproveOrderAsync(order.Id);
        Assert.True(ok2);
        Assert.Equal(PurchaseOrderStatus.Approved, (await db.PurchaseOrders.FindAsync(order.Id))!.Status);
    }

    [Fact]
    public async Task CreateOrder_NoLines_IsRejected()
    {
        using var db = CreateContext();
        var (_, supId) = await SeedAsync(db);
        var (proc, _) = Services(db);

        var order = new PurchaseOrder { SupplierId = supId };
        var (ok, err) = await proc.CreateOrderAsync(order, new List<PurchaseOrderItem>(), "test");

        Assert.False(ok);
        Assert.NotNull(err);
    }

    [Fact]
    public async Task Receive_UpdatesReceived_And_MarksReceivedWhenComplete()
    {
        using var db = CreateContext();
        var (itemId, supId) = await SeedAsync(db);
        var (proc, _) = Services(db);

        var orderId = await CreateApprovedOrderAsync(db, itemId, supId, 10);
        var orderItem = (await db.PurchaseOrderItems.FirstAsync(o => o.PurchaseOrderId == orderId));

        var (ok, _) = await proc.ReceiveOrderLineAsync(orderId, orderItem.Id, 10, 10);
        Assert.True(ok);

        var refreshed = await db.PurchaseOrders.Include(o => o.Items).FirstAsync(o => o.Id == orderId);
        Assert.Equal(10, refreshed.Items.Single().ReceivedQty);
        Assert.Equal(10, refreshed.Items.Single().ReceivedCount);
        Assert.Equal(PurchaseOrderStatus.Received, refreshed.Status);
    }

    [Fact]
    public async Task Receive_Partial_SetsPartiallyReceived()
    {
        using var db = CreateContext();
        var (itemId, supId) = await SeedAsync(db);
        var (proc, _) = Services(db);

        var orderId = await CreateApprovedOrderAsync(db, itemId, supId, 10);
        var orderItem = (await db.PurchaseOrderItems.FirstAsync(o => o.PurchaseOrderId == orderId));

        var (ok, _) = await proc.ReceiveOrderLineAsync(orderId, orderItem.Id, 4, 4);
        Assert.True(ok);

        var refreshed = await db.PurchaseOrders.Include(o => o.Items).FirstAsync(o => o.Id == orderId);
        Assert.Equal(4, refreshed.Items.Single().ReceivedQty);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, refreshed.Status);
    }

    [Fact]
    public async Task Receive_CannotExceedOrdered()
    {
        using var db = CreateContext();
        var (itemId, supId) = await SeedAsync(db);
        var (proc, _) = Services(db);

        var orderId = await CreateApprovedOrderAsync(db, itemId, supId, 10);
        var orderItem = (await db.PurchaseOrderItems.FirstAsync(o => o.PurchaseOrderId == orderId));

        var (ok, err) = await proc.ReceiveOrderLineAsync(orderId, orderItem.Id, 11, 0);
        Assert.False(ok);
        Assert.Contains("أكبر", err);

        var refreshed = await db.PurchaseOrders.Include(o => o.Items).FirstAsync(o => o.Id == orderId);
        Assert.Equal(0, refreshed.Items.Single().ReceivedQty);
    }

    [Fact]
    public async Task InvoiceFromOrder_ProducesStock_FifoLayer_AndGlEntry()
    {
        using var db = CreateContext();
        var (itemId, supId) = await SeedAsync(db);
        var (proc, _) = Services(db);

        var orderId = await CreateApprovedOrderAsync(db, itemId, supId, 10);
        var orderItem = (await db.PurchaseOrderItems.FirstAsync(o => o.PurchaseOrderId == orderId));
        await proc.ReceiveOrderLineAsync(orderId, orderItem.Id, 10, 10);

        var (ok, _) = await proc.CreateInvoiceFromOrderAsync(orderId, "test");
        Assert.True(ok);

        var invoice = await db.PurchaseInvoices.Include(i => i.Items).SingleAsync();
        Assert.Equal(orderId, invoice.PurchaseOrderId);
        Assert.Equal(10, invoice.Items.Single().Quantity);
        Assert.Equal(400, invoice.TotalAmount);

        var item = await db.Items.SingleAsync();
        Assert.Equal(10, item.CurrentQuantity);
        Assert.Equal(10, item.CurrentCount);

        var layer = await db.StockLayers.SingleAsync();
        Assert.Equal(itemId, layer.ItemId);
        Assert.Equal(10, layer.Qty);
        Assert.Equal(40, layer.UnitCost);

        var entry = await db.JournalEntries.SingleAsync(e => e.Source == JournalSource.PurchaseInvoice);
        var lines = await db.JournalEntryLines.Where(l => l.JournalEntryId == entry.Id).Include(l => l.Account).ToListAsync();

        // Receiving goods is not paying for them. A PO-derived invoice must stay
        // outstanding, otherwise the payable is cleared and a disbursement journal
        // entry is posted for money that never left the bank.
        Assert.Empty(await db.JournalEntries.Where(e => e.Source == JournalSource.Disbursement).ToListAsync());
        Assert.False(invoice.IsPaid);
        Assert.Equal(0m, invoice.PaidAmount);
        Assert.Equal(InvoicePaymentTerms.OpenTerm, invoice.PaymentTerms);

        // The payable is credited and left open, and that is the whole entry.
        Assert.Contains(lines, l => l.Account!.Code == "1300" && l.Debit == 400m);
        Assert.Contains(lines, l => l.Account!.Code == "2000" && l.Credit == 400m);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task InvoiceFromOrder_NoReceivedLines_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, supId) = await SeedAsync(db);
        var (proc, _) = Services(db);

        var orderId = await CreateApprovedOrderAsync(db, itemId, supId, 10);

        var (ok, err) = await proc.CreateInvoiceFromOrderAsync(orderId, "test");
        Assert.False(ok);
        Assert.Contains("مستلمة", err);
    }

    [Fact]
    public async Task SaveSupplierQuote_Upserts_AndConvertToOrder()
    {
        using var db = CreateContext();
        var (itemId, supId) = await SeedAsync(db);
        var (proc, _) = Services(db);

        var (ok, _) = await proc.SaveSupplierQuoteAsync(new SupplierQuote { SupplierId = supId, ItemId = itemId, UnitPrice = 35 });
        Assert.True(ok);

        var (ok2, _) = await proc.SaveSupplierQuoteAsync(new SupplierQuote { SupplierId = supId, ItemId = itemId, UnitPrice = 42 });
        Assert.True(ok2);

        var quotes = await db.SupplierQuotes.ToListAsync();
        Assert.Single(quotes);
        Assert.Equal(42, quotes[0].UnitPrice);
    }

    [Fact]
    public async Task CancelOrder_Approved_Succeeds()
    {
        using var db = CreateContext();
        var (itemId, supId) = await SeedAsync(db);
        var (proc, _) = Services(db);

        var orderId = await CreateApprovedOrderAsync(db, itemId, supId, 10);

        var (ok, err) = await proc.CancelOrderAsync(orderId);
        Assert.True(ok);
        Assert.Null(err);
        Assert.Equal(PurchaseOrderStatus.Cancelled, (await db.PurchaseOrders.FindAsync(orderId))!.Status);
    }

    [Fact]
    public async Task CancelOrder_PartiallyReceived_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, supId) = await SeedAsync(db);
        var (proc, _) = Services(db);

        var orderId = await CreateApprovedOrderAsync(db, itemId, supId, 10);
        var orderItem = await db.PurchaseOrderItems.FirstAsync(o => o.PurchaseOrderId == orderId);
        await proc.ReceiveOrderLineAsync(orderId, orderItem.Id, 4, 4);

        var (ok, err) = await proc.CancelOrderAsync(orderId);
        Assert.False(ok);
        Assert.Contains("غاء", err);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, (await db.PurchaseOrders.FindAsync(orderId))!.Status);
    }

    [Fact]
    public async Task CancelOrder_AfterInvoice_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, supId) = await SeedAsync(db);
        var (proc, _) = Services(db);

        var orderId = await CreateApprovedOrderAsync(db, itemId, supId, 10);
        var orderItem = await db.PurchaseOrderItems.FirstAsync(o => o.PurchaseOrderId == orderId);
        await proc.ReceiveOrderLineAsync(orderId, orderItem.Id, 10, 10);
        await proc.CreateInvoiceFromOrderAsync(orderId, "test");

        var (ok, err) = await proc.CancelOrderAsync(orderId);
        Assert.False(ok);
        Assert.Contains("غاء", err);
        Assert.Equal(PurchaseOrderStatus.Received, (await db.PurchaseOrders.FindAsync(orderId))!.Status);
    }

    [Fact]
    public async Task CreateOrder_DuplicateItem_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, supId) = await SeedAsync(db);
        var (proc, _) = Services(db);

        var order = new PurchaseOrder { SupplierId = supId };
        var (ok, err) = await proc.CreateOrderAsync(order, new List<PurchaseOrderItem>
        {
            new() { ItemId = itemId, Quantity = 5, Count = 0, UnitPrice = 40 },
            new() { ItemId = itemId, Quantity = 0, Count = 3, UnitPrice = 40 }
        }, "test");

        Assert.False(ok);
        Assert.Contains("نفسه", err);
        Assert.Equal(0, await db.PurchaseOrders.CountAsync());
        Assert.Equal(0, await db.PurchaseOrderItems.CountAsync());
    }

    [Fact]
    public async Task UpdateOrder_DuplicateItem_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, supId) = await SeedAsync(db);
        var (proc, _) = Services(db);

        var order = new PurchaseOrder { SupplierId = supId };
        var (okC, _) = await proc.CreateOrderAsync(order, new List<PurchaseOrderItem> { new() { ItemId = itemId, Quantity = 1, Count = 0, UnitPrice = 40 } }, "test");
        Assert.True(okC);

        var (ok, err) = await proc.UpdateOrderAsync(new PurchaseOrder { Id = order.Id, SupplierId = supId }, new List<PurchaseOrderItem>
        {
            new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 40 },
            new() { ItemId = itemId, Quantity = 0, Count = 2, UnitPrice = 40 }
        }, "test");

        Assert.False(ok);
        Assert.Contains("نفسه", err);
    }

    [Fact]
    public async Task UpdateOrder_MissingSupplier_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, supId) = await SeedAsync(db);
        var (proc, _) = Services(db);

        var order = new PurchaseOrder { SupplierId = supId };
        var (okC, _) = await proc.CreateOrderAsync(order, new List<PurchaseOrderItem> { new() { ItemId = itemId, Quantity = 1, Count = 0, UnitPrice = 40 } }, "test");
        Assert.True(okC);

        var (ok, err) = await proc.UpdateOrderAsync(new PurchaseOrder { Id = order.Id, SupplierId = 99999 }, new List<PurchaseOrderItem> { new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 40 } }, "test");
        Assert.False(ok);
        Assert.Contains("المورد", err);
    }

    /// <summary>
    /// رمز التزامن مملوكٌ للخادم في هذا المسار: لا يأتي من النموذج ولا من الواجهة، فلا يجوز
    /// أن يتسرّب إلى <c>SaveChanges</c>. ولو دخل من النموذج إلى
    /// الخدمة لأصبحت القيمة الأصلية في EF رمزًا اختاره العميل، وبات الحفظ يرفض أو يقبل على
    /// هواه. يلتقط الاختبار القيمة الأصلية لحظة الحفظ نفسه، لا بعد فوات الأوان.
    /// </summary>
    [Fact]
    public async Task UpdateOrder_ACallerSuppliedLineToken_NeverReachesTheSave()
    {
        using var db = CreateContext();
        var (itemId, supId) = await SeedAsync(db);
        var (proc, _) = Services(db);

        var order = new PurchaseOrder { SupplierId = supId };
        var (okC, _) = await proc.CreateOrderAsync(order, new List<PurchaseOrderItem>
        {
            new() { ItemId = itemId, Quantity = 5, Count = 5, UnitPrice = 40 }
        }, "test");
        Assert.True(okC);

        var lineId = await db.PurchaseOrderItems.Select(i => i.Id).SingleAsync();
        var foreignToken = new byte[] { 9, 9, 9, 9 };

        byte[]? originalAtSaveTime = null;
        var reachedSave = false;
        db.SavingChanges += (_, __) =>
        {
            reachedSave = true;
            originalAtSaveTime = db.ChangeTracker.Entries<PurchaseOrderItem>()
                .Where(e => e.Entity.Id == lineId)
                .Select(e => e.Property(i => i.RowVersion).OriginalValue)
                .FirstOrDefault();
        };

        var (ok, error) = await proc.UpdateOrderAsync(
            new PurchaseOrder { Id = order.Id, SupplierId = supId },
            new List<PurchaseOrderItem>
            {
                new()
                {
                    Id = lineId,
                    ItemId = itemId,
                    Quantity = 7,
                    Count = 7,
                    UnitPrice = 40,
                    RowVersion = foreignToken
                }
            },
            "test");

        Assert.True(ok, error);
        Assert.True(reachedSave);
        Assert.NotEqual(foreignToken, originalAtSaveTime);
    }
}
