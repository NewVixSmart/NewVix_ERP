using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Core;

namespace NewVixSmart.Web.Models.Stock;

public class InventoryAdjustment
{
    [BindNever]
    public int Id { get; set; }

    [Required(ErrorMessage = "رقم المستند مطلوب")]
    [StringLength(50)]
    [Display(Name = "رقم المستند")]
    public string ReferenceNumber { get; set; } = string.Empty;

    [Display(Name = "الصنف")]
    public int ItemId { get; set; }

    [BindNever]
    public Item Item { get; set; } = null!;

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "العدد الجديد")]
    [Range(0, 999999999, ErrorMessage = "العدد الجديد لا يمكن أن يكون سالباً")]
    public decimal NewCount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الكمية الجديدة")]
    [Range(0, 999999999, ErrorMessage = "الكمية الجديدة لا يمكن أن تكون سالبة")]
    public decimal NewQuantity { get; set; }

    [StringLength(500)]
    [Display(Name = "سبب التعديل")]
    public string? Reason { get; set; }

    [Display(Name = "تاريخ الجرد")]
    public DateTime AdjustmentDate { get; set; } = DateTime.Today;

    [Display(Name = "أنشئ بواسطة")]
    [BindNever]
    public string? CreatedBy { get; set; }

    [Display(Name = "تاريخ الإنشاء")]
    [BindNever]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
