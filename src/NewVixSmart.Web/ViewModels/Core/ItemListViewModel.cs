using NewVixSmart.Web.Models.Core;

namespace NewVixSmart.Web.ViewModels.Core;

public class ItemListViewModel
{
    public List<Item> Items { get; set; } = new();
    public List<ItemCategory> Categories { get; set; } = new();
    public string? Search { get; set; }
    public int? CategoryId { get; set; }
}
