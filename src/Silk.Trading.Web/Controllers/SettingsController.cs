using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Extensions;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Models.Core;
using Silk.Trading.Web.ViewModels.Core;
using System.ComponentModel.DataAnnotations;

namespace Silk.Trading.Web.Controllers;

[Authorize]
[RequirePerm("Settings.View")]
public class SettingsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;
    public SettingsController(AppDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public async Task<IActionResult> Index()
    {
        var vm = new SettingsViewModel
        {
            Units = await _db.Units.AsNoTracking().OrderBy(u => u.Name).ToListAsync(),
            Categories = await _db.ItemCategories.Include(c => c.Items).AsNoTracking().OrderBy(c => c.Name).ToListAsync(),
            ItemTypes = await _db.ItemTypes.Include(t => t.Items).AsNoTracking().OrderBy(t => t.Name).ToListAsync(),
            Currencies = await _db.Currencies.AsNoTracking().OrderByDescending(c => c.IsBase).ThenBy(c => c.Code).ToListAsync(),
            Branches = await _db.Branches.AsNoTracking().OrderBy(b => b.Code).ToListAsync(),
            CurrentBranchId = _http.GetCurrentBranchId()
        };
        return View(vm);
    }

    // ---------- Currencies ----------

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> AddCurrency(AddCurrencyRequest request)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Distinct());
            return RedirectToAction(nameof(Index));
        }
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Currencies.AnyAsync(c => c.Code == code))
        {
            TempData["Error"] = "عملة بهذا الرمز موجودة بالفعل";
            return RedirectToAction(nameof(Index));
        }
        _db.Currencies.Add(new Currency
        {
            Code = code,
            Name = request.Name.Trim(),
            Symbol = request.Symbol,
            ExchangeRate = request.ExchangeRate,
            IsActive = request.IsActive,
            IsBase = false
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة العملة بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> UpdateCurrency(Currency currency)
    {
        var existing = await _db.Currencies.FindAsync(currency.Id);
        if (existing == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(currency.Code) && !string.IsNullOrWhiteSpace(currency.Name) &&
            currency.ExchangeRate > 0)
        {
            if (await _db.Currencies.AnyAsync(c => c.Id != currency.Id && c.Code == currency.Code.Trim().ToUpperInvariant()))
                TempData["Error"] = "عملة بهذا الرمز موجودة بالفعل";
            else
            {
                existing.Code = currency.Code.Trim().ToUpperInvariant();
                existing.Name = currency.Name.Trim();
                existing.Symbol = currency.Symbol;
                existing.ExchangeRate = currency.ExchangeRate;
                existing.IsActive = currency.IsActive;
                await _db.SaveChangesAsync();
                TempData["Success"] = "تم تعديل العملة بنجاح";
            }
        }
        else
        {
            TempData["Error"] = "تحقق من بيانات العملة وسعر الصرف";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> DeleteCurrency(int id)
    {
        var currency = await _db.Currencies.FindAsync(id);
        if (currency == null) return NotFound();
        if (currency.IsBase)
        {
            TempData["Error"] = "لا يمكن حذف العملة الأساسية؛ اختر أساسًا آخر أولاً";
            return RedirectToAction(nameof(Index));
        }
        bool inUse = await _db.SaleInvoices.AnyAsync(s => s.CurrencyId == id) ||
                     await _db.PurchaseInvoices.AnyAsync(p => p.CurrencyId == id) ||
                     await _db.Customers.AnyAsync(c => c.CurrencyId == id) ||
                     await _db.Suppliers.AnyAsync(s => s.CurrencyId == id);
        if (inUse)
        {
            currency.IsActive = false;
            await _db.SaveChangesAsync();
            TempData["Error"] = "لا يمكن حذف العملة لأنها مستخدمة؛ تم تعطيلها بدلاً من ذلك";
        }
        else
        {
            _db.Currencies.Remove(currency);
            await _db.SaveChangesAsync();
            TempData["Success"] = "تم حذف العملة بنجاح";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> SetBaseCurrency(int id)
    {
        var currency = await _db.Currencies.FindAsync(id);
        if (currency == null) return NotFound();
        currency.IsBase = true;
        currency.ExchangeRate = 1m;
        await _db.Currencies.Where(c => c.Id != id).ForEachAsync(c => c.IsBase = false);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"أُعيدت إلى العملة الأساسية: {currency.Name}";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Branches ----------

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> AddBranch(Branch branch)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Distinct());
            return RedirectToAction(nameof(Index));
        }
        branch.Code = branch.Code.Trim().ToUpperInvariant();
        if (await _db.Branches.AnyAsync(b => b.Code == branch.Code))
            TempData["Error"] = "فرع بهذا الرمز موجود بالفعل";
        else
        {
            branch.CreatedAt = DateTime.UtcNow;
            _db.Branches.Add(branch);
            await _db.SaveChangesAsync();
            TempData["Success"] = "تم إضافة الفرع بنجاح";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> UpdateBranch(Branch branch)
    {
        var existing = await _db.Branches.FindAsync(branch.Id);
        if (existing == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(branch.Code) && !string.IsNullOrWhiteSpace(branch.Name))
        {
            if (await _db.Branches.AnyAsync(b => b.Id != branch.Id && b.Code == branch.Code.Trim().ToUpperInvariant()))
                TempData["Error"] = "فرع بهذا الرمز موجود بالفعل";
            else
            {
                existing.Code = branch.Code.Trim().ToUpperInvariant();
                existing.Name = branch.Name.Trim();
                existing.Address = branch.Address;
                existing.Phone = branch.Phone;
                existing.IsActive = branch.IsActive;
                await _db.SaveChangesAsync();
                TempData["Success"] = "تم تعديل الفرع بنجاح";
            }
        }
        else
        {
            TempData["Error"] = "يرجى إدخال رمز واسم الفرع";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> DeleteBranch(int id)
    {
        var branch = await _db.Branches.FindAsync(id);
        if (branch == null) return NotFound();
        bool inUse = await _db.SaleInvoices.AnyAsync(s => s.BranchId == id) ||
                     await _db.PurchaseInvoices.AnyAsync(p => p.BranchId == id) ||
                     await _db.Payments.AnyAsync(p => p.BranchId == id) ||
                     await _db.JournalEntries.AnyAsync(j => j.BranchId == id);
        if (inUse)
        {
            branch.IsActive = false;
            await _db.SaveChangesAsync();
            TempData["Error"] = "لا يمكن حذف الفرع لأن لديه حركات؛ تم تعطيله بدلاً من ذلك";
        }
        else
        {
            _db.Branches.Remove(branch);
            await _db.SaveChangesAsync();
            _http.SetCurrentBranchId(null);
            TempData["Success"] = "تم حذف الفرع بنجاح";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> SetCurrentBranch(int branchId)
    {
        if (branchId <= 0 || !await _db.Branches.AnyAsync(b => b.Id == branchId && b.IsActive))
        {
            TempData["Error"] = "الفرع المحدد غير موجود أو غير نشط";
            return RedirectToAction(nameof(Index));
        }
        _http.SetCurrentBranchId(branchId);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> AddUnit(Unit unit)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Distinct());
            return RedirectToAction(nameof(Index));
        }
        if (await _db.Units.AnyAsync(u => u.Name == unit.Name.Trim()))
        {
            TempData["Error"] = "الوحدة بهذا الاسم موجودة بالفعل";
        }
        else if (unit.Id == unit.ParentUnitId || await CreatesCycleAsync(unit.Id, unit.ParentUnitId))
        {
            TempData["Error"] = "لا يمكن ربط الوحدة بنفسها أو إنشاء حلقة في الوحدات";
        }
        else
        {
            unit.Id = 0;
            unit.Name = unit.Name.Trim();
            unit.ShortName = string.IsNullOrWhiteSpace(unit.ShortName) ? unit.Name : unit.ShortName;
            _db.Units.Add(unit);
            await _db.SaveChangesAsync();
            TempData["Success"] = "تم إضافة الوحدة بنجاح";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> UpdateUnit(Unit unit)
    {
        var existing = await _db.Units.FindAsync(unit.Id);
        if (existing != null && !string.IsNullOrWhiteSpace(unit.Name) && unit.ParentUnitId != unit.Id && !await CreatesCycleAsync(unit.Id, unit.ParentUnitId))
        {
            if (await _db.Units.AnyAsync(u => u.Id != unit.Id && u.Name == unit.Name.Trim()))
                TempData["Error"] = "الوحدة بهذا الاسم موجودة بالفعل";
            else
            {
                existing.Name = unit.Name.Trim();
                existing.ShortName = string.IsNullOrWhiteSpace(unit.ShortName) ? unit.Name : unit.ShortName;
                existing.SubUnits = unit.SubUnits;
                existing.ParentUnitId = unit.ParentUnitId;
                existing.IsActive = unit.IsActive;
                await _db.SaveChangesAsync();
                TempData["Success"] = "تم تعديل الوحدة بنجاح";
            }
        }
        else
        {
            TempData["Error"] = "تعذر حفظ التعديلات، تحقق من البيانات المدخلة أو من عدم إنشاء حلقة في الوحدات";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Settings.Edit")]
    public async Task<IActionResult> DeleteUnit(int id)
    {
        var unit = await _db.Units.FindAsync(id);
        if (unit == null) return NotFound();

        bool inUse = await _db.Items.AnyAsync(i => i.CountUnitId == id || i.QuantityUnitId == id) ||
                     await _db.Units.AnyAsync(u => u.ParentUnitId == id);

        if (inUse)
        {
            unit.IsActive = false;
            await _db.SaveChangesAsync();
            TempData["Error"] = "لا يمكن حذف الوحدة لأنها مستخدمة في أصناف؛ تم تعطيلها بدلاً من ذلك";
        }
        else
        {
            _db.Units.Remove(unit);
            await _db.SaveChangesAsync();
            TempData["Success"] = "تم حذف الوحدة بنجاح";
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> CreatesCycleAsync(int unitId, int? parentUnitId)
    {
        if (parentUnitId == null) return false;
        var visited = new HashSet<int>();
        int? current = parentUnitId;
        while (current != null && visited.Add(current.Value))
        {
            if (current.Value == unitId) return true;
            var parent = await _db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == current.Value);
            current = parent?.ParentUnitId;
        }
        return false;
    }
}

public class AddCurrencyRequest
{
    [Required(ErrorMessage = "رمز العملة مطلوب")]
    [StringLength(10)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "اسم العملة مطلوب")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(10)]
    public string? Symbol { get; set; }

    [Range(0.000001, 999999999, ErrorMessage = "سعر الصرف يجب أن يكون أكبر من صفر")]
    public decimal ExchangeRate { get; set; } = 1m;

    public bool IsActive { get; set; } = true;
}