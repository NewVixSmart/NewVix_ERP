using Microsoft.AspNetCore.Mvc.Rendering;
using Silk.Trading.Web.Models.Core;
using Silk.Trading.Web.Models.Purchases;

namespace Silk.Trading.Web.ViewModels.Purchases;

public class PurchaseInvoiceViewModel
{
    public PurchaseInvoice Invoice { get; set; } = new();
    public List<PurchaseInvoiceItem> Items { get; set; } = new();
    public IEnumerable<SelectListItem>? Suppliers { get; set; }
    public List<Item> ItemsData { get; set; } = new();
    public IEnumerable<SelectListItem>? Currencies { get; set; }
}
