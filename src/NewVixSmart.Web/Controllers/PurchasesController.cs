using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.ViewModels.Purchases;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class PurchasesController : Controller
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    public PurchasesController(AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

[RequirePerm("Purchases.View")]
    public async Task<IActionResult> Index()
    {
        var query = _db.PurchaseInvoices.Include(p => p.Supplier).AsNoTracking().OrderByDescending(p => p.InvoiceDate);
        var invoices = await query.ToListAsync();
        return View(invoices);
    }

    [RequirePerm("Purchases.Create")]
    public async Task<IActionResult> Create()
    {
        var lastInvoice = await _db.PurchaseInvoices.AsNoTracking().OrderByDescending(p => p.Id).FirstOrDefaultAsync();
        string nextNumber = $"PO-{(lastInvoice == null ? 1 : lastInvoice.Id + 1):D5}";

        var vm = new PurchaseInvoiceViewModel
        {
            Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name"),
            Currencies = new SelectList(await _db.Currencies.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Code"),
            ItemsData = await _db.Items.Where(i => i.IsActive).AsNoTracking().ToListAsync(),
            Invoice = new PurchaseInvoice
            {
                InvoiceNumber = nextNumber,
                InvoiceDate = DateTime.Today,
                CurrencyId = await BaseCurrencyIdAsync(),
                ExchangeRate = 1m
            }
        };
        return View(vm);
    }

[HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("Purchases.Create")]
    public async Task<IActionResult> Create(PurchaseInvoiceViewModel vm)
    {
        vm.Invoice ??= new PurchaseInvoice();
        var items = vm.Items ?? new List<PurchaseInvoiceItem>();
        ModelState.IgnoreEmptyLineItemRows();

        var baseCurrencyId = await BaseCurrencyIdAsync();
        if (vm.Invoice.CurrencyId.HasValue && vm.Invoice.CurrencyId.Value != baseCurrencyId
            && (!vm.Invoice.ExchangeRate.HasValue || vm.Invoice.ExchangeRate.Value <= 0))
        {
            ModelState.AddModelError("", "سعر الصرف يجب أن يكون أكبر من صفر للفواتير بالعملة الأجنبية");
        }

        if (ModelState.IsValid && items.Any(i => i.ItemId > 0))
        {
            int? branchId = HttpContext.Session.GetCurrentBranchId();
            var (ok, error) = await _inventory.CreatePurchaseAsync(vm.Invoice, items, User.Identity?.Name, branchId);
            if (ok)
            {
                TempData["Success"] = "تم حفظ فاتورة الشراء بنجاح";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError("", error ?? "تعذر حفظ فاتورة الشراء");
        }

        vm.Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        vm.Currencies = new SelectList(await _db.Currencies.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Code");
        vm.ItemsData = await _db.Items.Where(i => i.IsActive).AsNoTracking().ToListAsync();
        return View(vm);
    }

    [RequirePerm("Purchases.View")]
    public async Task<IActionResult> Details(int id)
    {
        var invoice = await _db.PurchaseInvoices.Include(p => p.Supplier).Include(p => p.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
.Include(p => p.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (invoice == null) return NotFound();
        return View(invoice);
    }

    [RequirePerm("Purchases.View")]
    public async Task<IActionResult> Print(int id)
    {
        var invoice = await _db.PurchaseInvoices.Include(p => p.Supplier).Include(p => p.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
.Include(p => p.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (invoice == null) return NotFound();
        return View(invoice);
    }

    private async Task<int?> BaseCurrencyIdAsync()
        => await _db.Currencies.AsNoTracking().Where(c => c.IsActive).OrderByDescending(c => c.IsBase)
            .Select(c => (int?)c.Id).FirstOrDefaultAsync();
}
