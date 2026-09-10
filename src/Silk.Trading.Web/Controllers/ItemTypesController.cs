using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Extensions;
using Silk.Trading.Web.Models.Core;

namespace Silk.Trading.Web.Controllers;

[Authorize]
public class ItemTypesController : Controller
{
    private readonly AppDbContext _db;
    public ItemTypesController(AppDbContext db) => _db = db;

    [RequirePerm("Items.View")]
    public async Task<IActionResult> Index(int page = 1)
    {
        page = Math.Max(1, page);
        var query = _db.ItemTypes.OrderBy(t => t.Name).AsQueryable();
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return PartialView(await query.ToListAsync());

        const int pageSize = 50;
        var total = await query.CountAsync();
        ViewBag.Page = page;
        ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var model = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Items.Create")]
    public async Task<IActionResult> Create(ItemType itemType)
    {
        if (ModelState.IsValid)
        {
            var name = itemType.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return BadRequest("يرجى إدخال اسم النوع");
            if (await _db.ItemTypes.AnyAsync(t => t.Name == name))
                return BadRequest("نوع الصنف بهذا الاسم موجود بالفعل");

            itemType.Name = name;
            _db.ItemTypes.Add(itemType);
            await _db.SaveChangesAsync();
            TempData["Success"] = "تم إضافة نوع الصنف بنجاح";
            return Ok(new { id = itemType.Id, name = itemType.Name });
        }
        return BadRequest("يرجى إدخال اسم النوع");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Items.Edit")]
    public async Task<IActionResult> Edit(ItemType itemType)
    {
        var existing = await _db.ItemTypes.FindAsync(itemType.Id);
        if (existing == null) return NotFound();
        if (!string.IsNullOrWhiteSpace(itemType.Name))
        {
            var name = itemType.Name.Trim();
            if (await _db.ItemTypes.AnyAsync(t => t.Id != itemType.Id && t.Name == name))
                return BadRequest("نوع الصنف بهذا الاسم موجود بالفعل");
            existing.Name = name;
        }
        if (itemType.Notes != null)
            existing.Notes = itemType.Notes;
        if (itemType.IsActive != existing.IsActive)
            existing.IsActive = itemType.IsActive;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم تعديل نوع الصنف بنجاح";
        return Ok(new { id = existing.Id, name = existing.Name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Items.Delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var itemType = await _db.ItemTypes.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id);
        if (itemType == null) return NotFound();
        if (itemType.Items.Any())
            return BadRequest("لا يمكن حذف نوع يحتوي على أصناف");

        _db.ItemTypes.Remove(itemType);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم حذف نوع الصنف بنجاح";
        return Ok();
    }
}
