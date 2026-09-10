using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Api.Dtos;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Extensions;
using Silk.Trading.Web.Services;

namespace Silk.Trading.Web.Api;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ItemsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;

    public ItemsController(AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    [HttpGet("items")]
    [ApiAuthorize("Items.View")]
    public async Task<IActionResult> GetItems()
    {
        var items = await _db.Items
            .Where(i => i.IsActive)
            .AsNoTracking()
            .Select(i => new ItemResponse
            {
                Id = i.Id,
                Name = i.Name,
                Code = i.Code,
                PurchasePrice = i.PurchasePrice,
                SalePrice = i.SalePrice,
                CurrentCount = i.CurrentCount,
                CurrentQuantity = i.CurrentQuantity,
                IsActive = i.IsActive
            })
            .ToListAsync();
        return Ok(items);
    }

    [HttpGet("items/{id:int}")]
    [ApiAuthorize("Items.View")]
    public async Task<IActionResult> GetItem(int id)
    {
        var item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        if (item == null) return NotFound(new { message = "Item not found" });

        var stockLayers = await _db.StockLayers
            .Where(sl => sl.ItemId == id && (sl.RemainingQty > 0 || sl.RemainingCount > 0))
            .AsNoTracking()
            .Select(sl => new StockLayerResponse
            {
                Id = sl.Id,
                WarehouseId = sl.WarehouseId,
                Qty = sl.RemainingQty,
                Count = sl.RemainingCount,
                UnitCost = sl.UnitCost,
                DateReceived = sl.DateReceived
            })
            .ToListAsync();

        return Ok(new ItemDetailResponse
        {
            Id = item.Id,
            Name = item.Name,
            Code = item.Code,
            PurchasePrice = item.PurchasePrice,
            SalePrice = item.SalePrice,
            CurrentCount = item.CurrentCount,
            CurrentQuantity = item.CurrentQuantity,
            IsActive = item.IsActive,
            StockLayers = stockLayers
        });
    }

    [HttpGet("stock")]
    [ApiAuthorize("Stock.View")]
    public async Task<IActionResult> GetStock([FromQuery] int? warehouseId)
    {
        var snapshot = await _inventory.GetStockSnapshotAsync(warehouseId);
        var result = snapshot.Select(s => new StockSnapshotResponse
        {
            ItemId = s.ItemId,
            ItemName = s.ItemName,
            TotalCount = s.TotalCount,
            TotalQuantity = s.TotalQuantity,
            WarehouseCount = s.WarehouseCount,
            WarehouseQuantity = s.WarehouseQuantity
        }).ToList();
        return Ok(result);
    }
}
