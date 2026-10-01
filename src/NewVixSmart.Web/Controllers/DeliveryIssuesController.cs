using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Forms;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Sales;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class DeliveryIssuesController : Controller
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;

    public DeliveryIssuesController(AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    [RequirePerm("DeliveryIssues.View")]
    public async Task<IActionResult> Index(DeliveryIssueStatus? status = null, int? deliveryOrderId = null)
    {
        var query = _db.DeliveryIssues
            .Include(i => i.DeliveryOrder)
            .Include(i => i.SalesOrder)
            .Include(i => i.SaleInvoice)
            .Include(i => i.Customer)
            .AsNoTracking()
            .AsQueryable();
        if (status.HasValue)
        {
            query = query.Where(i => i.Status == status.Value);
        }

        if (deliveryOrderId.HasValue)
        {
            query = query.Where(i => i.DeliveryOrderId == deliveryOrderId.Value);
        }

        var list = await query.OrderByDescending(i => i.Id).Take(500).ToListAsync();

        ViewBag.StatusFilter = status;
        ViewBag.DeliveryOrderId = deliveryOrderId;
        return View(list);
    }

    [RequirePerm("DeliveryIssues.View")]
    public async Task<IActionResult> Details(string id)
    {
        var issue = await LoadAsync(id);
        if (issue == null)
        {
            return NotFound();
        }

        return View(issue);
    }

    [RequirePerm("DeliveryIssues.View")]
    public async Task<IActionResult> Print(string id)
    {
        var issue = await LoadAsync(id);
        if (issue == null)
        {
            return NotFound();
        }

        return View(issue);
    }

    [RequirePerm("DeliveryIssues.Create")]
    public async Task<IActionResult> Create(int? deliveryOrderId = null)
    {
        var notes = await OpenNotesAsync();
        var vm = new DeliveryIssueViewModel
        {
            NoteOptions = notes,
            Notes = new SelectList(
                new[] { new { Id = 0, Label = "— اختر الأذن —" } }
                    .Concat(notes.Select(n => new
                    {
                        n.Id,
                        Label = $"{n.DeliveryNumber} — {(n.SalesOrder?.OrderNumber ?? "بدون أمر بيع")} — متبقٍ {DeliveryOpenLines.OpenQuantity(n):N2}"
                    })),
                "Id", "Label", deliveryOrderId)
        };

        if (deliveryOrderId.HasValue)
        {
            var note = notes.FirstOrDefault(n => n.Id == deliveryOrderId.Value);
            if (note != null)
            {
                vm.Issue.DeliveryOrderId = note.Id;
                vm.Issue.CustomerId = note.CustomerId;
                vm.NoteLines = note.Items.ToList();
                vm.Items = note.Items
                    .Where(i => i.Quantity > 0 || i.Count > 0)
                    .Select(i => new DeliveryIssueLineFormModel
                    {
                        ItemId = i.ItemId,
                        DeliveryOrderItemId = i.Id,
                        Quantity = i.Quantity,
                        Count = i.Count
                    })
                    .ToList();
            }
        }
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("DeliveryIssues.Create")]
    public async Task<IActionResult> Create(DeliveryIssueViewModel vm)
    {
        vm.Items = vm.Items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
        ModelState.IgnoreEmptyLineItemRows();

        // The page validates its own rules with TempData messages, so only a malformed
        // payload is an error here. Swallowing an invalid ModelState would drop the
        // delivery order the operator picked and silently restart the form.
        if (!ModelState.IsValid)
        {
            vm.Items = new List<DeliveryIssueLineFormModel>();
            TempData["Error"] = "بيانات الأمر غير صحيحة، راجع السطور المدخلة";
            return await Create(vm.Issue.DeliveryOrderId);
        }

        if (vm.Issue.DeliveryOrderId <= 0)
        {
            TempData["Error"] = "اختر أذن التسليم";
            return await Create();
        }
        if (vm.Items.Count == 0)
        {
            TempData["Error"] = "أضف صنفاً واحداً على الأقل";
            return await Create(vm.Issue.DeliveryOrderId);
        }

        foreach (var line in vm.Items)
        {
            if (!TryQuantize(line.Quantity, out var quantity)
                || !TryQuantize(line.Count, out var count))
            {
                TempData["Error"] = _quantityStepError;
                return await Create(vm.Issue.DeliveryOrderId);
            }
            line.Quantity = quantity;
            line.Count = count;
        }

        var (ok, error, issue) = await _inventory.CreateDeliveryIssueAsync(vm.Issue.DeliveryOrderId,
            vm.Items.Select(i => i.ToEntity()).ToList(),
            User.Identity?.Name, vm.Issue.IssueDate, vm.Issue.Notes, vm.Issue.Carrier, vm.Issue.TrackingNumber);
        if (!ok)
        {
            TempData["Error"] = error ?? "تعذر إنشاء أمر التسليم";
            return await Create(vm.Issue.DeliveryOrderId);
        }

        TempData["Success"] = $"تم إنشاء أمر التسليم {issue!.IssueNumber}";
        return RedirectToAction(nameof(Details), new { id = issue.PublicId });
    }

    /// <summary>
    /// Operator-facing message for a posted quantity that is finer than the grid can store.
    /// </summary>
    private const string _quantityStepError =
        "الكمية والعدد يجب أن تكونا بأربع خانات عشرية كحدٍّ أقصى — أصغر خطوة يمكن تسجيلها هي 0.0001";

    /// <summary>
    /// The window this input check allows around the grid: none, deliberately.
    /// <para>
    /// This is not <see cref="DeliveryOpenLines.QuantityTolerance"/>, and it must never be replaced by
    /// it. That constant answers a different question — "is this physical line finished?" — and its
    /// half-step window earns its keep there, absorbing arithmetic that has already passed through a
    /// lossy step: a sum, a derived unit cost, an allocation residual. This check asks something else
    /// entirely: "is what the operator typed a value this store can hold?"
    /// </para>
    /// <para>
    /// Nothing legitimate arrives off-grid here. The posted figure comes from a field whose
    /// <c>step</c> is 0.0001, so the browser has already constrained it to multiples of that step before
    /// it is submitted, and <c>decimal(18,4)</c> holds one quantum exactly. A tolerance could therefore
    /// only ever do one thing: rewrite a figure the operator did not type. It would also be
    /// unreachable as a check — a decimal is never further than half a step from the nearest grid point,
    /// so a half-step tolerance accepts every value there is, and the error message below could never be
    /// shown.
    /// </para>
    /// </summary>
    private const decimal _inputGridTolerance = 0m;

    /// <summary>
    /// Confirms one posted quantity or count is already on the store's own grid, and hands it back
    /// unchanged.
    /// <para>
    /// A quantity or count column is <c>decimal(18,4)</c>, so the smallest difference two stored
    /// figures can show is 0.0001. <c>decimal</c> is base ten, so every multiple of that step — including
    /// <c>0.0001</c> itself — is exactly representable, and comparing against the rounded value is an
    /// exact test of divisibility rather than an approximation. This matters: a check written in
    /// <c>double</c> would test divisibility by multiplying by 10000, and <c>(double)0.0003 * 10000</c>
    /// is 2.9999999999999996, not 3. Five hundred and fifty-eight of the first four thousand four-decimal
    /// values fail that way, so the arithmetic here has to stay in <see cref="decimal"/>.
    /// </para>
    /// <para>
    /// A value that is not on the grid is refused rather than rounded, because the previous behaviour
    /// here was <c>decimal.Round(x, 2)</c>: it did not merely lose digits, it changed the delivery. An
    /// issue of 99.9957 against an ordered 100 became 100.00 and the order read as fully delivered, or
    /// became 99.99 and left a hundredth owed that no one ever shipped. Rounding 99.99575 to 99.9958
    /// would be the same fault one decimal place in: reporting a delivery the operator did not make.
    /// </para>
    /// </summary>
    private static bool TryQuantize(decimal value, out decimal onGrid)
    {
        onGrid = decimal.Round(value, DecimalPrecision.QuantityScale, MidpointRounding.AwayFromZero);
        return Math.Abs(value - onGrid) <= _inputGridTolerance;
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("DeliveryIssues.Issue")]
    public async Task<IActionResult> Issue(string id)
    {
        if (!Guid.TryParse(id, out var publicId))
        {
            return NotFound();
        }

        var issue = await _db.DeliveryIssues.AsNoTracking().FirstOrDefaultAsync(i => i.PublicId == publicId);
        if (issue == null)
        {
            return NotFound();
        }

        var (ok, error) = await _inventory.IssueDeliveryAsync(issue.Id, User.Identity?.Name);
        if (!ok)
        {
            TempData["Error"] = error ?? "تعذر ترحيل أمر التسليم";
        }
        else
        {
            TempData["Success"] = $"تم ترحيل أمر التسليم {issue.IssueNumber} وخصم المخزون";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("DeliveryIssues.Issue")]
    public async Task<IActionResult> Cancel(string id)
    {
        if (!Guid.TryParse(id, out var publicId))
        {
            return NotFound();
        }

        var issue = await _db.DeliveryIssues.AsNoTracking().FirstOrDefaultAsync(i => i.PublicId == publicId);
        if (issue == null)
        {
            return NotFound();
        }

        var (ok, error) = await _inventory.CancelDeliveryIssueAsync(issue.Id, User.Identity?.Name);
        if (!ok)
        {
            TempData["Error"] = error ?? "تعذر إلغاء أمر التسليم";
        }
        else
        {
            TempData["Success"] = $"تم إلغاء أمر التسليم {issue.IssueNumber}";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private Task<DeliveryIssue?> LoadAsync(string id)
        => Guid.TryParse(id, out var publicId)
            ? _db.DeliveryIssues
                .Include(i => i.DeliveryOrder)
                .Include(i => i.SalesOrder)
                .Include(i => i.SaleInvoice)
                .Include(i => i.Customer)
                .Include(i => i.Items).ThenInclude(l => l.Item)
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.PublicId == publicId)
            : Task.FromResult<DeliveryIssue?>(null);

    private async Task<List<DeliveryOrder>> OpenNotesAsync()
    {
        var notes = await _db.DeliveryOrders
            .Include(d => d.SalesOrder)
            .Include(d => d.Items)
            .Include(d => d.Issues).ThenInclude(i => i.Items)
            .Where(d => d.Status != DeliveryOrderStatus.Cancelled && d.SaleInvoiceId == null)
            .AsNoTracking()
            .ToListAsync();
        // The same predicate the delivery order details screen uses to decide whether the note is
        // still open, so a note can never be offered here while that screen calls it complete. This
        // method used to end with a private `OpenQuantity` that compared issued quantity only, which
        // made a wholly unissued count-traded line look like zero outstanding.
        return notes.Where(DeliveryOpenLines.HasOutstandingLines).ToList();
    }
}
