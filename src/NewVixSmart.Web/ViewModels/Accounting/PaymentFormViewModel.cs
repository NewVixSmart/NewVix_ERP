using Microsoft.AspNetCore.Mvc.Rendering;
using NewVixSmart.Web.Models.Forms;

namespace NewVixSmart.Web.ViewModels.Accounting;

public class PaymentFormViewModel
{
    public PaymentFormModel Payment { get; set; } = new();
    public string Type { get; set; } = "receipt";
    public IEnumerable<SelectListItem>? Customers { get; set; }
    public IEnumerable<SelectListItem>? Suppliers { get; set; }
}
