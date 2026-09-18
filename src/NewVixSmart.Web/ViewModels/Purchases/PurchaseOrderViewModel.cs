using Microsoft.AspNetCore.Mvc.Rendering;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;

namespace NewVixSmart.Web.ViewModels.Purchases;

public class PurchaseOrderViewModel
{
    public PurchaseOrder Order { get; set; } = new();
    public List<PurchaseOrderItem> Items { get; set; } = new();
    public IEnumerable<SelectListItem>? Suppliers { get; set; }
    public List<Item> ItemsData { get; set; } = new();
}
