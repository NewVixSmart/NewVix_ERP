using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Models.Accounting;

public enum InvoicePaymentTerms
{
    [Display(Name = "عند الاستلام")]
    OnReceipt = 0,
    [Display(Name = "7 أيام")]
    Net7 = 1,
    [Display(Name = "15 يوم")]
    Net15 = 2,
    [Display(Name = "30 يوم")]
    Net30 = 3,
    [Display(Name = "60 يوم")]
    Net60 = 4,
    [Display(Name = "آجل مفتوح")]
    OpenTerm = 5
}

public class Payment
{
    public int Id { get; set; }

    [Required(ErrorMessage = "رقم الإيصال مطلوب")]
    [StringLength(50)]
    [Display(Name = "رقم الإيصال")]
    public string ReceiptNumber { get; set; } = string.Empty;

    [Display(Name = "نوع الدفعة")]
    public PaymentType Type { get; set; }

    [Display(Name = "العميل")]
    public int? CustomerId { get; set; }

    [BindNever]
    public Customer? Customer { get; set; }

    [Display(Name = "المورد")]
    public int? SupplierId { get; set; }

    [BindNever]
    public Supplier? Supplier { get; set; }

    [Display(Name = "العملة")]
    public int? CurrencyId { get; set; }

    [BindNever]
    public Currency? Currency { get; set; }

    [Column(TypeName = "decimal(18,6)")]
    [Display(Name = "سعر الصرف")]
    [Range(0.000001, 999999999, ErrorMessage = "سعر الصرف يجب أن يكون أكبر من صفر")]
    public decimal? ExchangeRate { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "المبلغ بالعملة الأساسية")]
    public decimal BaseAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "المبلغ")]
    [Range(0.01, 999999999, ErrorMessage = "المبلغ يجب أن يكون أكبر من صفر")]
    public decimal Amount { get; set; }

    [Display(Name = "طريقة الدفع")]
    public PaymentMethod Method { get; set; }

    [Display(Name = "تاريخ الدفعة")]
    [DataType(DataType.Date)]
    public DateTime PaymentDate { get; set; } = DateTime.Today;

    [StringLength(200)]
    [Display(Name = "رقم المرجع")]
    public string? ReferenceNumber { get; set; }

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "الفرع")]
    public int? BranchId { get; set; }

    [Display(Name = "أنشئ بواسطة")]
    public string? CreatedBy { get; set; }

    [Display(Name = "تاريخ الإنشاء")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BindNever]
    public string? DedupeKey { get; set; }

    public ICollection<PaymentAllocation>? PaymentAllocations { get; set; } = [];
}

public enum PaymentType
{
    [Display(Name = "قبض (من عميل)")]
    Receipt = 1,
    [Display(Name = "صرف (لمورد)")]
    Disbursement = 2
}

public enum PaymentMethod
{
    [Display(Name = "نقداً")]
    Cash = 1,
    [Display(Name = "شيك")]
    Check = 2,
    [Display(Name = "تحويل بنكي")]
    BankTransfer = 3,
    [Display(Name = "بطاقة ائتمان")]
    CreditCard = 4
}
