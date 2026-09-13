using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Extensions;
using Silk.Trading.Web.Models.Sales;
using Silk.Trading.Web.Services;
using Silk.Trading.Web.ViewModels.Sales;

namespace Silk.Trading.Web.Controllers;

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
    public async Task<IActionResult> Index(int page = 1)
    {
        page = Math.Max(1, page);
        const int pageSize = 50;
        var query = _db.SaleQuotes.Include(q => q.Customer).AsNoTracking().OrderByDescending(q => q.QuoteDate);
        var total = await query.CountAsync();
        ViewBag.Page = page;
        ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var list = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
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
                CurrencyId = await BaseCurrencyIdAsync(),
                ExchangeRate = 1m
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
        return View(vm);
    }

    [RequirePerm("SalesQuotes.View")]
    public async Task<IActionResult> Details(int id)
    {
        var quote = await _db.SaleQuotes
            .Include(q => q.Customer)
            .Include(q => q.Currency)
            .Include(q => q.SaleInvoice)
            .Include(q => q.SupplierQuote).ThenInclude(sq => sq!.Supplier)
            .Include(q => q.Items).ThenInclude(i => i.Item)
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id);
        if (quote == null) return NotFound();
        return View(quote);
    }

    [RequirePerm("SalesQuotes.View")]
    public async Task<IActionResult> Print(int id)
    {
        var quote = await _db.SaleQuotes
            .Include(q => q.Customer)
            .Include(q => q.Currency)
            .Include(q => q.SupplierQuote).ThenInclude(sq => sq!.Supplier)
            .Include(q => q.Items).ThenInclude(i => i.Item)
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id);
        if (quote == null) return NotFound();
        return View(quote);
    }

    [RequirePerm("SalesQuotes.View")]
    public async Task<IActionResult> Pdf(int id)
    {
        var quote = await _db.SaleQuotes
            .Include(q => q.Customer)
            .Include(q => q.Currency)
            .Include(q => q.SupplierQuote).ThenInclude(sq => sq!.Supplier)
            .Include(q => q.Items).ThenInclude(i => i.Item)
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id);
        if (quote == null) return NotFound();
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
            TempData["Success"] = $"تم تحويل {converted} عرضاً بنجاح";
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
        var (ok, error, invoice) = await _quotes.ConvertToInvoiceAsync(id, User.Identity?.Name, branchId);
        if (ok && invoice != null)
        {
            TempData["Success"] = "تم تحويل عرض السعر إلى فاتورة بيع وترحيل المخزون";
            return RedirectToAction(nameof(Details), new { controller = "Sales", id = invoice.Id });
        }
        TempData["Error"] = error ?? "تعذر تحويل عرض السعر إلى فاتورة";
        return RedirectToAction(nameof(Details), new { id });
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
        vm.Currencies = new SelectList(await _db.Currencies.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Code");
        vm.SupplierQuotes = new SelectList(await _db.SupplierQuotes.AsNoTracking()
            .Select(sq => new { sq.Id, Label = sq.Supplier.Name + " — " + sq.Item.Name + " (" + sq.UnitPrice.ToString("N2") + ")" })
            .OrderByDescending(x => x.Id)
            .ToListAsync(), "Id", "Label");
        vm.ItemsData = await _db.Items.Where(i => i.IsActive && i.IsSellable).AsNoTracking().ToListAsync();
        return vm;
    }

    private async Task<int?> BaseCurrencyIdAsync()
        => await _db.Currencies.AsNoTracking().Where(c => c.IsActive).OrderByDescending(c => c.IsBase)
            .Select(c => (int?)c.Id).FirstOrDefaultAsync();

    private async Task<string> NextNumberPreviewAsync()
    {
        var next = await _db.SaleQuotes.AsNoTracking().CountAsync() + 1;
        return $"SQ-{DateTime.Now:yyyyMMdd}-{next:D3}";
    }
}