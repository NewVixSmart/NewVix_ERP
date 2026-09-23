using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace NewVixSmart.Web.Models.Sales;

public class DeliveryOrder
{
    [BindNever]
    public int Id { get; set; }

    [Required(ErrorMessage = "رقم الإذن مطلوب")]
    [StringLength(50)]
    [Display(Name = "رقم الإذن")]
    public string DeliveryNumber { get; set; } = string.Empty;

    [Display(Name = "فاتورة البيع")]
    public int? SaleInvoiceId { get; set; }

    [BindNever]
    public SaleInvoice? SaleInvoice { get; set; }

    [Display(Name = "العميل")]
    public int CustomerId { get; set; }

    [BindNever]
    public Customer? Customer { get; set; }

    [Display(Name = "تاريخ التسليم")]
    [DataType(DataType.Date)]
    public DateTime DeliveryDate { get; set; } = DateTime.Today;

    [Display(Name = "حالة الإذن")]
    public DeliveryOrderStatus Status { get; set; } = DeliveryOrderStatus.Draft;

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
    public string? DeliveredBy { get; set; }

    [Display(Name = "تاريخ ترحيل التسليم")]
    [BindNever]
    public DateTime? DeliveredAt { get; set; }

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
    public ICollection<DeliveryOrderItem> Items { get; set; } = new List<DeliveryOrderItem>();
}