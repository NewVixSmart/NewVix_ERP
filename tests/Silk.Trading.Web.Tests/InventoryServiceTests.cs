using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Core;
using Silk.Trading.Web.Models.Purchases;
using Silk.Trading.Web.Models.Sales;
using Silk.Trading.Web.Models.Stock;
using Silk.Trading.Web.Services;
using Xunit;

namespace Silk.Trading.Web.Tests;

public sealed class InventoryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public InventoryServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private async Task<(int itemId, int custId, int supId)> SeedAsync(AppDbContext db)
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

    private static SaleInvoiceItem QtyLine(int itemId, decimal qty, decimal price) => new()
    {
        ItemId = itemId, Quantity = qty, Count = 0, UnitPrice = price
    };

    private static SaleInvoiceItem CountLine(int itemId, decimal count, decimal price) => new()
    {
        ItemId = itemId, Quantity = 0, Count = count, UnitPrice = price
    };

    // ---------- CreateSaleAsync ----------

    [Fact]
    public async Task CreateSale_ReducesStock_And_ComputesTotals()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var invoice = new SaleInvoice
        {
            CustomerId = custId,
            PaymentTerms = Silk.Trading.Web.Models.Accounting.InvoicePaymentTerms.Net30
        };
        var lines = new List<SaleInvoiceItem> { QtyLine(itemId, 10, 50) };

        var (ok, _) = await svc.CreateSaleAsync(invoice, lines, "test");

        Assert.True(ok);
        Assert.Equal(90, db.Items.Single().CurrentQuantity);
        Assert.Equal(500, invoice.TotalAmount);
        Assert.True(invoice.IsPaid == false);
        Assert.Equal(0, invoice.PaidAmount);
        Assert.Equal("SI-", invoice.InvoiceNumber[..3]);
    }

    [Fact]
    public async Task CreateSale_DoesNotOversell()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var invoice = new SaleInvoice { CustomerId = custId };
        var lines = new List<SaleInvoiceItem> { QtyLine(itemId, 150, 50) };

        var (ok, err) = await svc.CreateSaleAsync(invoice, lines, "test");

        Assert.False(ok);
        Assert.NotNull(err);
        Assert.Equal(100, db.Items.Single().CurrentQuantity);
        Assert.Equal(0, await db.SaleInvoices.CountAsync());
    }

    [Fact]
    public async Task CreateSale_NegativeNetAmount_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var invoice = new SaleInvoice { CustomerId = custId, Discount = 1000 };
        var lines = new List<SaleInvoiceItem> { QtyLine(itemId, 10, 50) };

        var (ok, _) = await svc.CreateSaleAsync(invoice, lines, "test");

        Assert.False(ok);
        Assert.Equal(100, db.Items.Single().CurrentQuantity);
    }

    [Fact]
    public async Task CreateSale_NetAmount_Equals_Total_MinusDiscount_PlusTax()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var invoice = new SaleInvoice { CustomerId = custId, Discount = 50, Tax = 25 };
        var lines = new List<SaleInvoiceItem> { QtyLine(itemId, 10, 50) };

        var (ok, _) = await svc.CreateSaleAsync(invoice, lines, "test");

        Assert.True(ok);
        Assert.Equal(500, invoice.TotalAmount);
        Assert.Equal(500 - 50 + 25, invoice.NetAmount);
    }

    [Fact]
    public async Task CreateSale_CountLine_UsesCount_NotQuantity()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var invoice = new SaleInvoice { CustomerId = custId };
        var lines = new List<SaleInvoiceItem> { CountLine(itemId, 4, 100) };

        var (ok, _) = await svc.CreateSaleAsync(invoice, lines, "test");

        Assert.True(ok);
        Assert.Equal(400, invoice.TotalAmount);
        Assert.Equal(96, db.Items.Single().CurrentCount);
    }

    // ---------- CreatePurchaseAsync ----------

    [Fact]
    public async Task CreatePurchase_IncreasesStock_And_SetsLatestPurchasePrice()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var invoice = new PurchaseInvoice { SupplierId = supId };
        var lines = new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 20, Count = 0, UnitPrice = 45 } };

        var (ok, _) = await svc.CreatePurchaseAsync(invoice, lines, "test");

        Assert.True(ok);
        Assert.Equal(120, db.Items.Single().CurrentQuantity);
        Assert.Equal(45, db.Items.Single().PurchasePrice);
        Assert.Equal(900, invoice.TotalAmount);
    }

    [Fact]
    public async Task CreatePurchase_ComputesNetAmount()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var invoice = new PurchaseInvoice { SupplierId = supId, Discount = 100, Tax = 50 };
        var lines = new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 20, Count = 0, UnitPrice = 45 } };

        var (ok, _) = await svc.CreatePurchaseAsync(invoice, lines, "test");

        Assert.True(ok);
        Assert.Equal(900, invoice.TotalAmount);
        Assert.Equal(900 - 100 + 50, invoice.NetAmount);
    }

    // ---------- CreateSaleReturnAsync ----------

    [Fact]
    public async Task CreateSaleReturn_Valid_Qty_ReturnsToStock()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var saleInv = new SaleInvoice { CustomerId = custId };
        await svc.CreateSaleAsync(saleInv, new List<SaleInvoiceItem> { QtyLine(itemId, 10, 50) }, "test");

        var ret = new SaleReturn { CustomerId = custId, SaleInvoiceId = saleInv.Id };
        var retLines = new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 50 } };

        var (ok, _) = await svc.CreateSaleReturnAsync(ret, retLines, "test");

        Assert.True(ok);
        Assert.Equal(94, db.Items.Single().CurrentQuantity);
        Assert.StartsWith("SRTN-", ret.ReturnNumber);
        Assert.Single(await db.StockMovements.Where(m => m.DocumentType == DocumentType.SaleReturn).ToListAsync());
    }

    [Fact]
    public async Task CreateSaleReturn_OverQuantity_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var saleInv = new SaleInvoice { CustomerId = custId };
        await svc.CreateSaleAsync(saleInv, new List<SaleInvoiceItem> { QtyLine(itemId, 10, 50) }, "test");

        var ret = new SaleReturn { CustomerId = custId, SaleInvoiceId = saleInv.Id };
        var retLines = new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 11, Count = 0, UnitPrice = 50 } };

        var (ok, err) = await svc.CreateSaleReturnAsync(ret, retLines, "test");

        Assert.False(ok);
        Assert.NotNull(err);
        Assert.Equal(90, db.Items.Single().CurrentQuantity);
    }

    [Fact]
    public async Task CreateSaleReturn_Cumulative_OverReturn_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var saleInv = new SaleInvoice { CustomerId = custId };
        await svc.CreateSaleAsync(saleInv, new List<SaleInvoiceItem> { QtyLine(itemId, 10, 50) }, "test");

        var ret1 = new SaleReturn { CustomerId = custId, SaleInvoiceId = saleInv.Id };
        await svc.CreateSaleReturnAsync(ret1, new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 7, Count = 0, UnitPrice = 50 } }, "test");

        var ret2 = new SaleReturn { CustomerId = custId, SaleInvoiceId = saleInv.Id };
        var (ok, _) = await svc.CreateSaleReturnAsync(ret2, new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 50 } }, "test");

        Assert.False(ok);
        Assert.Equal(97, db.Items.Single().CurrentQuantity);
    }

    [Fact]
    public async Task CreateSaleReturn_CountOverReturn_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var saleInv = new SaleInvoice { CustomerId = custId };
        await svc.CreateSaleAsync(saleInv, new List<SaleInvoiceItem> { CountLine(itemId, 5, 100) }, "test");

        var ret = new SaleReturn { CustomerId = custId, SaleInvoiceId = saleInv.Id };
        var retLines = new List<SaleReturnItem> { new() { ItemId = itemId, Count = 6, Quantity = 0, UnitPrice = 100 } };

        var (ok, _) = await svc.CreateSaleReturnAsync(ret, retLines, "test");

        Assert.False(ok);
        Assert.Equal(95, db.Items.Single().CurrentCount);
    }

    [Fact]
    public async Task CreateSaleReturn_WrongCustomer_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var saleInv = new SaleInvoice { CustomerId = custId };
        await svc.CreateSaleAsync(saleInv, new List<SaleInvoiceItem> { QtyLine(itemId, 10, 50) }, "test");

        var otherCust = new Customer { Name = "عميل آخر" };
        db.Customers.Add(otherCust);
        await db.SaveChangesAsync();

        var ret = new SaleReturn { CustomerId = otherCust.Id, SaleInvoiceId = saleInv.Id };
        var retLines = new List<SaleReturnItem> { new() { ItemId = itemId, Quantity = 1, Count = 0, UnitPrice = 50 } };

        var (ok, err) = await svc.CreateSaleReturnAsync(ret, retLines, "test");

        Assert.False(ok);
        Assert.NotNull(err);
    }

    [Fact]
    public async Task CreateSaleReturn_NoLines_IsRejected()
    {
        using var db = CreateContext();
        var svc = new InventoryService(db);

        var ret = new SaleReturn();
        var (ok, _) = await svc.CreateSaleReturnAsync(ret, new List<SaleReturnItem>(), "test");

        Assert.False(ok);
    }

    // ---------- New feature coverage ----------

    [Fact]
    public async Task CreateSale_Net30_AutoSetsDueDate()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var invoice = new SaleInvoice
        {
            CustomerId = custId,
            InvoiceDate = new DateTime(2026, 1, 10),
            PaymentTerms = Silk.Trading.Web.Models.Accounting.InvoicePaymentTerms.Net30
        };
        var lines = new List<SaleInvoiceItem> { QtyLine(itemId, 1, 50) };

        var (ok, _) = await svc.CreateSaleAsync(invoice, lines, "test");

        Assert.True(ok);
        Assert.Equal(new DateTime(2026, 2, 9), invoice.DueDate);
    }

    [Fact]
    public async Task CreateSale_OnReceipt_LeavesDueDateNull()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var invoice = new SaleInvoice
        {
            CustomerId = custId,
            PaymentTerms = Silk.Trading.Web.Models.Accounting.InvoicePaymentTerms.OnReceipt
        };

        var (ok, _) = await svc.CreateSaleAsync(invoice, new List<SaleInvoiceItem> { QtyLine(itemId, 1, 50) }, "test");

        Assert.True(ok);
        Assert.Null(invoice.DueDate);
    }

    [Fact]
    public async Task CreateSale_NetAmount_IncludesExtraDiscounts()
    {
        using var db = CreateContext();
        var (itemId, custId, _) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var invoice = new SaleInvoice
        {
            CustomerId = custId,
            Discount = 10,
            Discount2 = 20,
            Discount3 = 5,
            Tax = 10
        };
        var lines = new List<SaleInvoiceItem> { QtyLine(itemId, 10, 50) };

        var (ok, _) = await svc.CreateSaleAsync(invoice, lines, "test");

        Assert.True(ok);
        Assert.Equal(500, invoice.TotalAmount);
        Assert.Equal(500 - 10 - 20 - 5 + 10, invoice.NetAmount);
    }

    [Fact]
    public async Task CreatePurchase_CreatesSupplierQuote()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var invoice = new PurchaseInvoice { SupplierId = supId };
        await svc.CreatePurchaseAsync(invoice, new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 3, Count = 0, UnitPrice = 60 } }, "test");

        var quote = await db.SupplierQuotes.SingleOrDefaultAsync(q => q.SupplierId == supId && q.ItemId == itemId);
        Assert.NotNull(quote);
        Assert.Equal(60, quote.UnitPrice);
    }

    [Fact]
    public async Task CreatePurchase_UpsertsQuote_NotDuplicates()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var inv1 = new PurchaseInvoice { SupplierId = supId };
        await svc.CreatePurchaseAsync(inv1, new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 1, Count = 0, UnitPrice = 60 } }, "test");

        var inv2 = new PurchaseInvoice { SupplierId = supId };
        await svc.CreatePurchaseAsync(inv2, new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 70 } }, "test");

        var quotes = await db.SupplierQuotes.Where(q => q.SupplierId == supId && q.ItemId == itemId).ToListAsync();
        Assert.Single(quotes);
        Assert.Equal(70, quotes[0].UnitPrice);
    }

    // ---------- FIFO layers ----------

    [Fact]
    public async Task Purchase_CreatesFifoLayer_WithFullRemaining()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var invoice = new PurchaseInvoice
        {
            SupplierId = supId,
            InvoiceDate = new DateTime(2026, 1, 1)
        };
        await svc.CreatePurchaseAsync(invoice, new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 10, Count = 0, UnitPrice = 40 } }, "test");

        var layer = await db.StockLayers.SingleAsync();
        Assert.Equal(itemId, layer.ItemId);
        Assert.Equal(10, layer.Qty);
        Assert.Equal(10, layer.RemainingQty);
        Assert.Equal(40, layer.UnitCost);
        Assert.Equal(new DateTime(2026, 1, 1), layer.DateReceived);
    }

    [Fact]
    public async Task Purchase_TwoLayers_DifferentCosts()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var inv1 = new PurchaseInvoice { SupplierId = supId, InvoiceDate = new DateTime(2026, 1, 1) };
        await svc.CreatePurchaseAsync(inv1, new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 10, Count = 0, UnitPrice = 40 } }, "test");

        var inv2 = new PurchaseInvoice { SupplierId = supId, InvoiceDate = new DateTime(2026, 2, 1) };
        await svc.CreatePurchaseAsync(inv2, new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 10, Count = 0, UnitPrice = 50 } }, "test");

        var layers = await db.StockLayers.OrderBy(l => l.DateReceived).ToListAsync();
        Assert.Equal(2, layers.Count);
        Assert.Equal(40, layers[0].UnitCost);
        Assert.Equal(50, layers[1].UnitCost);
        Assert.Equal(10, layers[0].RemainingQty);
        Assert.Equal(10, layers[1].RemainingQty);
    }

    [Fact]
    public async Task Sale_ConsumesOldestFifoLayerFirst()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var inv1 = new PurchaseInvoice { SupplierId = supId, InvoiceDate = new DateTime(2026, 1, 1) };
        await svc.CreatePurchaseAsync(inv1, new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 10, Count = 0, UnitPrice = 40 } }, "test");

        var inv2 = new PurchaseInvoice { SupplierId = supId, InvoiceDate = new DateTime(2026, 2, 1) };
        await svc.CreatePurchaseAsync(inv2, new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 10, Count = 0, UnitPrice = 50 } }, "test");

        var sale = new SaleInvoice { CustomerId = (await SeedCustomer(db)) };
        await svc.CreateSaleAsync(sale, new List<SaleInvoiceItem> { QtyLine(itemId, 6, 100) }, "test");

        var layers = await db.StockLayers.OrderBy(l => l.DateReceived).ToListAsync();
        Assert.Equal(4, layers[0].RemainingQty);
        Assert.Equal(10, layers[1].RemainingQty);
    }

    [Fact]
    public async Task Sale_ConsumesAcrossLayers_OldestFirst()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var custId = await SeedCustomer(db);
        var svc = new InventoryService(db);

        var inv1 = new PurchaseInvoice { SupplierId = supId, InvoiceDate = new DateTime(2026, 1, 1) };
        await svc.CreatePurchaseAsync(inv1, new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 10, Count = 0, UnitPrice = 40 } }, "test");

        var inv2 = new PurchaseInvoice { SupplierId = supId, InvoiceDate = new DateTime(2026, 2, 1) };
        await svc.CreatePurchaseAsync(inv2, new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 10, Count = 0, UnitPrice = 50 } }, "test");

        var sale = new SaleInvoice { CustomerId = custId };
        await svc.CreateSaleAsync(sale, new List<SaleInvoiceItem> { QtyLine(itemId, 12, 100) }, "test");

        var layers = await db.StockLayers.OrderBy(l => l.DateReceived).ToListAsync();
        Assert.Equal(0, layers[0].RemainingQty);
        Assert.Equal(8, layers[1].RemainingQty);
    }

    [Fact]
    public async Task GetConsumedCost_ComputesFifoAcrossLayers()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var inv1 = new PurchaseInvoice { SupplierId = supId, InvoiceDate = new DateTime(2026, 1, 1) };
        await svc.CreatePurchaseAsync(inv1, new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 5, Count = 0, UnitPrice = 30 } }, "test");

        var inv2 = new PurchaseInvoice { SupplierId = supId, InvoiceDate = new DateTime(2026, 1, 15) };
        await svc.CreatePurchaseAsync(inv2, new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 5, Count = 0, UnitPrice = 60 } }, "test");

        var cogs = await svc.GetConsumedCostAsync(itemId, new List<StockLine> { new(itemId, 0, 7) });
        Assert.NotNull(cogs);
        Assert.Equal(5m * 30m + 2m * 60m, cogs.QtyCost);
        Assert.Equal(0, cogs.CountCost);
    }

    [Fact]
    public async Task Purchase_CountLayer_And_Consume_CountCost()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var inv = new PurchaseInvoice { SupplierId = supId, InvoiceDate = new DateTime(2026, 1, 1) };
        await svc.CreatePurchaseAsync(inv, new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 0, Count = 8, UnitPrice = 75 } }, "test");

        var layer = await db.StockLayers.SingleAsync();
        Assert.Equal(8, layer.Count);
        Assert.Equal(8, layer.RemainingCount);
        Assert.Equal(75, layer.CountCost);
    }

    private async Task<int> SeedCustomer(AppDbContext db)
    {
        var customer = new Customer { Name = "عميل FIFO" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    // ---------- Transfer helpers ----------

    private async Task<(int wh1Id, int wh2Id)> SeedWarehousesAsync(AppDbContext db)
    {
        var wh1 = new Warehouse { Code = "WH-T1", Name = "مخزن اختبار 1", IsActive = true };
        var wh2 = new Warehouse { Code = "WH-T2", Name = "مخزن اختبار 2", IsActive = true };
        db.Warehouses.AddRange(wh1, wh2);
        await db.SaveChangesAsync();
        return (wh1.Id, wh2.Id);
    }

    private async Task SeedPurchaseLayerAsync(AppDbContext db, int itemId, int? warehouseId, decimal qty, decimal unitCost, DateTime date)
    {
        var layer = new StockLayer
        {
            ItemId = itemId,
            WarehouseId = warehouseId,
            Qty = qty,
            Count = 0,
            UnitCost = unitCost,
            CountCost = unitCost,
            DateReceived = date,
            RemainingQty = qty,
            RemainingCount = 0
        };
        db.StockLayers.Add(layer);
        await db.SaveChangesAsync();
    }

    // ---------- CreateTransferAsync ----------

    [Fact]
    public async Task Transfer_MovesFifoLayers_OldestFirst()
    {
        using var db = CreateContext();
        var (itemId, _, _) = await SeedAsync(db);
        var (wh1Id, wh2Id) = await SeedWarehousesAsync(db);
        var svc = new InventoryService(db);

        await SeedPurchaseLayerAsync(db, itemId, wh1Id, 10, 30, new DateTime(2026, 1, 1));
        await SeedPurchaseLayerAsync(db, itemId, wh1Id, 10, 50, new DateTime(2026, 2, 1));

        var transfer = new StockTransfer { SourceWarehouseId = wh1Id, TargetWarehouseId = wh2Id, TransferDate = DateTime.UtcNow };
        var items = new List<StockTransferItem> { new() { ItemId = itemId, Quantity = 12, Count = 0, UnitCost = 0 } };
        var (ok, _) = await svc.CreateTransferAsync(transfer, items, "test");

        Assert.True(ok);
        var sourceLayers = await db.StockLayers.Where(sl => sl.ItemId == itemId && sl.WarehouseId == wh1Id)
            .OrderBy(sl => sl.DateReceived).ToListAsync();
        Assert.Equal(0, sourceLayers[0].RemainingQty);
        Assert.Equal(8, sourceLayers[1].RemainingQty);

        var targetLayers = await db.StockLayers.Where(sl => sl.ItemId == itemId && sl.WarehouseId == wh2Id)
            .OrderBy(sl => sl.DateReceived).ToListAsync();
        Assert.Equal(10, targetLayers[0].RemainingQty);
        Assert.Equal(2, targetLayers[1].RemainingQty);
        Assert.Equal(30, targetLayers[0].UnitCost);
        Assert.Equal(50, targetLayers[1].UnitCost);
    }

    [Fact]
    public async Task Transfer_CannotOverTransfer()
    {
        using var db = CreateContext();
        var (itemId, _, _) = await SeedAsync(db);
        var (wh1Id, wh2Id) = await SeedWarehousesAsync(db);
        var svc = new InventoryService(db);

        await SeedPurchaseLayerAsync(db, itemId, wh1Id, 5, 30, new DateTime(2026, 1, 1));

        var transfer = new StockTransfer { SourceWarehouseId = wh1Id, TargetWarehouseId = wh2Id, TransferDate = DateTime.UtcNow };
        var items = new List<StockTransferItem> { new() { ItemId = itemId, Quantity = 10, Count = 0, UnitCost = 0 } };
        var (ok, err) = await svc.CreateTransferAsync(transfer, items, "test");

        Assert.False(ok);
        Assert.Contains("غير كافٍ", err);
        Assert.Equal(5, (await db.StockLayers.SingleAsync(sl => sl.WarehouseId == wh1Id)).RemainingQty);
    }

    [Fact]
    public async Task Transfer_SameWarehouse_IsRejected()
    {
        using var db = CreateContext();
        var (itemId, _, _) = await SeedAsync(db);
        var (wh1Id, _) = await SeedWarehousesAsync(db);
        var svc = new InventoryService(db);

        var transfer = new StockTransfer { SourceWarehouseId = wh1Id, TargetWarehouseId = wh1Id, TransferDate = DateTime.UtcNow };
        var items = new List<StockTransferItem> { new() { ItemId = itemId, Quantity = 1, Count = 0, UnitCost = 0 } };
        var (ok, err) = await svc.CreateTransferAsync(transfer, items, "test");

        Assert.False(ok);
        Assert.Contains("نفسه", err);
    }

    [Fact]
    public async Task Transfer_NoItems_IsRejected()
    {
        using var db = CreateContext();
        var (_, wh1Id, wh2Id) = (0, 1, 2);
        db.Warehouses.AddRange(
            new Warehouse { Id = 1, Code = "W1", Name = "م1" },
            new Warehouse { Id = 2, Code = "W2", Name = "م2" });
        await db.SaveChangesAsync();
        var svc = new InventoryService(db);

        var transfer = new StockTransfer { SourceWarehouseId = 1, TargetWarehouseId = 2, TransferDate = DateTime.UtcNow };
        var (ok, err) = await svc.CreateTransferAsync(transfer, new List<StockTransferItem>(), "test");

        Assert.False(ok);
        Assert.NotNull(err);
    }

    [Fact]
    public async Task Transfer_CreatesStockMovements()
    {
        using var db = CreateContext();
        var (itemId, _, _) = await SeedAsync(db);
        var (wh1Id, wh2Id) = await SeedWarehousesAsync(db);
        var svc = new InventoryService(db);

        await SeedPurchaseLayerAsync(db, itemId, wh1Id, 10, 30, new DateTime(2026, 1, 1));

        var transfer = new StockTransfer { SourceWarehouseId = wh1Id, TargetWarehouseId = wh2Id, TransferDate = DateTime.UtcNow };
        var items = new List<StockTransferItem> { new() { ItemId = itemId, Quantity = 5, Count = 0, UnitCost = 0 } };
        var (ok, _) = await svc.CreateTransferAsync(transfer, items, "test");

        Assert.True(ok);
        var movements = await db.StockMovements.Where(m => m.DocumentType == DocumentType.Transfer).ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.Equal(MovementType.Out, movements[0].Type);
        Assert.Equal(MovementType.In, movements[1].Type);
        Assert.Equal(5, movements[0].Quantity);
        Assert.Equal(5, movements[1].Quantity);
    }

    [Fact]
    public async Task Transfer_PreservesCostBasis()
    {
        using var db = CreateContext();
        var (itemId, _, _) = await SeedAsync(db);
        var (wh1Id, wh2Id) = await SeedWarehousesAsync(db);
        var svc = new InventoryService(db);

        await SeedPurchaseLayerAsync(db, itemId, wh1Id, 8, 40, new DateTime(2026, 3, 15));

        var transfer = new StockTransfer { SourceWarehouseId = wh1Id, TargetWarehouseId = wh2Id, TransferDate = DateTime.UtcNow };
        var items = new List<StockTransferItem> { new() { ItemId = itemId, Quantity = 8, Count = 0, UnitCost = 0 } };
        var (ok, _) = await svc.CreateTransferAsync(transfer, items, "test");

        Assert.True(ok);
        var targetLayer = await db.StockLayers.SingleAsync(sl => sl.WarehouseId == wh2Id);
        Assert.Equal(40, targetLayer.UnitCost);
        Assert.Equal(new DateTime(2026, 3, 15), targetLayer.DateReceived);
        Assert.Equal(0, (await db.StockLayers.SingleAsync(sl => sl.WarehouseId == wh1Id)).RemainingQty);
    }

    // ---------- Adjustment countdown (N-6) ----------

    [Fact]
    public async Task Adjustment_Countdown_ConsumesFifoLayers()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var svc = new InventoryService(db);

        var item = await db.Items.SingleAsync(i => i.Id == itemId);
        item.CurrentCount = 0;
        item.CurrentQuantity = 0;
        await db.SaveChangesAsync();

        var inv1 = new PurchaseInvoice { SupplierId = supId, InvoiceDate = new DateTime(2026, 1, 1) };
        await svc.CreatePurchaseAsync(inv1, new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 60, Count = 0, UnitPrice = 30 } }, "test");

        var inv2 = new PurchaseInvoice { SupplierId = supId, InvoiceDate = new DateTime(2026, 2, 1) };
        await svc.CreatePurchaseAsync(inv2, new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 60, Count = 0, UnitPrice = 50 } }, "test");

        Assert.Equal(120, db.Items.Single(i => i.Id == itemId).CurrentQuantity);

        var adjustment = new InventoryAdjustment { ItemId = itemId, NewCount = 0, NewQuantity = 110, AdjustmentDate = new DateTime(2026, 3, 1) };
        var (ok, err) = await svc.CreateAdjustmentAsync(adjustment, "test");

        Assert.True(ok);
        Assert.Null(err);

        var layers = await db.StockLayers.OrderBy(l => l.DateReceived).ToListAsync();
        Assert.Equal(50, layers[0].RemainingQty);
        Assert.Equal(60, layers[1].RemainingQty);
        Assert.Equal(110, db.Items.Single(i => i.Id == itemId).CurrentQuantity);
    }

    // ---------- Warehouse snapshot (N-5) ----------

    [Fact]
    public async Task WarehouseSnapshot_IncludesUnassignedPurchaseLayers()
    {
        using var db = CreateContext();
        var (itemId, _, supId) = await SeedAsync(db);
        var (wh1Id, _) = await SeedWarehousesAsync(db);
        var svc = new InventoryService(db);

        var invoice = new PurchaseInvoice { SupplierId = supId, InvoiceDate = new DateTime(2026, 1, 1) };
        await svc.CreatePurchaseAsync(invoice, new List<PurchaseInvoiceItem> { new() { ItemId = itemId, Quantity = 25, Count = 0, UnitPrice = 40 } }, "test");

        var snap = await svc.GetStockSnapshotAsync(wh1Id);
        var row = Assert.Single(snap, r => r.ItemId == itemId);
        Assert.Equal(25, row.WarehouseQuantity);
        Assert.Equal(25, row.TotalQuantity);
    }
}