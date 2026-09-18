using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace NewVixSmart.Web.Models.Core;

public class Item
{
    public int Id { get; set; }

    [Required(ErrorMessage = "اسم الصنف مطلوب")]
    [StringLength(200)]
    [Display(Name = "اسم الصنف")]
    public string Name { get; set; } = string.Empty;

    [StringLength(50)]
    [Display(Name = "كود الصنف")]
    public string? Code { get; set; }

    [StringLength(50)]
    [Display(Name = "الباركود")]
    public string? Barcode { get; set; }

    [Display(Name = "نوع الصنف")]
    public int ItemTypeId { get; set; }

    [BindNever]
    public ItemType? ItemType { get; set; }

    [Display(Name = "قابل للبيع")]
    public bool IsSellable { get; set; } = true;

    [Display(Name = "التصنيف الرئيسي")]
    public int CategoryId { get; set; }

    [BindNever]
    public ItemCategory Category { get; set; } = null!;

    [Display(Name = "وحدة العدد")]
    public int? CountUnitId { get; set; }

    [BindNever]
    public Unit? CountUnit { get; set; }

    [Display(Name = "وحدة الكمية")]
    public int? QuantityUnitId { get; set; }

    [BindNever]
    public Unit? QuantityUnit { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "سعر الشراء")]
    [DataType(DataType.Currency)]
    [Range(0, 999999999, ErrorMessage = "سعر الشراء لا يمكن أن يكون سالباً")]
    public decimal PurchasePrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "سعر البيع")]
    [DataType(DataType.Currency)]
    [Range(0, 999999999, ErrorMessage = "سعر البيع لا يمكن أن يكون سالباً")]
    public decimal SalePrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الحد الأدنى للعدد")]
    [Range(0, 999999999, ErrorMessage = "الحد الأدنى للعدد لا يمكن أن يكون سالباً")]
    public decimal MinCount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الحد الأدنى للكمية")]
    [Range(0, 999999999, ErrorMessage = "الحد الأدنى للكمية لا يمكن أن يكون سالباً")]
    public decimal MinQuantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "رصيد أول المدة (عدد)")]
    [Range(0, 999999999, ErrorMessage = "الرصيد العددي لا يمكن أن يكون سالباً")]
    public decimal CurrentCount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "رصيد أول المدة (كمية)")]
    [Range(0, 999999999, ErrorMessage = "الرصيد الكمي لا يمكن أن يكون سالباً")]
    public decimal CurrentQuantity { get; set; }

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "الفرع")]
    public int? BranchId { get; set; }

    [Display(Name = "نشط")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "تاريخ الإنشاء")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Timestamp]
    public byte[]? RowVersion { get; set; }

    [BindNever]
    public ICollection<Purchases.PurchaseInvoiceItem> PurchaseInvoiceItems { get; set; } = new List<Purchases.PurchaseInvoiceItem>();
    [BindNever]
    public ICollection<Purchases.PurchaseReturnItem> PurchaseReturnItems { get; set; } = new List<Purchases.PurchaseReturnItem>();
    [BindNever]
    public ICollection<Sales.SaleInvoiceItem> SaleInvoiceItems { get; set; } = new List<Sales.SaleInvoiceItem>();
    [BindNever]
    public ICollection<Sales.SaleReturnItem> SaleReturnItems { get; set; } = new List<Sales.SaleReturnItem>();
    [BindNever]
    public ICollection<Stock.StockMovement> StockMovements { get; set; } = new List<Stock.StockMovement>();
}
