using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class SalesOrderLifecycleTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public SalesOrderLifecycleTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static async Task<(int itemId, int custId)> SeedAsync(AppDbContext db)
    {
        var cat = new ItemCategory { Name = "تصنيف اختبار" };
        var type = new ItemType { Name = "نوع اختبار" };
        var unit = new Unit { Name = "قطعة" };
        db.ItemCategories.Add(cat);
        db.ItemTypes.Add(type);
        db.Units.Add(unit);

        var item = new Item
        {
            Name = "صنف اختبار",
            Category = cat,
            ItemType = type,
            CountUnit = unit,
            QuantityUnit = unit,
            PurchasePrice = 50,
            SalePrice = 80,
            CurrentCount = 100,
            CurrentQuantity = 100
        };
        db.Items.Add(item);

        var customer = new Customer { Name = "عميل اختبار" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        return (item.Id, customer.Id);
    }

    private async Task<int> CreateDraftOrderAsync(AppDbContext db, int itemId, int custId, decimal qty)
    {
        var orders = new SalesOrdersService(db, new InventoryService(db));
        var order = new SalesOrder { CustomerId = custId, OrderDate = DateTime.Today };
        var (ok, _) = await orders.CreateOrderAsync(order, new List<SalesOrderItem> { new() { ItemId = itemId, Quantity = qty, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok);
        return order.Id;
    }

    [Fact]
    public async Task CreateOrder_Succeeds_AsDraft()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var orders = new SalesOrdersService(db, new InventoryService(db));

        var order = new SalesOrder { CustomerId = custId };
        var (ok, _) = await orders.CreateOrderAsync(order, new List<SalesOrderItem> { new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 80 } }, "test");

        Assert.True(ok);
        Assert.Equal(SalesOrderStatus.Draft, order.Status);
        Assert.StartsWith("SO-", order.OrderNumber);
    }

    [Fact]
    public async Task CreateOrder_DuplicateItem_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var orders = new SalesOrdersService(db, new InventoryService(db));

        var order = new SalesOrder { CustomerId = custId };
        var (ok, err) = await orders.CreateOrderAsync(order, new List<SalesOrderItem>
        {
            new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 80 },
            new() { ItemId = itemId, Quantity = 0, Count = 3, UnitPrice = 80 }
        }, "test");

        Assert.False(ok);
        Assert.Contains("نفسه", err);
        Assert.Equal(0, await db.SalesOrders.CountAsync());
        Assert.Equal(0, await db.SalesOrderItems.CountAsync());
    }

    [Fact]
    public async Task UpdateOrder_DuplicateItem_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var orders = new SalesOrdersService(db, new InventoryService(db));

        var orderId = await CreateDraftOrderAsync(db, itemId, custId, 1);

        var (ok, err) = await orders.UpdateOrderAsync(new SalesOrder { Id = orderId, CustomerId = custId }, new List<SalesOrderItem>
        {
            new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 80 },
            new() { ItemId = itemId, Quantity = 0, Count = 2, UnitPrice = 80 }
        }, "test");

        Assert.False(ok);
        Assert.Contains("نفسه", err);
    }

    [Fact]
    public async Task UpdateOrder_MissingCustomer_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var orders = new SalesOrdersService(db, new InventoryService(db));

        var orderId = await CreateDraftOrderAsync(db, itemId, custId, 1);

        var (ok, err) = await orders.UpdateOrderAsync(new SalesOrder { Id = orderId, CustomerId = 99999 }, new List<SalesOrderItem> { new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 80 } }, "test");
        Assert.False(ok);
        Assert.Contains("العميل", err);
    }

    [Fact]
    public async Task Cancel_AfterInvoiced_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var orders = new SalesOrdersService(db, new InventoryService(db));

        var orderId = await CreateDraftOrderAsync(db, itemId, custId, 1);
        var order = await db.SalesOrders.FindAsync(orderId);
        order!.Status = SalesOrderStatus.Invoiced;
        await db.SaveChangesAsync();

        var (ok, err) = await orders.CancelOrderAsync(orderId);
        Assert.False(ok);
        Assert.Contains("فوترت", err);
        Assert.Equal(SalesOrderStatus.Invoiced, (await db.SalesOrders.FindAsync(orderId))!.Status);
    }
}