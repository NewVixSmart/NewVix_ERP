using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Forms;
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
            Order = new PurchaseOrderFormModel { OrderDate = DateTime.Today }
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
                vm.Items.Add(new PurchaseOrderLineFormModel
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
        vm.Order ??= new PurchaseOrderFormModel();
        vm.Items ??= new List<PurchaseOrderLineFormModel>();
        ModelState.IgnoreEmptyLineItemRows();

        if (ModelState.IsValid && vm.Items.Any(i => i.ItemId > 0))
        {
            // Id stays 0 on create: a posted key would ask EF to insert a row with an
            // identity the client chose.
            vm.Order.Id = 0;
            var (ok, error) = await _procurement.CreateOrderAsync(
                vm.Order.ToEntity(), Lines(vm.Items), User.Identity?.Name);
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
        if (order == null)
        {
            return NotFound();
        }

        if (order.Status != PurchaseOrderStatus.Draft)
        {
            TempData["Error"] = "لا يمكن تعديل أمر شراء غير مسودة";
            return RedirectToAction(nameof(Details), new { id = order.PublicId });
        }

        var vm = new PurchaseOrderViewModel
        {
            Order = order.ToFormModel(),
            Items = order.Items.Select(i => i.ToFormModel()).ToList(),
            Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name"),
            ItemsData = await _db.Items.Where(i => i.IsActive).AsNoTracking().Include(i => i.CountUnit).Include(i => i.QuantityUnit).ToListAsync()
        };
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("PurchaseOrders.Edit")]
    public async Task<IActionResult> Edit(int id, PurchaseOrderViewModel vm)
    {
        vm.Order ??= new PurchaseOrderFormModel();
        vm.Items ??= new List<PurchaseOrderLineFormModel>();
        // The order's identity rides in the route, not in the body: a posted Order.Id is
        // ignored, so the record edited is the one the URL names.
        vm.Order.Id = id;
        ModelState.IgnoreEmptyLineItemRows();

        if (ModelState.IsValid && vm.Items.Any(i => i.ItemId > 0))
        {
            try
            {
                var (ok, error) = await _procurement.UpdateOrderAsync(
                    vm.Order.ToEntity(), Lines(vm.Items), User.Identity?.Name);
                if (ok)
                {
                    TempData["Success"] = "تم تحديث أمر الشراء بنجاح";
                    return await RedirectToDetailsAsync(vm.Order.Id);
                }
                ModelState.AddModelError("", error ?? "تعذر تحديث أمر الشراء");
            }
            catch (DbUpdateConcurrencyException)
            {
                _db.ChangeTracker.Clear();
                ModelState.AddModelError("", "تعذر تعديل أمر الشراء بسبب تعارض في البيانات، حاول مرة أخرى");
            }
        }

        vm.Suppliers = new SelectList(await _db.Suppliers.Where(s => s.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        vm.ItemsData = await _db.Items.Where(i => i.IsActive).AsNoTracking().Include(i => i.CountUnit).Include(i => i.QuantityUnit).ToListAsync();
        return View(vm);
    }

    private static List<PurchaseOrderItem> Lines(IEnumerable<PurchaseOrderLineFormModel> lines)
        => lines.Select(i => i.ToEntity()).ToList();

    [RequirePerm("PurchaseOrders.View")]
    public async Task<IActionResult> Details(string id)
    {
        if (!Guid.TryParse(id, out var publicId))
        {
            return NotFound();
        }

        var targetOrderId = await _db.PurchaseOrders.AsNoTracking()
            .Where(o => o.PublicId == publicId)
            .Select(o => (int?)o.Id)
            .FirstOrDefaultAsync();
        if (targetOrderId == null)
        {
            return NotFound();
        }

        var orderId = targetOrderId.Value;

        var order = await _procurement.GetOrderAsync(orderId);
        if (order == null)
        {
            return NotFound();
        }

        return View(order);
    }

    [RequirePerm("PurchaseOrders.View")]
    public async Task<IActionResult> Print(int id)
    {
        var order = await _procurement.GetOrderAsync(id);
        if (order == null)
        {
            return NotFound();
        }

        return View(order);
    }

    [RequirePerm("PurchaseOrders.View")]
    public async Task<IActionResult> Pdf(int id)
    {
        var order = await _procurement.GetOrderAsync(id);
        if (order == null)
        {
            return NotFound();
        }

        var bytes = PrintPdfBuilder.RenderPurchaseOrderPdf(order);
        return File(bytes, "application/pdf", $"purchase-order-{order.OrderNumber}.pdf");
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("PurchaseOrders.Approve")]
    public async Task<IActionResult> Approve(int id)
    {
        try
        {
            var (ok, error) = await _procurement.ApproveOrderAsync(id);
            if (ok)
            {
                TempData["Success"] = "تم اعتماد أمر الشراء";
            }
            else
            {
                TempData["Error"] = error;
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            TempData["Error"] = "تعذر تعديل أمر الشراء بسبب تعارض في البيانات، حاول مرة أخرى";
        }
        return await RedirectToDetailsAsync(id);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("PurchaseOrders.Approve")]
    public async Task<IActionResult> Cancel(int id)
    {
        try
        {
            var (ok, error) = await _procurement.CancelOrderAsync(id);
            if (ok)
            {
                TempData["Success"] = "تم إلغاء أمر الشراء";
            }
            else
            {
                TempData["Error"] = error;
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            TempData["Error"] = "تعذر تعديل أمر الشراء بسبب تعارض في البيانات، حاول مرة أخرى";
        }
        return await RedirectToDetailsAsync(id);
    }

    [RequirePerm("PurchaseOrders.Receive")]
    public async Task<IActionResult> Receive(int id)
    {
        var order = await _procurement.GetOrderAsync(id);
        if (order == null)
        {
            return NotFound();
        }

        if (order.Status != PurchaseOrderStatus.Approved && order.Status != PurchaseOrderStatus.PartiallyReceived)
        {
            TempData["Error"] = "لا يمكن الاستلام على هذا الأمر";
            return await RedirectToDetailsAsync(id);
        }
        return View(order);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("PurchaseOrders.Receive")]
    public async Task<IActionResult> Receive(int id, int orderItemId, decimal receiveQty, decimal receiveCount)
    {
        try
        {
            var (ok, error) = await _procurement.ReceiveOrderLineAsync(id, orderItemId, receiveQty, receiveCount);
            if (ok)
            {
                TempData["Success"] = "تم تسجيل الاستلام";
            }
            else
            {
                TempData["Error"] = error;
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            TempData["Error"] = "تعذر تعديل أمر الشراء بسبب تعارض في البيانات، حاول مرة أخرى";
        }
        return RedirectToAction(nameof(Receive), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("PurchaseOrders.Receive")]
    public async Task<IActionResult> CreateInvoice(int id)
    {
        try
        {
            var (ok, error) = await _procurement.CreateInvoiceFromOrderAsync(id, User.Identity?.Name);
            if (ok)
            {
                TempData["Success"] = "تم إنشاء فاتورة الشراء من أمر الشراء";
                return RedirectToAction("Index", "Purchases");
            }
            TempData["Error"] = error ?? "تعذر إنشاء الفاتورة";
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            TempData["Error"] = "تعذر تعديل أمر الشراء بسبب تعارض في البيانات، حاول مرة أخرى";
            return await RedirectToDetailsAsync(id);
        }
        return await RedirectToDetailsAsync(id);
    }

    /// <summary>
    /// Details only resolves the public id now, so internal redirects translate the numeric id
    /// they were handed instead of passing it straight through.
    /// </summary>
    private async Task<IActionResult> RedirectToDetailsAsync(int id)
    {
        var publicId = await _db.PurchaseOrders.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => (Guid?)o.PublicId)
            .FirstOrDefaultAsync();
        if (publicId == null)
        {
            return RedirectToAction(nameof(Index));
        }

        return RedirectToAction(nameof(Details), new { id = publicId.Value });
    }
}
