using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Purchases;
using NewVixSmart.Web.ViewModels.Sales;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// Two ways a money figure can be wrong while looking right on screen: pricing a return at its
/// gross when the ledger priced it at its net basis, and deciding "is this invoice still owed?"
/// with a test the rest of the system does not use.
/// <para>
/// PART A. <see cref="ReturnValuation.ReceivableBase"/> prorates a return's gross down to the
/// source invoice's net-of-VAT basis, because <c>ReturnMirror</c> posts exactly that slice to
/// account 1200/2000. A statement that summed the raw gross therefore disagreed with its own rows
/// and with the AR/AP sub-ledger. <c>CustomerLedgerViewModel.ReturnsTotal</c> and
/// <c>SupplierLedgerViewModel.ReturnsTotal</c> now sum the prorated figure, and the Razor rows
/// print the identical expression - which is why "the total equals the sum of the rows" is a test
/// here and not an assumption.
/// </para>
/// <para>
/// PART B. <c>ReportService.GetDashboardAsync</c> used to gate its overdue tiles on a bare
/// <c>PaidAmount &lt; NetAmount</c>, which a posted return leaves stale forever: the return never
/// rewrites the invoice's own columns. It now reads <c>OpenAmountRule</c>, the same rule the aging
/// report, <c>PaymentService</c> and the printed invoice use.
/// </para>
/// <para>
/// ON THE TOLERANCE, because it decides what the last tests are allowed to assert.
/// <c>OpenAmountRule</c> is an <c>internal static class</c> declared at the foot of
/// <c>ReportService.cs</c> (line 1403), and the Web project declares no
/// <c>InternalsVisibleTo</c> for this test assembly - so the rule and its
/// <c>OpenTolerance = 0.005m</c> constant are unreachable from here by name, and these tests pin
/// the rule only through what it does to a dashboard, which is the honest way round.
/// That tolerance gates <c>decimal(18,2)</c> money, and every input it sums - the delivered net,
/// <c>PaidBase</c>, and each return credit - is itself rounded to two decimals. So the open amount
/// is always an exact multiple of 0.01, the band strictly between 0 and 0.005 is unreachable from
/// storable data, and "settled" can only ever mean a residual of exactly 0.00 (or negative).
/// <c>DeliveryOpenLines</c> already documents the same arithmetic from the other side: its
/// quantity tolerance is 0.00005 rather than 0.005 precisely because a half-quantum gate and a
/// half-piastre gate are different questions. The tests below therefore prove exclusion at a
/// residual of exactly 0.00, and separately prove that the smallest residual the column CAN hold -
/// 0.01 - is still reported as open.
/// </para>
/// </summary>
public sealed class ReportOpenAmountRuleTests : IDisposable
{
    private const decimal _invoiceGross = 1100m;
    private const decimal _invoiceNet = 1000m;
    private const decimal _returnGross = 200m;

    /// <summary>200 gross against a 1100/1000 invoice: 200 * 1000/1100, to two decimals.</summary>
    private const decimal _proratedReturn = 181.82m;

    /// <summary>
    /// A transcription of <c>ReportService.OpenAmountRule.OpenTolerance</c>, which is internal and
    /// unreachable from this assembly. Used only to narrate the boundary in a failure message -
    /// every assertion about it below is behavioural, so the tests still fail if the real
    /// constant moves.
    /// </summary>
    private const decimal _openTolerance = 0.005m;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public ReportOpenAmountRuleTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static ReportService Report(AppDbContext db) => new(db, new FinancialReportService(db));

