using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Core;

namespace NewVixSmart.Web.Models.Sales;

public class DeliveryOrderItem
{
    public int Id { get; set; }

    public int DeliveryOrderId { get; set; }

    [BindNever]
    public DeliveryOrder DeliveryOrder { get; set; } = null!;

    public int ItemId { get; set; }

    [BindNever]
    public Item Item { get; set; } = null!;

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الكمية المسلّمة")]
    [Range(0, 999999999, ErrorMessage = "الكمية لا يمكن أن تكون سالبة")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "العدد المسلّم")]
    [Range(0, 999999999, ErrorMessage = "العدد لا يمكن أن يكون سالباً")]
    public decimal Count { get; set; }
}