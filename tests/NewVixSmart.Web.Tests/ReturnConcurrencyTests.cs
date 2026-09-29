using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class ReturnConcurrencyTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public ReturnConcurrencyTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static async Task<(int itemId, int custId, int supId)> SeedAsync(AppDbContext db)
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
            PurchasePrice = 30,
            SalePrice = 80,
            CurrentCount = 100,
            CurrentQuantity = 100
        };
        db.Items.Add(item);

        var customer = new Customer { Name = "عميل اختبار" };
        var supplier = new Supplier { Name = "مورد اختبار" };
        db.Customers.Add(customer);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        return (item.Id, customer.Id, supplier.Id);
    }

    private static void SeedChart(AppDbContext db)
    {
        (string Code, string Name, GLAccountType Type, NormalBalance Normal)[] accounts =
        {
            ("1000", "النقد", GLAccountType.Asset, NormalBalance.Debit),
            ("1100", "البنوك", GLAccountType.Asset, NormalBalance.Debit),
            ("1200", "المدينون", GLAccountType.Asset, NormalBalance.Debit),
            ("1300", "المخزون", GLAccountType.Asset, NormalBalance.Debit),
            ("2000", "الدائنون", GLAccountType.Liability, NormalBalance.Credit),
            ("3000", "رأس المال", GLAccountType.Equity, NormalBalance.Credit),
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
            ("5101", "مرتجعات المبيعات", GLAccountType.Expense, NormalBalance.Debit),
            ("5102", "مرتجعات المشتريات", GLAccountType.Expense, NormalBalance.Debit)
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.SaveChanges();
    }

    [Theory]
    [InlineData(typeof(SaleReturn))]
    [InlineData(typeof(PurchaseReturn))]
    public void Return_ExposesRowVersionConfiguredAsConcurrencyToken(Type returnType)
    {
        using var db = CreateContext();

        var clrProperty = returnType.GetProperty("RowVersion");
        Assert.NotNull(clrProperty);
        Assert.Equal(typeof(byte[]), clrProperty!.PropertyType);
        Assert.NotNull(clrProperty.GetCustomAttribute<TimestampAttribute>());
        Assert.NotNull(clrProperty.GetCustomAttribute<BindNeverAttribute>());

        var mapped = db.Model.FindEntityType(returnType)!.FindProperty("RowVersion");
        Assert.NotNull(mapped);
        Assert.True(mapped!.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, mapped.ValueGenerated);
    }

    [Fact]
    public async Task PostSaleReturn_Twice_PostsSingleEntryAndRestoresStockOnce()
    {
        using var db = CreateContext();
        SeedChart(db);
        var (itemId, custId, _) = await SeedAsync(db);

        db.StockLayers.Add(new StockLayer
        {
            ItemId = itemId,
            Qty = 100,
            Count = 100,
            RemainingQty = 100,
            RemainingCount = 100,
            UnitCost = 25m,
            CountCost = 25m,
            DateReceived = new DateTime(2026, 1, 1)
        });
        await db.SaveChangesAsync();

        var svc = new InventoryService(db, new AccountingService(db));

        var saleInv = new SaleInvoice { CustomerId = custId };
        await svc.CreateSaleAsync(saleInv,
            new List<SaleInvoiceItem> { new() { ItemId = itemId, Quantity = 10, Count = 0, UnitPrice = 80 } }, "test");

        var note = new DeliveryOrder { SaleInvoiceId = saleInv.Id, DeliveryDate = new DateTime(2026, 3, 2) };
        var (noteOk, noteErr) = await svc.CreateDeliveryOrderAsync(note,
            new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = 10, Count = 0 } }, "test");
        Assert.True(noteOk, noteErr);
        var (delivered, deliveredErr) = await svc.DeliverDeliveryOrderAsync(note.Id, "test");
        Assert.True(delivered, deliveredErr);

        var saleReturn = new SaleReturn { CustomerId = custId, SaleInvoiceId = saleInv.Id, ReturnDate = new DateTime(2026, 3, 3) };
        var (draftOk, draftErr, returnId) = await svc.CreateSaleReturnDraftAsync(saleReturn,
            new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(draftOk, draftErr);

        var (first, firstErr) = await svc.PostSaleReturnAsync(returnId, "test");
        Assert.True(first, firstErr);

        var (second, secondErr) = await svc.PostSaleReturnAsync(returnId, "test");

        Assert.False(second);
        Assert.Contains("بالفعل", secondErr);
        Assert.Equal(ReturnStatus.Posted, (await db.SaleReturns.SingleAsync(r => r.Id == returnId)).Status);

        var entries = await db.JournalEntries
            .Include(e => e.Lines)
            .Where(e => e.Source == JournalSource.SaleReturn)
            .ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Equal(returnId, entry.SourceId);
        var codes = await db.GLAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Code);
        Assert.Contains(entry.Lines, l => codes[l.AccountId] == "5101" && l.Debit == 160m);
        Assert.Contains(entry.Lines, l => codes[l.AccountId] == "1300" && l.Debit == 50m);
        Assert.Equal(entry.Lines.Sum(l => l.Debit), entry.Lines.Sum(l => l.Credit));

        Assert.Single(await db.StockMovements
            .Where(m => m.DocumentType == DocumentType.SaleReturn).ToListAsync());
        Assert.Equal(92m, (await db.Items.SingleAsync()).CurrentQuantity);
    }

    [Fact]
    public async Task PostPurchaseReturn_Twice_PostsSingleEntryAndReducesStockOnce()
    {
        using var db = CreateContext();
        SeedChart(db);
        var (itemId, _, supId) = await SeedAsync(db);
        var svc = new InventoryService(db, new AccountingService(db));

        var purchase = new PurchaseInvoice { SupplierId = supId };
        var (invoiceOk, invoiceErr) = await svc.CreatePurchaseAsync(purchase,
            new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 20, Count = 0, UnitPrice = 45 } }, "test");
        Assert.True(invoiceOk, invoiceErr);

        var purchaseReturn = new PurchaseReturn { SupplierId = supId, PurchaseInvoiceId = purchase.Id, ReturnDate = new DateTime(2026, 3, 4) };
        var (draftOk, draftErr, returnId) = await svc.CreatePurchaseReturnDraftAsync(purchaseReturn,
            new List<PurchaseReturnItem> { new() { ItemId = itemId, Quantity = 5, Count = 0, UnitPrice = 45 } }, "test");
        Assert.True(draftOk, draftErr);

        var (first, firstErr) = await svc.PostPurchaseReturnAsync(returnId, "test");
        Assert.True(first, firstErr);

        var (second, secondErr) = await svc.PostPurchaseReturnAsync(returnId, "test");

        Assert.False(second);
        Assert.Contains("بالفعل", secondErr);
        Assert.Equal(ReturnStatus.Posted, (await db.PurchaseReturns.SingleAsync(r => r.Id == returnId)).Status);

        var entry = await db.JournalEntries
            .SingleAsync(e => e.Source == JournalSource.PurchaseReturn);
        Assert.Equal(returnId, entry.SourceId);

        Assert.Single(await db.StockMovements
            .Where(m => m.DocumentType == DocumentType.PurchaseReturn).ToListAsync());
        Assert.Equal(115m, (await db.Items.SingleAsync()).CurrentQuantity);
    }
}
