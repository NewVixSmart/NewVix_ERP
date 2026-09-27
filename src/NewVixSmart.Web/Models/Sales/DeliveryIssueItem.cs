using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Core;

namespace NewVixSmart.Web.Models.Sales;

public class DeliveryIssueItem
{
    [BindNever]
    public int Id { get; set; }

    [BindNever]
    public int DeliveryIssueId { get; set; }

    [BindNever]
    public DeliveryIssue DeliveryIssue { get; set; } = null!;

    [Display(Name = "سطر أذن التسليم")]
    public int DeliveryOrderItemId { get; set; }

    [BindNever]
    public DeliveryOrderItem? DeliveryOrderItem { get; set; }

    [Display(Name = "الصنف")]
    public int ItemId { get; set; }

    [BindNever]
    public Item Item { get; set; } = null!;

    [Display(Name = "سطر أمر البيع")]
    public int? SalesOrderItemId { get; set; }

    [BindNever]
    public SalesOrderItem? SalesOrderItem { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الكمية المسلّمة")]
    [Range(0, 999999999, ErrorMessage = "الكمية لا يمكن أن تكون سالبة")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "العدد المسلّم")]
    [Range(0, 999999999, ErrorMessage = "العدد لا يمكن أن يكون سالباً")]
    public decimal Count { get; set; }
}
