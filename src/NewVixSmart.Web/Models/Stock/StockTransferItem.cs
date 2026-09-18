using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Core;

namespace NewVixSmart.Web.Models.Stock;

public class StockTransferItem
{
    public int Id { get; set; }

    [Display(Name = "التحويل")]
    public int StockTransferId { get; set; }

    [ForeignKey(nameof(StockTransferId))]
    [BindNever]
    public StockTransfer StockTransfer { get; set; } = null!;

    [Display(Name = "الصنف")]
    public int ItemId { get; set; }

    [ForeignKey(nameof(ItemId))]
    public Item Item { get; set; } = null!;

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الكمية")]
    [Range(0, 999999999, ErrorMessage = "الكمية لا يمكن أن تكون سالبة")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "العدد")]
    [Range(0, 999999999, ErrorMessage = "العدد لا يمكن أن يكون سالباً")]
    public decimal Count { get; set; }

    [Column(TypeName = "decimal(18,6)")]
    [Display(Name = "تكلفة الوحدة")]
    public decimal UnitCost { get; set; }

    [Display(Name = "تاريخ الاستلام الأصلي")]
    public DateTime DateReceived { get; set; }
}
