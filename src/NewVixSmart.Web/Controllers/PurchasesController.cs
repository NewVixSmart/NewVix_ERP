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
        var invoices = await query.Take(500).ToListAsync();
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
            ItemsData = await _db.Items.Where(i => i.IsActive).AsNoTracking().ToListAsync(),
            Invoice = new PurchaseInvoice
            {
                InvoiceNumber = nextNumber,
                InvoiceDate = DateTime.Today,
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

        var lastInvoice = await _db.PurchaseInvoices.AsNoTracking().OrderByDescending(p => p.Id).FirstOrDefaultAsync();
        vm.Invoice.InvoiceNumber = $"PO-{(lastInvoice == null ? 1 : lastInvoice.Id + 1):D5}";
        ModelState.Remove("Invoice.InvoiceNumber");

        vm.Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        vm.ItemsData = await _db.Items.Where(i => i.IsActive).AsNoTracking().ToListAsync();
        return View(vm);
    }

    [RequirePerm("Purchases.View")]
    public async Task<IActionResult> Details(string id)
    {
        var query = _db.PurchaseInvoices.Include(p => p.Supplier).Include(p => p.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
.Include(p => p.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking();

        PurchaseInvoice? invoice;
        if (!Guid.TryParse(id, out var publicId)) return NotFound();
        invoice = await query.FirstOrDefaultAsync(p => p.PublicId == publicId);

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
}
