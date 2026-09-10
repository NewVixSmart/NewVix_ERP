using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Silk.Trading.Web.Models.Accounting;

namespace Silk.Trading.Web.Models.Purchases;

public class PurchaseInvoice
{
    [BindNever]
    public int Id { get; set; }

    [Required(ErrorMessage = "رقم الفاتورة مطلوب")]
    [StringLength(50)]
    [Display(Name = "رقم الفاتورة")]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Display(Name = "المورد")]
    public int SupplierId { get; set; }

    [BindNever]
    public Supplier Supplier { get; set; } = null!;

    [Display(Name = "الفرع")]
    public int? BranchId { get; set; }

    [BindNever]
    public Core.Branch? Branch { get; set; }

    [Display(Name = "العملة")]
    public int? CurrencyId { get; set; }

    [BindNever]
    public Currency? Currency { get; set; }

    [Column(TypeName = "decimal(18,6)")]
    [Display(Name = "سعر الصرف")]
    public decimal? ExchangeRate { get; set; }

    [Display(Name = "تاريخ الفاتورة")]
    [DataType(DataType.Date)]
    public DateTime InvoiceDate { get; set; } = DateTime.Today;

    [Display(Name = "شروط الدفع")]
    public InvoicePaymentTerms PaymentTerms { get; set; } = InvoicePaymentTerms.OnReceipt;

    [Display(Name = "تاريخ الاستحقاق")]
    [DataType(DataType.Date)]
    public DateTime? DueDate { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "المبلغ الإجمالي")]
    [Range(0, 999999999, ErrorMessage = "المبلغ الإجمالي لا يمكن أن يكون سالباً")]
    public decimal TotalAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الخصم")]
    [Range(0, 999999999, ErrorMessage = "الخصم لا يمكن أن يكون سالباً")]
    public decimal Discount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "خصم إضافي 2")]
    [Range(0, 999999999, ErrorMessage = "الخصم الإضافي لا يمكن أن يكون سالباً")]
    public decimal? Discount2 { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "خصم إضافي 3")]
    [Range(0, 999999999, ErrorMessage = "الخصم الإضافي لا يمكن أن يكون سالباً")]
    public decimal? Discount3 { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الضريبة")]
    [Range(0, 999999999, ErrorMessage = "الضريبة لا يمكن أن تكون سالبة")]
    public decimal Tax { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "المبلغ الصافي")]
    [Range(0, 999999999, ErrorMessage = "المبلغ الصافي لا يمكن أن يكون سالباً")]
    public decimal NetAmount { get; set; }

    [Display(Name = "تم الدفع")]
    public bool IsPaid { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "المبلغ المدفوع")]
    [Range(0, 999999999, ErrorMessage = "المبلغ المدفوع لا يمكن أن يكون سالباً")]
    public decimal PaidAmount { get; set; }

    [StringLength(50)]
    [Display(Name = "رقم طلب الشراء")]
    public string? OrderReference { get; set; }

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "أمر الشراء")]
    public int? PurchaseOrderId { get; set; }

    [BindNever]
    public PurchaseOrder? PurchaseOrder { get; set; }

    [Display(Name = "أنشئ بواسطة")]
    [BindNever]
    public string? CreatedBy { get; set; }

    [Display(Name = "تاريخ الإنشاء")]
    [BindNever]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Timestamp]
    public byte[]? RowVersion { get; set; }

    [BindNever]
    public ICollection<PurchaseInvoiceItem> Items { get; set; } = new List<PurchaseInvoiceItem>();
}
