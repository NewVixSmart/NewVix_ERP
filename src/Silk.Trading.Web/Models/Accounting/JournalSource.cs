using System.ComponentModel.DataAnnotations;

namespace Silk.Trading.Web.Models.Accounting;

public enum JournalSource
{
    [Display(Name = "فاتورة بيع")]
    SaleInvoice = 1,
    [Display(Name = "فاتورة شراء")]
    PurchaseInvoice = 2,
    [Display(Name = "قبض")]
    Receipt = 3,
    [Display(Name = "صرف")]
    Disbursement = 4,
    [Display(Name = "مرتجع بيع")]
    SaleReturn = 5,
    [Display(Name = "مرتجع شراء")]
    PurchaseReturn = 6,
    [Display(Name = "جرد افتتاحي")]
    OpeningStock = 7,
    [Display(Name = "تسوية مخزون")]
    InventoryAdjustment = 8,
    [Display(Name = "إقفال سنوي")]
    YearEndClose = 9,
    [Display(Name = "استيراد")]
    Import = 10
}
