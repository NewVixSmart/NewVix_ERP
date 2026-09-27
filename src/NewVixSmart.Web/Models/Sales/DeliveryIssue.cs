using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace NewVixSmart.Web.Models.Sales;

public class DeliveryIssue
{
    [BindNever]
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "رقم أمر التسليم مطلوب")]
    [StringLength(50)]
    [Display(Name = "رقم أمر التسليم")]
    public string IssueNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "أذن التسليم مطلوب")]
    [Display(Name = "أذن التسليم")]
    public int DeliveryOrderId { get; set; }

    [BindNever]
    public DeliveryOrder DeliveryOrder { get; set; } = null!;

    [Display(Name = "العميل")]
    public int CustomerId { get; set; }

    [BindNever]
    public Customer? Customer { get; set; }

    [Display(Name = "أمر البيع")]
    public int? SalesOrderId { get; set; }

    [BindNever]
    public SalesOrder? SalesOrder { get; set; }

    [Display(Name = "فاتورة البيع")]
    public int? SaleInvoiceId { get; set; }

    [BindNever]
    public SaleInvoice? SaleInvoice { get; set; }

    [Display(Name = "تاريخ التسليم")]
    [DataType(DataType.Date)]
    public DateTime IssueDate { get; set; } = DateTime.Today;

    [Display(Name = "حالة الأمر")]
    public DeliveryIssueStatus Status { get; set; } = DeliveryIssueStatus.Draft;

    [StringLength(100)]
    [Display(Name = "الناقل")]
    public string? Carrier { get; set; }

    [StringLength(100)]
    [Display(Name = "رقم التتبع")]
    public string? TrackingNumber { get; set; }

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "تم التسليم بواسطة")]
    [BindNever]
    public string? IssuedBy { get; set; }

    [Display(Name = "تاريخ ترحيل التسليم")]
    [BindNever]
    public DateTime? IssuedAt { get; set; }

    [Display(Name = "أنشئ بواسطة")]
    [BindNever]
    public string? CreatedBy { get; set; }

    [Display(Name = "تاريخ الإنشاء")]
    [BindNever]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Timestamp]
    [BindNever]
    public byte[]? RowVersion { get; set; }

    [BindNever]
    public ICollection<DeliveryIssueItem> Items { get; set; } = new List<DeliveryIssueItem>();
}
