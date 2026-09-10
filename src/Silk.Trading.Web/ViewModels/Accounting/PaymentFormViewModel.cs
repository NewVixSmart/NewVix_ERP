using Microsoft.AspNetCore.Mvc.Rendering;
using Silk.Trading.Web.Models.Accounting;

namespace Silk.Trading.Web.ViewModels.Accounting;

public class PaymentFormViewModel
{
    public Payment Payment { get; set; } = new();
    public string Type { get; set; } = "receipt";
    public IEnumerable<SelectListItem>? Customers { get; set; }
    public IEnumerable<SelectListItem>? Suppliers { get; set; }
    public IEnumerable<SelectListItem>? Currencies { get; set; }
    public IEnumerable<Currency>? CurrencyOptions { get; set; }
}