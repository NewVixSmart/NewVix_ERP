using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Core;

namespace NewVixSmart.Web.Models.Sales;

public class SaleQuoteItem
{
    public int Id { get; set; }

    [Display(Name = "عرض السعر")]
    public int SaleQuoteId { get; set; }

    [BindNever]
    public SaleQuote SaleQuote { get; set; } = null!;

    [Display(Name = "الصنف")]
    public int ItemId { get; set; }

    [BindNever]
    public Item Item { get; set; } = null!;

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "العدد")]
    [Range(0, 999999999, ErrorMessage = "العدد لا يمكن أن يكون سالباً")]
    public decimal Count { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الكمية")]
    [Range(0, 999999999, ErrorMessage = "الكمية لا يمكن أن تكون سالبة")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "سعر الوحدة")]
    [Range(0, 999999999, ErrorMessage = "سعر الوحدة لا يمكن أن يكون سالباً")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الإجمالي")]
    public decimal Total => (Quantity > 0 ? Quantity : Count) * UnitPrice;
}
