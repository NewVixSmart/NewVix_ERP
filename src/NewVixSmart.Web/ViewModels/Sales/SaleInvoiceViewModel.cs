using Microsoft.AspNetCore.Mvc.Rendering;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.ViewModels.Sales;

public class SaleInvoiceViewModel
{
    public SaleInvoice Invoice { get; set; } = new();
    public List<SaleInvoiceItem> Items { get; set; } = new();
    public IEnumerable<SelectListItem>? Customers { get; set; }
    public List<Item> ItemsData { get; set; } = new();
}
