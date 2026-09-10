using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Extensions;
using Silk.Trading.Web.Models.Purchases;
using Silk.Trading.Web.Services;

namespace Silk.Trading.Web.Controllers;

[Authorize]
public class PurchaseRequestsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IProcurementService _procurement;

    public PurchaseRequestsController(AppDbContext db, IProcurementService procurement)
    {
        _db = db;
        _procurement = procurement;
    }

    [RequirePerm("PurchaseRequests.View")]
    public async Task<IActionResult> Index()
    {
        var quotes = await _procurement.GetSupplierQuotesAsync();
        return View(quotes);
    }

    [RequirePerm("PurchaseRequests.Create")]
    public async Task<IActionResult> Create()
    {
        ViewBag.Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        ViewBag.ItemsData = await _db.Items.Where(i => i.IsActive).AsNoTracking().ToListAsync();
        return View(new SupplierQuote());
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("PurchaseRequests.Create")]
    public async Task<IActionResult> Create(SupplierQuote quote)
    {
        if (quote.SupplierId <= 0 || quote.ItemId <= 0)
        {
            ModelState.AddModelError("", "يرجى اختيار المورد والصنف");
        }
        if (ModelState.IsValid)
        {
            var (ok, error) = await _procurement.SaveSupplierQuoteAsync(quote);
            if (ok)
            {
                TempData["Success"] = "تم تسجيل عرض السعر";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError("", error ?? "تعذر حفظ عرض السعر");
        }
        ViewBag.Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        ViewBag.ItemsData = await _db.Items.Where(i => i.IsActive).AsNoTracking().ToListAsync();
        return View(quote);
    }

    [RequirePerm("PurchaseRequests.View")]
    public async Task<IActionResult> Remove(int id)
    {
        var quote = await _db.SupplierQuotes.FindAsync(id);
        if (quote == null) return NotFound();
        _db.SupplierQuotes.Remove(quote);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم حذف عرض السعر";
        return RedirectToAction(nameof(Index));
    }
}
