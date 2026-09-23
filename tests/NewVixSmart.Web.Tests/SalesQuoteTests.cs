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

public sealed class SalesQuoteTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public SalesQuoteTests()
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

    private static void SeedChartOfAccounts(AppDbContext db)
    {
        (string Code, string Name, GLAccountType Type, NormalBalance Normal)[] accounts =
        {
            ("1000", "النقد / الصندوق", GLAccountType.Asset, NormalBalance.Debit),
            ("1200", "المدينون (العملاء)", GLAccountType.Asset, NormalBalance.Debit),
            ("1300", "المخزون", GLAccountType.Asset, NormalBalance.Debit),
            ("2000", "الدائنون (الموردون)", GLAccountType.Liability, NormalBalance.Credit),
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.SaveChanges();
    }

    [Fact]
    public async Task CreateQuote_ComputesTotals_LineQtyVsCount()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var quotes = new SalesQuotesService(db, new SalesOrdersService(db, new InventoryService(db)));

        var quote = new SaleQuote { CustomerId = custId, Discount = 10, Tax = 5 };
        var (ok, err, saved) = await quotes.CreateAsync(quote, new List<SaleQuoteItem>
        {
            new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 80 },
            new() { ItemId = itemId, Quantity = 0, Count = 3, UnitPrice = 80 }
        }, "test");

        Assert.True(ok);
        Assert.Null(err);
        Assert.NotNull(saved);
        Assert.Equal(400m, saved.TotalAmount);
        Assert.Equal(395m, saved.NetAmount);

        var persisted = await db.SaleQuotes.SingleAsync();
        Assert.Equal(400m, persisted.TotalAmount);
        Assert.Equal(395m, persisted.NetAmount);
        Assert.Equal(2, persisted.Items.Count);
    }

    [Fact]
    public async Task CreateQuote_DiscountGreaterThanTotalPlusTax_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var quotes = new SalesQuotesService(db, new SalesOrdersService(db, new InventoryService(db)));

        var quote = new SaleQuote { CustomerId = custId, Discount = 500m, Tax = 0m };
        var (ok, err, saved) = await quotes.CreateAsync(quote, new List<SaleQuoteItem>
        {
            new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 80 }
        }, "test");

        Assert.False(ok);
        Assert.Contains("الخصم أكبر", err);
        Assert.Null(saved);
        Assert.Equal(0, await db.SaleQuotes.CountAsync());
    }

    [Fact]
    public async Task CreateQuote_EmptyOrInvalidLines_Rejected()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var quotes = new SalesQuotesService(db, new SalesOrdersService(db, new InventoryService(db)));

        var empty = new SaleQuote { CustomerId = custId };
        var (ok, err, saved) = await quotes.CreateAsync(empty, new List<SaleQuoteItem>(), "test");
        Assert.False(ok);
        Assert.Contains("صنف", err);
        Assert.Null(saved);

        var invalid = new SaleQuote { CustomerId = custId };
        var (ok2, _, saved2) = await quotes.CreateAsync(invalid, new List<SaleQuoteItem>
        {
            new() { ItemId = itemId, Quantity = 0, Count = 0, UnitPrice = 80 },
            new() { ItemId = 0, Quantity = 5, Count = 0, UnitPrice = 80 }
        }, "test");
        Assert.False(ok2);
        Assert.Null(saved2);
        Assert.Equal(0, await db.SaleQuotes.CountAsync());
        Assert.Equal(0, await db.SaleQuoteItems.CountAsync());
    }

    [Fact]
    public async Task ConvertQuote_CreatesSaleInvoice_MarksConverted_LinksInvoice()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedAsync(db);
        db.Currencies.Add(new Currency { Code = "SDG", Name = "جنيه سوداني", Symbol = "ج.س", ExchangeRate = 1m, IsBase = true, IsActive = true });
        db.StockLayers.Add(new StockLayer { ItemId = itemId, Qty = 10, Count = 0, UnitCost = 40m, RemainingQty = 10, RemainingCount = 0, DateReceived = new DateTime(2026, 1, 1), CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var quotes = new SalesQuotesService(db, new SalesOrdersService(db, new InventoryService(db)));
        var quote = new SaleQuote { CustomerId = custId, QuoteDate = new DateTime(2026, 3, 10), ValidUntil = new DateTime(2026, 4, 10), Discount = 10, Tax = 5 };
        var (ok, err, saved) = await quotes.CreateAsync(quote, new List<SaleQuoteItem> { new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok);
        Assert.Null(err);

        var (convOk, convErr, order) = await quotes.ConvertToOrderAsync(saved!.Id, "user1");
        Assert.True(convOk);
        Assert.Null(convErr);
        Assert.NotNull(order);

        var converted = await db.SaleQuotes.SingleAsync();
        Assert.Equal(SaleQuoteStatus.Converted, converted.Status);
        Assert.Equal(order!.Id, converted.SalesOrderId);
        Assert.Equal("user1", converted.ConvertedBy);
        Assert.NotNull(converted.ConvertedAt);

        var salesOrder = await db.SalesOrders.SingleAsync();
        Assert.Equal(custId, salesOrder.CustomerId);
        Assert.Equal(320m, salesOrder.Items.Sum(i => i.Total));
        Assert.StartsWith("SO-", salesOrder.OrderNumber);
        Assert.Equal(quote.Id, salesOrder.SaleQuoteId);
        Assert.Equal(SalesOrderStatus.Draft, salesOrder.Status);

        Assert.Equal(100, db.Items.Single().CurrentQuantity);
        Assert.Equal(10, (await db.StockLayers.SingleAsync()).RemainingQty);
        Assert.Equal(0, await db.StockMovements.CountAsync());
        Assert.Equal(0, await db.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task Convert_AlreadyConverted_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var quotes = new SalesQuotesService(db, new SalesOrdersService(db, new InventoryService(db)));

        var quote = new SaleQuote { CustomerId = custId, QuoteDate = new DateTime(2026, 3, 11) };
        var (ok, _, saved) = await quotes.CreateAsync(quote, new List<SaleQuoteItem> { new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok);

        var (first, _, _) = await quotes.ConvertToOrderAsync(saved!.Id, "user1");
        Assert.True(first);

        var (second, err, _) = await quotes.ConvertToOrderAsync(saved!.Id, "user1");
        Assert.False(second);
        Assert.Contains("محوّل", err);
        Assert.Single(await db.SalesOrders.ToListAsync());
        Assert.Equal(0, await db.StockMovements.CountAsync());
    }

    [Fact]
    public async Task Convert_CancelledQuote_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var quote = new SaleQuote { QuoteNumber = "SQ-TEST-C-001", CustomerId = custId, Status = SaleQuoteStatus.Cancelled, QuoteDate = new DateTime(2026, 3, 12) };
        quote.Items.Add(new SaleQuoteItem { ItemId = itemId, Quantity = 1, Count = 0, UnitPrice = 80 });
        db.SaleQuotes.Add(quote);
        await db.SaveChangesAsync();

        var quotes = new SalesQuotesService(db, new SalesOrdersService(db, new InventoryService(db)));
        var (ok, err, _) = await quotes.ConvertToOrderAsync(quote.Id, "user1");
        Assert.False(ok);
        Assert.Contains("ملغي", err);
        Assert.Equal(0, await db.SalesOrders.CountAsync());
        Assert.Equal(100, db.Items.Single().CurrentQuantity);
    }

    [Fact]
    public async Task Convert_QuoteAlreadyConverting_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var quotes = new SalesQuotesService(db, new SalesOrdersService(db, new InventoryService(db)));

        var quote = new SaleQuote { CustomerId = custId, QuoteDate = new DateTime(2026, 3, 15) };
        var (ok, _, saved) = await quotes.CreateAsync(quote, new List<SaleQuoteItem> { new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok);

        var locked = await db.SaleQuotes.SingleAsync();
        locked.Status = SaleQuoteStatus.Converting;
        await db.SaveChangesAsync();

        var (convOk, err, _) = await quotes.ConvertToOrderAsync(saved!.Id, "user1");
        Assert.False(convOk);
        Assert.Contains("جارٍ", err);
        Assert.Equal(0, await db.SalesOrders.CountAsync());
        Assert.Equal(100, db.Items.Single().CurrentQuantity);
    }

    [Fact]
    public async Task Delete_DraftQuote_Succeeds_ThenRemoves()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var quotes = new SalesQuotesService(db, new SalesOrdersService(db, new InventoryService(db)));

        var quote = new SaleQuote { CustomerId = custId, QuoteDate = new DateTime(2026, 3, 13) };
        var (ok, _, saved) = await quotes.CreateAsync(quote, new List<SaleQuoteItem> { new() { ItemId = itemId, Quantity = 3, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok);

        var (delOk, delErr) = await quotes.DeleteAsync(saved!.Id);
        Assert.True(delOk);
        Assert.Null(delErr);
        Assert.Equal(0, await db.SaleQuotes.CountAsync());
        Assert.Equal(0, await db.SaleQuoteItems.CountAsync());
    }

    [Fact]
    public async Task Delete_ConvertedQuote_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var quotes = new SalesQuotesService(db, new SalesOrdersService(db, new InventoryService(db)));

        var quote = new SaleQuote { CustomerId = custId, QuoteDate = new DateTime(2026, 3, 14) };
        var (ok, _, saved) = await quotes.CreateAsync(quote, new List<SaleQuoteItem> { new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok);
        Assert.True((await quotes.ConvertToOrderAsync(saved!.Id, "user1")).Success);

        var (delOk, delErr) = await quotes.DeleteAsync(saved!.Id);
        Assert.False(delOk);
        Assert.Contains("حذف", delErr);
        var kept = await db.SaleQuotes.SingleAsync();
        Assert.Equal(SaleQuoteStatus.Converted, kept.Status);
        Assert.NotNull(kept.SalesOrderId);
    }
}