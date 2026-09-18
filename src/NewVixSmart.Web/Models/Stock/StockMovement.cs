using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NewVixSmart.Web.Models.Core;

namespace NewVixSmart.Web.Models.Stock;

public class StockMovement
{
    public int Id { get; set; }

    [Display(Name = "الصنف")]
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;

    [Display(Name = "نوع الحركة")]
    public MovementType Type { get; set; }

    [Display(Name = "الكمية")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Quantity { get; set; }

    [Display(Name = "العدد")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Count { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الرصيد الكمي قبل")]
    public decimal BalanceBefore { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الرصيد الكمي بعد")]
    public decimal BalanceAfter { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الرصيد العددي قبل")]
    public decimal CountBefore { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الرصيد العددي بعد")]
    public decimal CountAfter { get; set; }

    [StringLength(50)]
    [Display(Name = "رقم المستند")]
    public string? DocumentNumber { get; set; }

    [Display(Name = "نوع المستند")]
    public DocumentType? DocumentType { get; set; }

    [Display(Name = "معرف المستند")]
    public int? DocumentId { get; set; }

    [Display(Name = "تاريخ الحركة")]
    public DateTime MovementDate { get; set; } = DateTime.UtcNow;

    [Display(Name = "أنشئ بواسطة")]
    public string? CreatedBy { get; set; }
}

public enum MovementType
{
    [Display(Name = "وارد (دخول)")]
    In = 1,
    [Display(Name = "صادر (خروج)")]
    Out = 2
}

public enum DocumentType
{
    [Display(Name = "فاتورة شراء")]
    PurchaseInvoice = 1,
    [Display(Name = "مرتجع شراء")]
    PurchaseReturn = 2,
    [Display(Name = "فاتورة بيع")]
    SaleInvoice = 3,
    [Display(Name = "مرتجع بيع")]
    SaleReturn = 4,
    [Display(Name = "جرد")]
    Adjustment = 5,
    [Display(Name = "تحويل")]
    Transfer = 6,
    [Display(Name = "أذن تسليم بيع")]
    SaleDeliveryOrder = 7
}
