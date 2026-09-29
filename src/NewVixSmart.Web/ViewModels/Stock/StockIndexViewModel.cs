using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Stock;

namespace NewVixSmart.Web.ViewModels.Stock;

public class StockIndexViewModel
{
    public List<StockMovement> Movements { get; set; } = new();
    public List<Item> Items { get; set; } = new();
    public int? ItemId { get; set; }
    public MovementType? Type { get; set; }
}
