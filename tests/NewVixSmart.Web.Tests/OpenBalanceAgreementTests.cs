using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// A posted return credits account 1200/2000 through the journal but never rewrites the source
/// invoice's own <c>NetAmount</c>/<c>PaidAmount</c> columns, so <c>NetAmount - PaidAmount</c> is
/// not an open amount. The aging report and the payment allocation already price every document
/// through <see cref="ReturnValuation"/>; the dashboard due tiles and the printed invoice used to
/// subtract the columns instead, which made an operator's board disagree with the ledger it was
/// summarising.
/// <para>
/// Each test here asserts the SAME document through all three surfaces — the aging report, the
/// dashboard tile, and the remaining amount on the PDF invoice. One surface proving the number is
/// not enough: the defect was precisely a disagreement between surfaces, so the invariant under
/// test is their agreement.
/// </para>
/// </summary>
public sealed class OpenBalanceAgreementTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public OpenBalanceAgreementTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static void SeedChartOfAccounts(AppDbContext db)
    {
        (string Code, string Name, GLAccountType Type, NormalBalance Normal)[] accounts =
        {
            ("1000", "النقد / الصندوق", GLAccountType.Asset, NormalBalance.Debit),
            ("1100", "البنك", GLAccountType.Asset, NormalBalance.Debit),
            ("1200", "المدينون (العملاء)", GLAccountType.Asset, NormalBalance.Debit),
            ("1300", "المخزون", GLAccountType.Asset, NormalBalance.Debit),
            ("2000", "الدائنون (الموردون)", GLAccountType.Liability, NormalBalance.Credit),
            ("2055", "الضريبة المستحقة", GLAccountType.Liability, NormalBalance.Credit),
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
            ("5101", "مرتجعات المبيعات", GLAccountType.Expense, NormalBalance.Debit),
            ("5102", "مرتجعات المشتريات", GLAccountType.Expense, NormalBalance.Debit),
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }

        db.SaveChanges();
    }

    private static async Task<(int CustomerId, int SupplierId, int ItemId)> SeedBaseAsync(AppDbContext db)
    {
        var category = new ItemCategory { Name = "تصنيف المطابقة" };
        var type = new ItemType { Name = "نوع المطابقة" };
        var unit = new Unit { Name = "قطعة" };
        db.ItemCategories.Add(category);
        db.ItemTypes.Add(type);
        db.Units.Add(unit);
        db.Items.Add(new Item
        {
            Name = "صنف المطابقة",
            Category = category,
            ItemType = type,
            CountUnit = unit,
            QuantityUnit = unit,
            SalePrice = 100m
        });
        var customer = new Customer { Name = "عميل المطابقة" };
        var supplier = new Supplier { Name = "مورد المطابقة" };
        db.Customers.Add(customer);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return (customer.Id, supplier.Id, db.Items.Single().Id);
    }

    /// <summary>
    /// A 10 x 100 sale invoice, delivered in full and booked exactly as InventoryService does, so
    /// the receivable account 1200 carries is the figure the reports derive.
    /// </summary>
    private static async Task<SaleInvoice> SeedDeliveredSaleAsync(
        AppDbContext db, int customerId, int itemId, string number, DateTime invoiceDate, DateTime dueDate)
    {
        var invoice = new SaleInvoice
        {
            InvoiceNumber = number,
            CustomerId = customerId,
            InvoiceDate = invoiceDate,
            DueDate = dueDate,
            PaymentTerms = InvoicePaymentTerms.Net30,
            CreatedAt = DateTime.UtcNow
        };
        invoice.Items.Add(new SaleInvoiceItem { ItemId = itemId, Quantity = 10m, Count = 0m, UnitPrice = 100m });
        invoice.TotalAmount = 1000m;
        invoice.NetAmount = 1000m;
        db.SaleInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var order = new DeliveryOrder
        {
            DeliveryNumber = $"DLV-{number}",
            SaleInvoiceId = invoice.Id,
            CustomerId = customerId,
            DeliveryDate = invoiceDate,
            Status = DeliveryOrderStatus.Delivered,
            DeliveredAt = DateTime.UtcNow
        };
        db.DeliveryOrders.Add(order);
        await db.SaveChangesAsync();
        db.DeliveryOrderItems.Add(new DeliveryOrderItem
        {
            DeliveryOrderId = order.Id,
            ItemId = itemId,
            Quantity = 10m
        });
        await db.SaveChangesAsync();

        await new AccountingService(db).RecordSaleDeliveryAsync(
            invoiceDate, customerId, 1000m, 0m, "test", null, order.Id);

        return invoice;
    }

    private static async Task PostSaleReturnAsync(AppDbContext db, SaleInvoice invoice, int itemId, decimal qty)
    {
        var unitPrice = invoice.Items.First(i => i.ItemId == itemId).UnitPrice;
        var saleReturn = new SaleReturn
        {
            ReturnNumber = $"SR-{invoice.InvoiceNumber}-{qty:0.##}",
            CustomerId = invoice.CustomerId,
            SaleInvoiceId = invoice.Id,
            ReturnDate = invoice.InvoiceDate,
            Status = ReturnStatus.Posted,
            TotalAmount = decimal.Round(qty * unitPrice, 2)
        };
        saleReturn.Items.Add(new SaleReturnItem { ItemId = itemId, Quantity = qty, Count = 0m, UnitPrice = unitPrice });
        db.SaleReturns.Add(saleReturn);
        await db.SaveChangesAsync();

        var mirror = ReturnMirror.ProratedAgainst(
            saleReturn.TotalAmount, invoice.TotalAmount, invoice.NetAmount, invoice.Tax);
        await new AccountingService(db).RecordSaleReturnWithCostAsync(
            saleReturn.ReturnDate, saleReturn.Id, saleReturn.CustomerId, mirror.ContraValue, 0m, "test", null, mirror.Tax);
    }

    private static async Task<PurchaseInvoice> SeedPurchaseAsync(
        AppDbContext db, int supplierId, string number, DateTime invoiceDate, DateTime dueDate)
    {
        var invoice = new PurchaseInvoice
        {
            InvoiceNumber = number,
            SupplierId = supplierId,
            InvoiceDate = invoiceDate,
            DueDate = dueDate,
            PaymentTerms = InvoicePaymentTerms.Net30,
            TotalAmount = 1000m,
            NetAmount = 1000m,
            CreatedAt = DateTime.UtcNow
        };
        db.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();
        await new AccountingService(db).RecordPurchaseInvoiceAsync(invoiceDate, supplierId, 1000m, "test");
        return invoice;
    }

    private static async Task PostPurchaseReturnAsync(AppDbContext db, PurchaseInvoice invoice, decimal returnedGross)
    {
        var purchaseReturn = new PurchaseReturn
        {
            ReturnNumber = $"PR-{invoice.InvoiceNumber}-{returnedGross:0.##}",
            SupplierId = invoice.SupplierId,
            PurchaseInvoiceId = invoice.Id,
            ReturnDate = invoice.InvoiceDate,
            Status = ReturnStatus.Posted,
            TotalAmount = returnedGross
        };
        db.PurchaseReturns.Add(purchaseReturn);
        await db.SaveChangesAsync();

        var mirror = ReturnMirror.ProratedAgainst(
            purchaseReturn.TotalAmount, invoice.TotalAmount, invoice.NetAmount, invoice.Tax);
        await new AccountingService(db).RecordPurchaseReturnWithCostAsync(
            purchaseReturn.ReturnDate, purchaseReturn.Id, purchaseReturn.SupplierId, mirror.Receivable, 0m, "test");
    }

    private static Task<decimal> ArControlAsync(AppDbContext db)
        => db.JournalEntryLines.AsNoTracking().Include(l => l.Account)
            .Where(l => l.Account!.Code == "1200")
            .SumAsync(l => l.Debit - l.Credit);

    private static Task<decimal> ApControlAsync(AppDbContext db)
        => db.JournalEntryLines.AsNoTracking().Include(l => l.Account)
            .Where(l => l.Account!.Code == "2000")
            .SumAsync(l => l.Credit - l.Debit);

    /// <summary>The invoice exactly as the print controller loads it before handing it to the PDF.</summary>
    private static Task<SaleInvoice> ReloadSaleAsync(AppDbContext db, int id)
        => db.SaleInvoices.AsNoTracking().Include(s => s.Customer).Include(s => s.Items)
            .ThenInclude(i => i.Item).SingleAsync(s => s.Id == id);

    private static Task<PurchaseInvoice> ReloadPurchaseAsync(AppDbContext db, int id)
        => db.PurchaseInvoices.AsNoTracking().Include(p => p.Supplier).Include(p => p.Items)
            .ThenInclude(i => i.Item).SingleAsync(p => p.Id == id);

    // ------------------------------------------------------------------
    // (1) Sale side: invoice 1000, receipt 400, posted return 200 -> open 400,
    //     and all three surfaces must say 400.
    // ------------------------------------------------------------------

    [Fact]
    public async Task PostedSaleReturn_Dashboard_PdfAndAging_AllReportTheSameOpenAmount()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (customerId, _, itemId) = await SeedBaseAsync(db);
        var today = DateTime.Today;

        var invoice = await SeedDeliveredSaleAsync(
            db, customerId, itemId, "S-AGREE", today.AddDays(-40), today.AddDays(-10));

        var (ok, error, _) = await new PaymentService(db, new AccountingService(db)).CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = customerId,
            Amount = 400m,
            Method = PaymentMethod.Cash,
            PaymentDate = today
        }, "test");
        Assert.True(ok, error);

        await PostSaleReturnAsync(db, invoice, itemId, 2m);

        // The precondition that makes this a regression test: the stored columns still read 600
        // open, so any surface computing NetAmount - PaidAmount returns the wrong figure.
        var stored = await db.SaleInvoices.AsNoTracking().SingleAsync(s => s.Id == invoice.Id);
        Assert.Equal(600m, decimal.Round(stored.NetAmount - stored.PaidAmount, 2));
        Assert.False(stored.IsPaid);

        var aging = await new ReportService(db, new FinancialReportService(db)).AgingAsync();
        var agingRow = Assert.Single(aging.Receivables);
        Assert.Equal("عميل المطابقة", agingRow.PartyName);
        Assert.Equal(400m, agingRow.Total);

        var board = await new DashboardService(db).GetDashboardAsync();
        Assert.Equal(1, board.OverdueReceivableCount);
        Assert.Equal(400m, board.OverdueReceivableTotal);

        var printed = await ReloadSaleAsync(db, invoice.Id);
        Assert.Equal(400m, PdfInvoiceService.SaleRemainingAmount(db, printed));

        // The three surfaces are one number, and it is the number the ledger carries.
        Assert.Equal(agingRow.Total, board.OverdueReceivableTotal);
        Assert.Equal(agingRow.Total, PdfInvoiceService.SaleRemainingAmount(db, printed));
        Assert.Equal(400m, await ArControlAsync(db));
    }

    // ------------------------------------------------------------------
    // (2) Purchase side: the payable mirror of the same scenario.
    // ------------------------------------------------------------------

    [Fact]
    public async Task PostedPurchaseReturn_Dashboard_PdfAndAging_AllReportTheSameOpenAmount()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (_, supplierId, _) = await SeedBaseAsync(db);
        var today = DateTime.Today;

        var invoice = await SeedPurchaseAsync(db, supplierId, "P-AGREE", today.AddDays(-40), today.AddDays(-10));

        var (ok, error, _) = await new PaymentService(db, new AccountingService(db)).CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Disbursement,
            SupplierId = supplierId,
            Amount = 400m,
            Method = PaymentMethod.Cash,
            PaymentDate = today
        }, "test");
        Assert.True(ok, error);

        await PostPurchaseReturnAsync(db, invoice, 200m);

        var stored = await db.PurchaseInvoices.AsNoTracking().SingleAsync(p => p.Id == invoice.Id);
        Assert.Equal(600m, decimal.Round(stored.NetAmount - stored.PaidAmount, 2));

        var aging = await new ReportService(db, new FinancialReportService(db)).AgingAsync();
        var agingRow = Assert.Single(aging.Payables);
        Assert.Equal("مورد المطابقة", agingRow.PartyName);
        Assert.Equal(400m, agingRow.Total);

        var board = await new DashboardService(db).GetDashboardAsync();
        Assert.Equal(1, board.OverduePayableCount);
        Assert.Equal(400m, board.OverduePayableTotal);

        var printed = await ReloadPurchaseAsync(db, invoice.Id);
        Assert.Equal(400m, PdfInvoiceService.PurchaseRemainingAmount(db, printed));

        Assert.Equal(agingRow.Total, board.OverduePayableTotal);
        Assert.Equal(agingRow.Total, PdfInvoiceService.PurchaseRemainingAmount(db, printed));
        Assert.Equal(400m, await ApControlAsync(db));
    }

    // ------------------------------------------------------------------
    // (3) Fully returned + fully paid: closed everywhere, and it must not show as overdue.
    // ------------------------------------------------------------------

    [Fact]
    public async Task FullyReturnedAndPaid_SaleInvoice_IsClosedOnEverySurface()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (customerId, _, itemId) = await SeedBaseAsync(db);
        var today = DateTime.Today;

        var invoice = await SeedDeliveredSaleAsync(
            db, customerId, itemId, "S-CLOSED", today.AddDays(-60), today.AddDays(-30));

        await PostSaleReturnAsync(db, invoice, itemId, 2m);

        // The receipt is accepted for exactly the 800 the return left open - the rule the payment
        // allocation prices through - and the invoice is then settled in full.
        var (ok, error, _) = await new PaymentService(db, new AccountingService(db)).CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = customerId,
            Amount = 800m,
            Method = PaymentMethod.Cash,
            PaymentDate = today
        }, "test");
        Assert.True(ok, error);
        Assert.Equal(0m, await ArControlAsync(db));

        // The precondition that makes this a regression test: the stored columns still read 200
        // open, so any surface computing NetAmount - PaidAmount both misreports the balance and
        // keeps counting a document the ledger has closed.
        var stored = await db.SaleInvoices.AsNoTracking().SingleAsync(s => s.Id == invoice.Id);
        Assert.Equal(800m, stored.PaidAmount);
        Assert.Equal(200m, decimal.Round(stored.NetAmount - stored.PaidAmount, 2));

        var aging = await new ReportService(db, new FinancialReportService(db)).AgingAsync();
        Assert.Empty(aging.Receivables);
        Assert.Equal(0m, aging.ArTotal);

        var board = await new DashboardService(db).GetDashboardAsync();
        Assert.Equal(0, board.OverdueReceivableCount);
        Assert.Equal(0m, board.OverdueReceivableTotal);
        Assert.Equal(0, board.DueSoonReceivableCount);
        Assert.Equal(0m, board.DueSoonReceivableTotal);

        var printed = await ReloadSaleAsync(db, invoice.Id);
        Assert.Equal(0m, PdfInvoiceService.SaleRemainingAmount(db, printed));
    }

    [Fact]
    public async Task FullyReturnedAndPaid_PurchaseInvoice_IsClosedOnEverySurface()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (_, supplierId, _) = await SeedBaseAsync(db);
        var today = DateTime.Today;

        var invoice = await SeedPurchaseAsync(db, supplierId, "P-CLOSED", today.AddDays(-60), today.AddDays(-30));

        await PostPurchaseReturnAsync(db, invoice, 200m);

        var (ok, error, _) = await new PaymentService(db, new AccountingService(db)).CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Disbursement,
            SupplierId = supplierId,
            Amount = 800m,
            Method = PaymentMethod.Cash,
            PaymentDate = today
        }, "test");
        Assert.True(ok, error);
        Assert.Equal(0m, await ApControlAsync(db));

        var stored = await db.PurchaseInvoices.AsNoTracking().SingleAsync(p => p.Id == invoice.Id);
        Assert.Equal(800m, stored.PaidAmount);
        Assert.Equal(200m, decimal.Round(stored.NetAmount - stored.PaidAmount, 2));

        var aging = await new ReportService(db, new FinancialReportService(db)).AgingAsync();
        Assert.Empty(aging.Payables);
        Assert.Equal(0m, aging.ApTotal);

        var board = await new DashboardService(db).GetDashboardAsync();
        Assert.Equal(0, board.OverduePayableCount);
        Assert.Equal(0m, board.OverduePayableTotal);
        Assert.Equal(0, board.DueSoonPayableCount);
        Assert.Equal(0m, board.DueSoonPayableTotal);

        var printed = await ReloadPurchaseAsync(db, invoice.Id);
        Assert.Equal(0m, PdfInvoiceService.PurchaseRemainingAmount(db, printed));
    }

    // ------------------------------------------------------------------
    // (4) Regression guard: with no returns at all, the shared rule must leave
    //     the normal balances exactly as they were.
    // ------------------------------------------------------------------

    [Fact]
    public async Task NoReturns_OpenAmountIsStillNetMinusPaid_OnEverySurface()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (customerId, supplierId, itemId) = await SeedBaseAsync(db);
        var today = DateTime.Today;

        var sale = await SeedDeliveredSaleAsync(
            db, customerId, itemId, "S-PLAIN", today.AddDays(-20), today.AddDays(-10));
        var purchase = await SeedPurchaseAsync(db, supplierId, "P-PLAIN", today.AddDays(-20), today.AddDays(-10));

        var (saleOk, saleError, _) = await new PaymentService(db, new AccountingService(db)).CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = customerId,
            Amount = 250m,
            Method = PaymentMethod.Cash,
            PaymentDate = today
        }, "test");
        Assert.True(saleOk, saleError);

        var (purchaseOk, purchaseError, _) = await new PaymentService(db, new AccountingService(db)).CreatePaymentAsync(new Payment
        {
            Type = PaymentType.Disbursement,
            SupplierId = supplierId,
            Amount = 300m,
            Method = PaymentMethod.Cash,
            PaymentDate = today
        }, "test");
        Assert.True(purchaseOk, purchaseError);

        Assert.False(await db.SaleReturns.AnyAsync());
        Assert.False(await db.PurchaseReturns.AnyAsync());

        var aging = await new ReportService(db, new FinancialReportService(db)).AgingAsync();
        Assert.Equal(750m, Assert.Single(aging.Receivables).Total);
        Assert.Equal(700m, Assert.Single(aging.Payables).Total);

        var board = await new DashboardService(db).GetDashboardAsync();
        Assert.Equal(1, board.OverdueReceivableCount);
        Assert.Equal(750m, board.OverdueReceivableTotal);
        Assert.Equal(1, board.OverduePayableCount);
        Assert.Equal(700m, board.OverduePayableTotal);

        Assert.Equal(750m, PdfInvoiceService.SaleRemainingAmount(db, await ReloadSaleAsync(db, sale.Id)));
        Assert.Equal(700m, PdfInvoiceService.PurchaseRemainingAmount(db, await ReloadPurchaseAsync(db, purchase.Id)));
    }

    // ------------------------------------------------------------------
    // (5) The due-soon window is the same rule on the other side of today,
    //     and must survive a return that closes the document.
    // ------------------------------------------------------------------

    [Fact]
    public async Task PostedReturn_ClosesDueSoonTile_AndLeavesTheOpenOneInPlace()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (customerId, _, itemId) = await SeedBaseAsync(db);
        var today = DateTime.Today;

        var closing = await SeedDeliveredSaleAsync(
            db, customerId, itemId, "S-SOON-CLOSED", today.AddDays(-5), today.AddDays(3));
        var open = await SeedDeliveredSaleAsync(
            db, customerId, itemId, "S-SOON-OPEN", today.AddDays(-5), today.AddDays(5));

        await PostSaleReturnAsync(db, closing, itemId, 10m);

        var board = await new DashboardService(db).GetDashboardAsync();

        // Without the shared rule the closed invoice would still sit in the due-soon tile at 1000.
        Assert.Equal(1, board.DueSoonReceivableCount);
        Assert.Equal(1000m, board.DueSoonReceivableTotal);
        Assert.Equal(0, board.OverdueReceivableCount);

        var aging = await new ReportService(db, new FinancialReportService(db)).AgingAsync();
        Assert.Equal(1000m, Assert.Single(aging.Receivables).Total);
        Assert.Equal(board.DueSoonReceivableTotal, aging.ArTotal);

        Assert.Equal(0m, PdfInvoiceService.SaleRemainingAmount(db, await ReloadSaleAsync(db, closing.Id)));
        Assert.Equal(1000m, PdfInvoiceService.SaleRemainingAmount(db, await ReloadSaleAsync(db, open.Id)));
    }
}
