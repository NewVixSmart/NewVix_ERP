using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Accounting;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class ShipmentsController : Controller
{
    private readonly AppDbContext _db;

    public ShipmentsController(AppDbContext db) => _db = db;

    [RequirePerm("Shipments.View")]
    public async Task<IActionResult> Index()
    {
        var shipments = await _db.Shipments
            .Include(s => s.Customer)
            .Include(s => s.Supplier)
            .Include(s => s.SaleInvoice)
            .Include(s => s.PurchaseInvoice)
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
        return View(shipments);
    }

    [RequirePerm("Shipments.Create")]
    public async Task<IActionResult> Create(int? saleId, int? purchaseId)
    {
        var vm = new ShipmentFormViewModel
        {
            Shipment = new Shipment
            {
                InvoiceType = saleId.HasValue ? ShipmentInvoiceType.Sale : ShipmentInvoiceType.Purchase,
                SaleInvoiceId = saleId,
                PurchaseInvoiceId = purchaseId,
                ShipDate = DateTime.Today
            },
            SaleInvoices = new SelectList(await _db.SaleInvoices.Include(s => s.Customer).AsNoTracking().ToListAsync(), "Id", "InvoiceNumber"),
            PurchaseInvoices = new SelectList(await _db.PurchaseInvoices.Include(p => p.Supplier).AsNoTracking().ToListAsync(), "Id", "InvoiceNumber"),
            Customers = new SelectList(await _db.Customers.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Name"),
            Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name")
        };
        if (saleId.HasValue)
        {
            var invoice = await _db.SaleInvoices.Include(s => s.Customer).AsNoTracking().FirstOrDefaultAsync(s => s.Id == saleId);
            if (invoice != null)
            {
                vm.Shipment.CustomerId = invoice.CustomerId;
                vm.Shipment.InvoiceType = ShipmentInvoiceType.Sale;
                vm.Shipment.SaleInvoiceId = invoice.Id;
            }
        }
        if (purchaseId.HasValue)
        {
            var invoice = await _db.PurchaseInvoices.Include(p => p.Supplier).AsNoTracking().FirstOrDefaultAsync(p => p.Id == purchaseId);
            if (invoice != null)
            {
                vm.Shipment.SupplierId = invoice.SupplierId;
                vm.Shipment.InvoiceType = ShipmentInvoiceType.Purchase;
                vm.Shipment.PurchaseInvoiceId = invoice.Id;
            }
        }
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Shipments.Create")]
    public async Task<IActionResult> Create(ShipmentFormViewModel vm)
    {
        vm.Shipment ??= new Shipment();
        if (ModelState.IsValid && vm.Shipment.InvoiceType == ShipmentInvoiceType.Sale
            ? vm.Shipment.SaleInvoiceId.HasValue
            : vm.Shipment.PurchaseInvoiceId.HasValue)
        {
            if (vm.Shipment.InvoiceType == ShipmentInvoiceType.Sale && vm.Shipment.SaleInvoiceId.HasValue)
            {
                var saleInv = await _db.SaleInvoices.AsNoTracking().FirstOrDefaultAsync(s => s.Id == vm.Shipment.SaleInvoiceId.Value);
                if (saleInv == null || saleInv.CustomerId != vm.Shipment.CustomerId)
                {
                    ModelState.AddModelError("", "الفاتورة المختارة لا تخص العميل المحدد للشحنة");
                    vm.SaleInvoices = new SelectList(await _db.SaleInvoices.AsNoTracking().ToListAsync(), "Id", "InvoiceNumber");
                    vm.PurchaseInvoices = new SelectList(await _db.PurchaseInvoices.AsNoTracking().ToListAsync(), "Id", "InvoiceNumber");
                    vm.Customers = new SelectList(await _db.Customers.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
                    vm.Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
                    return View(vm);
                }
            }
            else if (vm.Shipment.InvoiceType == ShipmentInvoiceType.Purchase && vm.Shipment.PurchaseInvoiceId.HasValue)
            {
                var purchaseInv = await _db.PurchaseInvoices.AsNoTracking().FirstOrDefaultAsync(p => p.Id == vm.Shipment.PurchaseInvoiceId.Value);
                if (purchaseInv == null || purchaseInv.SupplierId != vm.Shipment.SupplierId)
                {
                    ModelState.AddModelError("", "الفاتورة المختارة لا تخص المورد المحدد للشحنة");
                    vm.SaleInvoices = new SelectList(await _db.SaleInvoices.AsNoTracking().ToListAsync(), "Id", "InvoiceNumber");
                    vm.PurchaseInvoices = new SelectList(await _db.PurchaseInvoices.AsNoTracking().ToListAsync(), "Id", "InvoiceNumber");
                    vm.Customers = new SelectList(await _db.Customers.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
                    vm.Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
                    return View(vm);
                }
            }

            vm.Shipment.ShipmentNumber = await NextShipmentNumberAsync();
            vm.Shipment.CreatedBy = User.Identity?.Name;
            vm.Shipment.CreatedAt = DateTime.UtcNow;
            _db.Shipments.Add(vm.Shipment);
            await _db.SaveChangesAsync();
            TempData["Success"] = "تم تسجيل الشحنة بنجاح";
            return RedirectToAction(nameof(Index));
        }

        ModelState.AddModelError("", "يرجى اختيار الفاتورة المرتبطة بالشحنة");
        vm.SaleInvoices = new SelectList(await _db.SaleInvoices.AsNoTracking().ToListAsync(), "Id", "InvoiceNumber");
        vm.PurchaseInvoices = new SelectList(await _db.PurchaseInvoices.AsNoTracking().ToListAsync(), "Id", "InvoiceNumber");
        vm.Customers = new SelectList(await _db.Customers.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        vm.Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        return View(vm);
    }

    [RequirePerm("Shipments.Edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var shipment = await _db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (shipment == null) return NotFound();
        var vm = new ShipmentFormViewModel
        {
            Shipment = shipment,
            SaleInvoices = new SelectList(await _db.SaleInvoices.AsNoTracking().ToListAsync(), "Id", "InvoiceNumber"),
            PurchaseInvoices = new SelectList(await _db.PurchaseInvoices.AsNoTracking().ToListAsync(), "Id", "InvoiceNumber"),
            Customers = new SelectList(await _db.Customers.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Name"),
            Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name")
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Shipments.Edit")]
    public async Task<IActionResult> Edit(int id, ShipmentFormViewModel vm)
    {
        var existing = await _db.Shipments.FindAsync(id);
        if (existing == null) return NotFound();
        if (vm.Shipment == null) return NotFound();

        existing.InvoiceType = vm.Shipment.InvoiceType;
        existing.SaleInvoiceId = vm.Shipment.SaleInvoiceId;
        existing.PurchaseInvoiceId = vm.Shipment.PurchaseInvoiceId;
        existing.CustomerId = vm.Shipment.CustomerId;
        existing.SupplierId = vm.Shipment.SupplierId;
        existing.Carrier = vm.Shipment.Carrier;
        existing.TrackingNumber = vm.Shipment.TrackingNumber;
        existing.ShipDate = vm.Shipment.ShipDate;
        existing.Status = vm.Shipment.Status;
        existing.Notes = vm.Shipment.Notes;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث الشحنة بنجاح";
        return RedirectToAction(nameof(Index));
    }

    private async Task<string> NextShipmentNumberAsync()
    {
        int next = await _db.Shipments.CountAsync() + 1;
        string num = $"SHP-{DateTime.Now:yyyyMMdd}-{next:D3}";
        while (await _db.Shipments.AnyAsync(s => s.ShipmentNumber == num))
        {
            next++;
            num = $"SHP-{DateTime.Now:yyyyMMdd}-{next:D3}";
        }
        return num;
    }
}
