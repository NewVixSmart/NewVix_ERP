using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Sales;
using Silk.Trading.Web.Services;
using Silk.Trading.Web.Extensions;
using Silk.Trading.Web.ViewModels.Sales;

namespace Silk.Trading.Web.Controllers;

[Authorize]
public class SalesController : Controller
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    public SalesController(AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

[RequirePerm("Sales.View")]
    public async Task<IActionResult> Index(int page = 1)
    {
        page = Math.Max(1, page);
        const int pageSize = 50;
        var query = _db.SaleInvoices.Include(s => s.Customer).AsNoTracking().OrderByDescending(s => s.InvoiceDate);
        var total = await query.CountAsync();
        ViewBag.Page = page;
        ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var invoices = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return View(invoices);
    }

    [RequirePerm("Sales.Create")]
    public async Task<IActionResult> Create()
    {
        var lastInvoice = await _db.SaleInvoices.AsNoTracking().OrderByDescending(s => s.Id).FirstOrDefaultAsync();
        string nextNumber = $"SI-{(lastInvoice == null ? 1 : lastInvoice.Id + 1):D5}";

        var vm = new SaleInvoiceViewModel
        {
            Customers = new SelectList(await _db.Customers.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Name"),
            Currencies = new SelectList(await _db.Currencies.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Code"),
            ItemsData = await _db.Items.Where(i => i.IsActive && i.IsSellable).AsNoTracking().ToListAsync(),
            Invoice = new SaleInvoice
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
    [RequirePerm("Sales.Create")]
    public async Task<IActionResult> Create(SaleInvoiceViewModel vm)
    {
        vm.Invoice ??= new SaleInvoice();
        var items = vm.Items ?? new List<SaleInvoiceItem>();
        ModelState.IgnoreEmptyLineItemRows();

        if (ModelState.IsValid && items.Any(i => i.ItemId > 0))
        {
            int? branchId = HttpContext.Session.GetCurrentBranchId();
            var (ok, error) = await _inventory.CreateSaleAsync(vm.Invoice, items, User.Identity?.Name, branchId);
            if (ok)
            {
                TempData["Success"] = "تم حفظ فاتورة البيع بنجاح";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError("", error ?? "تعذر حفظ فاتورة البيع");
        }

        vm.Customers = new SelectList(await _db.Customers.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        vm.Currencies = new SelectList(await _db.Currencies.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Code");
        vm.ItemsData = await _db.Items.Where(i => i.IsActive && i.IsSellable).AsNoTracking().ToListAsync();
        return View(vm);
    }

    [RequirePerm("Sales.View")]
    public async Task<IActionResult> Details(int id)
    {
        var invoice = await _db.SaleInvoices.Include(s => s.Customer).Include(s => s.Items).ThenInclude(i => i.Item)
            .AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (invoice == null) return NotFound();
        return View(invoice);
    }

    [RequirePerm("Sales.View")]
    public async Task<IActionResult> Print(int id)
    {
        var invoice = await _db.SaleInvoices.Include(s => s.Customer).Include(s => s.Items).ThenInclude(i => i.Item)
            .AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (invoice == null) return NotFound();
        return View(invoice);
    }

    private async Task<int?> BaseCurrencyIdAsync()
        => await _db.Currencies.AsNoTracking().Where(c => c.IsActive).OrderByDescending(c => c.IsBase)
            .Select(c => (int?)c.Id).FirstOrDefaultAsync();
}
