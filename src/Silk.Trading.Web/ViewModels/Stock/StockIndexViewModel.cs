using Silk.Trading.Web.Models.Core;
using Silk.Trading.Web.Models.Stock;

namespace Silk.Trading.Web.ViewModels.Stock;

public class StockIndexViewModel
{
    public List<StockMovement> Movements { get; set; } = new();
    public List<Item> Items { get; set; } = new();
    public int? ItemId { get; set; }
    public MovementType? Type { get; set; }
}