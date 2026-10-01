using Microsoft.AspNetCore.Mvc.Rendering;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Forms;

namespace NewVixSmart.Web.ViewModels.Purchases;

public class PurchaseOrderViewModel
{
    public PurchaseOrderFormModel Order { get; set; } = new();
    public List<PurchaseOrderLineFormModel> Items { get; set; } = new();
    public IEnumerable<SelectListItem>? Suppliers { get; set; }
    public List<Item> ItemsData { get; set; } = new();
}
