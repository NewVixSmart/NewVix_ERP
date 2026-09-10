using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Silk.Trading.Web.Models.Purchases;
using Silk.Trading.Web.Models.Sales;

namespace Silk.Trading.Web.Models.Accounting;

public class Shipment
{
    [BindNever]
    public int Id { get; set; }

    [Required(ErrorMessage = "رقم الشحنة مطلوب")]
    [StringLength(50)]
    [Display(Name = "رقم الشحنة")]
    public string ShipmentNumber { get; set; } = string.Empty;

    [Display(Name = "نوع الفاتورة")]
    public ShipmentInvoiceType InvoiceType { get; set; }

    [Display(Name = "فاتورة بيع")]
    public int? SaleInvoiceId { get; set; }

    [BindNever]
    public SaleInvoice? SaleInvoice { get; set; }

    [Display(Name = "فاتورة شراء")]
    public int? PurchaseInvoiceId { get; set; }

    [BindNever]
    public PurchaseInvoice? PurchaseInvoice { get; set; }

    [Display(Name = "العميل")]
    public int? CustomerId { get; set; }

    [BindNever]
    public Customer? Customer { get; set; }

    [Display(Name = "المورد")]
    public int? SupplierId { get; set; }

    [BindNever]
    public Supplier? Supplier { get; set; }

    [StringLength(100)]
    [Display(Name = "الناقل")]
    public string? Carrier { get; set; }

    [StringLength(100)]
    [Display(Name = "رقم التتبع")]
    public string? TrackingNumber { get; set; }

    [Display(Name = "تاريخ الشحن")]
    [DataType(DataType.Date)]
    public DateTime? ShipDate { get; set; }

    [Display(Name = "الحالة")]
    public ShipmentStatus Status { get; set; } = ShipmentStatus.Preparing;

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "أنشئ بواسطة")]
    public string? CreatedBy { get; set; }

    [Display(Name = "تاريخ الإنشاء")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
