using NewVixSmart.Web.Models.Core;

namespace NewVixSmart.Web.ViewModels.Stock;

public class StockReportViewModel
{
    public List<Item> Items { get; set; } = new();
    public List<ItemCategory> Categories { get; set; } = new();
    public string? Search { get; set; }
    public int? CategoryId { get; set; }
    public bool LowOnly { get; set; }
    public int TotalItems { get; set; }
    public int LowItems { get; set; }
    public decimal TotalCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TotalValue { get; set; }
}
