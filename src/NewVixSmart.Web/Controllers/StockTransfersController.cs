using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Forms;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Stock;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class StockTransfersController : Controller
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    public StockTransfersController(AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    [RequirePerm("StockTransfers.View")]
    public async Task<IActionResult> Index()
    {
        var list = await _inventory.GetTransfersAsync();
        return View(list);
    }

    [RequirePerm("StockTransfers.Create")]
    public async Task<IActionResult> Create()
    {
        return View(await BuildAsync(new StockTransferFormViewModel()));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequirePerm("StockTransfers.Create")]
    public async Task<IActionResult> Create(StockTransferFormViewModel vm)
    {
        vm.Transfer ??= new StockTransferFormModel();
        vm.Items ??= new List<StockTransferLineFormModel>();
        ModelState.IgnoreEmptyLineItemRows();

        var filled = vm.Items
            .Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0))
            .ToList();

        if (!ModelState.IsValid)
        {
            vm.Items = vm.Items.Count > 0 ? vm.Items : [new StockTransferLineFormModel()];
            return View(await BuildAsync(vm));
        }

        if (filled.Count == 0)
        {
            ModelState.AddModelError("", "يرجى إضافة صنف واحد على الأقل");
            vm.Items = vm.Items.Count > 0 ? vm.Items : [new StockTransferLineFormModel()];
            return View(await BuildAsync(vm));
        }

        var (ok, error) = await _inventory.CreateTransferAsync(
            vm.Transfer.ToEntity(), filled.Select(i => i.ToEntity()).ToList(), User.Identity?.Name);
        if (ok)
        {
            TempData["Success"] = "تم تنفيذ التحويل بنجاح";
            return RedirectToAction(nameof(Index));
        }

        ModelState.AddModelError("", error ?? "تعذر حفظ التحويل");
        vm.Items = vm.Items.Count > 0 ? vm.Items : [new StockTransferLineFormModel()];
        return View(await BuildAsync(vm));
    }

    [RequirePerm("StockTransfers.View")]
    public async Task<IActionResult> Print(int id)
    {
        var transfer = await _db.StockTransfers
            .Include(t => t.SourceWarehouse)
            .Include(t => t.TargetWarehouse)
            .Include(t => t.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
.Include(t => t.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);
        if (transfer == null)
        {
            return NotFound();
        }

        return View(transfer);
    }

    [RequirePerm("StockTransfers.View")]
    public async Task<IActionResult> Pdf(int id)
    {
        var transfer = await _db.StockTransfers
            .Include(t => t.SourceWarehouse)
            .Include(t => t.TargetWarehouse)
            .Include(t => t.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
.Include(t => t.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);
        if (transfer == null)
        {
            return NotFound();
        }

        var bytes = PrintPdfBuilder.RenderStockTransferPdf(transfer);
        return File(bytes, "application/pdf", $"stock-transfer-{transfer.TransferNumber}.pdf");
    }

    private async Task<StockTransferFormViewModel> BuildAsync(StockTransferFormViewModel vm)
    {
        vm.Warehouses = new SelectList(
            await _db.Warehouses.Where(w => w.IsActive).AsNoTracking().ToListAsync(), "Id", "Name",
            vm.Transfer.SourceWarehouseId);
        vm.ItemsData = await _db.Items.Include(i => i.CountUnit).Include(i => i.QuantityUnit)
            .Where(i => i.IsActive).AsNoTracking().ToListAsync();
        return vm;
    }
}
