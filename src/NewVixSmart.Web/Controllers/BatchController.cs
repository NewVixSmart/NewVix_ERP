using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Batch;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class BatchController : Controller
{
    private readonly AppDbContext _db;
    private readonly IBatchService _batch;

    public BatchController(AppDbContext db, IBatchService batch)
    {
        _db = db;
        _batch = batch;
    }

    [RequirePerm("Batch.SalesCreate")]
    public async Task<IActionResult> Index()
    {
        var vm = new BatchIndexViewModel
        {
            RecentSales = (await _batch.RecentSalesAsync(10)).ToList(),
            RecentAdjustments = (await _batch.RecentAdjustmentsAsync(10)).ToList()
        };
        return View(vm);
    }

    [RequirePerm("Batch.SalesCreate")]
    public async Task<IActionResult> Sales()
    {
        var vm = new BatchSalesViewModel
        {
        };
        await PopulateSalesAsync(vm);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Batch.SalesCreate")]
    public async Task<IActionResult> Sales(BatchSalesViewModel vm)
    {
        var blocks = (vm.Invoices ?? new List<BatchInvoiceBlock>())
            .Where(b => (b.Items ?? new List<SaleInvoiceItem>())
                .Any(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)))
            .ToList();

        var hasNoCustomer = vm.CustomerId <= 0;

        if (ModelState.IsValid && blocks.Count > 0 && !hasNoCustomer)
        {
            var request = new BatchSalesBatchRequest(
                vm.CustomerId,
                vm.InvoiceDate,
                vm.PaymentTerms,
                vm.Discount,
                vm.Discount2,
                vm.Discount3,
                vm.Tax,
                vm.Notes,
                blocks.Select(b => new BatchSalesInvoice(
                    b.Items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0))
                        .Select(i => new BatchSalesLine(i.ItemId, i.Quantity, i.Count, i.UnitPrice, i.Discount))
                        .ToList())).ToList());

            int? branchId = HttpContext.Session.GetCurrentBranchId();
            var result = await _batch.RunSalesBatchAsync(request, User.Identity?.Name, branchId);

            return View("Results", new BatchResultsViewModel
            {
                Title = "نتائج المبيعات الجماعية",
                Results = result.Results,
                BackAction = nameof(Sales)
            });
        }

        if (blocks.Count == 0)
        {
            ModelState.AddModelError("", "لا توجد فواتير صالحة — أضف صنفًا واحدًا على الأقل لكل فاتورة");
        }

        vm.Invoices = blocks.Count > 0 ? blocks : new List<BatchInvoiceBlock> { new() };
        await PopulateSalesAsync(vm);
        return View(vm);
    }

    [RequirePerm("Batch.AdjustmentCreate")]
    public async Task<IActionResult> Adjustment()
    {
        var vm = new BatchAdjustmentViewModel
        {
            ItemsData = await ItemsDataAsync()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("Batch.AdjustmentCreate")]
    public async Task<IActionResult> Adjustment(BatchAdjustmentViewModel vm)
    {
        var lines = (vm.Lines ?? new List<BatchAdjustmentLineInput>())
            .Where(l => l.ItemId > 0)
            .ToList();

        if (ModelState.IsValid && lines.Count > 0)
        {
            var request = new BatchAdjustmentRequest(
                vm.AdjustDate,
                vm.Reason,
                lines.Select(l => new BatchAdjustmentLine(l.ItemId, l.NewCount, l.NewQuantity)).ToList());

            var result = await _batch.RunAdjustmentBatchAsync(request, User.Identity?.Name);

            return View("Results", new BatchResultsViewModel
            {
                Title = "نتائج الجرد الجماعي",
                Results = result.Results,
                BackAction = nameof(Adjustment)
            });
        }

        if (lines.Count == 0)
        {
            ModelState.AddModelError("", "لا توجد أصناف صالحة للجرد — أضف صنفًا واحدًا على الأقل");
        }

        vm.Lines = lines.Count > 0 ? lines : new List<BatchAdjustmentLineInput> { new() };
        vm.ItemsData = await ItemsDataAsync();
        return View(vm);
    }

    private async Task PopulateSalesAsync(BatchSalesViewModel vm)
    {
        vm.Customers = new SelectList(
            await _db.Customers.Where(c => c.IsActive).AsNoTracking().ToListAsync(), "Id", "Name");
        vm.ItemsData = await ItemsDataAsync();
    }

    private Task<List<NewVixSmart.Web.Models.Core.Item>> ItemsDataAsync() =>
        _db.Items.Include(i => i.CountUnit).Include(i => i.QuantityUnit)
            .Where(i => i.IsActive).AsNoTracking().ToListAsync();
}
