using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Reports;

namespace NewVixSmart.Web.Controllers;

[Authorize]
[RequirePerm("Reports.View")]
public class ReportsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ReportExportService _export;
    private readonly IFinancialReportService _financial;
    private readonly IReportService _report;
    public ReportsController(AppDbContext db, ReportExportService export, IFinancialReportService financial, IReportService report)
    {
        _db = db;
        _export = export;
        _financial = financial;
        _report = report;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.PendingDeliveries = await _report.GetPendingDeliveriesAsync();
        return View();
    }

    public async Task<IActionResult> Sales(DateTime? from, DateTime? to)
    {
        var now = DateTime.Today;
        from ??= new DateTime(now.Year, now.Month, 1);
        to ??= now;

        var fromDate = from.Value.Date;
        var toDate = to.Value.Date;

        var query = _db.SaleInvoices
            .AsNoTracking()
            .Include(s => s.Customer)
            .Where(s => s.InvoiceDate >= fromDate && s.InvoiceDate <= toDate);

        var total = await query.CountAsync();
        ViewBag.From = fromDate.ToString("yyyy-MM-dd");
        ViewBag.To = toDate.ToString("yyyy-MM-dd");

        decimal totalReturns = await _db.SaleReturns
            .Where(r => r.ReturnDate >= fromDate && r.ReturnDate <= toDate && r.Status == ReturnStatus.Posted)
            .SumAsync(r => (decimal?)r.TotalAmount) ?? 0;

        var vm = new SalesReportViewModel
        {
            From = fromDate,
            To = toDate,
            Invoices = await query.OrderByDescending(s => s.InvoiceDate).ToListAsync(),
            InvoiceCount = total,
            TotalSales = await query.SumAsync(s => (decimal?)s.TotalAmount) ?? 0,
            TotalDiscounts = await query.SumAsync(s => (decimal?)s.Discount + (s.Discount2 ?? 0) + (s.Discount3 ?? 0)) ?? 0,
            TotalTax = await query.SumAsync(s => (decimal?)s.Tax) ?? 0,
            NetSales = await query.SumAsync(s => (decimal?)s.NetAmount) ?? 0,
            TotalReturns = totalReturns
        };
        return View(vm);
    }

    public async Task<IActionResult> Purchases(DateTime? from, DateTime? to)
    {
        var now = DateTime.Today;
        from ??= new DateTime(now.Year, now.Month, 1);
        to ??= now;

        var fromDate = from.Value.Date;
        var toDate = to.Value.Date;

        var query = _db.PurchaseInvoices
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Where(p => p.InvoiceDate >= fromDate && p.InvoiceDate <= toDate);

        var total = await query.CountAsync();
        ViewBag.From = fromDate.ToString("yyyy-MM-dd");
        ViewBag.To = toDate.ToString("yyyy-MM-dd");

        decimal totalReturns = await _db.PurchaseReturns
            .Where(r => r.ReturnDate >= fromDate && r.ReturnDate <= toDate && r.Status == ReturnStatus.Posted)
            .SumAsync(r => (decimal?)r.TotalAmount) ?? 0;

        var vm = new PurchaseReportViewModel
        {
            From = fromDate,
            To = toDate,
            Invoices = await query.OrderByDescending(p => p.InvoiceDate).ToListAsync(),
            InvoiceCount = total,
            TotalPurchases = await query.SumAsync(p => (decimal?)p.TotalAmount) ?? 0,
            TotalDiscounts = await query.SumAsync(p => (decimal?)p.Discount + (p.Discount2 ?? 0) + (p.Discount3 ?? 0)) ?? 0,
            TotalTax = await query.SumAsync(p => (decimal?)p.Tax) ?? 0,
            NetPurchases = await query.SumAsync(p => (decimal?)p.NetAmount) ?? 0,
            TotalReturns = totalReturns
        };
        return View(vm);
    }

    public async Task<IActionResult> Payments(DateTime? from, DateTime? to)
    {
        var vm = await _report.PaymentsReportAsync(from, to);
        ViewBag.From = vm.From?.ToString("yyyy-MM-dd");
        ViewBag.To = vm.To?.ToString("yyyy-MM-dd");
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> TrialBalance(DateTime? asOf)
    {
        var now = DateTime.Today;
        asOf ??= now;

        var vm = await _financial.TrialBalanceAsync(asOf.Value.Date);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> IncomeStatement(DateTime? from, DateTime? to)
    {
        var now = DateTime.Today;
        from ??= new DateTime(now.Year, now.Month, 1);
        to ??= now;

        var vm = await _financial.IncomeStatementAsync(from.Value.Date, to.Value.Date);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> BalanceSheet(DateTime? asOf)
    {
        var now = DateTime.Today;
        asOf ??= now;

        var vm = await _financial.BalanceSheetAsync(asOf.Value.Date);
        return View(vm);
    }

    [HttpGet]
    [RequirePerm("Reports.Export")]
    public async Task<IActionResult> ExportSalesCsv(DateTime? from, DateTime? to)
    {
        var now = DateTime.Today;
        from ??= new DateTime(now.Year, now.Month, 1);
        to ??= now;
        var fromDate = from.Value.Date;
        var toDate = to.Value.Date;

        var result = await _export.SalesToCsv(fromDate, toDate);
        if (result.Truncated)
        {
            TempData["Error"] = $"عدد فواتير البيع في هذا النطاق يتجاوز حد التصدير ({ReportExportService.MaxExportRows:N0} سجل)؛ ضيّق نطاق التاريخ ثم أعد التصدير.";
            return RedirectToAction(nameof(Sales), new { from = fromDate, to = toDate });
        }
        return File(result.Bytes, "text/csv; charset=utf-8", $"sales_{fromDate:yyyyMMdd}-{toDate:yyyyMMdd}.csv");
    }

    [HttpGet]
    [RequirePerm("Reports.Export")]
    public async Task<IActionResult> ExportPurchasesCsv(DateTime? from, DateTime? to)
    {
        var now = DateTime.Today;
        from ??= new DateTime(now.Year, now.Month, 1);
        to ??= now;
        var fromDate = from.Value.Date;
        var toDate = to.Value.Date;

        var result = await _export.PurchasesToCsv(fromDate, toDate);
        if (result.Truncated)
        {
            TempData["Error"] = $"عدد فواتير الشراء في هذا النطاق يتجاوز حد التصدير ({ReportExportService.MaxExportRows:N0} سجل)؛ ضيّق نطاق التاريخ ثم أعد التصدير.";
            return RedirectToAction(nameof(Purchases), new { from = fromDate, to = toDate });
        }
        return File(result.Bytes, "text/csv; charset=utf-8", $"purchases_{fromDate:yyyyMMdd}-{toDate:yyyyMMdd}.csv");
    }

    [HttpGet]
    [RequirePerm("Reports.Export")]
    public async Task<IActionResult> ExportPaymentsCsv(DateTime? from, DateTime? to)
    {
        var now = DateTime.Today;
        from ??= new DateTime(now.Year, now.Month, 1);
        to ??= now;
        var fromDate = from.Value.Date;
        var toDate = to.Value.Date;

        var result = await _export.PaymentsToCsv(fromDate, toDate);
        if (result.Truncated)
        {
            TempData["Error"] = $"عدد الحركات النقدية في هذا النطاق يتجاوز حد التصدير ({ReportExportService.MaxExportRows:N0} سجل)؛ ضيّق نطاق التاريخ ثم أعد التصدير.";
            return RedirectToAction(nameof(Payments), new { from = fromDate, to = toDate });
        }
        return File(result.Bytes, "text/csv; charset=utf-8", $"payments_{fromDate:yyyyMMdd}-{toDate:yyyyMMdd}.csv");
    }

    [HttpGet]
    [RequirePerm("Reports.Dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var vm = await _report.GetDashboardAsync();
        return View(vm);
    }

    [HttpGet]
    [RequirePerm("AuditLedger.View")]
    public async Task<IActionResult> AuditLedger(DateTime? from, DateTime? to, int? accountId, JournalSource? source, int? branchId = null)
    {
        var now = DateTime.Today;
        from ??= new DateTime(now.Year, now.Month, 1);
        to ??= now;

        var fromDate = from.Value.Date;
        var toDate = to.Value.Date;

        var query = _db.JournalEntries
            .AsNoTracking()
            .Where(j => j.IsPosted && j.Date >= fromDate && j.Date <= toDate);
        if (source is not null)
            query = query.Where(j => j.Source == source);
        if (accountId is not null)
            query = query.Where(j => j.Lines.Any(l => l.AccountId == accountId));
        if (branchId is not null)
            query = query.Where(j => j.BranchId == branchId);

        var total = await query.CountAsync();
        ViewBag.From = fromDate.ToString("yyyy-MM-dd");
        ViewBag.To = toDate.ToString("yyyy-MM-dd");
        ViewBag.AccountId = accountId;
        ViewBag.Source = source?.ToString();
        ViewBag.BranchId = branchId;

        ViewBag.Accounts = new SelectList(
            await _db.GLAccounts.AsNoTracking().OrderBy(a => a.Code).Select(a => new { a.Id, Display = a.Code + " — " + a.Name }).ToListAsync(),
            "Id", "Display");

        var filteredEntryIds = query.Select(j => j.Id);
        var totalDebit = await _db.JournalEntryLines
            .AsNoTracking()
            .Where(l => filteredEntryIds.Contains(l.JournalEntryId))
            .SumAsync(l => (decimal?)l.Debit) ?? 0;
        var totalCredit = await _db.JournalEntryLines
            .AsNoTracking()
            .Where(l => filteredEntryIds.Contains(l.JournalEntryId))
            .SumAsync(l => (decimal?)l.Credit) ?? 0;

        var entriesRaw = await query.OrderBy(j => j.EntryNumber).ToListAsync();
        var pageEntryIds = entriesRaw.Select(e => e.Id).ToArray();
        var pageLines = await _db.JournalEntryLines
            .AsNoTracking()
            .Include(l => l.Account)
            .Where(l => pageEntryIds.Contains(l.JournalEntryId))
            .OrderBy(l => l.Account!.Code)
            .ToListAsync();

        var entries = entriesRaw.Select(e =>
        {
            var entryLines = pageLines.Where(l => l.JournalEntryId == e.Id).ToList();
            return new AuditLedgerEntryViewModel
            {
                Id = e.Id,
                EntryNumber = e.EntryNumber,
                Date = e.Date,
                Description = e.Description,
                Source = e.Source,
                SourceDisplayName = e.Source.GetDisplayName(),
                SourceId = e.SourceId,
                CreatedBy = e.CreatedBy,
                BranchId = e.BranchId,
                TotalDebit = entryLines.Sum(l => l.Debit),
                TotalCredit = entryLines.Sum(l => l.Credit),
                Lines = entryLines.Select(l => new AuditLedgerLineViewModel
                {
                    AccountCode = l.Account?.Code ?? "",
                    AccountName = l.Account?.Name ?? "",
                    Debit = l.Debit,
                    Credit = l.Credit,
                    Description = l.Description
                }).ToList()
            };
        }).ToList();

        var vm = new AuditLedgerViewModel
        {
            From = fromDate,
            To = toDate,
            AccountId = accountId,
            Source = source,
            Entries = entries,
            TotalEntries = total,
            TotalDebit = totalDebit,
            TotalCredit = totalCredit
        };
        return View(vm);
    }

    [HttpGet]
    [RequirePerm("AuditLedger.Export")]
    public async Task<IActionResult> AuditLedgerXlsx(DateTime? from, DateTime? to, int? accountId, JournalSource? source)
    {
        var bytes = await _report.ExportAuditLedgerXlsxAsync(from, to, accountId, source);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"audit-ledger-{DateTime.Now:yyyyMMdd}.xlsx");
    }

    // ---------- Budget variance ----------

    [HttpGet]
    [RequirePerm("Reports.View")]
    public async Task<IActionResult> BudgetVariance(int? year)
    {
        year ??= await _db.BudgetYears.Where(b => b.IsActive).OrderByDescending(b => b.Year).Select(b => (int?)b.Year).FirstOrDefaultAsync() ?? DateTime.Today.Year;

        var budget = await _db.BudgetYears.AsNoTracking().FirstOrDefaultAsync(b => b.Year == year.Value);
        var budgetLines = budget == null
            ? new List<BudgetLineRecord>()
            : await _db.BudgetLines.AsNoTracking()
                .Where(l => l.BudgetYearId == budget.Id && l.AnnualAmount != 0)
                .Include(l => l.Account)
                .Select(l => new { l.Account, l.AnnualAmount })
                .OrderBy(x => x.Account!.Code)
                .Select(x => new BudgetLineRecord(x.Account!, x.AnnualAmount))
                .ToListAsync();

        var rows = new List<VarianceRow>();
        decimal totalBudgetRev = 0, totalActualRev = 0, totalBudgetExp = 0, totalActualExp = 0;
        var activityMap = await _financial.GetAccountsYearlyActivityAsync(
            budgetLines.Select(bl => bl.Account.Id).Distinct(), year.Value);

        foreach (var bl in budgetLines)
        {
            var account = bl.Account;
            var amount = bl.Amount;
            (decimal Debit, decimal Credit) activity = activityMap.TryGetValue(account.Id, out var act) ? act : (0m, 0m);
            decimal actual = account.NormalBalance == NormalBalance.Debit
                ? activity.Debit - activity.Credit
                : activity.Credit - activity.Debit;

            decimal variance = actual - amount;
            decimal pct = amount != 0 ? variance / amount : 0;

            rows.Add(new VarianceRow
            {
                Code = account.Code,
                Name = account.Name,
                Type = account.Type,
                Budget = amount,
                Actual = actual,
                Variance = variance,
                VariancePct = pct
            });

            if (account.Type == GLAccountType.Revenue)
            {
                totalBudgetRev += amount;
                totalActualRev += actual;
            }
            else
            {
                totalBudgetExp += amount;
                totalActualExp += actual;
            }
        }

        var vm = new BudgetVarianceViewModel
        {
            Year = year.Value,
            Rows = rows,
            TotalBudgetRevenue = totalBudgetRev,
            TotalActualRevenue = totalActualRev,
            TotalBudgetExpense = totalBudgetExp,
            TotalActualExpense = totalActualExp,
            Revenues = await _db.BudgetYears.AsNoTracking().OrderByDescending(b => b.Year).Select(b => b.Year).ToListAsync()
        };
        return View(vm);
    }

    [HttpGet]
    [RequirePerm("Reports.Export")]
    public async Task<IActionResult> BudgetVarianceXlsx(int year)
    {
        var budget = await _db.BudgetYears.AsNoTracking().FirstOrDefaultAsync(b => b.Year == year);
        var budgetLines = budget == null
            ? new List<BudgetLineRecord>()
            : await _db.BudgetLines.AsNoTracking()
                .Where(l => l.BudgetYearId == budget.Id && l.AnnualAmount != 0)
                .Include(l => l.Account)
                .Select(l => new { l.Account, l.AnnualAmount })
                .OrderBy(x => x.Account!.Code)
                .Select(x => new BudgetLineRecord(x.Account!, x.AnnualAmount))
                .ToListAsync();

        var rows = new List<(string Code, string Name, decimal Budget, decimal Actual, decimal Variance, decimal VariancePct)>();
        var activityMap = await _financial.GetAccountsYearlyActivityAsync(
            budgetLines.Select(bl => bl.Account.Id).Distinct(), year);
        foreach (var bl in budgetLines)
        {
            var account = bl.Account;
            var amount = bl.Amount;
            (decimal Debit, decimal Credit) activity = activityMap.TryGetValue(account.Id, out var act) ? act : (0m, 0m);
            decimal actual = account.NormalBalance == NormalBalance.Debit
                ? activity.Debit - activity.Credit
                : activity.Credit - activity.Debit;
            decimal variance = actual - amount;
            decimal pct = amount != 0 ? variance / amount : 0;
            rows.Add((account.Code, account.Name, amount, actual, variance, pct));
        }

        var bytes = await _report.ExportBudgetVarianceXlsxAsync(year, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"budget-variance-{year}.xlsx");
    }

    // ---------- Aging (القائمة العمرية) ----------

    [RequirePerm("Aging.View")]
    public async Task<IActionResult> Aging()
    {
        return View(await _report.AgingAsync());
    }

    [HttpGet]
    [RequirePerm("Aging.Export")]
    public async Task<IActionResult> AgingXlsx()
    {
        var bytes = await _report.ExportAgingXlsxAsync();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"aging_{DateTime.Today:yyyyMMdd}.xlsx");
    }

    // ---------- Cash flow (التدفق النقدي) ----------

    [RequirePerm("Reports.View")]
    public async Task<IActionResult> CashFlow(DateTime? from, DateTime? to)
    {
        var now = DateTime.Today;
        from ??= new DateTime(now.Year, now.Month, 1);
        to ??= now;
        return View(await _report.CashFlowAsync(from.Value, to.Value));
    }

    [HttpGet]
    [RequirePerm("Reports.Export")]
    public async Task<IActionResult> CashFlowXlsx(DateTime? from, DateTime? to)
    {
        var now = DateTime.Today;
        from ??= new DateTime(now.Year, now.Month, 1);
        to ??= now;
        var bytes = await _report.ExportCashFlowXlsxAsync(from.Value, to.Value);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"cashflow_{from.Value:yyyyMMdd}_{to.Value:yyyyMMdd}.xlsx");
    }

    // ---------- Financial report exports (PDF + XLSX) ----------

    [HttpGet]
    [RequirePerm("Reports.Export")]
    public async Task<IActionResult> ExportTrialBalancePdf(DateTime? asOf)
    {
        var today = DateTime.Today;
        asOf ??= today;
        var bytes = await _report.ExportTrialBalancePdfAsync(asOf.Value.Date);
        return File(bytes, "application/pdf", $"trial-balance-{asOf.Value:yyyyMMdd}.pdf");
    }

    [HttpGet]
    [RequirePerm("Reports.Export")]
    public async Task<IActionResult> ExportTrialBalanceXlsx(DateTime? asOf)
    {
        var today = DateTime.Today;
        asOf ??= today;
        var bytes = await _report.ExportTrialBalanceXlsxAsync(asOf.Value.Date);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"trial-balance-{asOf.Value:yyyyMMdd}.xlsx");
    }

    [HttpGet]
    [RequirePerm("Reports.Export")]
    public async Task<IActionResult> ExportIncomeStatementPdf(DateTime? from, DateTime? to)
    {
        var now = DateTime.Today;
        from ??= new DateTime(now.Year, now.Month, 1);
        to ??= now;
        var bytes = await _report.ExportIncomeStatementPdfAsync(from.Value.Date, to.Value.Date);
        return File(bytes, "application/pdf", $"income-statement-{from.Value:yyyyMMdd}-{to.Value:yyyyMMdd}.pdf");
    }

    [HttpGet]
    [RequirePerm("Reports.Export")]
    public async Task<IActionResult> ExportIncomeStatementXlsx(DateTime? from, DateTime? to)
    {
        var now = DateTime.Today;
        from ??= new DateTime(now.Year, now.Month, 1);
        to ??= now;
        var bytes = await _report.ExportIncomeStatementXlsxAsync(from.Value.Date, to.Value.Date);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"income-statement-{from.Value:yyyyMMdd}-{to.Value:yyyyMMdd}.xlsx");
    }

    [HttpGet]
    [RequirePerm("Reports.Export")]
    public async Task<IActionResult> ExportBalanceSheetPdf(DateTime? asOf)
    {
        var today = DateTime.Today;
        asOf ??= today;
        var bytes = await _report.ExportBalanceSheetPdfAsync(asOf.Value.Date);
        return File(bytes, "application/pdf", $"balance-sheet-{asOf.Value:yyyyMMdd}.pdf");
    }

    [HttpGet]
    [RequirePerm("Reports.Export")]
    public async Task<IActionResult> ExportBalanceSheetXlsx(DateTime? asOf)
    {
        var today = DateTime.Today;
        asOf ??= today;
        var bytes = await _report.ExportBalanceSheetXlsxAsync(asOf.Value.Date);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"balance-sheet-{asOf.Value:yyyyMMdd}.xlsx");
    }

    // ---------- Operational reports (items / stock) ----------

    [HttpGet]
    [RequirePerm("Reports.Export")]
    public async Task<IActionResult> ExportItemsXlsx()
    {
        var bytes = await _report.ExportItemsXlsxAsync();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"items-{DateTime.Now:yyyyMMdd}.xlsx");
    }

    [HttpGet]
    [RequirePerm("Reports.Export")]
    public async Task<IActionResult> ExportStockXlsx(bool lowOnly = false)
    {
        var bytes = await _report.ExportStockXlsxAsync(lowOnly);
        var name = lowOnly ? "low-stock" : "stock";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{name}-{DateTime.Now:yyyyMMdd}.xlsx");
    }

    [HttpGet]
    [RequirePerm("Reports.Export")]
    public async Task<IActionResult> ExportSalesXlsx(DateTime? from, DateTime? to)
    {
        var now = DateTime.Today;
        from ??= new DateTime(now.Year, now.Month, 1);
        to ??= now;
        var fromDate = from.Value.Date;
        var toDate = to.Value.Date;
        var bytes = await _report.ExportSalesXlsxAsync(fromDate, toDate);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"sales-{fromDate:yyyyMMdd}-{toDate:yyyyMMdd}.xlsx");
    }

    [HttpGet]
    [RequirePerm("Reports.Export")]
    public async Task<IActionResult> ExportPurchasesXlsx(DateTime? from, DateTime? to)
    {
        var now = DateTime.Today;
        from ??= new DateTime(now.Year, now.Month, 1);
        to ??= now;
        var fromDate = from.Value.Date;
        var toDate = to.Value.Date;
        var bytes = await _report.ExportPurchasesXlsxAsync(fromDate, toDate);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"purchases-{fromDate:yyyyMMdd}-{toDate:yyyyMMdd}.xlsx");
    }

    [HttpGet]
    [RequirePerm("Reports.Export")]
    public async Task<IActionResult> ExportPaymentsXlsx(DateTime? from, DateTime? to)
    {
        var now = DateTime.Today;
        from ??= new DateTime(now.Year, now.Month, 1);
        to ??= now;
        var fromDate = from.Value.Date;
        var toDate = to.Value.Date;
        var bytes = await _report.ExportPaymentsXlsxAsync(fromDate, toDate);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"payments-{fromDate:yyyyMMdd}-{toDate:yyyyMMdd}.xlsx");
    }

    [HttpGet]
    [RequirePerm("Reports.Export")]
    public async Task<IActionResult> PrintInvoice(int id, string type)
    {
        if (type == "sale")
        {
            var sale = await _db.SaleInvoices.AsNoTracking().Include(s => s.Customer).Include(s => s.Items).ThenInclude(i => i.Item).FirstOrDefaultAsync(s => s.Id == id);
            if (sale == null) return NotFound();
            var saleBytes = PdfInvoiceService.RenderSalePdf(sale);
            return File(saleBytes, "application/pdf", $"sale-{sale.InvoiceNumber}.pdf");
        }
        if (type == "purchase")
        {
            var purchase = await _db.PurchaseInvoices.AsNoTracking().Include(p => p.Supplier).Include(p => p.Items).ThenInclude(i => i.Item).FirstOrDefaultAsync(p => p.Id == id);
            if (purchase == null) return NotFound();
            var purchaseBytes = PdfInvoiceService.RenderPurchasePdf(purchase);
            return File(purchaseBytes, "application/pdf", $"purchase-{purchase.InvoiceNumber}.pdf");
        }
        return NotFound();
    }
}