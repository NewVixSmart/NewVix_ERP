using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.ViewModels.Core;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class ItemsController : Controller
{
    private readonly AppDbContext _db;

    public ItemsController(AppDbContext db) => _db = db;

    [RequirePerm("Items.View")]
    public async Task<IActionResult> Index(int? categoryId)
    {
        var query = _db.Items.Include(i => i.Category).Include(i => i.CountUnit).Include(i => i.QuantityUnit).Include(i => i.ItemType).AsNoTracking().AsQueryable();

        if (categoryId.HasValue)
            query = query.Where(i => i.CategoryId == categoryId.Value);

        ViewBag.CategoryId = categoryId;

        var vm = new ItemListViewModel
        {
            Items = await query.Where(i => i.IsActive).OrderBy(i => i.Name).Take(500).ToListAsync(),
            Categories = await _db.ItemCategories.Where(c => c.IsActive).AsNoTracking().ToListAsync(),
            CategoryId = categoryId
        };
        return View(vm);
    }

    [RequirePerm("Items.Create")]
    public async Task<IActionResult> Create()
    {
        await PopulateDropdowns();
        return View(new Item { IsActive = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Items.Create")]
    public async Task<IActionResult> Create(Item item)
    {
        if (!User.IsInRole("Admin"))
        {
            item.PurchasePrice = 0;
            item.SalePrice = 0;
        }

        if (!string.IsNullOrWhiteSpace(item.Code) && await _db.Items.AnyAsync(i => i.Code == item.Code))
            ModelState.AddModelError(nameof(Item.Code), "الكود مستخدم بالفعل لصنف آخر");

        if (ModelState.IsValid)
        {
            try
            {
                item.CreatedAt = DateTime.UtcNow;
                item.CurrentCount = 0;
                item.CurrentQuantity = 0;
                if (string.IsNullOrWhiteSpace(item.Barcode))
                    item.Barcode = string.IsNullOrWhiteSpace(item.Code) ? $"ITM{item.Id:D8}" : item.Code;
                _db.Items.Add(item);
                await _db.SaveChangesAsync();
                if (item.Barcode!.StartsWith("ITM"))
                    item.Barcode = $"ITM{item.Id:D8}";
                await _db.SaveChangesAsync();
                TempData["Success"] = "تم إضافة الصنف بنجاح";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException)
            {
                ModelState.AddModelError("", "تعذر الحفظ: تأكد من عدم تكرار كود الصنف");
            }
        }
        await PopulateDropdowns();
        return View(item);
    }

    [RequirePerm("Items.Edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        if (item == null) return NotFound();
        await PopulateDropdowns();
        return View(item);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Items.Edit")]
    public async Task<IActionResult> Edit(int id, Item item)
    {
        if (id != item.Id) return NotFound();
        if (ModelState.IsValid)
        {
            var existing = await _db.Items.FirstOrDefaultAsync(i => i.Id == id);
            if (existing == null) return NotFound();

            existing.Code = item.Code;
            existing.Name = item.Name;
            existing.Barcode = string.IsNullOrWhiteSpace(item.Barcode) ? (existing.Barcode ?? existing.Code ?? $"ITM{existing.Id:D8}") : item.Barcode;
            existing.ItemTypeId = item.ItemTypeId;
            existing.IsSellable = item.IsSellable;
            existing.CategoryId = item.CategoryId;
            existing.Notes = item.Notes;
            existing.CountUnitId = item.CountUnitId;
            existing.QuantityUnitId = item.QuantityUnitId;
            existing.MinCount = item.MinCount;
            existing.MinQuantity = item.MinQuantity;
            if (User.IsInRole("Admin"))
            {
                existing.PurchasePrice = item.PurchasePrice;
                existing.SalePrice = item.SalePrice;
            }
            existing.IsActive = item.IsActive;

            if (!string.IsNullOrWhiteSpace(existing.Code) && await _db.Items.AnyAsync(i => i.Id != id && i.Code == existing.Code))
                ModelState.AddModelError(nameof(Item.Code), "الكود مستخدم بالفعل لصنف آخر");

            if (ModelState.IsValid)
            {
                try
                {
                    if (item.RowVersion != null)
                        _db.Entry(existing).Property(i => i.RowVersion).OriginalValue = item.RowVersion;
                    await _db.SaveChangesAsync();
                    TempData["Success"] = "تم تعديل الصنف بنجاح";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    TempData["Error"] = "تغير الصنف بواسطة عملية أخرى أثناء التعديل؛ أعد المحاولة";
                    return RedirectToAction(nameof(Edit), new { id });
                }
                catch (DbUpdateException)
                {
                    ModelState.AddModelError("", "تعذر الحفظ: تأكد من عدم تكرار كود الصنف");
                }
            }
        }
        await PopulateDropdowns();
        return View(item);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Items.Delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _db.Items.FindAsync(id);
        if (item == null) return NotFound();

        item.IsActive = false;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم حذف الصنف بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [RequirePerm("Items.View")]
    public async Task<IActionResult> Details(string id)
    {
        var query = _db.Items.Include(i => i.Category).Include(i => i.CountUnit).Include(i => i.QuantityUnit).Include(i => i.ItemType);

        Item? item;
        if (Guid.TryParse(id, out var publicId))
            item = await query.FirstOrDefaultAsync(i => i.PublicId == publicId);
        else if (int.TryParse(id, out var numericId))
            item = await query.FirstOrDefaultAsync(i => i.Id == numericId);
        else
            return NotFound();

        if (item == null) return NotFound();
        return View(item);
    }

    [RequirePerm("Items.View")]
    public async Task<IActionResult> PrintLabel(int id)
    {
        var item = await _db.Items.Include(i => i.Category).Include(i => i.CountUnit).Include(i => i.QuantityUnit).Include(i => i.ItemType).FirstOrDefaultAsync(i => i.Id == id);
        if (item == null) return NotFound();
        if (string.IsNullOrWhiteSpace(item.Barcode))
        {
            item.Barcode = string.IsNullOrWhiteSpace(item.Code) ? $"ITM{item.Id:D8}" : item.Code;
            _db.Items.Update(item);
            await _db.SaveChangesAsync();
        }
        return View(item);
    }

    private async Task PopulateDropdowns()
    {
        ViewBag.IsAdmin = User.IsInRole("Admin");
        ViewBag.Categories = new SelectList(await _db.ItemCategories.Where(c => c.IsActive).ToListAsync(), "Id", "Name");
        ViewBag.ItemTypes = new SelectList(await _db.ItemTypes.Where(t => t.IsActive).ToListAsync(), "Id", "Name");
        ViewBag.CountUnits = new SelectList(await _db.Units.Where(u => u.IsActive).ToListAsync(), "Id", "Name");
        ViewBag.QuantityUnits = new SelectList(await _db.Units.Where(u => u.IsActive).ToListAsync(), "Id", "Name");
    }
}
