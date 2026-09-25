using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.Extensions;

namespace NewVixSmart.Web.Controllers;

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
    public async Task<IActionResult> Index()
    {
        var list = await _db.SaleReturns
            .Include(r => r.Customer)
            .AsNoTracking()
            .OrderByDescending(r => r.ReturnDate)
            .Take(500)
            .ToListAsync();
        return View(list);
    }

[RequirePerm("SaleReturns.Create")]
public async Task<IActionResult> Create()
    {
        await PopulateDropdowns();
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

        var baseCurrencyId = await BaseCurrencyIdAsync();
        if (!saleReturn.CurrencyId.HasValue)
        {
            saleReturn.CurrencyId = baseCurrencyId;
            saleReturn.ExchangeRate = baseCurrencyId == null ? null : 1m;
            ModelState.Remove("CurrencyId");
            ModelState.Remove("ExchangeRate");
        }
        if (saleReturn.CurrencyId.HasValue && saleReturn.CurrencyId.Value != baseCurrencyId
            && (!saleReturn.ExchangeRate.HasValue || saleReturn.ExchangeRate.Value <= 0))
        {
            ModelState.AddModelError("", "سعر الصرف يجب أن يكون أكبر من صفر للمرتجعات بالعملة الأجنبية");
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
                if (invoice.CurrencyId.HasValue && saleReturn.SaleInvoiceId != null)
                {
                    saleReturn.CurrencyId = invoice.CurrencyId;
                    saleReturn.ExchangeRate = invoice.ExchangeRate ?? 1m;
                    ModelState.Remove("CurrencyId");
                    ModelState.Remove("ExchangeRate");
                }
                var alreadyReturned = await _db.SaleReturnItems.Where(r => r.SaleReturn.SaleInvoiceId == invoice.Id && r.SaleReturnId != saleReturn.Id && r.SaleReturn.Status == ReturnStatus.Posted).ToListAsync();
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
        var prefix = $"SRTN-{DateTime.Now:yyyyMMdd}-";
        var taken = await _db.SaleReturns.AsNoTracking().Where(r => r.ReturnNumber.StartsWith(prefix)).Select(r => r.ReturnNumber).ToListAsync();
        var next = (taken.Count > 0 ? taken.Select(n => int.TryParse(n.AsSpan(prefix.Length), out var v) ? v : 0).Max() : 0) + 1;
        saleReturn.ReturnNumber = $"{prefix}{next:D3}";
        ModelState.Remove("ReturnNumber");
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
    public async Task<IActionResult> Details(string id)
    {
        var query = _db.SaleReturns
            .Include(r => r.Customer)
            .Include(r => r.SaleInvoice)
            .Include(r => r.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
.Include(r => r.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking();

        SaleReturn? saleReturn;
        if (Guid.TryParse(id, out var publicId))
            saleReturn = await query.FirstOrDefaultAsync(r => r.PublicId == publicId);
        else if (int.TryParse(id, out var numericId))
            saleReturn = await query.FirstOrDefaultAsync(r => r.Id == numericId);
        else
            return NotFound();

        if (saleReturn == null) return NotFound();
        return View(saleReturn);
    }

    [RequirePerm("SaleReturns.View")]
    public async Task<IActionResult> Print(int id)
    {
        var saleReturn = await _db.SaleReturns
            .Include(r => r.Customer)
            .Include(r => r.SaleInvoice)
            .Include(r => r.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
.Include(r => r.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);
        if (saleReturn == null) return NotFound();
        return View(saleReturn);
    }

    [RequirePerm("SaleReturns.View")]
    public async Task<IActionResult> Pdf(int id)
    {
        var saleReturn = await _db.SaleReturns
            .Include(r => r.Customer)
            .Include(r => r.SaleInvoice)
            .Include(r => r.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
.Include(r => r.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);
        if (saleReturn == null) return NotFound();
        var bytes = PrintPdfBuilder.RenderSaleReturnPdf(saleReturn);
        return File(bytes, "application/pdf", $"sale-return-{saleReturn.ReturnNumber}.pdf");
    }

private async Task PopulateDropdowns()
    {
        ViewBag.Customers = new SelectList(await _db.Customers.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        ViewBag.Items = await _db.Items.Where(i => i.IsActive).Where(i => i.IsSellable).AsNoTracking().ToListAsync();
ViewBag.SaleInvoices = new SelectList(await _db.SaleInvoices.AsNoTracking().OrderByDescending(s => s.Id).Take(200).ToListAsync(), "Id", "InvoiceNumber");
        var prefix = $"SRTN-{DateTime.Now:yyyyMMdd}-";
        var taken = await _db.SaleReturns.AsNoTracking().Where(r => r.ReturnNumber.StartsWith(prefix)).Select(r => r.ReturnNumber).ToListAsync();
        var next = (taken.Count > 0 ? taken.Select(n => int.TryParse(n.AsSpan(prefix.Length), out var v) ? v : 0).Max() : 0) + 1;
        ViewBag.NextNumber = $"{prefix}{next:D3}";
    }

    private async Task<int?> BaseCurrencyIdAsync()
        => await _db.Currencies.AsNoTracking().Where(c => c.IsActive).OrderByDescending(c => c.IsBase)
            .Select(c => (int?)c.Id).FirstOrDefaultAsync();
}
