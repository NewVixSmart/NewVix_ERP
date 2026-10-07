using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Stock;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class WarehousesController : Controller
{
    private readonly AppDbContext _db;
    public WarehousesController(AppDbContext db) => _db = db;

    [RequirePerm("Warehouses.View")]
    public async Task<IActionResult> Index()
    {
        var list = await _db.Warehouses.OrderByDescending(w => w.CreatedAt).AsNoTracking().ToListAsync();
        return View(list);
    }

    [RequirePerm("Warehouses.Create")]
    public IActionResult Create() => View();

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("Warehouses.Create")]
    public async Task<IActionResult> Create(Warehouse warehouse)
    {
        if (!ModelState.IsValid)
        {
            return View(warehouse);
        }

        _db.Warehouses.Add(warehouse);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة المستودع بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [RequirePerm("Warehouses.Edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var wh = await _db.Warehouses.FindAsync(id);
        if (wh == null)
        {
            return NotFound();
        }

        return View(wh);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("Warehouses.Edit")]
    public async Task<IActionResult> Edit(Warehouse warehouse)
    {
        if (!ModelState.IsValid)
        {
            return View(warehouse);
        }

        var wh = await _db.Warehouses.FindAsync(warehouse.Id);
        if (wh == null)
        {
            return NotFound();
        }

        wh.Code = warehouse.Code;
        wh.Name = warehouse.Name;
        wh.IsActive = warehouse.IsActive;

        // الرمز المُرسَل هو الصفّ الذي رُسم منه النموذج؛ فجعلُه قيمةً أصليةً يحوّل محوَ
        // كتابة زميلٍ إلى رفضٍ برسالة، بدل أن يمرّ آخر كاتبٍ دون أن يعلم.
        if (warehouse.RowVersion is { Length: > 0 } posted)
        {
            _db.Entry(wh).Property(w => w.RowVersion).OriginalValue = posted;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            TempData["Error"] = "تعذّر حفظ التعديل لأن المستودع عُدّل في جلسة أخرى. أعد فتح الصفحة وحاول مجددًا.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = "تم تعديل المستودع بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("Warehouses.Delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var wh = await _db.Warehouses.FindAsync(id);
        if (wh == null)
        {
            return NotFound();
        }

        var hasLayers = await _db.StockLayers.AnyAsync(sl => sl.WarehouseId == id);
        if (hasLayers)
        {
            TempData["Error"] = "لا يمكن حذف المستودع لأنه يحتوي على حركات مخزون مرتبطة";
            return RedirectToAction(nameof(Index));
        }
        _db.Warehouses.Remove(wh);

        // `RowVersion` داخل شرط `DELETE`، فتزاحمُ زميلٍ بين القراءة والحذف استثناءُ تزامن.
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            TempData["Error"] = "تعذّر حذف المستودع لأنه عُدّل في جلسة أخرى. أعد فتح الصفحة وحاول مجددًا.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = "تم حذف المستودع بنجاح";
        return RedirectToAction(nameof(Index));
    }
}
