using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Primitives;
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

public sealed class Round20FinancialSecurityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public Round20FinancialSecurityTests()
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

    [Fact]
    public void SaleInvoiceItem_Discount_ExceedingLineGross_IsInvalid()
    {
        var item = new SaleInvoiceItem { ItemId = 1, Quantity = 1, Count = 0, UnitPrice = 80, Discount = 85 };
        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(item, new ValidationContext(item), results, validateAllProperties: true);
        Assert.False(valid);
        Assert.Contains(results, r => r.ErrorMessage != null && r.ErrorMessage.Contains("خصم الصنف"));
    }

    [Fact]
    public void SaleInvoiceItem_Discount_EqualToLineGross_IsValid()
    {
        var item = new SaleInvoiceItem { ItemId = 1, Quantity = 1, Count = 0, UnitPrice = 80, Discount = 80 };
        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(item, new ValidationContext(item), results, validateAllProperties: true);
        Assert.True(valid);
    }

    [Fact]
    public async Task CreateSaleAsync_Discount_OverGross_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedBasicAsync(db);
        var svc = new InventoryService(db);

        var invoice = new SaleInvoice { CustomerId = custId };
        var (ok, err) = await svc.CreateSaleAsync(invoice,
            new List<SaleInvoiceItem> { new() { ItemId = itemId, Quantity = 1, Count = 0, UnitPrice = 80, Discount = 100 } }, "test");

        Assert.False(ok);
        Assert.Contains("خصم الصنف", err);
        Assert.Equal(0, await db.SaleInvoices.CountAsync());
    }

    [Fact]
    public async Task DeliverAsync_ZeroNetInvoice_BooksNoRevenue()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedBasicAsync(db);
        var svc = new InventoryService(db, new AccountingService(db));

        var freeInvoice = new SaleInvoice { CustomerId = custId };
        var (okInv, errInv) = await svc.CreateSaleAsync(freeInvoice,
            new List<SaleInvoiceItem> { new() { ItemId = itemId, Quantity = 1, Count = 0, UnitPrice = 80, Discount = 80 } }, "test");
        Assert.True(okInv, errInv);
        Assert.Equal(0m, freeInvoice.NetAmount);

        var (ok, err) = await DeliverDirectAsync(db, freeInvoice, itemId, 1);
        Assert.True(ok, err);
        var freeEntry = await db.JournalEntries.SingleAsync(e => e.Source == JournalSource.SaleDeliveryOrder);
        var freeLines = await db.JournalEntryLines.Where(l => l.JournalEntryId == freeEntry.Id).Include(l => l.Account).ToListAsync();
        Assert.DoesNotContain(freeLines, l => l.Account!.Code is "1200" or "4000");

        var paidInvoice = new SaleInvoice { CustomerId = custId };
        var (okInv2, errInv2) = await svc.CreateSaleAsync(paidInvoice,
            new List<SaleInvoiceItem> { new() { ItemId = itemId, Quantity = 1, Count = 0, UnitPrice = 80, Discount = 0 } }, "test");
        Assert.True(okInv2, errInv2);

        var (ok2, err2) = await DeliverDirectAsync(db, paidInvoice, itemId, 1);
        Assert.True(ok2, err2);

        var entry = await db.JournalEntries.SingleAsync(e =>
            e.Source == JournalSource.SaleDeliveryOrder &&
            e.Lines.Any(l => l.Account!.Code == "4000"));
        var lines = await db.JournalEntryLines.Where(l => l.JournalEntryId == entry.Id).Include(l => l.Account).ToListAsync();
        Assert.Contains(lines, l => l.Account!.Code == "1200" && l.Debit == 80m);
        Assert.Contains(lines, l => l.Account!.Code == "4000" && l.Credit == 80m);
    }

    [Fact]
    public async Task SaleReturn_Create_PostWithoutPermission_IsRejected_NoDraft()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedBasicAsync(db);
        var svc = new InventoryService(db, new AccountingService(db));

        var invoice = new SaleInvoice { CustomerId = custId };
        var (okInv, errInv) = await svc.CreateSaleAsync(invoice,
            new List<SaleInvoiceItem> { new() { ItemId = itemId, Quantity = 10, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(okInv, errInv);
        await DeliverDirectAsync(db, invoice, itemId, 10);

        var controller = new SaleReturnsController(db, svc, new TestPermissionService("SaleReturns.Create"));
        var ctx = CreateHttpContext();
        ctx.Request.Form = new FormCollection(new Dictionary<string, StringValues> { ["submitAction"] = "post" });
        WireController(controller, ctx);

        var result = await controller.Create(new SaleReturn
        {
            SaleInvoiceId = invoice.Id, CustomerId = custId, ReturnDate = DateTime.Today
        }, new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 80 } });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState.Values.SelectMany(v => v.Errors), e => e.ErrorMessage.Contains("صلاحية"));
        Assert.Equal(0, await db.SaleReturns.CountAsync());
    }

    [Fact]
    public async Task SaleReturn_Create_PostWithPermission_Posts()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedBasicAsync(db);
        var svc = new InventoryService(db);

        var invoice = new SaleInvoice { CustomerId = custId };
        var (okInv, errInv) = await svc.CreateSaleAsync(invoice,
            new List<SaleInvoiceItem> { new() { ItemId = itemId, Quantity = 10, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(okInv, errInv);
        await DeliverDirectAsync(db, invoice, itemId, 10);

        var controller = new SaleReturnsController(db, svc, new TestPermissionService("SaleReturns.Post"));
        var ctx = CreateHttpContext();
        ctx.Request.Form = new FormCollection(new Dictionary<string, StringValues> { ["submitAction"] = "post" });
        WireController(controller, ctx);

        var result = await controller.Create(new SaleReturn
        {
            SaleInvoiceId = invoice.Id, CustomerId = custId, ReturnDate = DateTime.Today
        }, new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 80 } });

        Assert.IsType<RedirectToActionResult>(result);
        var returnEntity = await db.SaleReturns.SingleAsync();
        Assert.Equal(ReturnStatus.Posted, returnEntity.Status);
    }

    [Fact]
    public async Task SaleReturn_Post_WithoutSourceInvoice_UsesItemPrice()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedBasicAsync(db);
        var svc = new InventoryService(db);

        var (ok, err) = await svc.CreateSaleReturnAsync(new SaleReturn
        {
            CustomerId = custId, ReturnDate = DateTime.Today
        }, new List<SaleReturnItem>
        {
            new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 999999m }
        }, "test");
        Assert.True(ok, err);

        var saved = await db.SaleReturns.Include(r => r.Items).SingleAsync();
        Assert.Equal(ReturnStatus.Posted, saved.Status);
        Assert.Equal(80m, saved.Items.Single().UnitPrice);
        Assert.Equal(160m, saved.TotalAmount);
    }

    [Fact]
    public async Task PurchaseReturn_Post_WithoutSourceInvoice_UsesItemPurchasePrice()
    {
        using var db = CreateContext();
        var (itemId, _, supplierId) = await SeedBasicAsync(db);
        var svc = new InventoryService(db);

        var (ok, err) = await svc.CreatePurchaseReturnAsync(new PurchaseReturn
        {
            SupplierId = supplierId, ReturnDate = DateTime.Today
        }, new List<PurchaseReturnItem>
        {
            new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 999999m }
        }, "test");
        Assert.True(ok, err);

        var saved = await db.PurchaseReturns.Include(r => r.Items).SingleAsync();
        Assert.Equal(ReturnStatus.Posted, saved.Status);
        Assert.Equal(50m, saved.Items.Single().UnitPrice);
        Assert.Equal(100m, saved.TotalAmount);
    }

    [Fact]
    public async Task SaleReturn_Post_WithSourceInvoice_IgnoresAbsurdClientPriceAndProratesDiscount()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedBasicAsync(db);
        var svc = new InventoryService(db, new AccountingService(db));

        // 10 x 80 = 800 gross with a header discount of 80 -> net 720, booked
        // Dr 1200 720 / Cr 4000 720. Returning 4 of 10 units is f = 320/800 = 0.4.
        var invoice = new SaleInvoice { CustomerId = custId, InvoiceDate = DateTime.Today };
        var (okInv, errInv) = await svc.CreateSaleAsync(invoice,
            new List<SaleInvoiceItem> { new() { ItemId = itemId, Quantity = 10, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(okInv, errInv);
        var created = await db.SaleInvoices.SingleAsync();
        created.Discount = 80m;
        created.NetAmount = 720m;
        await db.SaveChangesAsync();
        await DeliverDirectAsync(db, invoice, itemId, 10);

        var (ok, err) = await svc.CreateSaleReturnAsync(new SaleReturn
        {
            SaleInvoiceId = invoice.Id, CustomerId = custId, ReturnDate = DateTime.Today
        }, new List<SaleReturnItem>
        {
            new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 999999m }
        }, "test");
        Assert.True(ok, err);

        var saved = await db.SaleReturns.Include(r => r.Items).SingleAsync();
        Assert.Equal(ReturnStatus.Posted, saved.Status);
        Assert.Equal(80m, saved.Items.Single().UnitPrice);
        Assert.Equal(320m, saved.TotalAmount);

        var entry = await db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account)
            .SingleAsync(e => e.Source == JournalSource.SaleReturn);
        Assert.Equal(288m, entry.Lines.Single(l => l.Account!.Code == "1200").Credit);
        Assert.Equal(288m, entry.Lines.Single(l => l.Account!.Code == "5101").Debit);
        Assert.DoesNotContain(entry.Lines, l => l.Account!.Code is "2055" or "4000");
        Assert.Equal(entry.Lines.Sum(l => l.Debit), entry.Lines.Sum(l => l.Credit));
        Assert.All(entry.Lines, l => Assert.True(l.Debit < 999999m && l.Credit < 999999m));
    }

    [Fact]
    public async Task PurchaseReturn_Post_WithSourceInvoice_IgnoresAbsurdClientPriceAndProratesDiscount()
    {
        using var db = CreateContext();
        var (itemId, _, supplierId) = await SeedBasicAsync(db);
        var svc = new InventoryService(db, new AccountingService(db));

        // 10 x 50 = 500 gross with a header discount of 50 -> net 450, booked Dr 1300 450 / Cr 2000 450.
        // Returning 4 of 10 units is f = 200/500 = 0.4, so the supplier credit is 0.4 * 450 = 180.
        var invoice = new PurchaseInvoice
        {
            SupplierId = supplierId,
            InvoiceDate = DateTime.Today,
            PaymentTerms = InvoicePaymentTerms.Net30,
            Discount = 50m
        };
        var (okInv, errInv) = await svc.CreatePurchaseAsync(invoice,
            new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 10, Count = 0, UnitPrice = 50 } }, "test");
        Assert.True(okInv, errInv);
        Assert.Equal(450m, invoice.NetAmount);

        var (ok, err) = await svc.CreatePurchaseReturnAsync(new PurchaseReturn
        {
            PurchaseInvoiceId = invoice.Id, SupplierId = supplierId, ReturnDate = DateTime.Today
        }, new List<PurchaseReturnItem>
        {
            new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 999999m }
        }, "test");
        Assert.True(ok, err);

        var saved = await db.PurchaseReturns.Include(r => r.Items).SingleAsync();
        Assert.Equal(ReturnStatus.Posted, saved.Status);
        Assert.Equal(50m, saved.Items.Single().UnitPrice);
        Assert.Equal(200m, saved.TotalAmount);

        var entry = await db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account)
            .SingleAsync(e => e.Source == JournalSource.PurchaseReturn);
        Assert.Equal(180m, entry.Lines.Single(l => l.Account!.Code == "2000").Debit);
        Assert.Equal(180m, entry.Lines.Single(l => l.Account!.Code == "5102").Credit);
        Assert.Equal(entry.Lines.Sum(l => l.Debit), entry.Lines.Sum(l => l.Credit));
        Assert.All(entry.Lines, l => Assert.True(l.Debit < 999999m && l.Credit < 999999m));
    }

    [Fact]
    public async Task PurchaseOrder_RowVersion_IsConcurrencyTokenGeneratedByStore()
    {
        using var db = CreateContext();
        var (_, _, supplierId) = await SeedBasicAsync(db);
        var order = new PurchaseOrder { OrderNumber = "PO-ROWS", SupplierId = supplierId };
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync();
        Assert.Single(await db.PurchaseOrders.ToListAsync());

        // SQLite cannot produce a real rowversion, so assert the EF contract that makes
        // SQL Server reject a stale update: concurrency token generated by the store.
        var property = db.Model.FindEntityType(typeof(PurchaseOrder))!
            .FindProperty(nameof(PurchaseOrder.RowVersion))!;
        Assert.True(property.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
    }

    private static async Task<(int itemId, int customerId, int supplierId)> SeedBasicAsync(AppDbContext db)
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
        var supplier = new Supplier { Name = "مورد اختبار" };
        db.Customers.Add(customer);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        return (item.Id, customer.Id, supplier.Id);
    }

    private static void SeedChartOfAccounts(AppDbContext db)
    {
        (string Code, string Name, GLAccountType Type, NormalBalance Normal)[] accounts =
        {
            ("1200", "المدينون", GLAccountType.Asset, NormalBalance.Debit),
            ("2000", "الدائنون", GLAccountType.Liability, NormalBalance.Credit),
            ("2055", "الضريبة مستحقة", GLAccountType.Liability, NormalBalance.Credit),
            ("4000", "المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
            ("5101", "مرتجعات البيع", GLAccountType.Revenue, NormalBalance.Credit),
            ("5102", "مرتجعات المشتريات", GLAccountType.Expense, NormalBalance.Debit),
            ("1300", "المخزون", GLAccountType.Asset, NormalBalance.Debit),
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.SaveChanges();
    }

    private static async Task<(bool, string?)> DeliverDirectAsync(AppDbContext db, SaleInvoice invoice, int itemId, decimal qty)
    {
        var svc = new InventoryService(db, new AccountingService(db));
        var delivery = new DeliveryOrder { SaleInvoiceId = invoice.Id, DeliveryDate = DateTime.Today };
        var (ok, err) = await svc.CreateDeliveryOrderAsync(delivery, new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = qty, Count = 0 } }, "test");
        if (!ok) return (false, err);
        return await svc.DeliverDeliveryOrderAsync(delivery.Id, "test");
    }

    private static HttpContext CreateHttpContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.ContentType = "application/x-www-form-urlencoded";
        ctx.Request.Form = new FormCollection(new Dictionary<string, StringValues>());
        return ctx;
    }

    private static void WireController(Controller controller, HttpContext ctx)
    {
        controller.ControllerContext = new ControllerContext { HttpContext = ctx };
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new FakeTempDataProvider());
    }

    private sealed class FakeTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }
}