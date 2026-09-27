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

    [RequirePerm("DeliveryOrders.Create")]
    public async Task<IActionResult> Create(int? invoiceId, int? salesOrderId, string? source)
    {
        var vm = new DeliveryOrderViewModel
        {
            Invoices = new SelectList((await InvoiceOptionsAsync()).Select(i => new { i.Id, Label = InvoiceLabel(i) }), "Id", "Label", invoiceId),
            SalesOrders = new SelectList((await OrderOptionsAsync()).Select(o => new { o.Id, o.OrderNumber, o.CustomerName }), "Id", "OrderNumber", salesOrderId),
            Customers = new SelectList(await CustomersAsync(), "Id", "Name"),
            ItemsData = await ItemsAsync(),
            Delivery = new DeliveryOrder { DeliveryDate = DateTime.Today }
        };

        if (!string.IsNullOrWhiteSpace(source)) vm.Source = source;

        if (salesOrderId.HasValue)
        {
            vm.Source = "Order";
            var lines = await RemainingOrderLinesAsync(salesOrderId.Value);
            if (lines == null) return NotFound();
            var order = vm.SalesOrders!.Cast<SelectListItem>().FirstOrDefault(s => s.Value == salesOrderId.Value.ToString());
            ViewBag.OrderLabel = order?.Text;
            ViewBag.OrderLines = lines;
            vm.Items = lines;
            vm.Delivery.SalesOrderId = salesOrderId.Value;
        }
        else if (invoiceId.HasValue)
        {
            vm.Source = "Invoice";
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

        var sources = new List<int>();
        if (vm.Delivery.SalesOrderId is int orderId && orderId > 0) sources.Add(orderId);
        if (vm.Delivery.SaleInvoiceId is int invId && invId > 0) sources.Add(invId);
        if (vm.Source == "Order" || vm.Source == "Invoice" || vm.Source == "Free")
        {
            if (sources.Count > 1)
            {
                ModelState.AddModelError("", "اختر مصدراً واحداً فقط: أمر بيع أو فاتورة أو إدخال حر");
                return await RepopulateAsync(vm);
            }
            if (vm.Source != "Free" && sources.Count == 0)
            {
                ModelState.AddModelError("", vm.Source == "Order" ? "اختر أمر البيع" : "اختر فاتورة البيع");
                return await RepopulateAsync(vm);
            }
        }

        (bool ok, string? error) result;
        if (vm.Delivery.SalesOrderId is int soId && soId > 0)
        {
            var (ok1, error1, _) = await _inventory.CreateSalesDeliveryNoteAsync(soId, null, null,
                vm.Items, User.Identity?.Name, vm.Delivery.DeliveryDate, vm.Delivery.Notes);
            result = (ok1, error1);
        }
        else if (vm.Delivery.SaleInvoiceId is int siId && siId > 0)
        {
            var (ok2, error2) = await _inventory.CreateDeliveryOrderAsync(vm.Delivery, vm.Items, User.Identity?.Name);
            result = (ok2, error2);
        }
        else
        {
            var (ok3, error3, _) = await _inventory.CreateSalesDeliveryNoteAsync(null, null,
                vm.Delivery.CustomerId, vm.Items, User.Identity?.Name, vm.Delivery.DeliveryDate, vm.Delivery.Notes);
            result = (ok3, error3);
        }

        if (result.ok)
        {
            TempData["Success"] = "تم إنشاء أذن التسليم بنجاح";
            return RedirectToAction(nameof(Index));
        }
        ModelState.AddModelError("", result.error ?? "تعذر حفظ أذن التسليم");
        return await RepopulateAsync(vm);
    }

    private async Task<IActionResult> RepopulateAsync(DeliveryOrderViewModel vm)
    {
        vm.Invoices = new SelectList((await InvoiceOptionsAsync()).Select(i => new { i.Id, Label = InvoiceLabel(i) }), "Id", "Label", vm.Delivery.SaleInvoiceId);
        vm.SalesOrders = new SelectList((await OrderOptionsAsync()).Select(o => new { o.Id, o.OrderNumber, o.CustomerName }), "Id", "OrderNumber", vm.Delivery.SalesOrderId);
        vm.Customers = new SelectList(await CustomersAsync(), "Id", "Name", vm.Delivery.CustomerId);
        vm.ItemsData = await ItemsAsync();
        if (vm.Delivery.SaleInvoiceId is int invId)
        {
            var invoice = (await InvoiceOptionsAsync()).FirstOrDefault(i => i.Id == invId);
            if (invoice != null) ViewBag.InvoiceLabel = InvoiceLabel(invoice);
        }
        return View(vm);
    }

    private async Task<List<DeliveryOrderItem>?> RemainingOrderLinesAsync(int orderId)
    {
        var order = await _db.SalesOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) return null;

        var orderLines = await _db.SalesOrderItems.AsNoTracking()
            .Where(i => i.SalesOrderId == orderId).ToListAsync();
        var noted = await _db.DeliveryOrders.AsNoTracking()
            .Where(d => d.SalesOrderId == orderId && d.Status != DeliveryOrderStatus.Cancelled)
            .SelectMany(d => d.Items)
            .GroupBy(x => x.ItemId)
            .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.Quantity), Cnt = g.Sum(x => x.Count) })
            .ToDictionaryAsync(x => x.ItemId, x => (Qty: x.Qty, Cnt: x.Cnt));
        var issued = await _db.DeliveryIssues.AsNoTracking()
            .Where(i => i.SalesOrderId == orderId && i.Status == DeliveryIssueStatus.Issued)
            .SelectMany(i => i.Items)
            .GroupBy(x => x.ItemId)
            .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.Quantity), Cnt = g.Sum(x => x.Count) })
            .ToDictionaryAsync(x => x.ItemId, x => (Qty: x.Qty, Cnt: x.Cnt));

        return orderLines.Select(l =>
        {
            var n = noted.TryGetValue(l.ItemId, out var nv) ? nv : (Qty: 0m, Cnt: 0m);
            var s = issued.TryGetValue(l.ItemId, out var sv) ? sv : (Qty: 0m, Cnt: 0m);
            return new DeliveryOrderItem
            {
                ItemId = l.ItemId,
                Quantity = l.Quantity - n.Qty + s.Qty,
                Count = l.Count - n.Cnt + s.Cnt
            };
        }).Where(i => i.Quantity > 0.005m || i.Count > 0.005m).ToList();
    }

    private async Task<List<OrderOption>> OrderOptionsAsync()
        => await _db.SalesOrders.AsNoTracking()
            .Where(o => o.Status != SalesOrderStatus.Draft && o.Status != SalesOrderStatus.Cancelled)
            .OrderByDescending(o => o.Id).Take(300)
            .Select(o => new OrderOption(o.Id, o.OrderNumber, o.Customer!.Name))
            .ToListAsync();

    private async Task<List<Customer>> CustomersAsync()
        => await _db.Customers.Where(c => c.IsActive).AsNoTracking().OrderBy(c => c.Name).ToListAsync();

    private record OrderOption(int Id, string OrderNumber, string CustomerName);

    private async Task<DeliveryOrder?> LoadDeliveryAsync(int id)
        => await _db.DeliveryOrders
            .Include(d => d.Customer)
            .Include(d => d.SaleInvoice).ThenInclude(i => i!.Customer)
            .Include(d => d.SalesOrder)
            .Include(d => d.Issues).ThenInclude(i => i.Items).ThenInclude(i => i.Item)
            .Include(d => d.Issues).ThenInclude(i => i.SaleInvoice)
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