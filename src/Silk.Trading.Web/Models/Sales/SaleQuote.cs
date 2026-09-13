using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Models.Purchases;

namespace Silk.Trading.Web.Models.Sales;

public class SaleQuote
{
    [BindNever]
    public int Id { get; set; }

    [Required(ErrorMessage = "رقم العرض مطلوب")]
    [StringLength(50)]
    [Display(Name = "رقم العرض")]
    public string QuoteNumber { get; set; } = string.Empty;

    [Display(Name = "العميل")]
    public int CustomerId { get; set; }

    [BindNever]
    public Customer Customer { get; set; } = null!;

    [Display(Name = "العملة")]
    public int? CurrencyId { get; set; }

    [BindNever]
    public Currency? Currency { get; set; }

    [Column(TypeName = "decimal(18,6)")]
    [Display(Name = "سعر الصرف")]
    public decimal? ExchangeRate { get; set; }

    [Display(Name = "تاريخ العرض")]
    [DataType(DataType.Date)]
    public DateTime QuoteDate { get; set; } = DateTime.Today;

    [Display(Name = "صالح حتى")]
    [DataType(DataType.Date)]
    public DateTime? ValidUntil { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الإجمالي")]
    [Range(0, 999999999, ErrorMessage = "الإجمالي لا يمكن أن يكون سالباً")]
    public decimal TotalAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الخصم")]
    [Range(0, 999999999, ErrorMessage = "الخصم لا يمكن أن يكون سالباً")]
    public decimal Discount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الضريبة")]
    [Range(0, 999999999, ErrorMessage = "الضريبة لا يمكن أن تكون سالبة")]
    public decimal Tax { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "المبلغ الصافي")]
    [Range(0, 999999999, ErrorMessage = "المبلغ الصافي لا يمكن أن يكون سالباً")]
    public decimal NetAmount { get; set; }

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "حالة العرض")]
    public SaleQuoteStatus Status { get; set; } = SaleQuoteStatus.Draft;

    [Display(Name = "عرض مورد مرجعي (اختياري)")]
    public int? SupplierQuoteId { get; set; }

    [BindNever]
    public SupplierQuote? SupplierQuote { get; set; }

    [Display(Name = "فاتورة البيع")]
    public int? SaleInvoiceId { get; set; }

    [BindNever]
    public SaleInvoice? SaleInvoice { get; set; }

    [Display(Name = "حُوّل بواسطة")]
    [BindNever]
    public string? ConvertedBy { get; set; }

    [Display(Name = "تاريخ التحويل")]
    [BindNever]
    public DateTime? ConvertedAt { get; set; }

    [Display(Name = "أنشئ بواسطة")]
    [BindNever]
    public string? CreatedBy { get; set; }

    [Display(Name = "تاريخ الإنشاء")]
    [BindNever]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Timestamp]
    public byte[]? RowVersion { get; set; }

    [BindNever]
    public ICollection<SaleQuoteItem> Items { get; set; } = new List<SaleQuoteItem>();
}