using System.ComponentModel.DataAnnotations;

namespace NewVixSmart.Web.Models.Sales;

public enum SaleQuoteStatus
{
    [Display(Name = "مسودة")]
    Draft = 0,

    [Display(Name = "جارٍ التحويل")]
    Converting = 1,

    [Display(Name = "محوّل إلى أمر بيع")]
    Converted = 2,

    [Display(Name = "ملغي")]
    Cancelled = 3
}