    private static async Task<int> SeedCustomerAsync(AppDbContext db, string name)
    {
        var customer = new Customer { Name = name };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private static async Task<int> SeedSupplierAsync(AppDbContext db, string name)
    {
        var supplier = new Supplier { Name = name };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier.Id;
    }

    /// <summary>
    /// An invoice whose due date has already passed, because the dashboard only considers
    /// invoices where <c>(DueDate ?? InvoiceDate).Date &lt; today</c>.
    /// </summary>
    private static SaleInvoice OverdueSale(int customerId, string number, decimal gross, decimal net, decimal paid) => new()
    {
        InvoiceNumber = number,
        CustomerId = customerId,
        InvoiceDate = DateTime.Today.AddDays(-30),
        DueDate = DateTime.Today.AddDays(-10),
        TotalAmount = gross,
        NetAmount = net,
        PaidAmount = paid,
        IsPaid = paid >= net
    };

    private static PurchaseInvoice OverduePurchase(int supplierId, string number, decimal gross, decimal net, decimal paid) => new()
    {
        InvoiceNumber = number,
        SupplierId = supplierId,
        InvoiceDate = DateTime.Today.AddDays(-30),
        DueDate = DateTime.Today.AddDays(-10),
        TotalAmount = gross,
        NetAmount = net,
        PaidAmount = paid,
        IsPaid = paid >= net
    };

    private static SaleReturn PostedSaleReturn(int customerId, int? invoiceId, string number, decimal gross, int daysAgo = 5) => new()
    {
        ReturnNumber = number,
        CustomerId = customerId,
        SaleInvoiceId = invoiceId,
        ReturnDate = DateTime.Today.AddDays(-daysAgo),
        Status = ReturnStatus.Posted,
        TotalAmount = gross
    };

    private static PurchaseReturn PostedPurchaseReturn(int supplierId, int? invoiceId, string number, decimal gross, int daysAgo = 5) => new()
    {
        ReturnNumber = number,
        SupplierId = supplierId,
        PurchaseInvoiceId = invoiceId,
        ReturnDate = DateTime.Today.AddDays(-daysAgo),
        Status = ReturnStatus.Posted,
        TotalAmount = gross
    };

    // ---------------------------------------------------------------------
    // PART A - return valuation.
    // ---------------------------------------------------------------------

    /// <summary>
    /// The rule itself, with no database: a return is credited at the fraction of the invoice it
    /// reverses, applied to the NET, because that is the slice the journal carries. 200 gross
    /// against a 1100 gross / 1000 net invoice is 200 * 1000/1100 = 181.82.
    /// <para>
    /// The degenerate inputs are in here too because they are the ones that can throw. There is no
    /// division-by-zero path: <c>invoiceGross &lt;= 0</c> returns the gross untouched before the
    /// division is reached, and an inverted basis (<c>invoiceNet &gt; invoiceGross</c>) is not
    /// clamped - it scales the return UP. That is not a claim the production code makes, it is what
    /// it does, and it is asserted so a future clamp shows up as a failing test instead of a quiet
    /// change in someone's statement.
    /// </para>
    /// </summary>
    [Fact]
    public void ReceivableBase_ProratesGrossToNetBasis()
    {
        var actual = ReturnValuation.ReceivableBase(_returnGross, _invoiceGross, _invoiceNet);

        Assert.Equal(_proratedReturn, actual);
        Assert.Equal(decimal.Round(_returnGross * (_invoiceNet / _invoiceGross), 2), actual);
        Assert.True(actual < _returnGross,
            "مرتجع على أساس صافي يجب أن يقل عن إجماليه الخام، لأن قاعدة الضريبة لم تُرجَع للعميل.");
        Assert.NotEqual(_returnGross, actual);

        // A zero return credits nothing, on either branch.
        Assert.Equal(0m, ReturnValuation.ReceivableBase(0m, _invoiceGross, _invoiceNet));
        Assert.Equal(0m, ReturnValuation.ReceivableBase(0m, 0m, 0m));

        // No source invoice (gross not loaded): nothing to prorate against, so the gross is the
        // whole value. This is the branch that keeps a standalone return - one with no parent
        // invoice at all - crediting its face amount.
        Assert.Equal(_returnGross, ReturnValuation.ReceivableBase(_returnGross, 0m, 0m));

        // A parent invoice that nets to zero (discounted away in full) credits nothing: nothing
        // was ever booked to 1200 for it, so a return against it must not invent a credit.
        Assert.Equal(0m, ReturnValuation.ReceivableBase(_returnGross, _invoiceGross, 0m));

        // Inverted basis: net above gross. Scales up rather than throwing or clamping.
        Assert.Equal(decimal.Round(_returnGross * (1200m / _invoiceGross), 2),
            ReturnValuation.ReceivableBase(_returnGross, _invoiceGross, 1200m));
    }

    /// <summary>
    /// The customer statement half of PART A, loaded the way <c>CustomersController.Ledger</c>
    /// loads it - <c>AsNoTracking().Include(r =&gt; r.SaleInvoice)</c>, filtered to posted
    /// returns. Loading the navigation is the whole point: <c>ReturnsTotal</c> reads
    /// <c>r.SaleInvoice?.TotalAmount</c>, so an unloaded navigation silently falls back to the
    /// gross and the bug comes back. The last assertion is the regression guard - the figure must
    /// NOT be the 200 the old code summed.
    /// </summary>
    [Fact]
    public async Task CustomerStatement_ReturnsTotal_UsesProratedNotGross()
    {
        using var db = CreateContext();
        var customerId = await SeedCustomerAsync(db, "عميل مرتجع");

        var invoice = OverdueSale(customerId, "S-PRORATE", _invoiceGross, _invoiceNet, 0m);
        db.SaleInvoices.Add(invoice);
        await db.SaveChangesAsync();

        db.SaleReturns.Add(PostedSaleReturn(customerId, invoice.Id, "SR-PRORATE", _returnGross));
        await db.SaveChangesAsync();

        var vm = new CustomerLedgerViewModel
        {
            Customer = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == customerId),
            Returns = await db.SaleReturns
                .AsNoTracking()
                .Include(r => r.SaleInvoice)
                .Where(r => r.CustomerId == customerId && r.Status == ReturnStatus.Posted)
                .ToListAsync()
        };

        var row = Assert.Single(vm.Returns);
        Assert.NotNull(row.SaleInvoice);
        Assert.Equal(_proratedReturn, vm.ReturnsTotal);
        Assert.NotEqual(_returnGross, vm.ReturnsTotal);
    }

