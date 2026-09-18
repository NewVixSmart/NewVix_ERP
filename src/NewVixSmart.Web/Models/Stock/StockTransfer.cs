using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace NewVixSmart.Web.Models.Stock;

public class StockTransfer
{
    [BindNever]
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    [Display(Name = "رقم التحويل")]
    public string TransferNumber { get; set; } = string.Empty;

    [Display(Name = "المستودع المصدر")]
    public int SourceWarehouseId { get; set; }

    [ForeignKey(nameof(SourceWarehouseId))]
    public Warehouse SourceWarehouse { get; set; } = null!;

    [Display(Name = "المستودع الوجهة")]
    public int TargetWarehouseId { get; set; }

    [ForeignKey(nameof(TargetWarehouseId))]
    public Warehouse TargetWarehouse { get; set; } = null!;

    [Display(Name = "تاريخ التحويل")]
    public DateTime TransferDate { get; set; } = DateTime.Today;

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "أنشئ بواسطة")]
    [BindNever]
    public string? CreatedBy { get; set; }

    [Display(Name = "تاريخ الإنشاء")]
    [BindNever]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<StockTransferItem> Items { get; set; } = new List<StockTransferItem>();
}
