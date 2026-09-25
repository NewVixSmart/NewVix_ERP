using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.Models.Sales;

public class SaleReturn
{
    [BindNever]
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "رقم المرتجع مطلوب")]
    [StringLength(50)]
    [Display(Name = "رقم المرتجع")]
    public string ReturnNumber { get; set; } = string.Empty;

    [Display(Name = "العميل")]
    public int CustomerId { get; set; }

    [BindNever]
    public Customer Customer { get; set; } = null!;

    [Display(Name = "فاتورة البيع الأصلية")]
    public int? SaleInvoiceId { get; set; }

    [BindNever]
    public SaleInvoice? SaleInvoice { get; set; }

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
    public ICollection<SaleReturnItem> Items { get; set; } = new List<SaleReturnItem>();
}
