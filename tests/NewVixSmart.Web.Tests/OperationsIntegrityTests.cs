using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using NewVixSmart.Web.Api.Dtos;
using NewVixSmart.Web.Controllers;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Forms;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Accounting;
using NewVixSmart.Web.ViewModels.Sales;
using NewVixSmart.Web.ViewModels.Stock;
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

    private static async Task SeedSaleInvoiceAsync(AppDbContext db, int customerId, decimal net)
    {
        var invoice = new SaleInvoice
        {
            InvoiceNumber = $"SI-OP-{Guid.NewGuid():N}".Substring(0, 12),
            CustomerId = customerId,
            TotalAmount = net,
            NetAmount = net,
            CreatedAt = DateTime.UtcNow
        };
        db.SaleInvoices.Add(invoice);
        await db.SaveChangesAsync();
        db.DeliveryOrders.Add(new DeliveryOrder
        {
            DeliveryNumber = $"DLV-OP-{invoice.Id}",
            SaleInvoiceId = invoice.Id,
            CustomerId = customerId,
            DeliveryDate = DateTime.Today,
            Status = DeliveryOrderStatus.Delivered,
            DeliveredAt = DateTime.UtcNow
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
        ItemId = itemId,
        Quantity = qty,
        Count = 0,
        UnitPrice = price
    };

    private static async Task<int> DeliverAsync(AppDbContext db, SaleInvoice invoice, int itemId, decimal qty)
    {
        var svc = new InventoryService(db);
        var delivery = new DeliveryOrder { SaleInvoiceId = invoice.Id, DeliveryDate = DateTime.Today };
        var (ok, err) = await svc.CreateDeliveryOrderAsync(delivery, new List<DeliveryOrderItem> { new() { ItemId = itemId, Quantity = qty, Count = 0 } }, "test");
        if (!ok)
        {
            throw new InvalidOperationException(err);
        }

        var (dok, derr) = await svc.DeliverDeliveryOrderAsync(delivery.Id, "test");
        if (!dok)
        {
            throw new InvalidOperationException(derr);
        }

        return delivery.Id;
    }

    private static HttpContext CreateHttpContext(string? user = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.ContentType = "application/x-www-form-urlencoded";
        ctx.Request.Form = new FormCollection(new Dictionary<string, StringValues>());
        if (user != null)
        {
            ctx.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, user)], "TestAuth"));
        }

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

        var delivery = new DeliveryOrder { SaleInvoiceId = invoice.Id, DeliveryDate = DateTime.Today };
        var (dOk, dErr) = await svc.CreateDeliveryOrderAsync(delivery, new List<DeliveryOrderItem>
        {
            new() { ItemId = item1Id, Quantity = 4, Count = 0 },
            new() { ItemId = item2Id, Quantity = 6, Count = 0 }
        }, "test");
        Assert.True(dOk, dErr);
        var (dlvOk, dlvErr) = await svc.DeliverDeliveryOrderAsync(delivery.Id, "test");
        Assert.True(dlvOk, dlvErr);

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

        await DeliverAsync(db, invoice, itemId, 10);

        var (ok1, _, postedId) = await svc.CreateSaleReturnDraftAsync(new SaleReturn
        {
            SaleInvoiceId = invoice.Id,
            CustomerId = custId,
            ReturnDate = DateTime.Today.AddDays(-1)
        }, new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok1);
        var (okPost, errPost) = await svc.PostSaleReturnAsync(postedId, "test");
        Assert.True(okPost, errPost);

        var (okDraft, _, _) = await svc.CreateSaleReturnDraftAsync(new SaleReturn
        {
            SaleInvoiceId = invoice.Id,
            CustomerId = custId,
            ReturnDate = DateTime.Today
        }, new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(okDraft);
        Assert.Equal(ReturnStatus.Draft, (await db.SaleReturns.OrderByDescending(r => r.Id).FirstAsync()).Status);

        var (okBig, errBig, bigId) = await svc.CreateSaleReturnDraftAsync(new SaleReturn
        {
            SaleInvoiceId = invoice.Id,
            CustomerId = custId,
            ReturnDate = DateTime.Today
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

        await DeliverAsync(db, invoice, itemId, 10);

        var (ok1, _, postedId) = await svc.CreateSaleReturnDraftAsync(new SaleReturn
        {
            SaleInvoiceId = invoice.Id,
            CustomerId = custId,
            ReturnDate = DateTime.Today.AddDays(-1)
        }, new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok1);
        var (okPost, errPost) = await svc.PostSaleReturnAsync(postedId, "test");
        Assert.True(okPost, errPost);

        var (okDraft, _, _) = await svc.CreateSaleReturnDraftAsync(new SaleReturn
        {
            SaleInvoiceId = invoice.Id,
            CustomerId = custId,
            ReturnDate = DateTime.Today
        }, new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(okDraft);

        var controller = new SaleReturnsController(db, svc, new TestPermissionService());
        WireController(controller, CreateHttpContext());

        var result = await controller.Create(new SaleReturnFormModel
        {
            SaleInvoiceId = invoice.Id,
            CustomerId = custId,
            ReturnDate = DateTime.Today
        }, new List<SaleReturnLineFormModel> { new() { ItemId = itemId, Quantity = 6, Count = 0, UnitPrice = 80 } });

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

        await DeliverAsync(db, invoice, itemId, 10);

        var (ok, _, postedId) = await svc.CreateSaleReturnDraftAsync(new SaleReturn
        {
            SaleInvoiceId = invoice.Id,
            CustomerId = custId,
            ReturnDate = DateTime.Today.AddDays(-1)
        }, new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 8, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok);
        var (okPost, errPost) = await svc.PostSaleReturnAsync(postedId, "test");
        Assert.True(okPost, errPost);

        var controller = new SaleReturnsController(db, svc, new TestPermissionService());
        WireController(controller, CreateHttpContext());

        var result = await controller.Create(new SaleReturnFormModel
        {
            SaleInvoiceId = invoice.Id,
            CustomerId = custId,
            ReturnDate = DateTime.Today
        }, new List<SaleReturnLineFormModel> { new() { ItemId = itemId, Quantity = 3, Count = 0, UnitPrice = 80 } });

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
            PurchaseInvoiceId = invoice.Id,
            SupplierId = supId,
            ReturnDate = DateTime.Today.AddDays(-1)
        }, new List<PurchaseReturnItem> { new() { ItemId = itemId, Quantity = 16, Count = 16, UnitPrice = 45 } }, "test");
        Assert.True(ok);
        var (okPost, errPost) = await svc.PostPurchaseReturnAsync(postedId, "test");
        Assert.True(okPost, errPost);

        var controller = new PurchaseReturnsController(db, svc, new TestPermissionService());
        WireController(controller, CreateHttpContext());

        var result = await controller.Create(new PurchaseReturnFormModel
        {
            PurchaseInvoiceId = invoice.Id,
            SupplierId = supId,
            ReturnDate = DateTime.Today
        }, new List<PurchaseReturnLineFormModel> { new() { ItemId = itemId, Quantity = 5, Count = 5, UnitPrice = 45 } });

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
    public async Task CreatePayment_DifferentAmountSameDay_IsNotDuplicate()
    {
        // With one currency the duplicate key is amount + party + date + type. It must still
        // discriminate on the amount, otherwise two real instalments would be refused.
        using var db = CreateContext();
        var custId = await AddCustomerAsync(db, "عميل أقساط");
        await SeedSaleInvoiceAsync(db, custId, 1000m);
        var svc = new PaymentService(db);

        var (ok1, e1, _) = await svc.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = custId,
            Amount = 200m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "test");
        Assert.True(ok1, e1);

        var (ok2, e2, _) = await svc.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = custId,
            Amount = 300m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "test");
        Assert.True(ok2, e2);

        Assert.Equal(2, await db.Payments.CountAsync());
        Assert.Equal(500m, (await db.SaleInvoices.SingleAsync()).PaidAmount);
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
            Type = PaymentType.Receipt,
            CustomerId = custId,
            Amount = 200m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
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
            Type = PaymentType.Receipt,
            CustomerId = custId,
            Amount = 100m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "test");
        Assert.True(ok1, e1);

        var (ok2, e2, p2) = await svc.CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = custId,
            Amount = 250m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
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
            Type = "receipt",
            CustomerId = custId,
            Amount = 30m,
            Method = "Cash"
        });

        var objectResult = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<ApiResponse<PaymentResponse>>(objectResult.Value);
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

        var result = await controller.Create(new StockTransferFormViewModel
        {
            Transfer = new StockTransferFormModel
            {
                SourceWarehouseId = wh1Id,
                TargetWarehouseId = wh1Id,
                TransferDate = DateTime.UtcNow
            },
            Items = new List<StockTransferLineFormModel> { new() { ItemId = itemId, Quantity = 5, Count = 0 } }
        });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState["TransferNumber"]!.Errors, e => e.ErrorMessage.Contains("مطلوب"));
        Assert.Equal(0, await db.StockTransfers.CountAsync());
        Assert.False(await db.StockLayers.AnyAsync());
    }

    [Fact]
    public async Task DeliveryOrdersController_ItemsExceedingInvoice_Rejected()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);
        var invoice = new SaleInvoice { CustomerId = custId };
        var (okInv, errInv) = await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem> { QtyLine(itemId, 1, 80) }, "test");
        Assert.True(okInv, errInv);

        var controller = new DeliveryOrdersController(db, svc);
        WireController(controller, CreateHttpContext());
        var vm = new DeliveryOrderViewModel
        {
            Delivery = new DeliveryOrderFormModel { SaleInvoiceId = invoice.Id, CustomerId = custId, DeliveryDate = DateTime.Today },
            Items = new List<DeliveryOrderLineFormModel> { new() { ItemId = itemId, Quantity = 2, Count = 0 } }
        };

        var result = await controller.Create(vm);

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState.Values.SelectMany(v => v.Errors), e => e.ErrorMessage.Contains("متبقي"));
        Assert.Equal(0, await db.DeliveryOrders.CountAsync());
    }

    [Fact]
    public async Task DeliveryOrdersController_NoItems_Rejected()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);
        var invoice = new SaleInvoice { CustomerId = custId };
        var (okInv, errInv) = await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem> { QtyLine(itemId, 1, 80) }, "test");
        Assert.True(okInv, errInv);

        var controller = new DeliveryOrdersController(db, svc);
        WireController(controller, CreateHttpContext());
        var vm = new DeliveryOrderViewModel
        {
            Delivery = new DeliveryOrderFormModel { SaleInvoiceId = invoice.Id, CustomerId = custId, DeliveryDate = DateTime.Today },
            Items = new List<DeliveryOrderLineFormModel>()
        };

        var result = await controller.Create(vm);

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState.Values.SelectMany(v => v.Errors), e => e.ErrorMessage.Contains("صنف"));
        Assert.Equal(0, await db.DeliveryOrders.CountAsync());
    }

    [Fact]
    public async Task DeliveryOrdersController_ValidCreate_Succeeds()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);
        var invoice = new SaleInvoice { CustomerId = custId };
        var (okInv, errInv) = await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem> { QtyLine(itemId, 1, 80) }, "test");
        Assert.True(okInv, errInv);

        var controller = new DeliveryOrdersController(db, svc);
        WireController(controller, CreateHttpContext());
        var vm = new DeliveryOrderViewModel
        {
            Delivery = new DeliveryOrderFormModel { SaleInvoiceId = invoice.Id, CustomerId = custId, DeliveryDate = DateTime.Today },
            Items = new List<DeliveryOrderLineFormModel> { new() { ItemId = itemId, Quantity = 1, Count = 0 } }
        };

        var result = await controller.Create(vm);

        Assert.IsType<RedirectToActionResult>(result);
        var delivery = await db.DeliveryOrders.SingleAsync();
        Assert.StartsWith("DLV-", delivery.DeliveryNumber);
        Assert.Equal(custId, delivery.CustomerId);
        Assert.Equal(invoice.Id, delivery.SaleInvoiceId);
    }

    [Fact]
    public async Task DeliveryOrdersController_OrderSource_IgnoresTheEmptyCustomerSelect()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var orders = new SalesOrdersService(db, new InventoryService(db));
        var order = new SalesOrder { CustomerId = custId, OrderDate = DateTime.Today };
        var (ok, err) = await orders.CreateOrderAsync(order,
            new List<SalesOrderItem> { new() { ItemId = itemId, Quantity = 5, Count = 0, UnitPrice = 80 } }, "test");
        Assert.True(ok, err);
        var approved = await db.SalesOrders.FindAsync(order.Id);
        approved!.Status = SalesOrderStatus.Approved;
        await db.SaveChangesAsync();

        var svc = new InventoryService(db);
        var controller = new DeliveryOrdersController(db, svc);
        WireController(controller, CreateHttpContext());
        var vm = new DeliveryOrderViewModel
        {
            Source = "Order",
            Delivery = new DeliveryOrderFormModel { SalesOrderId = order.Id, DeliveryDate = DateTime.Today },
            Items = new List<DeliveryOrderLineFormModel> { new() { ItemId = itemId, Quantity = 2, Count = 0 } }
        };

        // The customer select is on the same page as the order select, so the browser posts its
        // placeholder to a non-nullable int. That binder error is what used to make an
        // order-sourced note unsaveable for every user, not only for a scripted form post.
        controller.ModelState.AddModelError("Delivery.CustomerId", "The value '' is invalid.");

        var result = await controller.Create(vm);

        Assert.IsType<RedirectToActionResult>(result);
        var delivery = await db.DeliveryOrders.Include(d => d.Items).SingleAsync();
        Assert.StartsWith("DLV-", delivery.DeliveryNumber);
        Assert.Equal(order.Id, delivery.SalesOrderId);
        Assert.Equal(custId, delivery.CustomerId);
        Assert.Equal(2m, delivery.Items.Single().Quantity);
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

    // ---------------------------------------------------------------------------
    // The regression this whole change exists for: a document POST that binds its
    // entity silently failed to save. SalesOrders posted no OrderNumber, the entity
    // carried [Required] on it, so the save died behind an error nothing showed.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SalesOrders_Create_WithoutServerGeneratedFields_SavesAndNumbersTheOrder()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var inventory = new InventoryService(db);
        var controller = new SalesOrdersController(db, new SalesOrdersService(db, inventory), new StockReservationsService(db));
        WireController(controller, CreateHttpContext());

        // Exactly what the page sends: no OrderNumber, no status, no totals.
        var vm = new SalesOrderViewModel
        {
            Order = new SalesOrderFormModel
            {
                CustomerId = custId,
                OrderDate = DateTime.Today,
                ExpectedDate = DateTime.Today.AddDays(7)
            },
            Items =
            [
                new SalesOrderLineFormModel { ItemId = itemId, Quantity = 3, Count = 3, UnitPrice = 80 }
            ]
        };

        var result = await controller.Create(vm);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        // The bug being closed: an [Required] on a server-generated field put an error in
        // ModelState on every submit, so the save died and the page looked fine.
        Assert.True(controller.ModelState.IsValid,
            string.Join(" | ", controller.ModelState.SelectMany(m => m.Value!.Errors.Select(e => $"{m.Key}: {e.ErrorMessage}"))));
        var order = await db.SalesOrders.Include(o => o.Items).SingleAsync();
        Assert.StartsWith("SO-", order.OrderNumber);
        Assert.Single(order.Items);
        Assert.Equal(3m, order.Items.Single().Quantity);
        Assert.Equal("Index", redirect.ActionName);
    }

    [Fact]
    public void SalesOrderFormModel_KeepsServerOwnedFieldsOutOfTheRequest()
    {
        // The guard is two different shapes, and both are asserted: a field that is simply
        // absent cannot be posted at all, a field kept for display must carry [BindNever] or
        // the browser can write it. Losing either is the bug this pass closed.
        AssertNotPosted<SalesOrderFormModel>(nameof(SalesOrder.OrderNumber));
        AssertNotPosted<SalesOrderFormModel>(nameof(SalesOrder.PublicId));
        AssertNotPosted<SalesOrderFormModel>(nameof(SalesOrder.RowVersion));
        AssertNotPosted<SalesOrderFormModel>(nameof(SalesOrder.CreatedAt));
        AssertNotPosted<SalesOrderFormModel>(nameof(SalesOrder.CreatedBy));
        AssertBindNever<SalesOrderFormModel>(nameof(SalesOrderFormModel.Status));

        // The order line is the same story: the calculated quantities never reach the form.
        AssertNotPosted<SalesOrderLineFormModel>(nameof(SalesOrderItem.InvoicedQty));
        AssertNotPosted<SalesOrderLineFormModel>(nameof(SalesOrderItem.ReservedQty));
        AssertNotPosted<SalesOrderLineFormModel>(nameof(SalesOrderItem.DeliveredQty));
        AssertNotPosted<SalesOrderLineFormModel>(nameof(SalesOrderItem.SalesOrderId));
    }

    [Fact]
    public void DocumentFormModels_KeepEveryServerOwnedFieldOutOfTheBinder()
    {
        // One table for the audit. Display-only fields must carry [BindNever]; fields with no
        // business in a POST must be gone altogether.
        AssertBindNever<DeliveryOrderFormModel>(nameof(DeliveryOrderFormModel.Status));
        AssertBindNever<DeliveryOrderFormModel>(nameof(DeliveryOrderFormModel.DeliveryNumber));
        AssertNotPosted<DeliveryOrderLineFormModel>(nameof(DeliveryOrderItem.Id));
        AssertNotPosted<DeliveryOrderFormModel>(nameof(DeliveryOrder.Id));

        AssertBindNever<DeliveryIssueFormModel>(nameof(DeliveryIssueFormModel.IssueNumber));
        AssertBindNever<DeliveryIssueFormModel>(nameof(DeliveryIssueFormModel.Status));
        AssertNotPosted<DeliveryIssueLineFormModel>(nameof(DeliveryIssueItem.Id));
        // The order line is derived by the service, never posted.
        AssertNotPosted<DeliveryIssueLineFormModel>(nameof(DeliveryIssueItem.SalesOrderItemId));

        AssertBindNever<PaymentFormModel>(nameof(PaymentFormModel.ReceiptNumber));
        AssertBindNever<PaymentFormModel>(nameof(PaymentFormModel.Type));
        AssertNotPosted<PaymentFormModel>(nameof(Payment.Id));
        AssertNotPosted<PaymentFormModel>(nameof(Payment.BranchId));
        AssertNotPosted<PaymentFormModel>(nameof(Payment.CreatedBy));
        AssertNotPosted<PaymentFormModel>(nameof(Payment.DedupeKey));

        AssertBindNever<StockTransferFormModel>(nameof(StockTransferFormModel.TransferNumber));
        AssertNotPosted<StockTransferLineFormModel>(nameof(StockTransferItem.Id));
        AssertNotPosted<StockTransferLineFormModel>(nameof(StockTransferItem.StockTransferId));
        AssertNotPosted<StockTransferFormModel>(nameof(StockTransfer.Id));
        AssertNotPosted<StockTransferFormModel>(nameof(StockTransfer.CreatedAt));

        AssertBindNever<PurchaseOrderFormModel>(nameof(PurchaseOrderFormModel.OrderNumber));
        AssertBindNever<PurchaseOrderFormModel>(nameof(PurchaseOrderFormModel.Status));
        AssertBindNever<PurchaseOrderFormModel>(nameof(PurchaseOrderFormModel.Id));
        AssertBindNever<PurchaseOrderLineFormModel>(nameof(PurchaseOrderLineFormModel.RowVersion));
        AssertNotPosted<PurchaseOrderFormModel>(nameof(PurchaseOrder.PublicId));
        AssertNotPosted<PurchaseOrderLineFormModel>(nameof(PurchaseOrderItem.PurchaseOrderId));
        AssertNotPosted<PurchaseOrderLineFormModel>(nameof(PurchaseOrderItem.ReceivedQty));
    }

    private static void AssertBindNever<T>(string propertyName)
    {
        var property = typeof(T).GetProperty(propertyName);
        Assert.NotNull(property);
        Assert.True(
            property!.GetCustomAttributes(typeof(BindNeverAttribute), inherit: true).Length > 0,
            $"{typeof(T).Name}.{propertyName} is bindable: a client can post it");
    }

    private static void AssertNotPosted<T>(string entityPropertyName)
    {
        var name = entityPropertyName;
        var idx = name.LastIndexOf('.');
        if (idx > 0)
        {
            name = name[(idx + 1)..];
        }

        Assert.Null(typeof(T).GetProperty(name));
    }

    [Fact]
    public async Task UpdateOrderAsync_PostedLineIdFromAnotherOrder_AdoptedAsANewLine()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var inventory = new InventoryService(db);
        var svc = new SalesOrdersService(db, inventory);
        var otherItem = await SeedSecondItemAsync(db);

        // Order A: one line we must not touch.
        var a = new SalesOrder { CustomerId = custId, OrderDate = DateTime.Today };
        Assert.True((await svc.CreateOrderAsync(a, [new SalesOrderItem { ItemId = itemId, Quantity = 5, Count = 5, UnitPrice = 80 }], "test")).Success);
        var aLine = await db.SalesOrderItems.FirstAsync(i => i.SalesOrderId == a.Id);

        // Order B: the line a hostile POST would name to make order A's row disappear.
        var b = new SalesOrder { CustomerId = custId, OrderDate = DateTime.Today };
        Assert.True((await svc.CreateOrderAsync(b, [new SalesOrderItem { ItemId = otherItem, Quantity = 2, Count = 2, UnitPrice = 90 }], "test")).Success);
        db.ChangeTracker.Clear();
        var aLineId = aLine.Id;
        var bId = b.Id;

        // A fresh context, as a request would get: the update must not depend on what the
        // seeding calls left in the change tracker.
        using var request = CreateContext();
        var (ok, error) = await new SalesOrdersService(request, new InventoryService(request)).UpdateOrderAsync(
            new SalesOrder { Id = bId, CustomerId = custId, OrderDate = DateTime.Today },
            [new SalesOrderItem { Id = aLineId, ItemId = otherItem, Quantity = 4, Count = 4, UnitPrice = 90 }],
            "test");

        Assert.True(ok, error);
        db.ChangeTracker.Clear();
        var aAfter = await db.SalesOrderItems.Where(i => i.SalesOrderId == a.Id).ToListAsync();
        Assert.Single(aAfter);
        Assert.Equal(5m, aAfter[0].Quantity);
        Assert.Equal(a.Id, aAfter[0].SalesOrderId);
        var bAfter = await db.SalesOrderItems.Where(i => i.SalesOrderId == b.Id).ToListAsync();
        Assert.Single(bAfter);
        Assert.Equal(4m, bAfter[0].Quantity);
        Assert.NotEqual(aAfter[0].Id, bAfter[0].Id);
    }

    [Fact]
    public async Task UpdateOrderAsync_Procurement_PostedLineIdFromAnotherOrder_AdoptedAsANewLine()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var otherItem = await SeedSecondItemAsync(db);
        var proc = new ProcurementService(db, new InventoryService(db));

        var a = new PurchaseOrder { SupplierId = supId, OrderDate = DateTime.Today };
        Assert.True((await proc.CreateOrderAsync(a, [new PurchaseOrderItem { ItemId = itemId, Quantity = 5, Count = 5, UnitPrice = 40 }], "test")).Success);
        var aLine = await db.PurchaseOrderItems.FirstAsync(i => i.PurchaseOrderId == a.Id);

        var b = new PurchaseOrder { SupplierId = supId, OrderDate = DateTime.Today };
        Assert.True((await proc.CreateOrderAsync(b, [new PurchaseOrderItem { ItemId = otherItem, Quantity = 2, Count = 2, UnitPrice = 45 }], "test")).Success);
        db.ChangeTracker.Clear();
        var aLineId = aLine.Id;
        var bId = b.Id;

        using var request = CreateContext();
        var requestProc = new ProcurementService(request, new InventoryService(request));
        var (ok, error) = await requestProc.UpdateOrderAsync(
            new PurchaseOrder { Id = bId, SupplierId = supId, OrderDate = DateTime.Today },
            [new PurchaseOrderItem { Id = aLineId, ItemId = otherItem, Quantity = 4, Count = 4, UnitPrice = 45 }],
            "test");

        Assert.True(ok, error);
        db.ChangeTracker.Clear();
        var aAfter = await db.PurchaseOrderItems.Where(i => i.PurchaseOrderId == a.Id).ToListAsync();
        Assert.Single(aAfter);
        Assert.Equal(5m, aAfter[0].Quantity);
        Assert.Equal(a.Id, aAfter[0].PurchaseOrderId);
    }

    [Fact]
    public async Task CreateDeliveryIssueAsync_DerivesTheOrderLineThePageNeverPosts()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);
        var sales = new SalesOrdersService(db, svc);

        var order = new SalesOrder { CustomerId = custId, OrderDate = DateTime.Today };
        Assert.True((await sales.CreateOrderAsync(order, [new SalesOrderItem { ItemId = itemId, Quantity = 5, Count = 5, UnitPrice = 80 }], "test")).Success);
        // A delivery note can only be cut against an approved order.
        Assert.True((await sales.ApproveOrderAsync(order.Id)).Success);
        var orderLine = await db.SalesOrderItems.FirstAsync(i => i.SalesOrderId == order.Id);

        var (noteOk, noteError, note) = await svc.CreateSalesDeliveryNoteAsync(
            order.Id, null, null,
            [new DeliveryOrderItem { ItemId = itemId, Quantity = 5, Count = 5 }], "test", DateTime.Today, null);
        Assert.True(noteOk, noteError);
        db.ChangeTracker.Clear();
        var noteLine = await db.DeliveryOrderItems.FirstAsync(i => i.DeliveryOrderId == note!.Id);

        // The hostile payload: it names a different order's line.
        var (ok, error, issue) = await svc.CreateDeliveryIssueAsync(
            note!.Id,
            [new DeliveryIssueItem
            {
                ItemId = itemId,
                DeliveryOrderItemId = noteLine.Id,
                SalesOrderItemId = orderLine.Id + 9999,
                Quantity = 2,
                Count = 2
            }],
            "tester");

        Assert.True(ok, error);
        db.ChangeTracker.Clear();
        var line = await db.DeliveryIssueItems.SingleAsync(i => i.DeliveryIssueId == issue!.Id);
        Assert.Equal(orderLine.Id, line.SalesOrderItemId);
    }

    [Fact]
    public async Task PaymentsController_DisbursementPost_StaysADisbursement()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var (proc, _) = Services(db);
        await CreateOutstandingPayableAsync(db, proc, itemId, supId, 400m);
        var controller = new PaymentsController(db, new PaymentService(db, new AccountingService(db)));
        WireController(controller, CreateHttpContext("tester"));

        // The route used to be the only thing carrying the type; a POST that lost it
        // defaulted to a customer receipt.
        var vm = new PaymentFormViewModel
        {
            Type = "disbursement",
            Payment = new PaymentFormModel
            {
                Type = PaymentType.Disbursement,
                SupplierId = supId,
                Amount = 100,
                Method = PaymentMethod.Cash,
                PaymentDate = DateTime.Today
            }
        };

        var result = await controller.Create(vm);

        Assert.True(result is RedirectToActionResult,
            string.Join(" | ", controller.ModelState.SelectMany(m => m.Value!.Errors.Select(e => $"{m.Key}: {e.ErrorMessage}"))));
        var payment = await db.Payments.SingleAsync();
        Assert.Equal(PaymentType.Disbursement, payment.Type);
        Assert.Equal(supId, payment.SupplierId);
        Assert.Null(payment.CustomerId);
        Assert.StartsWith("PAY-", payment.ReceiptNumber);
    }

    [Fact]
    public async Task PaymentsController_PostedIdAndBranch_AreNotAdopted()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        SeedChartOfAccounts(db);
        var svc = new PaymentService(db, new AccountingService(db));
        var invoice = new SaleInvoice { CustomerId = custId, InvoiceDate = DateTime.Today };
        Assert.True((await svcSeedSaleAsync(db, invoice, itemId, 100m, 500m)).Item1);

        var controller = new PaymentsController(db, svc);
        WireController(controller, CreateHttpContext("tester"));

        // The body carries a foreign primary key and a forged receipt number: neither is on
        // the form model, so the payment lands as a new row numbered by the service.
        var vm = new PaymentFormViewModel
        {
            Type = "receipt",
            Payment = new PaymentFormModel
            {
                Type = PaymentType.Receipt,
                CustomerId = custId,
                Amount = 60,
                Method = PaymentMethod.Cash,
                PaymentDate = DateTime.Today,
                ReceiptNumber = "PAY-99999"
            }
        };

        var result = await controller.Create(vm);

        Assert.True(result is RedirectToActionResult,
            string.Join(" | ", controller.ModelState.SelectMany(m => m.Value!.Errors.Select(e => $"{m.Key}: {e.ErrorMessage}"))));
        db.ChangeTracker.Clear();
        var payment = await db.Payments.SingleAsync(p => p.Amount == 60m);
        Assert.Equal(PaymentType.Receipt, payment.Type);
        Assert.Equal(custId, payment.CustomerId);
        Assert.Equal("tester", payment.CreatedBy);
        Assert.Null(payment.BranchId);
        Assert.NotEqual("PAY-99999", payment.ReceiptNumber);
        Assert.StartsWith("PAY-", payment.ReceiptNumber);
        Assert.Equal(1, await db.SalePaymentAllocations.CountAsync());
    }

    /// <summary>يبيع فاتورة للعميل ويسلّمها بالكامل، فيترك رصيداً مستحقاً يمكن تحصيله.
    /// التسليم شرط: الفاتورة لا تدخل التحصيل قبل أن تُسلَّم فعلاً.</summary>
    private static async Task<(bool Item1, string? Item2)> svcSeedSaleAsync(
        AppDbContext db, SaleInvoice invoice, int itemId, decimal price, decimal quantity)
    {
        var (ok, error) = await new InventoryService(db, new AccountingService(db))
            .CreateSaleAsync(invoice, [QtyLine(itemId, quantity, price)], "seed");
        if (!ok)
        {
            return (false, error);
        }

        var delivery = new DeliveryOrder
        {
            DeliveryNumber = $"DO-SEED-{Guid.NewGuid():N}".Substring(0, 12),
            SaleInvoiceId = invoice.Id,
            CustomerId = invoice.CustomerId,
            DeliveryDate = invoice.InvoiceDate,
            Status = DeliveryOrderStatus.Delivered,
            DeliveredAt = DateTime.UtcNow
        };
        db.DeliveryOrders.Add(delivery);
        await db.SaveChangesAsync();
        db.DeliveryOrderItems.Add(new DeliveryOrderItem
        {
            DeliveryOrderId = delivery.Id,
            ItemId = itemId,
            Quantity = quantity
        });
        await db.SaveChangesAsync();
        return (true, null);
    }

    /// <summary>A purchase invoice the supplier can be paid against: a disbursement with
    /// nothing outstanding is rejected by the allocation check, not by the binder.</summary>
    private static async Task CreateOutstandingPayableAsync(
        AppDbContext db, ProcurementService proc, int itemId, int supplierId, decimal amount)
    {
        // A real purchase invoice posts through the chart of accounts, so the seed has to
        // provide the accounts it touches; otherwise this helper fails for the wrong reason.
        if (!db.GLAccounts.Any())
        {
            SeedChartOfAccounts(db);
        }

        var order = new PurchaseOrder { SupplierId = supplierId, OrderDate = DateTime.Today };
        var quantity = amount / 40m;
        var (created, createError) = await proc.CreateOrderAsync(order,
            [new PurchaseOrderItem { ItemId = itemId, Quantity = quantity, Count = quantity, UnitPrice = 40 }], "test");
        Assert.True(created, createError);
        var (approved, approveError) = await proc.ApproveOrderAsync(order.Id);
        Assert.True(approved, approveError);
        var line = await db.PurchaseOrderItems.FirstAsync(i => i.PurchaseOrderId == order.Id);
        var (received, receiveError) = await proc.ReceiveOrderLineAsync(order.Id, line.Id, quantity, quantity);
        Assert.True(received, receiveError);
        var (invoiced, invoiceError) = await proc.CreateInvoiceFromOrderAsync(order.Id, "test");
        Assert.True(invoiced, invoiceError);

        // الفاتورة من أمر شراء تُسجَّل "دفع عند الاستلام" فتُعتبر مسدَّدة بالكامل، ولا يبقى
        // ما يمكن صرفه؛ نحوّلها إلى آجل حتى يوجد رصيد مستحق للمورّد.
        var invoice = await db.PurchaseInvoices.FirstAsync(p => p.PurchaseOrderId == order.Id);
        invoice.PaymentTerms = InvoicePaymentTerms.Net30;
        invoice.PaidAmount = 0m;
        invoice.IsPaid = false;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task StockTransfersController_InvalidModelState_ReRendersWhatWasPosted()
    {
        using var db = CreateContext();
        var (itemId, _, _) = await SeedAsync(db);
        var (wh1Id, wh2Id) = await SeedWarehousesAsync(db);
        var controller = new StockTransfersController(db, new InventoryService(db));
        WireController(controller, CreateHttpContext());

        var vm = new StockTransferFormViewModel
        {
            Transfer = new StockTransferFormModel
            {
                SourceWarehouseId = wh1Id,
                TargetWarehouseId = wh2Id,
                TransferDate = DateTime.Today,
                Notes = "ملاحظة يجب ألا تضيع"
            },
            Items =
            [
                new StockTransferLineFormModel { ItemId = itemId, Quantity = 3, Count = 2, UnitCost = 12.5m },
                new StockTransferLineFormModel { ItemId = itemId, Quantity = 9, Count = 0 }
            ]
        };
        controller.ModelState.AddModelError("Transfer.SourceWarehouseId", "المستودع المصدر غير صحيح");

        var result = await controller.Create(vm);

        // A rejected transfer used to come back as one hardcoded row of zeros: everything
        // the operator typed was gone.
        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<StockTransferFormViewModel>(view.Model);
        Assert.Equal(2, model.Items.Count);
        Assert.Equal(3m, model.Items[0].Quantity);
        Assert.Equal(9m, model.Items[1].Quantity);
        Assert.Equal("ملاحظة يجب ألا تضيع", model.Transfer.Notes);
        Assert.NotNull(model.ItemsData);
        Assert.NotNull(model.Warehouses);
        Assert.Equal(0, await db.StockTransfers.CountAsync());
    }

    private async Task<int> SeedSecondItemAsync(AppDbContext db)
    {
        var cat = await db.ItemCategories.FirstAsync();
        var type = await db.ItemTypes.FirstAsync();
        var unit = await db.Units.FirstAsync();
        var item = new Item
        {
            Name = "صنف اختبار ثانٍ",
            CategoryId = cat.Id,
            ItemTypeId = type.Id,
            CountUnitId = unit.Id,
            QuantityUnitId = unit.Id,
            PurchasePrice = 45,
            SalePrice = 90,
            CurrentCount = 50,
            CurrentQuantity = 50
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }
}
