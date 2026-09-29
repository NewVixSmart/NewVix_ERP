using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Sales;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class SalesQuotesController : Controller
{
    private readonly AppDbContext _db;
    private readonly ISalesQuotesService _quotes;

    public SalesQuotesController(AppDbContext db, ISalesQuotesService quotes)
    {
        _db = db;
        _quotes = quotes;
    }

    [RequirePerm("SalesQuotes.View")]
    public async Task<IActionResult> Index()
    {
        var query = _db.SaleQuotes.Include(q => q.Customer).AsNoTracking().OrderByDescending(q => q.QuoteDate);
        var list = await query.ToListAsync();
        return View(list);
    }

    [RequirePerm("SalesQuotes.Create")]
    public async Task<IActionResult> Create()
    {
        var vm = await PopulateDropdowns(new SaleQuoteViewModel
        {
            Quote = new SaleQuote
            {
                QuoteNumber = await NextNumberPreviewAsync(),
                QuoteDate = DateTime.Today,
                ValidUntil = DateTime.Today.AddDays(7),
            }
        });
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("SalesQuotes.Create")]
    public async Task<IActionResult> Create(SaleQuoteViewModel vm)
    {
        vm.Quote ??= new SaleQuote();
        var items = (vm.Items ?? new List<SaleQuoteItem>())
            .Where(i => i.ItemId > 0 && (i.Count != 0 || i.Quantity != 0)).ToList();
        ModelState.IgnoreEmptyLineItemRows();

        if (items.Count == 0)
        {
            ModelState.AddModelError("", "يرجى إضافة صنف واحد على الأقل بالعدد أو الكمية");
        }

        if (ModelState.IsValid)
        {
            int? branchId = HttpContext.Session.GetCurrentBranchId();
            var (ok, error, quote) = await _quotes.CreateAsync(vm.Quote, items, User.Identity?.Name, branchId);
            if (ok && quote != null)
            {
                TempData["Success"] = "تم حفظ عرض السعر بنجاح";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError("", error ?? "تعذر حفظ عرض السعر");
        }

        await PopulateDropdowns(vm);
        vm.Quote.QuoteNumber = await NextNumberPreviewAsync();
        ModelState.Remove("Quote.QuoteNumber");
        return View(vm);
    }

    [RequirePerm("SalesQuotes.View")]
    public async Task<IActionResult> Details(string id)
    {
        var query = _db.SaleQuotes
            .Include(q => q.Customer)
            .Include(q => q.SaleInvoice)
            .Include(q => q.SalesOrder)
            .Include(q => q.SupplierQuote).ThenInclude(sq => sq!.Supplier)
            .Include(q => q.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
.Include(q => q.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking();

        SaleQuote? quote;
        if (!Guid.TryParse(id, out var publicId))
        {
            return NotFound();
        }

        quote = await query.FirstOrDefaultAsync(q => q.PublicId == publicId);

        if (quote == null)
        {
            return NotFound();
        }

        return View(quote);
    }

    [RequirePerm("SalesQuotes.View")]
    public async Task<IActionResult> Print(int id)
    {
        var quote = await _db.SaleQuotes
            .Include(q => q.Customer)
            .Include(q => q.SupplierQuote).ThenInclude(sq => sq!.Supplier)
            .Include(q => q.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
.Include(q => q.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id);
        if (quote == null)
        {
            return NotFound();
        }

        return View(quote);
    }

    [RequirePerm("SalesQuotes.View")]
    public async Task<IActionResult> Pdf(int id)
    {
        var quote = await _db.SaleQuotes
            .Include(q => q.Customer)
            .Include(q => q.SupplierQuote).ThenInclude(sq => sq!.Supplier)
            .Include(q => q.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
.Include(q => q.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id);
        if (quote == null)
        {
            return NotFound();
        }

        var bytes = SalesQuotesService.RenderQuotePdf(quote);
        return File(bytes, "application/pdf", $"SaleQuote-{quote.QuoteNumber}.pdf");
    }

    [RequirePerm("SalesQuotes.Convert")]
    public async Task<IActionResult> MassConvert()
    {
        var drafts = await _db.SaleQuotes
            .Include(q => q.Customer)
            .AsNoTracking()
            .Where(q => q.Status == SaleQuoteStatus.Draft)
            .OrderByDescending(q => q.QuoteDate)
            .ToListAsync();
        return View(drafts);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("SalesQuotes.Convert")]
    public async Task<IActionResult> MassConvert(int[] ids)
    {
        if (ids is null || ids.Length == 0)
        {
            TempData["Error"] = "لم يتم تحديد أي عروض للتحويل";
            return RedirectToAction(nameof(MassConvert));
        }
        int? branchId = HttpContext.Session.GetCurrentBranchId();
        var (converted, failed, failures) = await _quotes.MassConvertAsync(ids, User.Identity?.Name, branchId);
        if (converted > 0)
        {
            TempData["Success"] = $"تم تحويل {converted} عرضاً إلى أوامر بيع بنجاح";
        }
        if (failed > 0)
        {
            TempData["Error"] = $"فشل تحويل {failed} من العروض: " + string.Join("، ", failures.Select(f => $"#{f.Id} ({f.Error})"));
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("SalesQuotes.Convert")]
    public async Task<IActionResult> Convert(int id)
    {
        int? branchId = HttpContext.Session.GetCurrentBranchId();
        var (ok, error, order) = await _quotes.ConvertToOrderAsync(id, User.Identity?.Name, branchId);
        if (ok && order != null)
        {
            TempData["Success"] = "تم تحويل عرض السعر إلى أمر بيع (لا تزال الفاتورة والمخزون معلّقين)";
            return RedirectToAction("Details", "SalesOrders", new { id = order.PublicId });
        }
        TempData["Error"] = error ?? "تعذر تحويل عرض السعر إلى أمر بيع";
        return await RedirectToDetailsAsync(id);
    }

    /// <summary>
    /// Details only resolves the public id now, so internal redirects translate the numeric id
    /// they were handed instead of passing it straight through.
    /// </summary>
    private async Task<IActionResult> RedirectToDetailsAsync(int id)
    {
        var publicId = await _db.SaleQuotes.AsNoTracking()
            .Where(q => q.Id == id)
            .Select(q => (Guid?)q.PublicId)
            .FirstOrDefaultAsync();
        if (publicId == null)
        {
            return RedirectToAction(nameof(Index));
        }

        return RedirectToAction(nameof(Details), new { id = publicId.Value });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("SalesQuotes.Delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var (ok, error) = await _quotes.DeleteAsync(id);
        if (ok)
        {
            TempData["Success"] = "تم حذف عرض السعر";
            return RedirectToAction(nameof(Index));
        }
        TempData["Error"] = error ?? "تعذر حذف عرض السعر";
        return RedirectToAction(nameof(Index));
    }

    private async Task<SaleQuoteViewModel> PopulateDropdowns(SaleQuoteViewModel vm)
    {
        vm.Customers = new SelectList(await _db.Customers.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        vm.SupplierQuotes = new SelectList(await _db.SupplierQuotes.AsNoTracking()
            .Select(sq => new { sq.Id, Label = sq.Supplier.Name + " — " + sq.Item.Name + " (" + sq.UnitPrice.ToString("N2") + ")" })
            .OrderByDescending(x => x.Id)
            .ToListAsync(), "Id", "Label");
        vm.ItemsData = await _db.Items.Where(i => i.IsActive && i.IsSellable).AsNoTracking().ToListAsync();
        return vm;
    }

    private async Task<string> NextNumberPreviewAsync()
    {
        var prefix = $"SQ-{DateTime.Now:yyyyMMdd}-";
        var taken = await _db.SaleQuotes.AsNoTracking().Where(q => q.QuoteNumber.StartsWith(prefix)).Select(q => q.QuoteNumber).ToListAsync();
        var next = (taken.Count > 0 ? taken.Select(n => int.TryParse(n.AsSpan(prefix.Length), out var v) ? v : 0).Max() : 0) + 1;
        return $"{prefix}{next:D3}";
    }
}
