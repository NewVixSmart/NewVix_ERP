using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.ViewModels.Batch;

public class BatchSalesViewModel
{
    [Required(ErrorMessage = "العميل مطلوب")]
    [Range(1, int.MaxValue, ErrorMessage = "اختر عميلًا صالحًا")]
    [Display(Name = "العميل")]
    public int CustomerId { get; set; }

    [Display(Name = "تاريخ الفاتورة")]
    [DataType(DataType.Date)]
    public DateTime InvoiceDate { get; set; } = DateTime.Today;

    [Display(Name = "شروط الدفع")]
    public InvoicePaymentTerms PaymentTerms { get; set; } = InvoicePaymentTerms.OnReceipt;

    [Display(Name = "العملة")]
    public int? CurrencyId { get; set; }

    [Display(Name = "سعر الصرف")]
    public decimal? ExchangeRate { get; set; }

    [Range(0, 999999999, ErrorMessage = "الخصم لا يمكن أن يكون سالبًا")]
    [Display(Name = "الخصم")]
    public decimal Discount { get; set; }

    [Range(0, 999999999, ErrorMessage = "الخصم الإضافي لا يمكن أن يكون سالبًا")]
    [Display(Name = "خصم إضافي 2")]
    public decimal? Discount2 { get; set; }

    [Range(0, 999999999, ErrorMessage = "الخصم الإضافي لا يمكن أن يكون سالبًا")]
    [Display(Name = "خصم إضافي 3")]
    public decimal? Discount3 { get; set; }

    [Range(0, 999999999, ErrorMessage = "الضريبة لا يمكن أن تكون سالبة")]
    [Display(Name = "الضريبة")]
    public decimal Tax { get; set; }

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public List<BatchInvoiceBlock> Invoices { get; set; } = new() { new() };

    public IEnumerable<SelectListItem>? Customers { get; set; }
    public List<Item> ItemsData { get; set; } = new();
    public IEnumerable<SelectListItem>? Currencies { get; set; }
}

public class BatchInvoiceBlock
{
    public List<SaleInvoiceItem> Items { get; set; } = new();
}