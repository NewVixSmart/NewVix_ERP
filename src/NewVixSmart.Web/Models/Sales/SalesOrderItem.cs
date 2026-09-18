using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Core;

namespace NewVixSmart.Web.Models.Sales;

public class SalesOrderItem
{
    public int Id { get; set; }

    public int SalesOrderId { get; set; }

    [BindNever]
    public SalesOrder SalesOrder { get; set; } = null!;

    public int ItemId { get; set; }

    [BindNever]
    public Item Item { get; set; } = null!;

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الكمية المطلوبة")]
    [Range(0, 999999999, ErrorMessage = "الكمية لا يمكن أن تكون سالبة")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "العدد المطلوب")]
    [Range(0, 999999999, ErrorMessage = "العدد لا يمكن أن يكون سالباً")]
    public decimal Count { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "سعر الوحدة")]
    [Range(0, 999999999, ErrorMessage = "السعر لا يمكن أن يكون سالباً")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الكمية المفوتَرة")]
    public decimal InvoicedQty { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "العدد المفوتَر")]
    public decimal InvoicedCount { get; set; }

    public decimal Total => (Quantity > 0 ? Quantity : Count) * UnitPrice;
}