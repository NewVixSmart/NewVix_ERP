using System.ComponentModel.DataAnnotations;

namespace NewVixSmart.Web.Models.Sales;

public enum SalesOrderStatus
{
    [Display(Name = "مسودة")]
    Draft = 0,
    [Display(Name = "معتمد")]
    Approved = 1,
    [Display(Name = "تمت الفوترة جزئياً")]
    PartiallyInvoiced = 2,
    [Display(Name = "تمت الفوترة")]
    Invoiced = 3,
    [Display(Name = "ملغي")]
    Cancelled = 4
}
