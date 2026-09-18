using Microsoft.AspNetCore.Mvc.Rendering;
using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.ViewModels.Accounting;

public class PaymentFormViewModel
{
    public Payment Payment { get; set; } = new();
    public string Type { get; set; } = "receipt";
    public IEnumerable<SelectListItem>? Customers { get; set; }
    public IEnumerable<SelectListItem>? Suppliers { get; set; }
    public IEnumerable<SelectListItem>? Currencies { get; set; }
    public IEnumerable<Currency>? CurrencyOptions { get; set; }
}