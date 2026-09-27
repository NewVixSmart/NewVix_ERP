using Microsoft.AspNetCore.Mvc.Rendering;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.ViewModels.Sales;

public class DeliveryOrderViewModel
{
    public DeliveryOrder Delivery { get; set; } = new();
    public List<DeliveryOrderItem> Items { get; set; } = new();
    public IEnumerable<SelectListItem>? Invoices { get; set; }
    public IEnumerable<SelectListItem>? SalesOrders { get; set; }
    public IEnumerable<SelectListItem>? Customers { get; set; }
    public List<Item> ItemsData { get; set; } = new();

    /// <summary>المصدر المختار: Order / Invoice / Free.</summary>
    public string Source { get; set; } = "Free";
}
