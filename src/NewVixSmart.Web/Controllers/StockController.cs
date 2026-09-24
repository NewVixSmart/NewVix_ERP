using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Stock;

namespace NewVixSmart.Web.Controllers;

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
    public async Task<IActionResult> Index(int? itemId, MovementType? type, int page = 1, string? search = null)
    {
        var query = _db.StockMovements.Include(s => s.Item).AsNoTracking().AsQueryable();

        if (itemId.HasValue) query = query.Where(s => s.ItemId == itemId.Value);
        if (type.HasValue && Enum.IsDefined(typeof(MovementType), type.Value))
            query = query.Where(s => s.Type == type.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(s => (s.DocumentNumber != null && s.DocumentNumber.Contains(term)) || s.Item.Name.Contains(term));
        }
        query = query.OrderByDescending(s => s.MovementDate);

        var total = await query.CountAsync();
        page = PagerExtensions.NormalizePage(page, total);
        var movements = await query
            .Skip((page - 1) * PagerExtensions.PageSize)
            .Take(PagerExtensions.PageSize)
            .ToListAsync();

        ViewBag.ItemId = itemId;
        ViewBag.Type = type?.ToString();

        var vm = new StockIndexViewModel
        {
            Movements = movements,
            Items = await _db.Items.Where(i => i.IsActive).AsNoTracking().ToListAsync(),
            ItemId = itemId,
            Type = type
        };
        this.SetPager(page, total, movements.Count, search);
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

        var baseQuery = _db.Items.AsNoTracking().Where(i => i.IsActive);

        var vm = new StockReportViewModel
        {
            Items = await query.OrderBy(i => i.Name).ToListAsync(),
            Categories = await _db.ItemCategories.Where(c => c.IsActive).AsNoTracking().ToListAsync(),
            CategoryId = categoryId,
            LowOnly = lowOnly,
            TotalItems = total,
            LowItems = lowOnly ? total : await query.CountAsync(i => (i.CurrentCount <= i.MinCount && i.MinCount > 0) || (i.CurrentQuantity <= i.MinQuantity && i.MinQuantity > 0)),
            TotalCount = await baseQuery.SumAsync(i => i.CurrentCount),
            TotalQuantity = await baseQuery.SumAsync(i => i.CurrentQuantity),
            TotalValue = await baseQuery.SumAsync(i => (i.QuantityUnitId.HasValue || i.CurrentQuantity > 0)
                ? i.CurrentQuantity * i.PurchasePrice
                : i.CurrentCount * i.PurchasePrice)
        };
        return View(vm);
    }

    [RequirePerm("LowStock.View")]
    public async Task<IActionResult> LowStock()
    {
        return View(await _dashboardService.GetLowStockItemsAsync());
    }
}
