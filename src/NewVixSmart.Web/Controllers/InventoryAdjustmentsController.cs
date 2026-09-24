using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class InventoryAdjustmentsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    public InventoryAdjustmentsController(AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    [RequirePerm("InventoryAdjustments.View")]
    public async Task<IActionResult> Index()
    {
        var query = _db.InventoryAdjustments.Include(a => a.Item).AsNoTracking();

        var list = await query
            .OrderByDescending(a => a.AdjustmentDate)
            .Take(500)
            .ToListAsync();
        return View(list);
    }

    [RequirePerm("InventoryAdjustments.Create")]
    public async Task<IActionResult> Create()
    {
        await PopulateDropdowns();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("InventoryAdjustments.Create")]
    public async Task<IActionResult> Create(InventoryAdjustment adjustment)
    {
        if (ModelState.IsValid)
        {
            var (ok, error) = await _inventory.CreateAdjustmentAsync(adjustment, User.Identity?.Name);
            if (ok)
            {
                TempData["Success"] = "تم تسجيل الجرد وتعديل المخزون بنجاح";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError(error?.StartsWith("الصنف") == true ? "ItemId" : string.Empty, error ?? "تعذر حفظ الجرد");
        }
        await PopulateDropdowns();
        return View(adjustment);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("InventoryAdjustments.Delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var (ok, error) = await _inventory.DeleteAdjustmentAsync(id, User.Identity?.Name);
        if (ok)
            TempData["Success"] = "تم حذف سجل الجرد وإعادة المخزون إلى حالته السابقة";
        else
            TempData["Error"] = error ?? "تعذر حذف سجل الجرد";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDropdowns()
    {
        ViewBag.Items = new SelectList(await _db.Items.Where(i => i.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        ViewBag.ItemsData = await _db.Items.Include(i => i.CountUnit).Include(i => i.QuantityUnit).Where(i => i.IsActive).AsNoTracking().ToListAsync();
        var next = await _db.InventoryAdjustments.AsNoTracking().CountAsync();
        ViewBag.NextNumber = $"ADJ-{DateTime.Now:yyyyMMdd}-{next + 1:D3}";
    }
}
