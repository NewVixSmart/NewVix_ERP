using System.ComponentModel.DataAnnotations;

namespace NewVixSmart.Web.Models.Accounting;

public enum ShipmentStatus
{
    [Display(Name = "قيد التحضير")]
    Preparing = 0,
    [Display(Name = "تم الشحن")]
    Shipped = 1,
    [Display(Name = "تم التسليم")]
    Delivered = 2,
    [Display(Name = "ملغي")]
    Cancelled = 3
}

public enum ShipmentInvoiceType
{
    [Display(Name = "بيع")]
    Sale = 1,
    [Display(Name = "شراء")]
    Purchase = 2
}
