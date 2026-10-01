using Microsoft.AspNetCore.Mvc.Rendering;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Forms;

namespace NewVixSmart.Web.ViewModels.Purchases;

public class PurchaseInvoiceViewModel
{
    public PurchaseInvoiceFormModel Invoice { get; set; } = new();
    public List<PurchaseInvoiceLineFormModel> Items { get; set; } = new();
    public IEnumerable<SelectListItem>? Suppliers { get; set; }
    public List<Item> ItemsData { get; set; } = new();
}
