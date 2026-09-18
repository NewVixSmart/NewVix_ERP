using Microsoft.AspNetCore.Mvc.Rendering;
using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.ViewModels.Accounting;

public class ShipmentFormViewModel
{
    public Shipment Shipment { get; set; } = new();
    public IEnumerable<SelectListItem>? SaleInvoices { get; set; }
    public IEnumerable<SelectListItem>? PurchaseInvoices { get; set; }
    public IEnumerable<SelectListItem>? Customers { get; set; }
    public IEnumerable<SelectListItem>? Suppliers { get; set; }
}
