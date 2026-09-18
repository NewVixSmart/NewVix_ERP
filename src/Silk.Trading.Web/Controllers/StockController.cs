using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Extensions;
using Silk.Trading.Web.Models.Core;
using Silk.Trading.Web.Models.Stock;
using Silk.Trading.Web.Services;
using Silk.Trading.Web.ViewModels.Stock;

namespace Silk.Trading.Web.Controllers;

[Authorize]
public class StockController : Controller
{
    private readonly AppDbContext _db;
    private readonly IDashboardService _dashboardService;

    public StockController(AppDbContext db, IDashboardService dashboardService)
    {
        _db = db;
        _dashboardService = dashboardService;
    }

    [RequirePerm("Stock.View")]
    public async Task<IActionResult> Index(int? itemId, MovementType? type)
    {
        var query = _db.StockMovements.Include(s => s.Item).AsNoTracking().AsQueryable();

        if (itemId.HasValue) query = query.Where(s => s.ItemId == itemId.Value);
        if (type.HasValue && Enum.IsDefined(typeof(MovementType), type.Value))
            query = query.Where(s => s.Type == type.Value);

        ViewBag.ItemId = itemId;
        ViewBag.Type = type?.ToString();

        var vm = new StockIndexViewModel
        {
            Movements = await query.OrderByDescending(s => s.MovementDate).ToListAsync(),
            Items = await _db.Items.Where(i => i.IsActive).AsNoTracking().ToListAsync(),
            ItemId = itemId,
            Type = type
        };
        return View(vm);
    }

    [RequirePerm("StockReport.View")]
    public async Task<IActionResult> Report(int? categoryId, bool lowOnly)
    {
        var query = _db.Items.Include(i => i.Category).Include(i => i.CountUnit).Include(i => i.QuantityUnit).Include(i => i.ItemType)
            .Where(i => i.IsActive).AsNoTracking().AsQueryable();

        if (categoryId.HasValue)
            query = query.Where(i => i.CategoryId == categoryId.Value);

        if (lowOnly)
            query = query.Where(i => (i.CountUnitId.HasValue && i.MinCount > 0 && i.CurrentCount < i.MinCount) || (i.QuantityUnitId.HasValue && i.MinQuantity > 0 && i.CurrentQuantity < i.MinQuantity));

        var total = await query.CountAsync();
        ViewBag.CategoryId = categoryId;
        ViewBag.LowOnly = lowOnly;

        var vm = new StockReportViewModel
        {
            Items = await query.OrderBy(i => i.Name).ToListAsync(),
            Categories = await _db.ItemCategories.Where(c => c.IsActive).AsNoTracking().ToListAsync(),
            CategoryId = categoryId,
            LowOnly = lowOnly,
            TotalItems = total,
            LowItems = lowOnly ? total : await query.CountAsync(i => (i.CurrentCount <= i.MinCount && i.MinCount > 0) || (i.CurrentQuantity <= i.MinQuantity && i.MinQuantity > 0)),
            TotalCount = await query.SumAsync(i => i.CurrentCount),
            TotalQuantity = await query.SumAsync(i => i.CurrentQuantity),
            TotalValue = await query.SumAsync(i => (i.QuantityUnitId.HasValue || i.CurrentQuantity > 0)
                ? i.CurrentQuantity * i.PurchasePrice
                : i.CurrentCount * i.PurchasePrice)
        };
        return View(vm);
    }

    [RequirePerm("Stock.View")]
    public async Task<IActionResult> LowStock()
    {
        return View(await _dashboardService.GetLowStockItemsAsync());
    }
}
