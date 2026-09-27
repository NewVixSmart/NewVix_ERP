using System.ComponentModel.DataAnnotations;

namespace NewVixSmart.Web.Models.Sales;

public enum SalesPostingMode
{
    [Display(Name = "الترحيل عند التسليم (فاتورة أولاً)")]
    AtDelivery = 0,

    [Display(Name = "الترحيل عند الفاتورة (تسليم أولاً)")]
    AtInvoice = 1
}
