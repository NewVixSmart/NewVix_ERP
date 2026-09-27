using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Controllers;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class SalesQuotesEnhancementsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public SalesQuotesEnhancementsTests()
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
    public async Task MassConvert_ConvertsMultipleDrafts_ReportsCounts_SkipsLocked()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedAsync(db);
        db.StockLayers.Add(new StockLayer { ItemId = itemId, Qty = 50, Count = 0, UnitCost = 40m, RemainingQty = 50, RemainingCount = 0, DateReceived = new DateTime(2026, 1, 1), CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var quotes = new SalesQuotesService(db, new SalesOrdersService(db, new InventoryService(db)));
        var (ok1, _, q1) = await quotes.CreateAsync(new SaleQuote { CustomerId = custId }, new List<SaleQuoteItem> { new() { ItemId = itemId, Quantity = 1, Count = 0, UnitPrice = 80 } }, "test");
        var (ok2, _, q2) = await quotes.CreateAsync(new SaleQuote { CustomerId = custId }, new List<SaleQuoteItem> { new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 80 } }, "test");
        var (ok3, _, q3) = await quotes.CreateAsync(new SaleQuote { CustomerId = custId }, new List<SaleQuoteItem> { new() { ItemId = itemId, Quantity = 1, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok1);
        Assert.True(ok2);
        Assert.True(ok3);
        Assert.NotNull(q1);
        Assert.NotNull(q2);
        Assert.NotNull(q3);

        var locked = await db.SaleQuotes.SingleAsync(q => q.Id == q3!.Id);
        locked.Status = SaleQuoteStatus.Converting;
        await db.SaveChangesAsync();

        var (converted, failed, failures) = await quotes.MassConvertAsync(new[] { q1!.Id, q2!.Id, q3!.Id }, "mass-user");

        Assert.Equal(2, converted);
        Assert.Equal(1, failed);
        Assert.Contains(failures, f => f.Id == q3!.Id);
        Assert.Equal(SaleQuoteStatus.Converted, (await db.SaleQuotes.SingleAsync(q => q.Id == q1!.Id)).Status);
        Assert.Equal(SaleQuoteStatus.Converted, (await db.SaleQuotes.SingleAsync(q => q.Id == q2!.Id)).Status);
        Assert.Equal(SaleQuoteStatus.Converting, (await db.SaleQuotes.SingleAsync(q => q.Id == q3!.Id)).Status);
        Assert.Equal(2, await db.SalesOrders.CountAsync());
    }

    [Fact]
    public async Task CreateQuote_WithSupplierQuoteLink_PersistsAndReadsBack()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var supplierQuote = new SupplierQuote
        {
            Supplier = new Supplier { Name = "مورد اختبار" },
            ItemId = itemId,
            UnitPrice = 70,
            EffectiveDate = new DateTime(2026, 5, 1)
        };
        db.SupplierQuotes.Add(supplierQuote);
        await db.SaveChangesAsync();

        var quotes = new SalesQuotesService(db, new SalesOrdersService(db, new InventoryService(db)));
        var (ok, err, saved) = await quotes.CreateAsync(
            new SaleQuote { CustomerId = custId, SupplierQuoteId = supplierQuote.Id },
            new List<SaleQuoteItem> { new() { ItemId = itemId, Quantity = 1, Count = 0, UnitPrice = 80 } },
            "test");

        Assert.True(ok);
        Assert.Null(err);
        Assert.NotNull(saved);
        Assert.Equal(supplierQuote.Id, saved.SupplierQuoteId);

        var read = await db.SaleQuotes.AsNoTracking()
            .Include(q => q.SupplierQuote).ThenInclude(s => s!.Supplier)
            .SingleAsync(q => q.Id == saved!.Id);
        Assert.Equal(supplierQuote.Id, read.SupplierQuoteId);
        Assert.NotNull(read.SupplierQuote);
        Assert.Equal("مورد اختبار", read.SupplierQuote.Supplier.Name);
    }

    [Fact]
    public async Task Pdf_ReturnsNonEmptyByteStream_WithApplicationPdf()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var quotes = new SalesQuotesService(db, new SalesOrdersService(db, new InventoryService(db)));
        var (ok, _, saved) = await quotes.CreateAsync(
            new SaleQuote { CustomerId = custId, Discount = 5, Tax = 2, Notes = "ملاحظة الطباعة" },
            new List<SaleQuoteItem> { new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 80 } },
            "test");
        Assert.True(ok);
        Assert.NotNull(saved);

        var controller = new SalesQuotesController(db, quotes);
        var result = await controller.Pdf(saved!.Id) as FileContentResult;

        Assert.NotNull(result);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.True(result!.FileContents.Length > 1000);
        Assert.StartsWith("SaleQuote-", result.FileDownloadName);
    }

    [Fact]
    public async Task MassConvert_GET_ListsOnlyDraftQuotes()
    {
        using var db = CreateContext();
        var (itemId, custId) = await SeedAsync(db);
        var quotes = new SalesQuotesService(db, new SalesOrdersService(db, new InventoryService(db)));

        var (ok, _, draft) = await quotes.CreateAsync(
            new SaleQuote { CustomerId = custId, QuoteDate = new DateTime(2026, 6, 1) },
            new List<SaleQuoteItem> { new() { ItemId = itemId, Quantity = 1, Count = 0, UnitPrice = 80 } },
            "test");
        Assert.True(ok);
        Assert.NotNull(draft);

        var converted = new SaleQuote { QuoteNumber = "SQ-CONVERTED", CustomerId = custId, Status = SaleQuoteStatus.Converted, QuoteDate = new DateTime(2026, 6, 2) };
        converted.Items.Add(new SaleQuoteItem { ItemId = itemId, Quantity = 1, Count = 0, UnitPrice = 80 });
        var cancelled = new SaleQuote { QuoteNumber = "SQ-CANCELLED", CustomerId = custId, Status = SaleQuoteStatus.Cancelled, QuoteDate = new DateTime(2026, 6, 3) };
        cancelled.Items.Add(new SaleQuoteItem { ItemId = itemId, Quantity = 1, Count = 0, UnitPrice = 80 });
        db.SaleQuotes.AddRange(converted, cancelled);
        await db.SaveChangesAsync();

        var controller = new SalesQuotesController(db, quotes);
        var result = await controller.MassConvert() as ViewResult;

        Assert.NotNull(result);
        Assert.NotNull(result!.Model);
        var model = Assert.IsAssignableFrom<IReadOnlyList<SaleQuote>>(result.Model);
        Assert.Single(model);
        Assert.Equal(SaleQuoteStatus.Draft, model[0].Status);
    }
}