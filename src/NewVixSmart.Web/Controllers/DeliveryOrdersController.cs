using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Sales;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class DeliveryOrdersController : Controller
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;

    public DeliveryOrdersController(AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    [RequirePerm("DeliveryOrders.View")]
    public async Task<IActionResult> Index(DeliveryOrderStatus? status = null)
    {
        var query = _db.DeliveryOrders
            .Include(d => d.Customer)
            .Include(d => d.SaleInvoice)
            .AsNoTracking()
            .AsQueryable();
        if (status.HasValue)
            query = query.Where(d => d.Status == status.Value);
        var list = await query.OrderByDescending(d => d.DeliveryDate).Take(500).ToListAsync();
        ViewBag.StatusFilter = status;
        return View(list);
    }

    [RequirePerm("DeliveryOrders.Create")]
    public async Task<IActionResult> Create(int? invoiceId)
    {
        var invoices = await InvoiceOptionsAsync();
        var vm = new DeliveryOrderViewModel
        {
            Invoices = new SelectList(invoices.Select(i => new { i.Id, Label = InvoiceLabel(i) }), "Id", "Label", invoiceId),
            ItemsData = await ItemsAsync(),
            Delivery = new DeliveryOrder { DeliveryDate = DateTime.Today }
        };

        if (invoiceId.HasValue)
        {
            var invoice = await _db.SaleInvoices.Include(i => i.Customer).Include(i => i.Items).AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == invoiceId.Value);
            if (invoice != null)
            {
                vm.Delivery.SaleInvoiceId = invoice.Id;
                vm.Delivery.CustomerId = invoice.CustomerId;
                vm.Items.AddRange(await RemainingLinesAsync(invoice));
                ViewBag.InvoiceLabel = InvoiceLabel(invoice);
                ViewBag.InvoiceNet = invoice.NetAmount;
            }
        }
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("DeliveryOrders.Create")]
    public async Task<IActionResult> Create(DeliveryOrderViewModel vm)
    {
        vm.Delivery ??= new DeliveryOrder();
        vm.Items ??= new List<DeliveryOrderItem>();
        ModelState.IgnoreEmptyLineItemRows();

        var (ok, error) = await _inventory.CreateDeliveryOrderAsync(vm.Delivery, vm.Items, User.Identity?.Name);
        if (ok)
        {
            TempData["Success"] = "تم إنشاء أذن التسليم بنجاح";
            return RedirectToAction(nameof(Index));
        }
        ModelState.AddModelError("", error ?? "تعذر حفظ أذن التسليم");

        var invoices = await InvoiceOptionsAsync();
        vm.Invoices = new SelectList(invoices.Select(i => new { i.Id, Label = InvoiceLabel(i) }), "Id", "Label", vm.Delivery.SaleInvoiceId);
        vm.ItemsData = await ItemsAsync();
        var invoice = invoices.FirstOrDefault(i => i.Id == vm.Delivery.SaleInvoiceId);
        if (invoice != null) ViewBag.InvoiceLabel = InvoiceLabel(invoice);
        return View(vm);
    }

    [RequirePerm("DeliveryOrders.View")]
    public async Task<IActionResult> Details(string id)
    {
        int deliveryId;
        if (Guid.TryParse(id, out var publicId))
        {
            var target = await _db.DeliveryOrders.AsNoTracking()
                .Where(d => d.PublicId == publicId)
                .Select(d => (int?)d.Id)
                .FirstOrDefaultAsync();
            if (target == null) return NotFound();
            deliveryId = target.Value;
        }
        else if (int.TryParse(id, out var numericId))
        {
            deliveryId = numericId;
        }
        else
        {
            return NotFound();
        }

        var delivery = await LoadDeliveryAsync(deliveryId);
        if (delivery == null) return NotFound();
        return View(delivery);
    }

    [RequirePerm("DeliveryOrders.View")]
    public async Task<IActionResult> Print(int id)
    {
        var delivery = await LoadDeliveryAsync(id);
        if (delivery == null) return NotFound();
        return View(delivery);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("DeliveryOrders.Deliver")]
    public async Task<IActionResult> Deliver(int id)
    {
        int? branchId = HttpContext.Session.GetCurrentBranchId();
        var (ok, error) = await _inventory.DeliverDeliveryOrderAsync(id, User.Identity?.Name, branchId);
        if (ok) TempData["Success"] = "تم ترحيل أذن التسليم: خُصم المخزون وسُجّلت قيود البيع";
        else TempData["Error"] = error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("DeliveryOrders.Deliver")]
    public async Task<IActionResult> Cancel(int id)
    {
        var (ok, error) = await _inventory.CancelDeliveryOrderAsync(id, User.Identity?.Name);
        if (ok) TempData["Success"] = "تم إلغاء أذن التسليم";
        else TempData["Error"] = error;
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<DeliveryOrder?> LoadDeliveryAsync(int id)
        => await _db.DeliveryOrders
            .Include(d => d.Customer)
            .Include(d => d.SaleInvoice).ThenInclude(i => i!.Customer)
            .Include(d => d.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
            .Include(d => d.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id);

    private async Task<List<DeliveryOrderItem>> RemainingLinesAsync(SaleInvoice invoice)
    {
        var delivered = await _db.DeliveryOrders
            .Where(d => d.SaleInvoiceId == invoice.Id && d.Status != DeliveryOrderStatus.Cancelled)
            .SelectMany(d => d.Items)
            .AsNoTracking()
            .ToListAsync();

        return invoice.Items.Select(line =>
        {
            decimal delCount = delivered.Where(x => x.ItemId == line.ItemId).Sum(x => x.Count);
            decimal delQty = delivered.Where(x => x.ItemId == line.ItemId).Sum(x => x.Quantity);
            return new DeliveryOrderItem
            {
                ItemId = line.ItemId,
                Quantity = line.Quantity - delQty,
                Count = line.Count - delCount
            };
        }).Where(i => i.Quantity > 0 || i.Count > 0).ToList();
    }

    private async Task<List<SaleInvoice>> InvoiceOptionsAsync()
        => await _db.SaleInvoices.Include(i => i.Customer).AsNoTracking()
            .OrderByDescending(i => i.InvoiceDate).Take(300).ToListAsync();

    private static string InvoiceLabel(SaleInvoice i)
        => $"{i.InvoiceNumber} — {i.Customer?.Name ?? "—"} ({i.InvoiceDate:dd/MM/yyyy}) الصافي {i.NetAmount:N2}";

    private async Task<List<Item>> ItemsAsync()
        => await _db.Items.Where(i => i.IsActive && i.IsSellable).AsNoTracking()
            .Include(i => i.CountUnit).Include(i => i.QuantityUnit).ToListAsync();
}