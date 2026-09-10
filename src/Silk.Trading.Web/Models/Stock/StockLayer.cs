using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Silk.Trading.Web.Models.Core;

namespace Silk.Trading.Web.Models.Stock;

public class StockLayer
{
    public int Id { get; set; }

    [Display(Name = "الصنف")]
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;

    [Display(Name = "المستودع")]
    public int? WarehouseId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الكمية الأصلية")]
    public decimal Qty { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "العدد الأصلي")]
    public decimal Count { get; set; }

    [Column(TypeName = "decimal(18,6)")]
    [Display(Name = "تكلفة الوحدة الكمية")]
    public decimal UnitCost { get; set; }

    [Column(TypeName = "decimal(18,6)")]
    [Display(Name = "تكلفة الوحدة العددية")]
    public decimal CountCost { get; set; }

    [Display(Name = "تاريخ الاستلام")]
    public DateTime DateReceived { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الكمية المتبقية")]
    public decimal RemainingQty { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "العدد المتبقّي")]
    public decimal RemainingCount { get; set; }

    [Display(Name = "تاريخ الإنشاء")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
