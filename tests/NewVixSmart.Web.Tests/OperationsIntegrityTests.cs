using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using NewVixSmart.Web.Api.Dtos;
using NewVixSmart.Web.Controllers;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Accounting;
using Xunit;
using ApiPaymentsController = NewVixSmart.Web.Api.PaymentsController;

namespace NewVixSmart.Web.Tests;

public sealed class OperationsIntegrityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public OperationsIntegrityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private async Task<(int itemId, int customerId, int supplierId)> SeedAsync(AppDbContext db)
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

    private static async Task<int> AddItemAsync(AppDbContext db, string name)
    {
        var cat = new ItemCategory { Name = $"تصنيف {name}" };
        var type = new ItemType { Name = $"نوع {name}" };
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Name == "قطعة");
        if (unit == null)
        {
            unit = new Unit { Name = "قطعة" };
            db.Units.Add(unit);
        }
        var item = new Item
        {
            Name = name,
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
        await db.SaveChangesAsync();
        return item.Id;
    }

    private static async Task<int> AddCustomerAsync(AppDbContext db, string name)
    {
        var customer = new Customer { Name = name };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private static async Task<int> AddSupplierAsync(AppDbContext db, string name)
    {
        var supplier = new Supplier { Name = name };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier.Id;
    }

    private static async Task<(int wh1Id, int wh2Id)> SeedWarehousesAsync(AppDbContext db)
    {
        var wh1 = new Warehouse { Code = "WH-OP1", Name = "مخزن اختبار 1", IsActive = true };
        var wh2 = new Warehouse { Code = "WH-OP2", Name = "مخزن اختبار 2", IsActive = true };
        db.Warehouses.AddRange(wh1, wh2);
        await db.SaveChangesAsync();
        return (wh1.Id, wh2.Id);
    }

    private static async Task SeedPurchaseLayerAsync(AppDbContext db, int itemId, int warehouseId, decimal qty, decimal unitCost)
    {
        db.StockLayers.Add(new StockLayer
        {
            ItemId = itemId,
            WarehouseId = warehouseId,
            Qty = qty,
            Count = 0,
            UnitCost = unitCost,
            CountCost = unitCost,
            DateReceived = DateTime.Today.AddDays(-10),
            RemainingQty = qty,
            RemainingCount = 0
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCurrenciesAsync(AppDbContext db)
    {
        db.Currencies.AddRange(
            new Currency { Code = "SDG", Name = "جنيه", ExchangeRate = 1m, IsBase = true, IsActive = true },
            new Currency { Code = "USD", Name = "دولار", ExchangeRate = 500m, IsBase = false, IsActive = true });
        await db.SaveChangesAsync();
    }

    private static async Task SeedSaleInvoiceAsync(AppDbContext db, int customerId, decimal net)
    {
        db.SaleInvoices.Add(new SaleInvoice
        {
            InvoiceNumber = $"SI-OP-{Guid.NewGuid():N}".Substring(0, 12),
            CustomerId = customerId,
            TotalAmount = net,
            NetAmount = net,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedPurchaseInvoiceAsync(AppDbContext db, int supplierId, decimal net)
    {
        db.PurchaseInvoices.Add(new PurchaseInvoice
        {
            InvoiceNumber = $"PI-OP-{Guid.NewGuid():N}".Substring(0, 12),
            SupplierId = supplierId,
            TotalAmount = net,
            NetAmount = net,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

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

    private static (ProcurementService proc, InventoryService inv) Services(AppDbContext db)
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

    private static SaleInvoiceItem QtyLine(int itemId, decimal qty, decimal price) => new()
    {
        ItemId = itemId, Quantity = qty, Count = 0, UnitPrice = price
    };

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

    [Fact]
    public async Task CreateSale_TwoLinesSameItem_Rejected()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var invoice = new SaleInvoice { CustomerId = custId };
        var (ok, err) = await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem>
        {
            new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 80 },
            new() { ItemId = itemId, Quantity = 6, Count = 0, UnitPrice = 80 }
        }, "test");

        Assert.False(ok);
        Assert.Contains("مكرر", err);
        Assert.Equal(0, await db.SaleInvoices.CountAsync());
        Assert.Equal(100, (await db.Items.SingleAsync()).CurrentQuantity);
    }

    [Fact]
    public async Task CreatePurchase_TwoLinesSameItem_Rejected()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var invoice = new PurchaseInvoice { SupplierId = supId };
        var (ok, err) = await svc.CreatePurchaseAsync(invoice, new List<PurchaseInvoiceItem>
        {
            new() { ItemId = itemId, Quantity = 5, Count = 0, UnitPrice = 45 },
            new() { ItemId = itemId, Quantity = 5, Count = 0, UnitPrice = 45 }
        }, "test");

        Assert.False(ok);
        Assert.Contains("مكرر", err);
        Assert.Equal(0, await db.PurchaseInvoices.CountAsync());
        Assert.Equal(0, await db.StockLayers.CountAsync());
        Assert.Equal(100, (await db.Items.SingleAsync()).CurrentQuantity);
    }

    [Fact]
    public async Task CreateSale_TwoLinesDifferentItems_Accepted()
    {
        using var db = CreateContext();
        var (item1Id, custId, _) = await SeedAsync(db);
        var item2Id = await AddItemAsync(db, "صنف اختبار ثانٍ");
        var svc = new InventoryService(db);

        var invoice = new SaleInvoice { CustomerId = custId };
        var (ok, err) = await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem>
        {
            new() { ItemId = item1Id, Quantity = 4, Count = 0, UnitPrice = 80 },
            new() { ItemId = item2Id, Quantity = 6, Count = 0, UnitPrice = 80 }
        }, "test");

        Assert.True(ok, err);
        Assert.Equal(1, await db.SaleInvoices.CountAsync());
        Assert.Equal(96, (await db.Items.FindAsync(item1Id))!.CurrentQuantity);
        Assert.Equal(94, (await db.Items.FindAsync(item2Id))!.CurrentQuantity);
    }

    [Fact]
    public async Task CreateTransfer_TwoLinesSameItem_Rejected()
    {
        using var db = CreateContext();
        var (itemId, _, _) = await SeedAsync(db);
        var (wh1Id, wh2Id) = await SeedWarehousesAsync(db);
        await SeedPurchaseLayerAsync(db, itemId, wh1Id, 20, 30);
        var svc = new InventoryService(db);

        var transfer = new StockTransfer { SourceWarehouseId = wh1Id, TargetWarehouseId = wh2Id, TransferDate = DateTime.UtcNow };
        var (ok, err) = await svc.CreateTransferAsync(transfer, new List<StockTransferItem>
        {
            new() { ItemId = itemId, Quantity = 5, Count = 0 },
            new() { ItemId = itemId, Quantity = 5, Count = 0 }
        }, "test");

        Assert.False(ok);
        Assert.Contains("مكرر", err);
        Assert.Equal(0, await db.StockTransfers.CountAsync());
        Assert.Equal(0, await db.StockMovements.CountAsync());
        Assert.Equal(20, (await db.StockLayers.SingleAsync()).RemainingQty);
    }

    [Fact]
    public async Task SaleReturn_PostedOnly_CountsTowardReturnableRemainder()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var invoice = new SaleInvoice { CustomerId = custId };
        var (okInv, errInv) = await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem> { QtyLine(itemId, 10, 80) }, "test");
        Assert.True(okInv, errInv);

        var (ok1, _, postedId) = await svc.CreateSaleReturnDraftAsync(new SaleReturn
        {
            SaleInvoiceId = invoice.Id, CustomerId = custId, ReturnDate = DateTime.Today.AddDays(-1)
        }, new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok1);
        var (okPost, errPost) = await svc.PostSaleReturnAsync(postedId, "test");
        Assert.True(okPost, errPost);

        var (okDraft, _, _) = await svc.CreateSaleReturnDraftAsync(new SaleReturn
        {
            SaleInvoiceId = invoice.Id, CustomerId = custId, ReturnDate = DateTime.Today
        }, new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(okDraft);
        Assert.Equal(ReturnStatus.Draft, (await db.SaleReturns.OrderByDescending(r => r.Id).FirstAsync()).Status);

        var (okBig, errBig, bigId) = await svc.CreateSaleReturnDraftAsync(new SaleReturn
        {
            SaleInvoiceId = invoice.Id, CustomerId = custId, ReturnDate = DateTime.Today
        }, new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 6, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(okBig, errBig);
        var (okPostBig, errPostBig) = await svc.PostSaleReturnAsync(bigId, "test");
        Assert.True(okPostBig, errPostBig);

        Assert.Equal(2, await db.SaleReturns.CountAsync(r => r.Status == ReturnStatus.Posted));
    }

    [Fact]
    public async Task SaleReturnsController_DraftDoesNotReduceReturnableRemainder()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var invoice = new SaleInvoice { CustomerId = custId };
        var (okInv, errInv) = await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem> { QtyLine(itemId, 10, 80) }, "test");
        Assert.True(okInv, errInv);

        var (ok1, _, postedId) = await svc.CreateSaleReturnDraftAsync(new SaleReturn
        {
            SaleInvoiceId = invoice.Id, CustomerId = custId, ReturnDate = DateTime.Today.AddDays(-1)
        }, new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok1);
        var (okPost, errPost) = await svc.PostSaleReturnAsync(postedId, "test");
        Assert.True(okPost, errPost);

        var (okDraft, _, _) = await svc.CreateSaleReturnDraftAsync(new SaleReturn
        {
            SaleInvoiceId = invoice.Id, CustomerId = custId, ReturnDate = DateTime.Today
        }, new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(okDraft);

        var controller = new SaleReturnsController(db, svc);
        WireController(controller, CreateHttpContext());

        var result = await controller.Create(new SaleReturn
        {
            SaleInvoiceId = invoice.Id, CustomerId = custId, ReturnDate = DateTime.Today
        }, new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 6, Count = 0, UnitPrice = 80 } });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(controller.ModelState.IsValid);
        Assert.Equal(3, await db.SaleReturns.CountAsync());
        var newReturn = await db.SaleReturns.OrderByDescending(r => r.Id).FirstAsync();
        Assert.Equal(ReturnStatus.Draft, newReturn.Status);
    }

    [Fact]
    public async Task SaleReturnsController_PostedReturn_ExceedingRemainder_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var invoice = new SaleInvoice { CustomerId = custId };
        var (okInv, errInv) = await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem> { QtyLine(itemId, 10, 80) }, "test");
        Assert.True(okInv, errInv);

        var (ok, _, postedId) = await svc.CreateSaleReturnDraftAsync(new SaleReturn
        {
            SaleInvoiceId = invoice.Id, CustomerId = custId, ReturnDate = DateTime.Today.AddDays(-1)
        }, new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 8, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok);
        var (okPost, errPost) = await svc.PostSaleReturnAsync(postedId, "test");
        Assert.True(okPost, errPost);

        var controller = new SaleReturnsController(db, svc);
        WireController(controller, CreateHttpContext());

        var result = await controller.Create(new SaleReturn
        {
            SaleInvoiceId = invoice.Id, CustomerId = custId, ReturnDate = DateTime.Today
        }, new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 3, Count = 0, UnitPrice = 80 } });

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(controller.ModelState.Values.SelectMany(v => v.Errors), e => e.ErrorMessage.Contains("أكبر"));
        Assert.Equal(1, await db.SaleReturns.CountAsync());
    }

    [Fact]
    public async Task PurchaseReturnsController_PostedReturn_ExceedingRemainder_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var invoice = new PurchaseInvoice { SupplierId = supId };
        var (okInv, errInv) = await svc.CreatePurchaseAsync(invoice, new List<PurchaseInvoiceItem>
        {
            new() { ItemId = itemId, Quantity = 20, Count = 20, UnitPrice = 45 }
        }, "test");
        Assert.True(okInv, errInv);

        var (ok, _, postedId) = await svc.CreatePurchaseReturnDraftAsync(new PurchaseReturn
        {
            PurchaseInvoiceId = invoice.Id, SupplierId = supId, ReturnDate = DateTime.Today.AddDays(-1)
        }, new List<PurchaseReturnItem> { new() { ItemId = itemId, Quantity = 16, Count = 16, UnitPrice = 45 } }, "test");
        Assert.True(ok);
        var (okPost, errPost) = await svc.PostPurchaseReturnAsync(postedId, "test");
        Assert.True(okPost, errPost);

        var controller = new PurchaseReturnsController(db, svc);
        WireController(controller, CreateHttpContext());

        var result = await controller.Create(new PurchaseReturn
        {
            PurchaseInvoiceId = invoice.Id, SupplierId = supId, ReturnDate = DateTime.Today
        }, new List<PurchaseReturnItem> { new() { ItemId = itemId, Quantity = 5, Count = 5, UnitPrice = 45 } });

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(controller.ModelState.Values.SelectMany(v => v.Errors), e => e.ErrorMessage.Contains("أكبر"));
        Assert.Equal(1, await db.PurchaseReturns.CountAsync());
    }

    [Fact]
    public async Task CreatePayment_DuplicateIdentical_SecondIsRejected()
    {
        using var db = CreateContext();
        var custId = await AddCustomerAsync(db, "عميل مدفوعات");
        await SeedSaleInvoiceAsync(db, custId, 1000m);
        var svc = new PaymentService(db);

        var p1 = new Payment { Type = PaymentType.Receipt, CustomerId = custId, Amount = 200m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today };
        var (ok1, err1, _) = await svc.CreatePaymentAsync(p1, "test");
        Assert.True(ok1, err1);

        var p2 = new Payment { Type = PaymentType.Receipt, CustomerId = custId, Amount = 200m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today };
        var (ok2, err2, _) = await svc.CreatePaymentAsync(p2, "test");
        Assert.False(ok2);
        Assert.Contains("مطابقة", err2);

        Assert.Equal(1, await db.Payments.CountAsync());
        Assert.Equal(200m, (await db.SaleInvoices.SingleAsync()).PaidAmount);
    }

    [Fact]
    public async Task CreatePayment_SameAmountDifferentCurrency_NotDuplicate()
    {
        using var db = CreateContext();
        var custId = await AddCustomerAsync(db, "عميل عملات");
        await SeedCurrenciesAsync(db);
        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        await SeedSaleInvoiceAsync(db, custId, 2000m);
        var svc = new PaymentService(db);

        var basePay = new Payment { Type = PaymentType.Receipt, CustomerId = custId, Amount = 300m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today };
        var (ok1, e1, _) = await svc.CreatePaymentAsync(basePay, "test");
        Assert.True(ok1, e1);

        var usdPay1 = new Payment { Type = PaymentType.Receipt, CustomerId = custId, Amount = 300m, CurrencyId = usd.Id, ExchangeRate = 2m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today };
        var (ok2, e2, _) = await svc.CreatePaymentAsync(usdPay1, "test");
        Assert.True(ok2, e2);

        var usdPay2 = new Payment { Type = PaymentType.Receipt, CustomerId = custId, Amount = 300m, CurrencyId = usd.Id, ExchangeRate = 3m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today };
        var (ok3, e3, _) = await svc.CreatePaymentAsync(usdPay2, "test");
        Assert.True(ok3, e3);

        Assert.Equal(3, await db.Payments.CountAsync());
    }

    [Fact]
    public async Task ForeignPayment_RateEqualsOne_StillForeign()
    {
        using var db = CreateContext();
        await SeedCurrenciesAsync(db);
        var custId = await AddCustomerAsync(db, "عميل دولاري");
        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        await SeedSaleInvoiceAsync(db, custId, 500m);
        var svc = new PaymentService(db);

        var (ok, err, payment) = await svc.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt, CustomerId = custId, Amount = 100m,
            CurrencyId = usd.Id, ExchangeRate = 1m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today
        }, "test");

        Assert.True(ok, err);
        Assert.Equal(100m, payment!.BaseAmount);
        var allocation = await db.PaymentAllocations.SingleAsync();
        Assert.Equal(1m, allocation.ExchangeRateAtSettlement);
    }

    [Fact]
    public async Task CreatePayment_MissingReceiptNumber_ServiceAssignsPayNumber()
    {
        using var db = CreateContext();
        var custId = await AddCustomerAsync(db, "عميل أرقام");
        await SeedSaleInvoiceAsync(db, custId, 500m);
        var svc = new PaymentService(db);

        var (ok, err, payment) = await svc.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt, CustomerId = custId, Amount = 200m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today
        }, "test");

        Assert.True(ok, err);
        Assert.NotNull(payment);
        Assert.StartsWith("PAY-", payment!.ReceiptNumber);
        Assert.Equal(1, await db.Payments.CountAsync());
        Assert.Equal(payment.ReceiptNumber, (await db.Payments.SingleAsync()).ReceiptNumber);
    }

    [Fact]
    public async Task CreatePayment_TwoRapidPayments_GetDistinctServiceNumbers()
    {
        using var db = CreateContext();
        var custId = await AddCustomerAsync(db, "عميل أرقام متتالية");
        await SeedSaleInvoiceAsync(db, custId, 1000m);
        var svc = new PaymentService(db);

        var (ok1, e1, p1) = await svc.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt, CustomerId = custId, Amount = 100m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today
        }, "test");
        Assert.True(ok1, e1);

        var (ok2, e2, p2) = await svc.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt, CustomerId = custId, Amount = 250m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today
        }, "test");
        Assert.True(ok2, e2);

        Assert.StartsWith("PAY-", p1!.ReceiptNumber);
        Assert.StartsWith("PAY-", p2!.ReceiptNumber);
        Assert.NotEqual(p1.ReceiptNumber, p2.ReceiptNumber);
    }

    [Fact]
    public async Task ApiCreatePayment_ReceiptNumber_AssignedByService()
    {
        using var db = CreateContext();
        var custId = await AddCustomerAsync(db, "عميل API");
        await SeedSaleInvoiceAsync(db, custId, 500m);
        var svc = new PaymentService(db);

        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var controller = new ApiPaymentsController(svc, http)
        {
            ControllerContext = new ControllerContext { HttpContext = http.HttpContext! }
        };

        var actionResult = await controller.CreatePayment(new CreatePaymentRequest
        {
            Type = "receipt", CustomerId = custId, Amount = 30m, Method = "Cash"
        });

        var objectResult = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<ApiResponse<Payment>>(objectResult.Value);
        Assert.True(response.Success);
        Assert.StartsWith("PAY-", response.Data!.ReceiptNumber);
    }

    [Fact]
    public async Task StockTransfersController_DoesNotWipeValidationErrors()
    {
        using var db = CreateContext();
        var (itemId, _, _) = await SeedAsync(db);
        var (wh1Id, wh2Id) = await SeedWarehousesAsync(db);
        var svc = new InventoryService(db);

        var controller = new StockTransfersController(db, svc);
        WireController(controller, CreateHttpContext());
        controller.ModelState.AddModelError("TransferNumber", "رقم التحويل مطلوب");

        var result = await controller.Create(new StockTransfer { SourceWarehouseId = wh1Id, TargetWarehouseId = wh1Id, TransferDate = DateTime.UtcNow },
            new List<StockTransferItem> { new() { ItemId = itemId, Quantity = 5, Count = 0 } });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState["TransferNumber"]!.Errors, e => e.ErrorMessage.Contains("مطلوب"));
        Assert.Equal(0, await db.StockTransfers.CountAsync());
        Assert.False(await db.StockLayers.AnyAsync());
    }

    [Fact]
    public async Task ShipmentsController_SaleShipment_WrongCustomer_Rejected()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var otherCustId = await AddCustomerAsync(db, "عميل خاطئ");
        var svc = new InventoryService(db);
        var invoice = new SaleInvoice { CustomerId = custId };
        var (okInv, errInv) = await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem> { QtyLine(itemId, 1, 80) }, "test");
        Assert.True(okInv, errInv);

        var controller = new ShipmentsController(db);
        WireController(controller, CreateHttpContext());
        var vm = new ShipmentFormViewModel
        {
            Shipment = new Shipment { InvoiceType = ShipmentInvoiceType.Sale, SaleInvoiceId = invoice.Id, CustomerId = otherCustId, ShipDate = DateTime.Today }
        };

        var result = await controller.Create(vm);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(controller.ModelState.Values.SelectMany(v => v.Errors), e => e.ErrorMessage.Contains("لا تخص العميل المحدد"));
        Assert.Equal(0, await db.Shipments.CountAsync());
    }

    [Fact]
    public async Task ShipmentsController_PurchaseShipment_WrongSupplier_Rejected()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var otherSupId = await AddSupplierAsync(db, "مورد خاطئ");
        var svc = new InventoryService(db);
        var invoice = new PurchaseInvoice { SupplierId = supId };
        var (okInv, errInv) = await svc.CreatePurchaseAsync(invoice, new List<PurchaseInvoiceItem>
        {
            new() { ItemId = itemId, Quantity = 5, Count = 0, UnitPrice = 45 }
        }, "test");
        Assert.True(okInv, errInv);

        var controller = new ShipmentsController(db);
        WireController(controller, CreateHttpContext());
        var vm = new ShipmentFormViewModel
        {
            Shipment = new Shipment { InvoiceType = ShipmentInvoiceType.Purchase, PurchaseInvoiceId = invoice.Id, SupplierId = otherSupId, ShipDate = DateTime.Today }
        };

        var result = await controller.Create(vm);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(controller.ModelState.Values.SelectMany(v => v.Errors), e => e.ErrorMessage.Contains("لا تخص المورد المحدد"));
        Assert.Equal(0, await db.Shipments.CountAsync());
    }

    [Fact]
    public async Task ShipmentsController_MatchingPartyShipment_Succeeds()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);
        var invoice = new SaleInvoice { CustomerId = custId };
        var (okInv, errInv) = await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem> { QtyLine(itemId, 1, 80) }, "test");
        Assert.True(okInv, errInv);

        var controller = new ShipmentsController(db);
        WireController(controller, CreateHttpContext());
        var vm = new ShipmentFormViewModel
        {
            Shipment = new Shipment { InvoiceType = ShipmentInvoiceType.Sale, SaleInvoiceId = invoice.Id, CustomerId = custId, ShipDate = DateTime.Today }
        };

        var result = await controller.Create(vm);

        Assert.IsType<RedirectToActionResult>(result);
        var shipment = await db.Shipments.SingleAsync();
        Assert.StartsWith("SHP-", shipment.ShipmentNumber);
        Assert.Equal(custId, shipment.CustomerId);
    }

    [Fact]
    public async Task ReceiveOrderLine_OverReceiveAfterPartial_LeavesStateIntact()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, _, supId) = await SeedAsync(db);
        var (proc, _) = Services(db);

        var orderId = await CreateApprovedOrderAsync(db, itemId, supId, 10);
        var orderItem = await db.PurchaseOrderItems.FirstAsync(o => o.PurchaseOrderId == orderId);

        var (ok, err) = await proc.ReceiveOrderLineAsync(orderId, orderItem.Id, 4, 4);
        Assert.True(ok, err);

        var (ok2, err2) = await proc.ReceiveOrderLineAsync(orderId, orderItem.Id, 7, 7);
        Assert.False(ok2);
        Assert.Contains("أكبر", err2);

        var refreshed = await db.PurchaseOrders.Include(o => o.Items).FirstAsync(o => o.Id == orderId);
        Assert.Equal(4, refreshed.Items.Single().ReceivedQty);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, refreshed.Status);
        Assert.False(await db.StockLayers.AnyAsync());
    }

    [Fact]
    public async Task ReceiveOrderLine_FullReceiveThenInvoice_PostsConsistently()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, _, supId) = await SeedAsync(db);
        var (proc, _) = Services(db);

        var orderId = await CreateApprovedOrderAsync(db, itemId, supId, 10);
        var orderItem = await db.PurchaseOrderItems.FirstAsync(o => o.PurchaseOrderId == orderId);

        var (ok, _) = await proc.ReceiveOrderLineAsync(orderId, orderItem.Id, 10, 10);
        Assert.True(ok);

        var (ok2, err2) = await proc.CreateInvoiceFromOrderAsync(orderId, "test");
        Assert.True(ok2, err2);

        var invoice = await db.PurchaseInvoices.Include(i => i.Items).SingleAsync();
        Assert.Equal(orderId, invoice.PurchaseOrderId);
        Assert.Equal(10, invoice.Items.Single().Quantity);
        Assert.Equal(400, invoice.TotalAmount);

        var item = await db.Items.SingleAsync();
        Assert.Equal(110, item.CurrentQuantity);
        Assert.Equal(110, item.CurrentCount);

        var layer = await db.StockLayers.SingleAsync();
        Assert.Equal(10, layer.Qty);
        Assert.Equal(40, layer.UnitCost);

        var entry = await db.JournalEntries.SingleAsync(e => e.Source == JournalSource.PurchaseInvoice);
        var lines = await db.JournalEntryLines.Where(l => l.JournalEntryId == entry.Id).Include(l => l.Account).ToListAsync();
        Assert.Contains(lines, l => l.Account!.Code == "1300" && l.Debit == 400m);
        Assert.Contains(lines, l => l.Account!.Code == "2000" && l.Credit == 400m);
    }
}