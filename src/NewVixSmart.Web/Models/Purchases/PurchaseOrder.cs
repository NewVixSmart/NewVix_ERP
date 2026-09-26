using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace NewVixSmart.Web.Models.Purchases;

public class PurchaseOrder
{
    [BindNever]
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "رقم أمر الشراء مطلوب")]
    [StringLength(50)]
    [Display(Name = "رقم أمر الشراء")]
    public string OrderNumber { get; set; } = string.Empty;

    [Display(Name = "المورد")]
    public int SupplierId { get; set; }

    [BindNever]
    public Supplier Supplier { get; set; } = null!;

    [Display(Name = "تاريخ الأمر")]
    [DataType(DataType.Date)]
    public DateTime OrderDate { get; set; } = DateTime.Today;

    [Display(Name = "التاريخ المتوقع")]
    [DataType(DataType.Date)]
    public DateTime? ExpectedDate { get; set; }

    [Display(Name = "الحالة")]
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "أنشئ بواسطة")]
    [BindNever]
    public string? CreatedBy { get; set; }

    [Display(Name = "تاريخ الإنشاء")]
    [BindNever]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Timestamp]
    public byte[]? RowVersion { get; set; }

    [BindNever]
    public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
}

public enum PurchaseOrderStatus
{
    [Display(Name = "مسودة")]
    Draft = 0,
    [Display(Name = "معتمد")]
    Approved = 1,
    [Display(Name = "مستلم جزئياً")]
    PartiallyReceived = 2,
    [Display(Name = "مستلم")]
    Received = 3,
    [Display(Name = "ملغي")]
    Cancelled = 4
}
