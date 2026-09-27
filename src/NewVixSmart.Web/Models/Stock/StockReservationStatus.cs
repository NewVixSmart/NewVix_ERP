using System.ComponentModel.DataAnnotations;

namespace NewVixSmart.Web.Models.Stock;

public enum StockReservationStatus
{
    [Display(Name = "ساري")]
    Active = 0,

    [Display(Name = "مستهلك جزئياً")]
    PartiallyConsumed = 1,

    [Display(Name = "مستهلك بالكامل")]
    Consumed = 2,

    [Display(Name = "محرَّر")]
    Released = 3,

    [Display(Name = "ملغي")]
    Cancelled = 4
}
