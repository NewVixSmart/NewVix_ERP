using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.Controllers;

[Authorize]
[RequirePerm("Budgets.View")]
public class BudgetsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IFiscalService _fiscal;

    public BudgetsController(AppDbContext db, IFiscalService fiscal)
    {
        _db = db;
        _fiscal = fiscal;
    }

    public async Task<IActionResult> Index()
    {
        var years = await _db.BudgetYears.AsNoTracking().OrderByDescending(b => b.Year).ToListAsync();
        var yearsWithCounts = new List<(BudgetYear Year, int LineCount, bool FiscalClosed)>();
        foreach (var y in years)
        {
            var lineCount = await _db.BudgetLines.AsNoTracking().CountAsync(l => l.BudgetYearId == y.Id);
            var fiscalClosed = await _db.FiscalPeriods.AsNoTracking().AnyAsync(p => p.Year == y.Year && p.IsClosed);
            yearsWithCounts.Add((y, lineCount, fiscalClosed));
        }
        return View(yearsWithCounts);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Budgets.Manage")]
    public async Task<IActionResult> Create(int year)
    {
        if (year < 2000 || year > 2100)
        {
            TempData["Error"] = "السنة يجب أن تكون بين 2000 و 2100";
            return RedirectToAction(nameof(Index));
        }
        if (await _db.BudgetYears.AnyAsync(b => b.Year == year))
        {
            TempData["Error"] = $"توجد ميزانية لسنة {year} بالفعل";
            return RedirectToAction(nameof(Index));
        }
        try { await _fiscal.ValidateBudgetWriteAsync(year); }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
        _db.BudgetYears.Add(new BudgetYear { Year = year, IsActive = true, CreatedBy = User.Identity?.Name });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"أُنشئت ميزانية سنة {year} بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Budgets.Manage")]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var budget = await _db.BudgetYears.FindAsync(id);
        if (budget == null)
        {
            return NotFound();
        }

        try { await _fiscal.ValidateBudgetWriteAsync(budget.Year); }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
        budget.IsActive = !budget.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = budget.IsActive ? "نُشّطت الميزانية بنجاح" : "أُلغيت الميزانية بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [RequirePerm("Budgets.Manage")]
    public async Task<IActionResult> Manage(int year)
    {
        var budget = await _db.BudgetYears.AsNoTracking().FirstOrDefaultAsync(b => b.Year == year);
        if (budget == null)
        {
            TempData["Error"] = $"لا توجد ميزانية لسنة {year} — أنشئها أولاً";
            return RedirectToAction(nameof(Index));
        }

        var plAccounts = await _db.GLAccounts.AsNoTracking()
            .Where(a => a.IsActive && (a.Type == GLAccountType.Revenue || a.Type == GLAccountType.Expense))
            .OrderBy(a => a.Code)
            .ToListAsync();

        // تُحمَّل السطور كاملةً لا المبالغ وحدها، لأنّ رمز التوفّر جزءٌ من الحالة التي
        // سيُحفظ مقابلَها؛ فلا يصحّ عرضُ قيمةٍ على الشبكة دون ما يثبت أيّ صفّ وُسم.
        var existingLines = await _db.BudgetLines.AsNoTracking()
            .Where(l => l.BudgetYearId == budget.Id)
            .ToDictionaryAsync(l => l.AccountId);

        var fiscalClosed = await _db.FiscalPeriods.AsNoTracking().AnyAsync(p => p.Year == year && p.IsClosed);

        ViewBag.Year = year;
        ViewBag.BudgetId = budget.Id;
        ViewBag.FiscalClosed = fiscalClosed;
        ViewBag.Budget = budget;

        return View(plAccounts.Select(a =>
        {
            existingLines.TryGetValue(a.Id, out var stored);
            return new BudgetLineVm
            {
                AccountId = a.Id,
                Code = a.Code,
                Name = a.Name,
                Type = a.Type,
                AnnualAmount = stored?.AnnualAmount ?? 0m,
                RowVersion = stored?.RowVersion
            };
        }).ToList());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Budgets.Manage")]
    public async Task<IActionResult> Manage(int year, List<BudgetLineVm> lines)
    {
        var budget = await _db.BudgetYears.AsNoTracking().FirstOrDefaultAsync(b => b.Year == year);
        if (budget == null)
        {
            TempData["Error"] = $"لا توجد ميزانية لسنة {year}";
            return RedirectToAction(nameof(Index));
        }

        try { await _fiscal.ValidateBudgetWriteAsync(year); }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Manage), new { year });
        }

        ViewBag.Year = year;
        ViewBag.BudgetId = budget.Id;
        ViewBag.FiscalClosed = false;
        ViewBag.Budget = budget;

        var plAccountIds = await _db.GLAccounts.AsNoTracking()
            .Where(a => a.IsActive && (a.Type == GLAccountType.Revenue || a.Type == GLAccountType.Expense))
            .Select(a => a.Id)
            .ToHashSetAsync();

        foreach (var line in lines ?? new List<BudgetLineVm>())
        {
            if (!plAccountIds.Contains(line.AccountId))
            {
                continue;
            }

            var existing = await _db.BudgetLines.FirstOrDefaultAsync(l => l.BudgetYearId == budget.Id && l.AccountId == line.AccountId);
            if (existing == null)
            {
                if (line.AnnualAmount != 0)
                {
                    _db.BudgetLines.Add(new BudgetLine { BudgetYearId = budget.Id, AccountId = line.AccountId, AnnualAmount = line.AnnualAmount });
                }
            }
            else
            {
                existing.AnnualAmount = line.AnnualAmount;

                // الشبكةُ تُحفظ ككتلةٍ واحدة، فسطرٌ واحدٌ غيّره مديرٌ آخر يجب أن يُفشل الحفظَ
                // كلَّه؛ فتجاوزُ سطرٍ واحدٍ يُسقط تعديلَ أحد المديرين بلا أثر يُرى.
                if (line.RowVersion is { Length: > 0 } posted)
                {
                    _db.Entry(existing).Property(l => l.RowVersion).OriginalValue = posted;
                }
            }
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            TempData["Error"] = "تعذّر حفظ الميزانية لأن أحد السطور عُدّل في جلسة أخرى. أعد فتح الصفحة وادخل الأرقام من جديد.";
            return RedirectToAction(nameof(Manage), new { year });
        }

        TempData["Success"] = "حُفظت الميزانية بنجاح";
        return RedirectToAction(nameof(Manage), new { year });
    }
}

public class BudgetLineVm
{
    public int AccountId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public GLAccountType Type { get; set; }
    public decimal AnnualAmount { get; set; }

    /// <summary>
    /// رمز توفّر الحجز للسطر؛ يُرسل كـBase64 في شبكة الحفظ ويُنقل إلى <c>OriginalValue</c>.
    /// وهو <c>null</c> لسطرٍ لم يُنشأ بعد، والحالةُ الوحيدة التي يُقبل فيها إدراجٌ بلا رمز.
    /// </summary>
    public byte[]? RowVersion { get; set; }
}
