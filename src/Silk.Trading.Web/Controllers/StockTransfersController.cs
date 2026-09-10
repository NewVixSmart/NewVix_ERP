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
public class StockTransfersController : Controller
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    public StockTransfersController(AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    [RequirePerm("StockTransfers.View")]
    public async Task<IActionResult> Index(int page = 1)
    {
        page = Math.Max(1, page);
        const int pageSize = 50;
        var list = await _inventory.GetTransfersAsync();
        var total = list.Count;
        ViewBag.Page = page;
        ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        return View(list.Skip((page - 1) * pageSize).Take(pageSize));
    }

    [RequirePerm("StockTransfers.Create")]
    public async Task<IActionResult> Create()
    {
        await PopulateDropdowns();
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("StockTransfers.Create")]
    public async Task<IActionResult> Create(StockTransfer transfer, List<StockTransferItem> items)
    {
        transfer ??= new StockTransfer();
        items ??= new List<StockTransferItem>();
        items = items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
        ModelState.Clear();

        if (items.Count == 0)
        {
            ModelState.AddModelError("", "يرجى إضافة صنف واحد على الأقل");
            await PopulateDropdowns();
            return View(transfer);
        }

        var (ok, error) = await _inventory.CreateTransferAsync(transfer, items, User.Identity?.Name);
        if (ok)
        {
            TempData["Success"] = "تم تنفيذ التحويل بنجاح";
            return RedirectToAction(nameof(Index));
        }

        ModelState.AddModelError("", error ?? "تعذر حفظ التحويل");
        await PopulateDropdowns();
        return View(transfer);
    }

    private async Task PopulateDropdowns()
    {
        var warehouses = await _db.Warehouses.Where(w => w.IsActive).AsNoTracking().ToListAsync();
        ViewBag.Warehouses = new SelectList(warehouses, "Id", "Name");
        ViewBag.ItemsData = await _db.Items.Include(i => i.CountUnit).Include(i => i.QuantityUnit)
            .Where(i => i.IsActive).AsNoTracking().ToListAsync();
    }
}
