using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Sales;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class SalesOrdersController : Controller
{
    private readonly AppDbContext _db;
    private readonly ISalesOrdersService _orders;
    private readonly IStockReservationsService _reservations;

    public SalesOrdersController(AppDbContext db, ISalesOrdersService orders, IStockReservationsService reservations)
    {
        _db = db;
        _orders = orders;
        _reservations = reservations;
    }

    [RequirePerm("SalesOrders.View")]
    public async Task<IActionResult> Index(SalesOrderStatus? status = null)
    {
        var orders = await _orders.GetOrdersAsync(status);
        ViewBag.StatusFilter = status;

        var counts = await _db.SalesOrders.AsNoTracking()
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);
        ViewBag.StatusCounts = counts;
        ViewBag.TotalCount = counts.Values.Sum();

        return View(orders);
    }

    [RequirePerm("SalesOrders.Create")]
    public async Task<IActionResult> Create(int? fromQuoteId)
    {
        var vm = new SalesOrderViewModel
        {
            Customers = new SelectList(await CustomersAsync(), "Id", "Name"),
            ItemsData = await ItemsAsync(),
            Order = new SalesOrder
            {
                OrderDate = DateTime.Today,
            }
        };

        if (fromQuoteId.HasValue)
        {
            var quote = await GetQuoteAsync(fromQuoteId.Value);
            if (quote != null)
            {
                vm.Order.CustomerId = quote.CustomerId;
                vm.Order.Notes = quote.Notes;
                vm.Order.SaleQuoteId = quote.Id;
                foreach (var l in quote.Items)
                {
                    vm.Items.Add(new SalesOrderItem { ItemId = l.ItemId, Quantity = l.Quantity, Count = l.Count, UnitPrice = l.UnitPrice });
                }
            }
        }

        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("SalesOrders.Create")]
    public async Task<IActionResult> Create(SalesOrderViewModel vm)
    {
        vm.Order ??= new SalesOrder();
        vm.Items ??= new List<SalesOrderItem>();
        ModelState.IgnoreEmptyLineItemRows();

        if (ModelState.IsValid && vm.Items.Any(i => i.ItemId > 0))
        {
            var (ok, error) = await _orders.CreateOrderAsync(vm.Order, vm.Items, User.Identity?.Name);
            if (ok)
            {
                TempData["Success"] = "تم إنشاء أمر البيع بنجاح";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError("", error ?? "تعذر حفظ أمر البيع");
        }

        await Populate(vm);
        return View(vm);
    }

    [RequirePerm("SalesOrders.Edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var order = await _orders.GetOrderAsync(id);
        if (order == null)
        {
            return NotFound();
        }

        if (order.Status != SalesOrderStatus.Draft)
        {
            TempData["Error"] = "لا يمكن تعديل أمر بيع غير مسودة";
            return RedirectToAction(nameof(Details), new { id = order.PublicId });
        }

        var vm = new SalesOrderViewModel
        {
            Order = order,
            Items = order.Items.ToList()
        };
        await Populate(vm);
        return View("Create", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("SalesOrders.Edit")]
    public async Task<IActionResult> Edit(SalesOrderViewModel vm)
    {
        vm.Order ??= new SalesOrder();
        vm.Items ??= new List<SalesOrderItem>();
        ModelState.IgnoreEmptyLineItemRows();

        if (ModelState.IsValid && vm.Items.Any(i => i.ItemId > 0))
        {
            var (ok, error) = await _orders.UpdateOrderAsync(vm.Order, vm.Items, User.Identity?.Name);
            if (ok)
            {
                TempData["Success"] = "تم تحديث أمر البيع بنجاح";
                return RedirectToAction(nameof(Details), new { id = vm.Order.PublicId });
            }
            ModelState.AddModelError("", error ?? "تعذر تحديث أمر البيع");
        }

        await Populate(vm);
        return View("Create", vm);
    }

    [RequirePerm("SalesOrders.View")]
    public async Task<IActionResult> Details(string id)
    {
        if (!Guid.TryParse(id, out var publicId))
        {
            return NotFound();
        }

        var targetOrderId = await _db.SalesOrders.AsNoTracking()
            .Where(o => o.PublicId == publicId)
            .Select(o => (int?)o.Id)
            .FirstOrDefaultAsync();
        if (targetOrderId == null)
        {
            return NotFound();
        }

        var orderId = targetOrderId.Value;

        var order = await _orders.GetOrderAsync(orderId);
        if (order == null)
        {
            return NotFound();
        }

        ViewBag.Reservation = await _reservations.GetForOrderAsync(order.Id);
        ViewBag.Invoices = await _db.SaleInvoices.AsNoTracking()
            .Where(i => i.SalesOrderId == order.Id)
            .OrderByDescending(i => i.Id)
            .ToListAsync();
        return View(order);
    }

    [RequirePerm("SalesOrders.View")]
    public async Task<IActionResult> Print(int id)
    {
        var order = await _orders.GetOrderAsync(id);
        if (order == null)
        {
            return NotFound();
        }

        return View(order);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("SalesOrders.Approve")]
    public async Task<IActionResult> Approve(int id)
    {
        var (ok, error) = await _orders.ApproveOrderAsync(id);
        if (ok)
        {
            TempData["Success"] = "تم اعتماد أمر البيع";
        }
        else
        {
            TempData["Error"] = error;
        }

        return await RedirectToDetailsAsync(id);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("SalesOrders.Approve")]
    public async Task<IActionResult> Cancel(int id)
    {
        var (ok, error) = await _orders.CancelOrderAsync(id);
        if (ok)
        {
            TempData["Success"] = "تم إلغاء أمر البيع";
        }
        else
        {
            TempData["Error"] = error;
        }

        return await RedirectToDetailsAsync(id);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("SalesOrders.Convert")]
    public async Task<IActionResult> CreateInvoice(int id)
    {
        var (ok, error, invoice) = await _orders.InvoiceOutstandingDeliveriesAsync(id, User.Identity?.Name);
        if (ok)
        {
            TempData["Success"] = $"تم إنشاء فاتورة رقم {invoice?.InvoiceNumber} بالكميات المسلَّمة فقط (غير مسدّدة)";
            return RedirectToAction("Index", "Sales");
        }
        TempData["Error"] = error ?? "تعذر إنشاء الفاتورة";
        return await RedirectToDetailsAsync(id);
    }

    /// <summary>
    /// Details only resolves the public id now, so internal redirects translate the numeric id
    /// they were handed instead of passing it straight through.
    /// </summary>
    private async Task<IActionResult> RedirectToDetailsAsync(int id)
    {
        var publicId = await _db.SalesOrders.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => (Guid?)o.PublicId)
            .FirstOrDefaultAsync();
        if (publicId == null)
        {
            return RedirectToAction(nameof(Index));
        }

        return RedirectToAction(nameof(Details), new { id = publicId.Value });
    }

    private async Task Populate(SalesOrderViewModel vm)
    {
        vm.Customers = new SelectList(await CustomersAsync(), "Id", "Name", vm.Order.CustomerId);
        vm.ItemsData = await ItemsAsync();
    }

    private async Task<List<Customer>> CustomersAsync()
        => await _db.Customers.Where(c => c.IsActive).AsNoTracking().ToListAsync();

    private async Task<List<Item>> ItemsAsync()
        => await _db.Items.Where(i => i.IsActive && i.IsSellable).AsNoTracking()
            .Include(i => i.CountUnit).Include(i => i.QuantityUnit).ToListAsync();

    private async Task<SaleQuote?> GetQuoteAsync(int id)
        => await _db.SaleQuotes.AsNoTracking().Include(q => q.Items)
            .FirstOrDefaultAsync(q => q.Id == id);
}
