using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Accounting;

namespace NewVixSmart.Web.Controllers;

[Authorize]
[RequirePerm("FiscalClose.Close")]
public class FiscalController : Controller
{
    private readonly AppDbContext _db;
    private readonly IFiscalService _fiscal;

    public FiscalController(AppDbContext db, IFiscalService fiscal)
    {
        _db = db;
        _fiscal = fiscal;
    }

    public async Task<IActionResult> Index()
    {
        var periods = await _db.FiscalPeriods.AsNoTracking().OrderByDescending(p => p.Year).ToListAsync();

        var rows = new List<FiscalPeriodRowViewModel>();
        foreach (var p in periods)
        {
            int count = 0;
            decimal net = 0;
            if (p.IsClosed)
            {
                var entryIds = await _db.JournalEntries.AsNoTracking()
                    .Where(j => j.Source == JournalSource.YearEndClose && j.SourceId == p.Id)
                    .Select(j => j.Id)
                    .ToListAsync();
                count = entryIds.Count;
                var retained = await _db.GLAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Code == "3001");
                if (retained != null && count > 0)
                {
                    decimal credit = await _db.JournalEntryLines.AsNoTracking()
                        .Where(l => entryIds.Contains(l.JournalEntryId) && l.AccountId == retained.Id)
                        .SumAsync(l => (decimal?)l.Credit) ?? 0m;
                    decimal debit = await _db.JournalEntryLines.AsNoTracking()
                        .Where(l => entryIds.Contains(l.JournalEntryId) && l.AccountId == retained.Id)
                        .SumAsync(l => (decimal?)l.Debit) ?? 0m;
                    net = credit - debit;
                }
            }
            rows.Add(new FiscalPeriodRowViewModel
            {
                Year = p.Year,
                Name = p.Name ?? $"سنة {p.Year}",
                IsClosed = p.IsClosed,
                ClosedAt = p.ClosedAt,
                ClosedById = p.ClosedById,
                CloseEntryCount = count,
                NetIncome = net
            });
        }

        return View(new FiscalCloseViewModel
        {
            Rows = rows,
            LatestYear = periods.Count > 0 ? periods.Max(p => p.Year) : DateTime.Today.Year,
            NextYear = periods.Count > 0 ? periods.Max(p => p.Year) + 1 : DateTime.Today.Year
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("FiscalClose.Close")]
    public async Task<IActionResult> Create(int year)
    {
        if (year < 2000 || year > 2100)
        {
            TempData["Error"] = "السنة المالية يجب أن تكون بين 2000 و 2100";
            return RedirectToAction(nameof(Index));
        }
        await _fiscal.EnsurePeriodAsync(year);
        TempData["Success"] = $"أُنشئت السنة المالية {year} بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("FiscalClose.Close")]
    public async Task<IActionResult> Close(int year)
    {
        try
        {
            var summary = await _fiscal.CloseYearAsync(year, User.Identity?.Name);
            TempData["Success"] = $"أُغلقت السنة المالية {year} — رُحّل رصيد {summary.AccountsCleared} حساب إلى الأرباح المحتجزة 3001، صافي الدخل «{summary.NetIncomeToRetainedEarnings:C}»";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("FiscalClose.Reopen")]
    public async Task<IActionResult> Reopen(int year)
    {
        try
        {
            var summary = await _fiscal.ReopenYearAsync(year, User.Identity?.Name);
            TempData["Success"] = $"أُعيد فتح السنة المالية {year} — حُذف {summary.EntriesRemoved} قيد إقفال سنوي وعادت أرصدة الأرباح/الخسائر إلى سابق عهدها";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }
}