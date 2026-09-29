using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Import;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class ImportDocumentIntegrityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public ImportDocumentIntegrityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    // A second context is used for the assertions so the tests read committed state from the
    // database instead of the entities the service still holds in its change tracker.
    private async Task<ImportDocumentState> ReadStateAsync()
    {
        using var db = CreateContext();
        return new ImportDocumentState(
            await db.JournalEntries.CountAsync(),
            await db.JournalEntryLines.CountAsync(),
            await db.SaleInvoices.CountAsync(),
            await db.SaleInvoiceItems.CountAsync(),
            await db.StockMovements.CountAsync(),
            await db.SalesOrders.CountAsync(),
            await db.StockReservations.CountAsync());
    }

    private static byte[] CsvBytes(string text) => Encoding.UTF8.GetBytes(text);

    private static ImportCenterService CreateImportService(AppDbContext db)
    {
        var accounting = new AccountingService(db);
        return new ImportCenterService(db, new InventoryService(db, accounting), null!, accounting,
            new MemoryCache(new MemoryCacheOptions()));
    }

    private static void SeedChartOfAccounts(AppDbContext db)
    {
        (string Code, string Name, GLAccountType Type, NormalBalance Normal)[] accounts =
        {
            ("1000", "النقد / الصندوق", GLAccountType.Asset, NormalBalance.Debit),
            ("1200", "المدينون (العملاء)", GLAccountType.Asset, NormalBalance.Debit),
            ("1300", "المخزون", GLAccountType.Asset, NormalBalance.Debit),
            ("2000", "الدائنون (الموردون)", GLAccountType.Liability, NormalBalance.Credit),
            ("3000", "دائنو التسويات", GLAccountType.Liability, NormalBalance.Credit),
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
        };
        foreach (var (code, name, type, normal) in accounts)
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        db.SaveChanges();
    }

    private static async Task<(int ItemId, int CustomerId)> SeedStockAsync(AppDbContext db, decimal quantity)
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
            CurrentCount = quantity,
            CurrentQuantity = quantity
        };
        db.Items.Add(item);
        var customer = new Customer { Name = "عميل اختبار" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return (item.Id, customer.Id);
    }

    private static async Task<ImportResult> ParseAndImportAsync(ImportCenterService svc, string entityKey, byte[] csv)
    {
        var vm = await svc.ParseAsync(entityKey, entityKey + ".csv", csv);
        Assert.Null(vm.FatalError);
        Assert.Equal(0, vm.ErrorCount);
        return await svc.ImportAsync(entityKey, vm.Payload, vm.ApplyToken!);
    }

    private static byte[] JournalCsv(params (string Number, string Date)[] entries)
    {
        var text = "رقم القيد,التاريخ,بيان القيد,رمز الحساب,مدين,دائن,بيان البند\n";
        foreach (var (number, date) in entries)
            text += $"{number},{date},قيد مستورد,1000,500,,\n{number},{date},قيد مستورد,3000,,500,\n";
        return CsvBytes(text);
    }

    private static byte[] InvoiceCsv(params (string Number, string Date)[] invoices)
        => CsvBytes("رقم الفاتورة,اسم العميل,تاريخ الفاتورة,اسم الصنف,الكمية,سعر الوحدة\n" +
            string.Join("", invoices.Select(i => $"{i.Number},عميل اختبار,{i.Date},صنف اختبار,2,80\n")));

    [Fact]
    public async Task Import_JournalFile_AppliedTwice_DoesNotDuplicateEntryOrLines()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var svc = CreateImportService(db);
        var csv = JournalCsv(("JV-IMPORT-1", "2026-03-01"));

        var first = await ParseAndImportAsync(svc, "journalEntries", csv);
        Assert.True(first.Success, first.Message);
        Assert.Equal(1, first.Created);

        var entry = Assert.Single(await db.JournalEntries.Include(e => e.Lines).ToListAsync());
        Assert.Equal("JV-IMPORT-1", entry.EntryNumber);
        Assert.Equal(JournalSource.Import, entry.Source);
        Assert.True(entry.SourceId > 0, "the import run must own the entry through its SourceId");
        Assert.Equal(2, entry.Lines.Count);

        var second = await ParseAndImportAsync(svc, "journalEntries", csv);
        Assert.True(second.Success, second.Message);
        Assert.Equal(0, second.Created);
        Assert.Equal(2, second.Skipped);
        Assert.Contains("مستوردة مسبقًا", second.Message);

        var state = await ReadStateAsync();
        Assert.Equal(1, state.JournalEntries);
        Assert.Equal(2, state.JournalLines);
        Assert.Equal(0, state.Invoices);
        Assert.Equal(0, state.StockMovements);
    }

    [Fact]
    public async Task Import_JournalBatch_ThirdEntryInClosedYear_LeavesNoPartialState()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        db.FiscalPeriods.Add(new FiscalPeriod { Year = 2025, IsClosed = true, Name = "سنة مغلقة" });
        await db.SaveChangesAsync();
        var svc = CreateImportService(db);

        var csv = JournalCsv(("JV-BATCH-1", "2026-03-01"), ("JV-BATCH-2", "2026-03-02"), ("JV-BATCH-3", "2025-06-01"));
        var result = await ParseAndImportAsync(svc, "journalEntries", csv);

        Assert.False(result.Success);
        Assert.Equal(0, result.Created);
        Assert.Contains("أُلغي الاستيراد بالكامل", result.Message);
        var state = await ReadStateAsync();
        Assert.Equal(0, state.JournalEntries);
        Assert.Equal(0, state.JournalLines);
        Assert.Equal(0, state.Invoices);
        Assert.Equal(0, state.StockMovements);
    }

    [Fact]
    public async Task Import_InvoiceFile_AppliedTwice_KeepsOneInvoiceAndOneNumber()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedStockAsync(db, 100);
        var svc = CreateImportService(db);
        var csv = InvoiceCsv(("SI-IMPORT-1", "2026-03-05"));

        var first = await ParseAndImportAsync(svc, "saleInvoices", csv);
        Assert.True(first.Success, first.Message);
        Assert.Equal(1, first.Created);
        var invoice = Assert.Single(await db.SaleInvoices.Include(i => i.Items).ToListAsync());
        Assert.Equal("SI-IMPORT-1", invoice.InvoiceNumber);
        Assert.Equal(160m, invoice.NetAmount);

        var second = await ParseAndImportAsync(svc, "saleInvoices", csv);
        Assert.True(second.Success, second.Message);
        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Skipped);
        Assert.Contains("مستوردة مسبقًا", second.Message);

        var state = await ReadStateAsync();
        Assert.Equal(1, state.Invoices);
        Assert.Equal(1, state.InvoiceLines);
        Assert.Equal(0, state.StockMovements);
    }

    [Fact]
    public async Task Import_InvoiceBatch_ThirdInvoiceInClosedYear_LeavesNoPartialState()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedStockAsync(db, 100);
        db.FiscalPeriods.Add(new FiscalPeriod { Year = 2025, IsClosed = true, Name = "سنة مغلقة" });
        await db.SaveChangesAsync();
        var svc = CreateImportService(db);

        var csv = InvoiceCsv(("SI-BATCH-1", "2026-03-01"), ("SI-BATCH-2", "2026-03-02"), ("SI-BATCH-3", "2025-06-01"));
        var result = await ParseAndImportAsync(svc, "saleInvoices", csv);

        Assert.False(result.Success);
        Assert.Equal(0, result.Created);
        Assert.Contains("أُلغي الاستيراد بالكامل", result.Message);
        var state = await ReadStateAsync();
        Assert.Equal(0, state.Invoices);
        Assert.Equal(0, state.InvoiceLines);
        Assert.Equal(0, state.JournalEntries);
        Assert.Equal(0, state.StockMovements);
    }

    [Fact]
    public async Task ConvertQuote_ApprovalFails_ReturnsQuoteToDraft_AndLeavesNoOrder()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedStockAsync(db, 1);
        var quotes = new SalesQuotesService(db, new SalesOrdersService(db, new InventoryService(db)));

        var quote = new SaleQuote { CustomerId = custId, QuoteDate = new DateTime(2026, 3, 10) };
        var (created, _, saved) = await quotes.CreateAsync(quote, new List<SaleQuoteItem>
        {
            new() { ItemId = itemId, Quantity = 5, Count = 0, UnitPrice = 80 }
        }, "user1");
        Assert.True(created);

        var (ok, error, order) = await quotes.ConvertToOrderAsync(saved!.Id, "user1");

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Null(order);
        var state = await ReadStateAsync();
        Assert.Equal(0, state.Orders);
        Assert.Equal(0, state.Reservations);

        var after = await ReadQuoteAsync(saved.Id);
        Assert.Equal(SaleQuoteStatus.Draft, after.Status);
        Assert.Null(after.SalesOrderId);
        Assert.Null(after.ConvertedBy);
        Assert.Null(after.ConvertedAt);

        var item = await ReadItemAsync(itemId);
        Assert.Equal(1, item.CurrentQuantity);
        Assert.Equal(0, item.ReservedQuantity);
    }

    [Fact]
    public async Task ConvertQuote_ApprovalThrows_CompensatesAndRethrows()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedStockAsync(db, 100);
        var orders = new SalesOrdersService(db, new InventoryService(db));
        var quotes = new SalesQuotesService(db, new ThrowingApprovalOrdersService(orders));

        var quote = new SaleQuote { CustomerId = custId, QuoteDate = new DateTime(2026, 3, 10) };
        var (created, _, saved) = await quotes.CreateAsync(quote, new List<SaleQuoteItem>
        {
            new() { ItemId = itemId, Quantity = 2, Count = 0, UnitPrice = 80 }
        }, "user1");
        Assert.True(created);

        await Assert.ThrowsAsync<InvalidOperationException>(() => quotes.ConvertToOrderAsync(saved!.Id, "user1"));

        var quoteId = saved!.Id;
        var state = await ReadStateAsync();
        Assert.Equal(0, state.Orders);
        Assert.Equal(0, state.Reservations);
        var after = await ReadQuoteAsync(quoteId);
        Assert.Equal(SaleQuoteStatus.Draft, after.Status);
        Assert.Null(after.SalesOrderId);
        Assert.Null(after.ConvertedBy);
        Assert.Null(after.ConvertedAt);
        Assert.Equal(0, (await ReadItemAsync(itemId)).ReservedQuantity);
    }

    [Fact]
    public async Task ConvertQuote_Succeeds_CreatesExactlyOneOrderAndConvertsQuote()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (itemId, custId) = await SeedStockAsync(db, 100);
        db.StockLayers.Add(new StockLayer
        {
            ItemId = itemId, Qty = 10, Count = 0, UnitCost = 40m, RemainingQty = 10, RemainingCount = 0,
            DateReceived = new DateTime(2026, 1, 1), CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var quotes = new SalesQuotesService(db, new SalesOrdersService(db, new InventoryService(db)));

        var quote = new SaleQuote { CustomerId = custId, QuoteDate = new DateTime(2026, 3, 10) };
        var (created, _, saved) = await quotes.CreateAsync(quote, new List<SaleQuoteItem>
        {
            new() { ItemId = itemId, Quantity = 4, Count = 0, UnitPrice = 80 }
        }, "user1");
        Assert.True(created);

        var (ok, error, order) = await quotes.ConvertToOrderAsync(saved!.Id, "user1");

        Assert.True(ok, error);
        Assert.NotNull(order);
        var state = await ReadStateAsync();
        Assert.Equal(1, state.Orders);
        Assert.Equal(1, state.Reservations);

        using (var verify = CreateContext())
        {
            var persisted = await verify.SalesOrders.AsNoTracking().SingleAsync();
            Assert.Equal(SalesOrderStatus.Approved, persisted.Status);
            Assert.Equal(saved.Id, persisted.SaleQuoteId);
        }

        var after = await ReadQuoteAsync(saved.Id);
        Assert.Equal(SaleQuoteStatus.Converted, after.Status);
        Assert.Equal(order!.Id, after.SalesOrderId);
        Assert.Equal("user1", after.ConvertedBy);
        Assert.NotNull(after.ConvertedAt);
    }

    private async Task<SaleQuote> ReadQuoteAsync(int quoteId)
    {
        using var db = CreateContext();
        return await db.SaleQuotes.AsNoTracking().SingleAsync(q => q.Id == quoteId);
    }

    private async Task<Item> ReadItemAsync(int itemId)
    {
        using var db = CreateContext();
        return await db.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
    }

    private sealed record ImportDocumentState(
        int JournalEntries, int JournalLines, int Invoices, int InvoiceLines, int StockMovements,
        int Orders, int Reservations);

    private sealed class ThrowingApprovalOrdersService : ISalesOrdersService
    {
        private readonly ISalesOrdersService _inner;

        public ThrowingApprovalOrdersService(ISalesOrdersService inner) => _inner = inner;

        public Task<(bool Success, string? Error)> ApproveOrderAsync(int orderId, bool beginOwnTransaction = true)
            => throw new InvalidOperationException("تعذر الاتصال بالخادم أثناء الاعتماد");

        public Task<IReadOnlyList<SalesOrder>> GetOrdersAsync(SalesOrderStatus? status = null)
            => _inner.GetOrdersAsync(status);

        public Task<SalesOrder?> GetOrderAsync(int id) => _inner.GetOrderAsync(id);

        public Task<(bool Success, string? Error)> CreateOrderAsync(SalesOrder order, List<SalesOrderItem> items, string? user)
            => _inner.CreateOrderAsync(order, items, user);

        public Task<(bool Success, string? Error)> UpdateOrderAsync(SalesOrder order, List<SalesOrderItem> items, string? user)
            => _inner.UpdateOrderAsync(order, items, user);

        public Task<(bool Success, string? Error)> CancelOrderAsync(int orderId) => _inner.CancelOrderAsync(orderId);

        public Task<(bool Success, string? Error)> CreateInvoiceFromOrderAsync(int orderId, string? user)
            => _inner.CreateInvoiceFromOrderAsync(orderId, user);

        public Task<(bool Success, string? Error, SaleInvoice? Invoice)> InvoiceOutstandingDeliveriesAsync(
            int orderId, string? user, int? branchId = null)
            => _inner.InvoiceOutstandingDeliveriesAsync(orderId, user, branchId);
    }
}
