using System.ComponentModel.DataAnnotations;

namespace NewVixSmart.Web.Models.Sales;

public enum DeliveryIssueStatus
{
    [Display(Name = "مسودة")]
    Draft = 0,

    [Display(Name = "تم التسليم")]
    Issued = 1,

    [Display(Name = "ملغي")]
    Cancelled = 2
}
