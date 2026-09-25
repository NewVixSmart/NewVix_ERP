using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.Models.Sales;

public class SalesOrder
{
    [BindNever]
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "رقم الأمر مطلوب")]
    [StringLength(50)]
    [Display(Name = "رقم الأمر")]
    public string OrderNumber { get; set; } = string.Empty;

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

    [Display(Name = "تاريخ الأمر")]
    [DataType(DataType.Date)]
    public DateTime OrderDate { get; set; } = DateTime.Today;

    [Display(Name = "تاريخ التنفيذ المتوقع")]
    [DataType(DataType.Date)]
    public DateTime? ExpectedDate { get; set; }

    [Display(Name = "حالة الأمر")]
    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Draft;

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "عرض السعر المصدر")]
    public int? SaleQuoteId { get; set; }

    [BindNever]
    public SaleQuote? SaleQuote { get; set; }

    [Display(Name = "أنشئ بواسطة")]
    [BindNever]
    public string? CreatedBy { get; set; }

    [Display(Name = "تاريخ الإنشاء")]
    [BindNever]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BindNever]
    public ICollection<SalesOrderItem> Items { get; set; } = new List<SalesOrderItem>();

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}