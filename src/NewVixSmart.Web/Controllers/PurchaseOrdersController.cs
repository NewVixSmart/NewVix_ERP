using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Purchases;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class PurchaseOrdersController : Controller
{
    private readonly AppDbContext _db;
    private readonly IProcurementService _procurement;

    public PurchaseOrdersController(AppDbContext db, IProcurementService procurement)
    {
        _db = db;
        _procurement = procurement;
    }

    [RequirePerm("PurchaseOrders.View")]
    public async Task<IActionResult> Index(PurchaseOrderStatus? status = null)
    {
        var orders = await _procurement.GetOrdersAsync(status, 500);
        ViewBag.StatusFilter = status;
        return View(orders);
    }

    [RequirePerm("PurchaseOrders.Create")]
    public IActionResult Create(int? fromQuoteId)
    {
        var vm = new PurchaseOrderViewModel
        {
            Suppliers = new SelectList(_db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToList(), "Id", "Name"),
            ItemsData = _db.Items.Where(i => i.IsActive).AsNoTracking().Include(i => i.CountUnit).Include(i => i.QuantityUnit).ToList(),
            Order = new PurchaseOrder { OrderDate = DateTime.Today }
        };

        if (fromQuoteId.HasValue)
        {
            var quote = _db.SupplierQuotes
                .Include(q => q.Supplier)
                .Include(q => q.Item)
                .AsNoTracking()
                .FirstOrDefault(q => q.Id == fromQuoteId.Value);
            if (quote != null)
            {
                vm.Order.SupplierId = quote.SupplierId;
                vm.Items.Add(new PurchaseOrderItem
                {
                    ItemId = quote.ItemId,
                    Quantity = 1,
                    Count = 1,
                    UnitPrice = quote.UnitPrice
                });
            }
        }

        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("PurchaseOrders.Create")]
    public async Task<IActionResult> Create(PurchaseOrderViewModel vm)
    {
        vm.Order ??= new PurchaseOrder();
        vm.Items ??= new List<PurchaseOrderItem>();
        ModelState.IgnoreEmptyLineItemRows();

        if (ModelState.IsValid && vm.Items.Any(i => i.ItemId > 0))
        {
            var (ok, error) = await _procurement.CreateOrderAsync(vm.Order, vm.Items, User.Identity?.Name);
            if (ok)
            {
                TempData["Success"] = "تم إنشاء أمر الشراء بنجاح";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError("", error ?? "تعذر حفظ أمر الشراء");
        }

        vm.Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        vm.ItemsData = await _db.Items.Where(i => i.IsActive).AsNoTracking().Include(i => i.CountUnit).Include(i => i.QuantityUnit).ToListAsync();
        return View(vm);
    }

    [RequirePerm("PurchaseOrders.Edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var order = await _db.PurchaseOrders.Include(o => o.Items).AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();
        if (order.Status != PurchaseOrderStatus.Draft)
        {
            TempData["Error"] = "لا يمكن تعديل أمر شراء غير مسودة";
            return RedirectToAction(nameof(Details), new { id });
        }

        var vm = new PurchaseOrderViewModel
        {
            Order = order,
            Items = order.Items.ToList(),
            Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name"),
            ItemsData = await _db.Items.Where(i => i.IsActive).AsNoTracking().Include(i => i.CountUnit).Include(i => i.QuantityUnit).ToListAsync()
        };
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("PurchaseOrders.Edit")]
    public async Task<IActionResult> Edit(PurchaseOrderViewModel vm)
    {
        vm.Order ??= new PurchaseOrder();
        vm.Items ??= new List<PurchaseOrderItem>();
        ModelState.IgnoreEmptyLineItemRows();

        if (ModelState.IsValid && vm.Items.Any(i => i.ItemId > 0))
        {
            var (ok, error) = await _procurement.UpdateOrderAsync(vm.Order, vm.Items, User.Identity?.Name);
            if (ok)
            {
                TempData["Success"] = "تم تحديث أمر الشراء بنجاح";
                return RedirectToAction(nameof(Details), new { id = vm.Order.Id });
            }
            ModelState.AddModelError("", error ?? "تعذر تحديث أمر الشراء");
        }

        vm.Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        vm.ItemsData = await _db.Items.Where(i => i.IsActive).AsNoTracking().Include(i => i.CountUnit).Include(i => i.QuantityUnit).ToListAsync();
        return View(vm);
    }

    [RequirePerm("PurchaseOrders.View")]
    public async Task<IActionResult> Details(string id)
    {
        int orderId;
        if (Guid.TryParse(id, out var publicId))
        {
            var target = await _db.PurchaseOrders.AsNoTracking()
                .Where(o => o.PublicId == publicId)
                .Select(o => (int?)o.Id)
                .FirstOrDefaultAsync();
            if (target == null) return NotFound();
            orderId = target.Value;
        }
        else if (int.TryParse(id, out var numericId))
        {
            orderId = numericId;
        }
        else
        {
            return NotFound();
        }

        var order = await _procurement.GetOrderAsync(orderId);
        if (order == null) return NotFound();
        return View(order);
    }

    [RequirePerm("PurchaseOrders.View")]
    public async Task<IActionResult> Print(int id)
    {
        var order = await _procurement.GetOrderAsync(id);
        if (order == null) return NotFound();
        return View(order);
    }

    [RequirePerm("PurchaseOrders.View")]
    public async Task<IActionResult> Pdf(int id)
    {
        var order = await _procurement.GetOrderAsync(id);
        if (order == null) return NotFound();
        var bytes = PrintPdfBuilder.RenderPurchaseOrderPdf(order);
        return File(bytes, "application/pdf", $"purchase-order-{order.OrderNumber}.pdf");
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("PurchaseOrders.Approve")]
    public async Task<IActionResult> Approve(int id)
    {
        var (ok, error) = await _procurement.ApproveOrderAsync(id);
        if (ok) TempData["Success"] = "تم اعتماد أمر الشراء";
        else TempData["Error"] = error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("PurchaseOrders.Approve")]
    public async Task<IActionResult> Cancel(int id)
    {
        var (ok, error) = await _procurement.CancelOrderAsync(id);
        if (ok) TempData["Success"] = "تم إلغاء أمر الشراء";
        else TempData["Error"] = error;
        return RedirectToAction(nameof(Details), new { id });
    }

    [RequirePerm("PurchaseOrders.Receive")]
    public async Task<IActionResult> Receive(int id)
    {
        var order = await _procurement.GetOrderAsync(id);
        if (order == null) return NotFound();
        if (order.Status != PurchaseOrderStatus.Approved && order.Status != PurchaseOrderStatus.PartiallyReceived)
        {
            TempData["Error"] = "لا يمكن الاستلام على هذا الأمر";
            return RedirectToAction(nameof(Details), new { id });
        }
        return View(order);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("PurchaseOrders.Receive")]
    public async Task<IActionResult> Receive(int id, int orderItemId, decimal receiveQty, decimal receiveCount)
    {
        var (ok, error) = await _procurement.ReceiveOrderLineAsync(id, orderItemId, receiveQty, receiveCount);
        if (ok) TempData["Success"] = "تم تسجيل الاستلام";
        else TempData["Error"] = error;
        return RedirectToAction(nameof(Receive), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("PurchaseOrders.Receive")]
    public async Task<IActionResult> CreateInvoice(int id)
    {
        var (ok, error) = await _procurement.CreateInvoiceFromOrderAsync(id, User.Identity?.Name);
        if (ok)
        {
            TempData["Success"] = "تم إنشاء فاتورة الشراء من أمر الشراء";
            return RedirectToAction("Index", "Purchases");
        }
        TempData["Error"] = error ?? "تعذر إنشاء الفاتورة";
        return RedirectToAction(nameof(Details), new { id });
    }
}
