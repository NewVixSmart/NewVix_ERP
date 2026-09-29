using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Stock;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class StockReservationsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IStockReservationsService _reservations;

    public StockReservationsController(AppDbContext db, IStockReservationsService reservations)
    {
        _db = db;
        _reservations = reservations;
    }

    [RequirePerm("StockReservations.View")]
    public async Task<IActionResult> Index(StockReservationStatus? status = null)
    {
        var query = _db.StockReservations
            .Include(r => r.Customer)
            .Include(r => r.SalesOrder)
            .Include(r => r.Items)
            .AsNoTracking()
            .AsQueryable();
        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);
        var list = await query.OrderByDescending(r => r.Id).Take(500).ToListAsync();
        ViewBag.StatusFilter = status;
        return View(list);
    }

    [RequirePerm("StockReservations.View")]
    public async Task<IActionResult> Details(string id)
    {
        if (!Guid.TryParse(id, out var publicId)) return NotFound();
        var reservation = await _db.StockReservations
            .Include(r => r.Customer)
            .Include(r => r.SalesOrder)
            .Include(r => r.Items).ThenInclude(i => i.Item)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.PublicId == publicId);
        if (reservation == null) return NotFound();

        var itemIds = reservation.Items.Select(i => i.ItemId).Distinct().ToList();
        ViewBag.Availability = (await _reservations.GetAvailabilityAsync(itemIds)).ToDictionary(a => a.ItemId);
        return View(reservation);
    }

    [RequirePerm("StockReservations.Create")]
    public async Task<IActionResult> Create()
    {
        var vm = new StockReservationViewModel
        {
            Customers = new SelectList(await CustomersAsync(), "Id", "Name"),
            Availability = (await _reservations.GetAvailabilityAsync(await ItemIdsAsync())).ToList()
        };
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("StockReservations.Create")]
    public async Task<IActionResult> Create(StockReservationViewModel vm)
    {
        vm.StandaloneItems = vm.StandaloneItems.Where(l => l.ItemId > 0).ToList();
        if (vm.StandaloneItems.Count == 0)
        {
            TempData["Error"] = "أضف صنفاً واحداً على الأقل";
            return await Create();
        }

        foreach (var line in vm.StandaloneItems)
        {
            if (!TryQuantize(line.Quantity, out var quantity)
                || !TryQuantize(line.Count, out var count))
            {
                TempData["Error"] = QuantityStepError;
                return await Create();
            }
            line.Quantity = quantity;
            line.Count = count;
            if (line.Quantity < 0 || line.Count < 0)
            {
                TempData["Error"] = "الكميات لا يمكن أن تكون بالسالب";
                return await Create();
            }
        }

        var availability = (await _reservations.GetAvailabilityAsync(vm.StandaloneItems.Select(l => l.ItemId).Distinct()))
            .ToDictionary(a => a.ItemId);
        foreach (var line in vm.StandaloneItems)
        {
            if (!availability.TryGetValue(line.ItemId, out var a)) continue;
            if (line.Quantity > a.AvailableQuantity || line.Count > a.AvailableCount)
            {
                TempData["Error"] = $"الكمية المطلوبة من «{a.ItemName}» أكبر من المتاح ({a.AvailableQuantity:N2} / {a.AvailableCount:N2})";
                return await Create();
            }
        }

        var reservation = new StockReservation
        {
            CustomerId = vm.Reservation.CustomerId,
            Reason = vm.Reservation.Reason,
            Notes = vm.Reservation.Notes
        };
        var (ok, error, created) = await _reservations.CreateStandaloneAsync(
            reservation, vm.StandaloneItems, User.Identity?.Name);
        if (!ok)
        {
            TempData["Error"] = error ?? "تعذر إنشاء الحجز";
            return await Create();
        }

        TempData["Success"] = $"تم إنشاء الحجز {created!.ReservationNumber}";
        return RedirectToAction(nameof(Details), new { id = created.PublicId });
    }

    /// <summary>
    /// Operator-facing message for a posted quantity that is finer than the grid can store.
    /// </summary>
    private const string QuantityStepError =
        "الكمية والعدد يجب أن تكونا بأربع خانات عشرية كحدٍّ أقصى — أصغر خطوة يمكن تسجيلها هي 0.0001";

    /// <summary>
    /// The window this input check allows around the grid: none, deliberately.
    /// <para>
    /// This is not <see cref="DeliveryOpenLines.QuantityTolerance"/>, and it must never be replaced by
    /// it. That constant answers "is this physical line finished?" and its half-step window absorbs
    /// arithmetic that has already passed through a lossy step. This check asks "is what the operator
    /// typed a value this store can hold?", and nothing legitimate arrives off-grid here: the posted
    /// figure comes from a field whose <c>step</c> is 0.0001, so the browser has already constrained it
    /// to multiples of that step, and <c>decimal(18,4)</c> holds one quantum exactly. A tolerance could
    /// only ever rewrite a figure the operator did not type — and as a check it would be unreachable, a
    /// decimal being never further than half a step from the nearest grid point.
    /// </para>
    /// </summary>
    private const decimal InputGridTolerance = 0m;

    /// <summary>
    /// Confirms one posted quantity or count is already on the store's own grid, and hands it back
    /// unchanged. <c>StockReservationLine.Quantity</c>/<c>Count</c> are <c>decimal(18,4)</c>, so the grid
    /// is 0.0001, and <c>decimal</c> being base ten makes every multiple of it exactly representable —
    /// the comparison against the rounded value is an exact test of divisibility, not an approximation.
    /// </summary>
    private static bool TryQuantize(decimal value, out decimal onGrid)
    {
        onGrid = decimal.Round(value, DecimalPrecision.QuantityScale, MidpointRounding.AwayFromZero);
        return Math.Abs(value - onGrid) <= InputGridTolerance;
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("StockReservations.Release")]
    public async Task<IActionResult> Release(string id)
    {
        if (!Guid.TryParse(id, out var publicId)) return NotFound();
        var reservation = await _db.StockReservations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.PublicId == publicId);
        if (reservation == null) return NotFound();

        var (ok, error) = await _reservations.ReleaseAsync(reservation.Id, User.Identity?.Name);
        if (!ok) TempData["Error"] = error ?? "تعذر تحرير الحجز";
        else TempData["Success"] = $"تم تحرير الحجز {reservation.ReservationNumber}";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<List<Customer>> CustomersAsync()
        => await _db.Customers.Where(c => c.IsActive).AsNoTracking().OrderBy(c => c.Name).ToListAsync();

    private async Task<List<int>> ItemIdsAsync()
        => await _db.Items.Where(i => i.IsActive).Select(i => i.Id).ToListAsync();
}
