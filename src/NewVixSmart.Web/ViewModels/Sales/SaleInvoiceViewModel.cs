using Microsoft.AspNetCore.Mvc.Rendering;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Forms;

namespace NewVixSmart.Web.ViewModels.Sales;

public class SaleInvoiceViewModel
{
    public SaleInvoiceFormModel Invoice { get; set; } = new();
    public List<SaleInvoiceLineFormModel> Items { get; set; } = new();
    public IEnumerable<SelectListItem>? Customers { get; set; }
    public List<Item> ItemsData { get; set; } = new();
}
