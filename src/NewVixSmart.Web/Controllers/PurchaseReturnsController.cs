using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Forms;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class PurchaseReturnsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IPermissionService _permissions;
    public PurchaseReturnsController(AppDbContext db, IInventoryService inventory, IPermissionService permissions)
    {
        _db = db;
        _inventory = inventory;
        _permissions = permissions;
    }

    [RequirePerm("PurchaseReturns.View")]
    public async Task<IActionResult> Index(int page = 1, string? search = null)
    {
        var query = _db.PurchaseReturns
            .Include(r => r.Supplier)
            .AsNoTracking()
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(r => r.ReturnNumber.Contains(term) || r.Supplier.Name.Contains(term));
        }
        query = query.OrderByDescending(r => r.ReturnDate).ThenByDescending(r => r.Id);

        var total = await query.CountAsync();
        page = PagerExtensions.NormalizePage(page, total);
        var list = await query
            .Skip((page - 1) * PagerExtensions.PageSize)
            .Take(PagerExtensions.PageSize)
            .ToListAsync();
        this.SetPager(page, total, list.Count, search);
        return View(list);
    }

    [RequirePerm("PurchaseReturns.Create")]
    public async Task<IActionResult> Create()
    {
        await PopulateDropdowns();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("PurchaseReturns.Create")]
    public async Task<IActionResult> Create(PurchaseReturnFormModel purchaseReturn, List<PurchaseReturnLineFormModel> items)
    {
        items = items?.Where(i => i.ItemId > 0 && (i.Count != 0 || i.Quantity != 0)).ToList() ?? new List<PurchaseReturnLineFormModel>();
        ModelState.IgnoreEmptyLineItemRows();
        if (items.Count == 0)
        {
            ModelState.AddModelError("", "يرجى إضافة صنف واحد على الأقل بالعدد أو الكمية");
        }

        if (purchaseReturn.PurchaseInvoiceId != null)
        {
            var invoice = await _db.PurchaseInvoices.Include(i => i.Items).AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == purchaseReturn.PurchaseInvoiceId.Value);
            if (invoice == null)
            {
                ModelState.AddModelError("PurchaseInvoiceId", "الفاتورة الأصلية غير موجودة");
            }
            else if (invoice.SupplierId != purchaseReturn.SupplierId)
            {
                ModelState.AddModelError("PurchaseInvoiceId", "الفاتورة الأصلية لا تخص هذا المورد");
            }
            else
            {
                var alreadyReturned = await _db.PurchaseReturnItems.Where(r => r.PurchaseReturn.PurchaseInvoiceId == invoice.Id && r.PurchaseReturn.Status == ReturnStatus.Posted).ToListAsync();
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
                        ModelState.AddModelError("", "الكمية المرتجعة أكبر من الكمية المشتراة في الفاتورة الأصلية");
                    }
                }
            }
        }

        if (ModelState.IsValid)
        {
            var submit = Request.Form["submitAction"].ToString();
            if (submit == "post" && !await _permissions.HasAsync("PurchaseReturns.Post"))
            {
                ModelState.AddModelError("", "ليست لديك صلاحية الترحيل المباشر؛ يمكنك الحفظ كمسودة فقط");
            }
            else
            {
                var (ok, error, returnId) = await _inventory.CreatePurchaseReturnDraftAsync(purchaseReturn.ToEntity(), items.Select(i => i.ToEntity()).ToList(), User.Identity?.Name);
                if (ok)
                {
                    if (submit == "post")
                    {
                        var (posted, postError) = await _inventory.PostPurchaseReturnAsync(returnId, User.Identity?.Name);
                        if (posted)
                        {
                            TempData["Success"] = "تم ترحيل مرتجع الشراء وخصم الكمية من المخزون";
                            return RedirectToAction(nameof(Index));
                        }
                        TempData["Error"] = postError ?? "تعذر ترحيل مرتجع الشراء";
                        return await RedirectToDetailsAsync(returnId);
                    }
                    TempData["Success"] = "تم حفظ مرتجع الشراء كمسودة";
                    return await RedirectToDetailsAsync(returnId);
                }
                ModelState.AddModelError("", error ?? "تعذر حفظ مرتجع الشراء");
            }
        }

        await PopulateDropdowns();
        var prefix = $"PRTN-{DateTime.Now:yyyyMMdd}-";
        var taken = await _db.PurchaseReturns.AsNoTracking().Where(r => r.ReturnNumber.StartsWith(prefix)).Select(r => r.ReturnNumber).ToListAsync();
        var next = (taken.Count > 0 ? taken.Select(n => int.TryParse(n.AsSpan(prefix.Length), out var v) ? v : 0).Max() : 0) + 1;
        purchaseReturn.ReturnNumber = $"{prefix}{next:D3}";
        return View(purchaseReturn);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("PurchaseReturns.Post")]
    public async Task<IActionResult> Post(int id)
    {
        var (ok, error) = await _inventory.PostPurchaseReturnAsync(id, User.Identity?.Name);
        if (ok)
        {
            TempData["Success"] = "تم ترحيل مرتجع الشراء وخصم الكمية من المخزون";
            return await RedirectToDetailsAsync(id);
        }
        TempData["Error"] = error ?? "تعذر ترحيل مرتجع الشراء";
        return await RedirectToDetailsAsync(id);
    }

    /// <summary>
    /// Details only resolves the public id now, so internal redirects translate the numeric id
    /// they were handed instead of passing it straight through.
    /// </summary>
    private async Task<IActionResult> RedirectToDetailsAsync(int id)
    {
        var publicId = await _db.PurchaseReturns.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => (Guid?)r.PublicId)
            .FirstOrDefaultAsync();
        if (publicId == null)
        {
            return RedirectToAction(nameof(Index));
        }

        return RedirectToAction(nameof(Details), new { id = publicId.Value });
    }

    [RequirePerm("PurchaseReturns.View")]
    public async Task<IActionResult> Details(string id)
    {
        var query = _db.PurchaseReturns
            .Include(r => r.Supplier)
            .Include(r => r.PurchaseInvoice)
            .Include(r => r.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
.Include(r => r.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking();

        PurchaseReturn? purchaseReturn;
        if (!Guid.TryParse(id, out var publicId))
        {
            return NotFound();
        }

        purchaseReturn = await query.FirstOrDefaultAsync(r => r.PublicId == publicId);

        if (purchaseReturn == null)
        {
            return NotFound();
        }

        return View(purchaseReturn);
    }

    [RequirePerm("PurchaseReturns.View")]
    public async Task<IActionResult> Print(int id)
    {
        var purchaseReturn = await _db.PurchaseReturns
            .Include(r => r.Supplier)
            .Include(r => r.PurchaseInvoice)
            .Include(r => r.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
.Include(r => r.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);
        if (purchaseReturn == null)
        {
            return NotFound();
        }

        return View(purchaseReturn);
    }

    [RequirePerm("PurchaseReturns.View")]
    public async Task<IActionResult> Pdf(int id)
    {
        var purchaseReturn = await _db.PurchaseReturns
            .Include(r => r.Supplier)
            .Include(r => r.PurchaseInvoice)
            .Include(r => r.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
.Include(r => r.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);
        if (purchaseReturn == null)
        {
            return NotFound();
        }

        var bytes = PrintPdfBuilder.RenderPurchaseReturnPdf(purchaseReturn);
        return File(bytes, "application/pdf", $"purchase-return-{purchaseReturn.ReturnNumber}.pdf");
    }

    private async Task PopulateDropdowns()
    {
        ViewBag.Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        ViewBag.Items = await _db.Items.Where(i => i.IsActive).AsNoTracking().ToListAsync();
        ViewBag.PurchaseInvoices = new SelectList(await _db.PurchaseInvoices.AsNoTracking().OrderByDescending(s => s.Id).Take(200).ToListAsync(), "Id", "InvoiceNumber");
        var prefix = $"PRTN-{DateTime.Now:yyyyMMdd}-";
        var taken = await _db.PurchaseReturns.AsNoTracking().Where(r => r.ReturnNumber.StartsWith(prefix)).Select(r => r.ReturnNumber).ToListAsync();
        var next = (taken.Count > 0 ? taken.Select(n => int.TryParse(n.AsSpan(prefix.Length), out var v) ? v : 0).Max() : 0) + 1;
        ViewBag.NextNumber = $"{prefix}{next:D3}";
    }
}
