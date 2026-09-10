using Microsoft.AspNetCore.Mvc.Rendering;
using Silk.Trading.Web.Models.Core;
using Silk.Trading.Web.Models.Purchases;

namespace Silk.Trading.Web.ViewModels.Purchases;

public class PurchaseOrderViewModel
{
    public PurchaseOrder Order { get; set; } = new();
    public List<PurchaseOrderItem> Items { get; set; } = new();
    public IEnumerable<SelectListItem>? Suppliers { get; set; }
    public List<Item> ItemsData { get; set; } = new();
}
