using Microsoft.AspNetCore.Mvc.Rendering;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Forms;

namespace NewVixSmart.Web.ViewModels.Sales;

public class SaleQuoteViewModel
{
    public SaleQuoteFormModel Quote { get; set; } = new();
    public List<SaleQuoteLineFormModel> Items { get; set; } = new();
    public IEnumerable<SelectListItem>? Customers { get; set; }
    public IEnumerable<SelectListItem>? SupplierQuotes { get; set; }
    public List<Item> ItemsData { get; set; } = new();
}
