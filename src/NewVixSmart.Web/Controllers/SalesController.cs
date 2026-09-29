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
    public async Task<IActionResult> Index(int page = 1, string? search = null)
    {
        var query = _db.SaleInvoices.Include(s => s.Customer).AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(s => s.InvoiceNumber.Contains(term) || s.Customer.Name.Contains(term));
        }
        query = query.OrderByDescending(s => s.InvoiceDate).ThenByDescending(s => s.Id);

        var total = await query.CountAsync();
        page = PagerExtensions.NormalizePage(page, total);
        var invoices = await query
            .Skip((page - 1) * PagerExtensions.PageSize)
            .Take(PagerExtensions.PageSize)
            .ToListAsync();
        this.SetPager(page, total, invoices.Count, search);

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
            ItemsData = await _db.Items.Where(i => i.IsActive && i.IsSellable).AsNoTracking().ToListAsync(),
            Invoice = new SaleInvoice
            {
                InvoiceNumber = nextNumber,
                InvoiceDate = DateTime.Today
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

        var lastInvoice = await _db.SaleInvoices.AsNoTracking().OrderByDescending(s => s.Id).FirstOrDefaultAsync();
        vm.Invoice.InvoiceNumber = $"SI-{(lastInvoice == null ? 1 : lastInvoice.Id + 1):D5}";
        ModelState.Remove("Invoice.InvoiceNumber");

        vm.Customers = new SelectList(await _db.Customers.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        vm.ItemsData = await _db.Items.Where(i => i.IsActive && i.IsSellable).AsNoTracking().ToListAsync();
        return View(vm);
    }

    [RequirePerm("Sales.View")]
    public async Task<IActionResult> Details(string id)
    {
        var query = _db.SaleInvoices.Include(s => s.Customer).Include(s => s.SalesOrder)
            .Include(s => s.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
.Include(s => s.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking();

        SaleInvoice? invoice;
        if (!Guid.TryParse(id, out var publicId))
        {
            return NotFound();
        }

        invoice = await query.FirstOrDefaultAsync(s => s.PublicId == publicId);

        if (invoice == null)
        {
            return NotFound();
        }

        return View(invoice);
    }

    [RequirePerm("Sales.View")]
    public async Task<IActionResult> Print(int id)
    {
        var invoice = await _db.SaleInvoices.Include(s => s.Customer).Include(s => s.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
.Include(s => s.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (invoice == null)
        {
            return NotFound();
        }

        return View(invoice);
    }
}
