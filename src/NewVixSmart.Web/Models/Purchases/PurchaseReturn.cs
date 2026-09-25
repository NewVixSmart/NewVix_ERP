using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.Models.Purchases;

public class PurchaseReturn
{
    [BindNever]
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "رقم المرتجع مطلوب")]
    [StringLength(50)]
    [Display(Name = "رقم المرتجع")]
    public string ReturnNumber { get; set; } = string.Empty;

    [Display(Name = "المورد")]
    public int SupplierId { get; set; }

    [BindNever]
    public Supplier Supplier { get; set; } = null!;

    [Display(Name = "فاتورة الشراء الأصلية")]
    public int? PurchaseInvoiceId { get; set; }

    [BindNever]
    public PurchaseInvoice? PurchaseInvoice { get; set; }

    [Display(Name = "العملة")]
    public int? CurrencyId { get; set; }

    [BindNever]
    public Currency? Currency { get; set; }

    [Column(TypeName = "decimal(18,6)")]
    [Display(Name = "سعر الصرف")]
    public decimal? ExchangeRate { get; set; }

    [Display(Name = "الفرع")]
    public int? BranchId { get; set; }

    [Display(Name = "حالة المرتجع")]
    public ReturnStatus Status { get; set; } = ReturnStatus.Draft;

    [Display(Name = "رحّله")]
    [BindNever]
    public string? PostedBy { get; set; }

    [Display(Name = "تاريخ الترحيل")]
    [BindNever]
    public DateTime? PostedAt { get; set; }

    [Display(Name = "تاريخ المرتجع")]
    [DataType(DataType.Date)]
    public DateTime ReturnDate { get; set; } = DateTime.Today;

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الإجمالي")]
    [Range(0, 999999999, ErrorMessage = "الإجمالي لا يمكن أن يكون سالباً")]
    public decimal TotalAmount { get; set; }

    [StringLength(500)]
    [Display(Name = "سبب المرتجع")]
    public string? Reason { get; set; }

    [Display(Name = "أنشئ بواسطة")]
    [BindNever]
    public string? CreatedBy { get; set; }

    [Display(Name = "تاريخ الإنشاء")]
    [BindNever]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BindNever]
    public ICollection<PurchaseReturnItem> Items { get; set; } = new List<PurchaseReturnItem>();
}