    /// <summary>
    /// The supplier statement half of PART A, through <c>PurchaseInvoice</c>. Same rule, opposite
    /// side of the ledger: a purchase return debits the supplier by the same prorated slice.
    /// </summary>
    [Fact]
    public async Task SupplierStatement_ReturnsTotal_UsesProratedNotGross()
    {
        using var db = CreateContext();
        var supplierId = await SeedSupplierAsync(db, "مورد مرتجع");

        var invoice = OverduePurchase(supplierId, "P-PRORATE", _invoiceGross, _invoiceNet, 0m);
        db.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();

        db.PurchaseReturns.Add(PostedPurchaseReturn(supplierId, invoice.Id, "PR-PRORATE", _returnGross));
        await db.SaveChangesAsync();

        var vm = new SupplierLedgerViewModel
        {
            Supplier = await db.Suppliers.AsNoTracking().SingleAsync(s => s.Id == supplierId),
            Returns = await db.PurchaseReturns
                .AsNoTracking()
                .Include(r => r.PurchaseInvoice)
                .Where(r => r.SupplierId == supplierId && r.Status == ReturnStatus.Posted)
                .ToListAsync()
        };

        var row = Assert.Single(vm.Returns);
        Assert.NotNull(row.PurchaseInvoice);
        Assert.Equal(_proratedReturn, vm.ReturnsTotal);
        Assert.NotEqual(_returnGross, vm.ReturnsTotal);
    }

    /// <summary>
    /// The drift guard, stated as arithmetic. Two returns on one invoice, each prorated and
    /// rounded ON ITS OWN: 333.33 -&gt; 303.03 twice, which sums to 606.06.
    /// <para>
    /// Prorate once on the combined 666.66 and you get 606.05. The two differ by exactly one
    /// piastre, so a statement that summed the gross column and prorated at the end would print a
    /// header total one piastre below the sum of the rows the same page prints - the exact
    /// disagreement this test exists to make impossible again.
    /// </para>
    /// </summary>
    [Fact]
    public async Task StatementReturnsTotal_EqualsSumOfItsOwnRows()
    {
        using var db = CreateContext();
        var customerId = await SeedCustomerAsync(db, "عميل مرتجعان");

        var invoice = OverdueSale(customerId, "S-TWOROWS", _invoiceGross, _invoiceNet, 0m);
        db.SaleInvoices.Add(invoice);
        await db.SaveChangesAsync();

        db.SaleReturns.AddRange(
            PostedSaleReturn(customerId, invoice.Id, "SR-TWO-A", 333.33m, daysAgo: 6),
            PostedSaleReturn(customerId, invoice.Id, "SR-TWO-B", 333.33m, daysAgo: 4));
        await db.SaveChangesAsync();

        var vm = new CustomerLedgerViewModel
        {
            Customer = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == customerId),
            Returns = await db.SaleReturns
                .AsNoTracking()
                .Include(r => r.SaleInvoice)
                .Where(r => r.CustomerId == customerId && r.Status == ReturnStatus.Posted)
                .OrderBy(r => r.ReturnDate)
                .ToListAsync()
        };

