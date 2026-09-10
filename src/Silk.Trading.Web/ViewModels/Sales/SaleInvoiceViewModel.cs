using Microsoft.AspNetCore.Mvc.Rendering;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Models.Core;
using Silk.Trading.Web.Models.Sales;

namespace Silk.Trading.Web.ViewModels.Sales;

public class SaleInvoiceViewModel
{
    public SaleInvoice Invoice { get; set; } = new();
    public List<SaleInvoiceItem> Items { get; set; } = new();
    public IEnumerable<SelectListItem>? Customers { get; set; }
    public List<Item> ItemsData { get; set; } = new();
    public IEnumerable<SelectListItem>? Currencies { get; set; }
}
