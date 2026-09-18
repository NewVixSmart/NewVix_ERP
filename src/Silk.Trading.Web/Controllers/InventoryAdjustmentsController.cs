using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Extensions;
using Silk.Trading.Web.Models.Stock;
using Silk.Trading.Web.Services;

namespace Silk.Trading.Web.Controllers;

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
            .ToListAsync();
        return View(list);
    }

    [RequirePerm("InventoryAdjustments.Create")]
    public async Task<IActionResult> Create()
    {
        await PopulateDropdowns();
        var next = await _db.InventoryAdjustments.AsNoTracking().CountAsync();
        ViewBag.NextNumber = $"ADJ-{DateTime.Now:yyyyMMdd}-{next + 1:D3}";
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
        var adj = await _db.InventoryAdjustments.FindAsync(id);
        if (adj == null) return NotFound();

        if (await _db.JournalEntries.AnyAsync(j => j.Source == Silk.Trading.Web.Models.Accounting.JournalSource.OpeningStock && j.SourceId == adj.ItemId))
        {
            TempData["Error"] = "لا يمكن حذف هذا الجرد لأن بياناته رُحّلت إلى قيود اليومية؛ اضبط المخزون بجرد جديد بدلاً من ذلك";
            return RedirectToAction(nameof(Index));
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        var stock = await _db.StockMovements.FirstOrDefaultAsync(s => s.DocumentType == DocumentType.Adjustment && s.DocumentNumber == adj.ReferenceNumber);
        if (stock != null)
        {
            var hasLaterMovements = await _db.StockMovements.AnyAsync(m => m.ItemId == stock.ItemId && m.Id > stock.Id);
            if (hasLaterMovements)
            {
                await tx.RollbackAsync();
                TempData["Error"] = "لا يمكن حذف هذا الجرد لأن حركات مخزون لاحقة تمت على نفس الصنف؛ اضبط المخزون بجرد جديد بدلاً من ذلك";
                return RedirectToAction(nameof(Index));
            }
            var item = await _db.Items.FindAsync(stock.ItemId);
            if (item != null)
            {
                item.CurrentCount = stock.CountBefore;
                item.CurrentQuantity = stock.BalanceBefore;
            }
            _db.StockMovements.Remove(stock);
        }

        _db.InventoryAdjustments.Remove(adj);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        TempData["Success"] = "تم حذف سجل الجرد وإعادة المخزون إلى حالته السابقة";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDropdowns()
    {
        ViewBag.Items = new SelectList(await _db.Items.Where(i => i.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        ViewBag.ItemsData = await _db.Items.Include(i => i.CountUnit).Include(i => i.QuantityUnit).Where(i => i.IsActive).AsNoTracking().ToListAsync();
    }
}