        // The expression the Razor row prints, transcribed so the total and the rows are compared
        // as the page computes them rather than as this file wishes they were computed.
        var perRow = vm.Returns
            .Select(r => decimal.Round(ReturnValuation.ReceivableBase(
                r.TotalAmount, r.SaleInvoice?.TotalAmount ?? 0m, r.SaleInvoice?.NetAmount ?? 0m), 2))
            .ToList();

        Assert.Equal(2, perRow.Count);
        Assert.Equal(303.03m, perRow[0]);
        Assert.Equal(303.03m, perRow[1]);
        Assert.Equal(perRow.Sum(), vm.ReturnsTotal);
        Assert.Equal(606.06m, vm.ReturnsTotal);

        var sumThenProrate = decimal.Round(666.66m * (_invoiceNet / _invoiceGross), 2);
        Assert.NotEqual(sumThenProrate, vm.ReturnsTotal);
        Assert.Equal(606.05m, sumThenProrate);
    }

    // ---------------------------------------------------------------------
    // PART B - the dashboard's outstanding amounts.
    // ---------------------------------------------------------------------

    /// <summary>
    /// The floor case, and the fixture every other dashboard test leans on: the report is not
    /// empty, and the open invoice really is in it. If this stops holding, the exclusion tests
    /// below would pass for the wrong reason.
    /// </summary>
    [Fact]
    public async Task Dashboard_ExcludesFullyPaidInvoice()
    {
        using var db = CreateContext();
        var customerId = await SeedCustomerAsync(db, "عميل لوحة");

        var settled = OverdueSale(customerId, "S-DASH-SETTLED", _invoiceGross, _invoiceNet, _invoiceNet);
        var owing = OverdueSale(customerId, "S-DASH-OPEN", _invoiceGross, _invoiceNet, 0m);
        db.SaleInvoices.AddRange(settled, owing);
        await db.SaveChangesAsync();

        var vm = await Report(db).GetDashboardAsync();

        Assert.DoesNotContain(vm.OverdueReceivables, r => r.Id == settled.Id);
        Assert.Contains(vm.OverdueReceivables, r => r.Id == owing.Id);

        var row = Assert.Single(vm.OverdueReceivables);
        Assert.Equal(owing.Id, row.Id);
        Assert.Equal(_invoiceNet, row.Outstanding);
    }

    /// <summary>
    /// The regression PART B exists for. This invoice IS part-paid: 800 collected against a net of
    /// 1000, so the naive <c>PaidAmount &lt; NetAmount</c> test the dashboard used to run says
    /// "collectable". It is not. A posted return of 220 gross credits 220 * 1000/1100 = 200 back,
    /// which lands the open amount on exactly 0.00 - below the tolerance - so the ledger considers
    /// the invoice closed and the tile must not offer it for collection.
    /// <para>
    /// The <c>PaidAmount &lt; NetAmount</c> assertion is the load-bearing one: it proves the
    /// fixture really does satisfy the old test, so the exclusion below is a disagreement between
    /// two rules rather than two fixtures that happen to agree.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Dashboard_ExcludesPartiallyPaidBelowTolerance()
    {
        using var db = CreateContext();
        var customerId = await SeedCustomerAsync(db, "عميل مرتجع كامل");

        var returned = OverdueSale(customerId, "S-DASH-RETURNED", _invoiceGross, _invoiceNet, 800m);
        var control = OverdueSale(customerId, "S-DASH-CONTROL", _invoiceGross, _invoiceNet, 0m);
        db.SaleInvoices.AddRange(returned, control);
        await db.SaveChangesAsync();

        db.SaleReturns.Add(PostedSaleReturn(customerId, returned.Id, "SR-DASH-FULL", 220m));
        await db.SaveChangesAsync();

        Assert.True(returned.PaidAmount < returned.NetAmount,
            "شرط الاختبار: المدفوع أقل من الصافي، أي أن الاختبار الساذج كان سيعتبرها محصّلة.");

        var vm = await Report(db).GetDashboardAsync();

        Assert.DoesNotContain(vm.OverdueReceivables, r => r.Id == returned.Id);
        Assert.Contains(vm.OverdueReceivables, r => r.Id == control.Id);
        var row = Assert.Single(vm.OverdueReceivables);
        Assert.Equal(control.Id, row.Id);
    }

    /// <summary>
    /// The positive control for the two exclusions above, on its own so it cannot be satisfied by a
    /// dashboard that returns nothing at all. An unpaid invoice with no returns against it has a
    /// real balance of 1000 and must appear, carrying the ledger's figure.
    /// </summary>
    [Fact]
    public async Task Dashboard_IncludesInvoiceWithRealOutstandingBalance()
    {
        using var db = CreateContext();
        var customerId = await SeedCustomerAsync(db, "عميل عليه رصيد");

        var invoice = OverdueSale(customerId, "S-DASH-BALANCE", _invoiceGross, _invoiceNet, 0m);
        db.SaleInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var vm = await Report(db).GetDashboardAsync();

        var row = Assert.Single(vm.OverdueReceivables);
        Assert.Equal(invoice.Id, row.Id);
        Assert.Equal(_invoiceNet, row.NetAmount);
        Assert.Equal(_invoiceNet, row.Outstanding);
    }

    // ---------------------------------------------------------------------
    // What the tolerance actually excludes, and the payable mirror.
    // ---------------------------------------------------------------------

    /// <summary>
    /// The other half of the band, and the reason the exclusion tests assert a residual of exactly
    /// 0.00. A one-piastre shortfall is the smallest residual a decimal(18,2) money column can
    /// hold, and 0.01 is above <c>OpenTolerance</c>, so it is still owed and still shown. Written
    /// behaviourally on purpose: <c>OpenTolerance</c> is internal, so raising it above 0.01 would
    /// silently start dropping real half-egypt debts and this test is what would notice.
    /// </summary>
    [Fact]
    public async Task Dashboard_OnePiastreResidual_StillCountsAsOpen()
    {
        using var db = CreateContext();
        var customerId = await SeedCustomerAsync(db, "عميل قرش واحد");

        var invoice = OverdueSale(customerId, "S-DASH-ONE", _invoiceGross, _invoiceNet, 999.99m);
        db.SaleInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var vm = await Report(db).GetDashboardAsync();

        var row = Assert.Single(vm.OverdueReceivables);
        Assert.Equal(invoice.Id, row.Id);
        Assert.Equal(0.01m, row.Outstanding);
        Assert.True(row.Outstanding > _openTolerance,
            "أقل فرق قابل للتخزين في عمود بخانتين عشريتين هو 0.01، وهو أعلى من حد التجاوز 0.005.");
    }

    /// <summary>
    /// The payable mirror of <see cref="Dashboard_ExcludesPartiallyPaidBelowTolerance"/>, covering
    /// the second call site - <c>PurchaseOpenByInvoiceAsync</c> - which the naive
    /// <c>PaidAmount &lt; NetAmount</c> test had wrong in exactly the same way. A purchase invoice
    /// booked at 800 against a net of 1000, credited back 200 by a posted purchase return, is
    /// closed for the ledger and must not be offered for payment.
    /// </summary>
    [Fact]
    public async Task Dashboard_ExcludesPartiallyPaidPurchaseBelowTolerance()
    {
        using var db = CreateContext();
        var supplierId = await SeedSupplierAsync(db, "مورد مرتجع كامل");

        var returned = OverduePurchase(supplierId, "P-DASH-RETURNED", _invoiceGross, _invoiceNet, 800m);
        var control = OverduePurchase(supplierId, "P-DASH-CONTROL", _invoiceGross, _invoiceNet, 0m);
        db.PurchaseInvoices.AddRange(returned, control);
        await db.SaveChangesAsync();

        db.PurchaseReturns.Add(PostedPurchaseReturn(supplierId, returned.Id, "PR-DASH-FULL", 220m));
        await db.SaveChangesAsync();

        Assert.True(returned.PaidAmount < returned.NetAmount,
            "شرط الاختبار: المدفوع للمورد أقل من الصافي، أي أن الاختبار الساذج كان سيعتبرها مستحقة.");

        var vm = await Report(db).GetDashboardAsync();

        Assert.DoesNotContain(vm.OverduePayables, r => r.Id == returned.Id);
        Assert.Contains(vm.OverduePayables, r => r.Id == control.Id);
        var row = Assert.Single(vm.OverduePayables);
        Assert.Equal(control.Id, row.Id);
    }
}
