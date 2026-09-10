using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Sales;
using Silk.Trading.Web.Services;
using Silk.Trading.Web.Extensions;

namespace Silk.Trading.Web.Controllers;

[Authorize]
public class SaleReturnsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    public SaleReturnsController(AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

[RequirePerm("SaleReturns.View")]
    public async Task<IActionResult> Index(int page = 1)
    {
        page = Math.Max(1, page);
        const int pageSize = 50;
        var total = await _db.SaleReturns.CountAsync();
        ViewBag.Page = page;
        ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var list = await _db.SaleReturns
            .Include(r => r.Customer)
            .AsNoTracking()
            .OrderByDescending(r => r.ReturnDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return View(list);
    }

[RequirePerm("SaleReturns.Create")]
    public async Task<IActionResult> Create()
    {
        await PopulateDropdowns();
        var next = await _db.SaleReturns.AsNoTracking().CountAsync() + 1;
        ViewBag.NextNumber = $"SRTN-{DateTime.Now:yyyyMMdd}-{next:D3}";
        return View();
    }

[HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("SaleReturns.Create")]
    public async Task<IActionResult> Create(SaleReturn saleReturn, List<SaleReturnItem> items)
    {
        items = items?.Where(i => i.ItemId > 0 && (i.Count != 0 || i.Quantity != 0)).ToList() ?? new List<SaleReturnItem>();
        ModelState.IgnoreEmptyLineItemRows();
        if (items.Count == 0)
        {
            ModelState.AddModelError("", "يرجى إضافة صنف واحد على الأقل بالعدد أو الكمية");
        }

        if (saleReturn.SaleInvoiceId != null)
        {
            var invoice = await _db.SaleInvoices.Include(i => i.Items).AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == saleReturn.SaleInvoiceId.Value);
            if (invoice == null)
            {
                ModelState.AddModelError("SaleInvoiceId", "الفاتورة الأصلية غير موجودة");
            }
            else if (invoice.CustomerId != saleReturn.CustomerId)
            {
                ModelState.AddModelError("SaleInvoiceId", "الفاتورة الأصلية لا تخص هذا العميل");
            }
            else
            {
                var alreadyReturned = await _db.SaleReturnItems.Where(r => r.SaleReturn.SaleInvoiceId == invoice.Id && r.SaleReturnId != saleReturn.Id).ToListAsync();
                foreach (var line in items)
                {
                    var invLine = invoice.Items.FirstOrDefault(i => i.ItemId == line.ItemId);
                    if (invLine == null)
                    {
                        ModelState.AddModelError("", $"الصنف «{line.ItemId}» غير موجود في الفاتورة الأصلية");
                        continue;
                    }
                    decimal returnedCount = alreadyReturned.Where(r => r.ItemId == line.ItemId).Sum(r => r.Count);
                    decimal returnedQty = alreadyReturned.Where(r => r.ItemId == line.ItemId).Sum(r => r.Quantity);
                    if (line.Count + returnedCount > invLine.Count || line.Quantity + returnedQty > invLine.Quantity)
                    {
                        ModelState.AddModelError("", "الكمية المرتجعة أكبر من الكمية المباعة في الفاتورة الأصلية");
                    }
                }
            }
        }

if (ModelState.IsValid)
        {
            var (ok, error, returnId) = await _inventory.CreateSaleReturnDraftAsync(saleReturn, items, User.Identity?.Name);
            if (ok)
            {
                var submit = Request.Form["submitAction"].ToString();
                if (submit == "post")
                {
                    var (posted, postError) = await _inventory.PostSaleReturnAsync(returnId, User.Identity?.Name);
                    if (posted)
                    {
                        TempData["Success"] = "تم ترحيل مرتجع البيع وإرجاع الكمية للمخزون";
                        return RedirectToAction(nameof(Index));
                    }
                    TempData["Error"] = postError ?? "تعذر ترحيل مرتجع البيع";
                    return RedirectToAction(nameof(Details), new { id = returnId });
                }
                TempData["Success"] = "تم حفظ مرتجع البيع كمسودة";
                return RedirectToAction(nameof(Details), new { id = returnId });
            }
            ModelState.AddModelError("", error ?? "تعذر حفظ مرتجع البيع");
        }

        await PopulateDropdowns();
        return View(saleReturn);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("SaleReturns.Post")]
    public async Task<IActionResult> Post(int id)
    {
        var (ok, error) = await _inventory.PostSaleReturnAsync(id, User.Identity?.Name);
        if (ok)
        {
            TempData["Success"] = "تم ترحيل مرتجع البيع وإرجاع الكمية للمخزون";
            return RedirectToAction(nameof(Details), new { id });
        }
        TempData["Error"] = error ?? "تعذر ترحيل مرتجع البيع";
        return RedirectToAction(nameof(Details), new { id });
    }

    [RequirePerm("SaleReturns.View")]
    public async Task<IActionResult> Details(int id)
    {
        var saleReturn = await _db.SaleReturns
            .Include(r => r.Customer)
            .Include(r => r.SaleInvoice)
            .Include(r => r.Items).ThenInclude(i => i.Item)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);
        if (saleReturn == null) return NotFound();
        return View(saleReturn);
    }

    private async Task PopulateDropdowns()
    {
        ViewBag.Customers = new SelectList(await _db.Customers.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        ViewBag.Items = await _db.Items.Where(i => i.IsActive).Where(i => i.IsSellable).AsNoTracking().ToListAsync();
        ViewBag.SaleInvoices = new SelectList(await _db.SaleInvoices.AsNoTracking().OrderByDescending(s => s.Id).Take(200).ToListAsync(), "Id", "InvoiceNumber");
    }
}
