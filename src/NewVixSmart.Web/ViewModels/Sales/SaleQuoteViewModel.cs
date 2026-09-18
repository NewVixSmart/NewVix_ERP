using Microsoft.AspNetCore.Mvc.Rendering;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.ViewModels.Sales;

public class SaleQuoteViewModel
{
    public SaleQuote Quote { get; set; } = new();
    public List<SaleQuoteItem> Items { get; set; } = new();
    public IEnumerable<SelectListItem>? Customers { get; set; }
    public IEnumerable<SelectListItem>? SupplierQuotes { get; set; }
    public List<Item> ItemsData { get; set; } = new();
    public IEnumerable<SelectListItem>? Currencies { get; set; }
}