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
        if (status.HasValue) query = query.Where(i => i.Status == status.Value);
        if (deliveryOrderId.HasValue) query = query.Where(i => i.DeliveryOrderId == deliveryOrderId.Value);
        var list = await query.OrderByDescending(i => i.Id).Take(500).ToListAsync();

        ViewBag.StatusFilter = status;
        ViewBag.DeliveryOrderId = deliveryOrderId;
        return View(list);
    }

    [RequirePerm("DeliveryIssues.View")]
    public async Task<IActionResult> Details(string id)
    {
        var issue = await LoadAsync(id);
        if (issue == null) return NotFound();
        return View(issue);
    }

    [RequirePerm("DeliveryIssues.View")]
    public async Task<IActionResult> Print(string id)
    {
        var issue = await LoadAsync(id);
        if (issue == null) return NotFound();
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
                        Label = $"{n.DeliveryNumber} — {(n.SalesOrder?.OrderNumber ?? "بدون أمر بيع")} — متبقٍ {OpenQuantity(n):N2}"
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
                    .Select(i => new DeliveryIssueItem
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
            line.Quantity = decimal.Round(line.Quantity, 2);
            line.Count = decimal.Round(line.Count, 2);
        }

        var (ok, error, issue) = await _inventory.CreateDeliveryIssueAsync(vm.Issue.DeliveryOrderId, vm.Items,
            User.Identity?.Name, vm.Issue.IssueDate, vm.Issue.Notes, vm.Issue.Carrier, vm.Issue.TrackingNumber);
        if (!ok)
        {
            TempData["Error"] = error ?? "تعذر إنشاء أمر التسليم";
            return await Create(vm.Issue.DeliveryOrderId);
        }

        TempData["Success"] = $"تم إنشاء أمر التسليم {issue!.IssueNumber}";
        return RedirectToAction(nameof(Details), new { id = issue.PublicId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("DeliveryIssues.Issue")]
    public async Task<IActionResult> Issue(string id)
    {
        if (!Guid.TryParse(id, out var publicId)) return NotFound();
        var issue = await _db.DeliveryIssues.AsNoTracking().FirstOrDefaultAsync(i => i.PublicId == publicId);
        if (issue == null) return NotFound();

        var (ok, error) = await _inventory.IssueDeliveryAsync(issue.Id, User.Identity?.Name);
        if (!ok) TempData["Error"] = error ?? "تعذر ترحيل أمر التسليم";
        else TempData["Success"] = $"تم ترحيل أمر التسليم {issue.IssueNumber} وخصم المخزون";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("DeliveryIssues.Issue")]
    public async Task<IActionResult> Cancel(string id)
    {
        if (!Guid.TryParse(id, out var publicId)) return NotFound();
        var issue = await _db.DeliveryIssues.AsNoTracking().FirstOrDefaultAsync(i => i.PublicId == publicId);
        if (issue == null) return NotFound();

        var (ok, error) = await _inventory.CancelDeliveryIssueAsync(issue.Id, User.Identity?.Name);
        if (!ok) TempData["Error"] = error ?? "تعذر إلغاء أمر التسليم";
        else TempData["Success"] = $"تم إلغاء أمر التسليم {issue.IssueNumber}";
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
        return notes.Where(n => OpenQuantity(n) > 0.005m).ToList();
    }

    private static decimal OpenQuantity(DeliveryOrder note)
    {
        var issued = note.Issues
            .Where(i => i.Status != DeliveryIssueStatus.Cancelled)
            .SelectMany(i => i.Items)
            .GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        return note.Items.Sum(l => l.Quantity - (issued.TryGetValue(l.ItemId, out var q) ? q : 0m));
    }
}
