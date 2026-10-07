using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class AccountsController : Controller
{
    private readonly AppDbContext _db;
    private readonly AccountsService _accounts;

    public AccountsController(AppDbContext db, AccountsService accounts)
    {
        _db = db;
        _accounts = accounts;
    }

    [RequirePerm("ChartOfAccounts.View")]
    public async Task<IActionResult> Index(GLAccountType? type, bool? active, int page = 1, string? search = null)
    {
        var query = _db.GLAccounts.AsNoTracking().AsQueryable();
        if (type.HasValue)
        {
            query = query.Where(a => a.Type == type.Value);
        }

        if (active.HasValue)
        {
            query = query.Where(a => a.IsActive == active.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(a => a.Code.Contains(term) || a.Name.Contains(term));
        }
        query = query.OrderBy(a => a.Code);

        var total = await query.CountAsync();
        page = PagerExtensions.NormalizePage(page, total);
        var accounts = await query
            .Skip((page - 1) * PagerExtensions.PageSize)
            .Take(PagerExtensions.PageSize)
            .ToListAsync();
        ViewBag.TypeFilter = type;
        ViewBag.ActiveFilter = active;
        ViewBag.TypeList = new SelectList(Enum.GetValues<GLAccountType>(), "Value", "Value");

        var accountIds = accounts.Select(a => a.Id).ToList();
        var balances = await _db.JournalEntryLines
            .AsNoTracking()
            .Where(l => accountIds.Contains(l.AccountId) && l.JournalEntry!.IsPosted)
            .GroupBy(l => l.AccountId)
            .Select(g => new { AccountId = g.Key, Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) })
            .ToListAsync();
        ViewBag.Balances = balances.ToDictionary(b => b.AccountId, b => (b.Debit, b.Credit));
        this.SetPager(page, total, accounts.Count, search);

        return View(accounts);
    }

    [RequirePerm("ChartOfAccounts.Create")]
    public IActionResult Create()
    {
        ViewBag.ParentList = new SelectList(
            _db.GLAccounts.AsNoTracking().Where(a => a.IsActive).OrderBy(a => a.Code).Select(a => new { a.Id, Display = a.Code + " — " + a.Name }).ToList(),
            "Id", "Display");
        return View(new GLAccount());
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("ChartOfAccounts.Create")]
    public async Task<IActionResult> Create(GLAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.Code) || string.IsNullOrWhiteSpace(account.Name))
        {
            TempData["Error"] = "يرجى إدخال رمز واسم الحساب";
            return RedirectToAction(nameof(Create));
        }

        account.Code = account.Code.Trim();
        if (await _accounts.CodeExistsAsync(account.Code))
        {
            TempData["Error"] = "حساب بهذا الرمز موجود بالفعل";
            return RedirectToAction(nameof(Create));
        }

        account.CreatedAt = DateTime.UtcNow;
        account.IsActive = true;
        _db.GLAccounts.Add(account);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة الحساب بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [RequirePerm("ChartOfAccounts.Edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var account = await _db.GLAccounts.FindAsync(id);
        if (account == null)
        {
            return NotFound();
        }

        ViewBag.ParentList = new SelectList(
            _db.GLAccounts.AsNoTracking().Where(a => a.IsActive && a.Id != id).OrderBy(a => a.Code).Select(a => new { a.Id, Display = a.Code + " — " + a.Name }).ToList(),
            "Id", "Display");
        ViewBag.IsPosted = await _accounts.HasPostedLinesAsync(id);
        ViewBag.IsSystem = _accounts.IsSystemAccount(account.Code);
        return View(account);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("ChartOfAccounts.Edit")]
    public async Task<IActionResult> Edit(GLAccount model)
    {
        var account = await _db.GLAccounts.FindAsync(model.Id);
        if (account == null)
        {
            return NotFound();
        }

        var result = await _accounts.ApplyEditAsync(account, model);
        if (!result.Ok)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Edit), new { id = model.Id });
        }

        // The posted token is the row the form was rendered from. Carrying it as the original
        // value turns a silent overwrite of someone else's rename into a refusal.
        if (model.RowVersion is { Length: > 0 } posted)
        {
            _db.Entry(account).Property(a => a.RowVersion).OriginalValue = posted;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            TempData["Error"] = "تعذّر حفظ التعديل لأن الحساب عُدّل في جلسة أخرى. أعد فتح الصفحة وحاول مجددًا.";
            return RedirectToAction(nameof(Edit), new { id = model.Id });
        }

        TempData["Success"] = "تم تعديل الحساب بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("ChartOfAccounts.Deactivate")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var account = await _db.GLAccounts.FindAsync(id);
        if (account == null)
        {
            return NotFound();
        }

        if (_accounts.IsSystemAccount(account.Code))
        {
            TempData["Error"] = "لا يمكن تعطيل حساب نظامي مُعرَّف بالبذرة";
            return RedirectToAction(nameof(Index));
        }

        account.IsActive = !account.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = account.IsActive ? "تم تنشيط الحساب بنجاح" : "تم تعطيل الحساب بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("ChartOfAccounts.Deactivate")]
    public async Task<IActionResult> Delete(int id)
    {
        var account = await _db.GLAccounts.FindAsync(id);
        if (account == null)
        {
            return NotFound();
        }

        var info = await _accounts.GetDeleteInfoAsync(account);
        if (!info.CanDelete)
        {
            if (info.IsSystem)
            {
                TempData["Error"] = "لا يمكن حذف حساب نظامي مُعرَّف بالبذرة";
            }
            else
            {
                TempData["Error"] = "لا يمكن حذف هذا الحساب لأنه له قيود مرحلة؛ يمكنك تعطيله بدلاً من ذلك";
            }

            return RedirectToAction(nameof(Index));
        }

        _db.GLAccounts.Remove(account);

        // الحذفُ نفسُه كتابةٌ، و`RowVersion` داخل شرط `DELETE`؛ فتزاحمُ زميلٍ بين القراءة والحذف
        // يُنتج `DbUpdateConcurrencyException` لا نتيجةً هادئة، وبلا التقاطٍ يكون خطأَ ٥٠٠.
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            TempData["Error"] = "تعذّر حذف الحساب لأنه عُدّل في جلسة أخرى. أعد فتح الصفحة وحاول مجددًا.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = "تم حذف الحساب بنجاح";
        return RedirectToAction(nameof(Index));
    }
}
