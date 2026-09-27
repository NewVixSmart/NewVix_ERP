using System.ComponentModel.DataAnnotations;

namespace NewVixSmart.Web.Models.Sales;

public enum DeliveryOrderStatus
{
    [Display(Name = "مسودة")]
    Draft = 0,
    [Display(Name = "تم التسليم")]
    Delivered = 1,
    [Display(Name = "ملغي")]
    Cancelled = 2,
    [Display(Name = "تسليم جزئي")]
    PartiallyIssued = 3
}