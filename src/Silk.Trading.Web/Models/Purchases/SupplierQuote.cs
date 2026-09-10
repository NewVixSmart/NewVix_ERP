using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Silk.Trading.Web.Models.Core;

namespace Silk.Trading.Web.Models.Purchases;

public class SupplierQuote
{
    [BindNever]
    public int Id { get; set; }

    [Display(Name = "المورد")]
    public int SupplierId { get; set; }

    [BindNever]
    public Supplier Supplier { get; set; } = null!;

    [Display(Name = "الصنف")]
    public int ItemId { get; set; }

    [BindNever]
    public Item Item { get; set; } = null!;

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "سعر الوحدة")]
    [Range(0, 999999999, ErrorMessage = "سعر الوحدة لا يمكن أن يكون سالباً")]
    public decimal UnitPrice { get; set; }

    [Display(Name = "تاريخ السعر")]
    [DataType(DataType.Date)]
    public DateTime EffectiveDate { get; set; } = DateTime.Today;

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}
