using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Extensions;
using Silk.Trading.Web.Models.Core;

namespace Silk.Trading.Web.Controllers;

[Authorize]
public class CategoriesController : Controller
{
    private readonly AppDbContext _db;
    public CategoriesController(AppDbContext db) => _db = db;

    [RequirePerm("Items.View")]
    public async Task<IActionResult> Index()
    {
        var query = _db.ItemCategories.OrderBy(c => c.Name).AsQueryable();
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return PartialView(await query.ToListAsync());

        var model = await query.ToListAsync();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Items.Create")]
    public async Task<IActionResult> Create(ItemCategory category)
    {
        if (ModelState.IsValid)
        {
            var name = category.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return BadRequest("يرجى إدخال اسم التصنيف");
            if (await _db.ItemCategories.AnyAsync(c => c.Name == name))
                return BadRequest("التصنيف بهذا الاسم موجود بالفعل");

            category.Name = name;
            _db.ItemCategories.Add(category);
            await _db.SaveChangesAsync();
            TempData["Success"] = "تم إضافة التصنيف بنجاح";
            return Ok(new { id = category.Id, name = category.Name });
        }
        return BadRequest("يرجى إدخال اسم التصنيف");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Items.Edit")]
    public async Task<IActionResult> Edit(ItemCategory category)
    {
        var existing = await _db.ItemCategories.FindAsync(category.Id);
        if (existing == null) return NotFound();
        if (!string.IsNullOrWhiteSpace(category.Name))
        {
            var name = category.Name.Trim();
            if (await _db.ItemCategories.AnyAsync(c => c.Id != category.Id && c.Name == name))
                return BadRequest("التصنيف بهذا الاسم موجود بالفعل");
            existing.Name = name;
        }
        if (category.Notes != null)
            existing.Notes = category.Notes;
        if (category.IsActive != existing.IsActive)
            existing.IsActive = category.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم تعديل التصنيف بنجاح";
        return Ok(new { id = existing.Id, name = existing.Name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Items.Delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await _db.ItemCategories.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == id);
        if (category == null) return NotFound();
        if (category.Items.Any())
            return BadRequest("لا يمكن حذف تصنيف يحتوي على أصناف");

        _db.ItemCategories.Remove(category);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم حذف التصنيف بنجاح";
        return Ok();
    }
}
