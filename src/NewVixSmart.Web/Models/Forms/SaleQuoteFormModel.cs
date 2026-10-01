using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Models.Forms;

/// <summary>
/// نموذج عرض السعر المُرسلة من النموذج.
/// <para>
/// رقم العرض وحالته وإجمالياته وحواله (الفاتورة والأمر) حقول يملكها الخادم؛
/// <see cref="Services.ISalesQuotesService.CreateAsync"/> تولّد رقم العرض وتحتسب الإجماليات،
/// فوجودها هنا للعرض فقط بـ<code>BindNever</code> لا يترك خطأ تحقق مخفيًا ولا يفتح باب الكتابة
/// من المتصفح.
/// </para>
/// <para>
/// هذا النوع ليس كيان EF ولا <c>DbSet</c> يشير إليه، فلا يلتقطه <c>AppDbContext</c>.
/// </para>
/// </summary>
public class SaleQuoteFormModel
{
    [BindNever]
    [Display(Name = "رقم العرض")]
    public string QuoteNumber { get; set; } = string.Empty;

    [Display(Name = "العميل")]
    [Range(1, int.MaxValue, ErrorMessage = "اختر العميل")]
    public int CustomerId { get; set; }

    [Display(Name = "تاريخ العرض")]
    [DataType(DataType.Date)]
    public DateTime QuoteDate { get; set; } = DateTime.Today;

    [Display(Name = "صالح حتى")]
    [DataType(DataType.Date)]
    public DateTime? ValidUntil { get; set; }

    [BindNever]
    [Display(Name = "الإجمالي")]
    public decimal TotalAmount { get; set; }

    [Display(Name = "الخصم")]
    [Range(0, 999999999, ErrorMessage = "الخصم لا يمكن أن يكون سالباً")]
    public decimal Discount { get; set; }

    [Display(Name = "الضريبة")]
    [Range(0, 999999999, ErrorMessage = "الضريبة لا يمكن أن تكون سالبة")]
    public decimal Tax { get; set; }

    [BindNever]
    [Display(Name = "المبلغ الصافي")]
    public decimal NetAmount { get; set; }

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "عرض مورد (اختياري)")]
    public int? SupplierQuoteId { get; set; }

    public SaleQuote ToEntity() => new()
    {
        CustomerId = CustomerId,
        QuoteDate = QuoteDate,
        ValidUntil = ValidUntil,
        Discount = Discount,
        Tax = Tax,
        Notes = Notes,
        SupplierQuoteId = SupplierQuoteId
    };
}

/// <summary>
/// سطر عرض السعر في النموذج؛ الإجمالي المحسوب يجلس على الكيان.
/// </summary>
public class SaleQuoteLineFormModel
{
    [Display(Name = "الصنف")]
    public int ItemId { get; set; }

    [Range(0, 999999999, ErrorMessage = "العدد لا يمكن أن يكون سالباً")]
    public decimal Count { get; set; }

    [Range(0, 999999999, ErrorMessage = "الكمية لا يمكن أن تكون سالبة")]
    public decimal Quantity { get; set; }

    [Range(0, 999999999, ErrorMessage = "سعر الوحدة لا يمكن أن يكون سالباً")]
    public decimal UnitPrice { get; set; }

    public SaleQuoteItem ToEntity() => new()
    {
        ItemId = ItemId,
        Count = Count,
        Quantity = Quantity,
        UnitPrice = UnitPrice
    };
}

/// <summary>
/// يحوّل الكيان المحفوظ إلى حمولة النموذج، فيبقى رقم العرض ظاهرًا بعد فشل الحفظ.
/// </summary>
public static class SaleQuoteFormModelExtensions
{
    public static SaleQuoteFormModel ToFormModel(this SaleQuote quote)
        => new()
        {
            QuoteNumber = quote.QuoteNumber,
            CustomerId = quote.CustomerId,
            QuoteDate = quote.QuoteDate,
            ValidUntil = quote.ValidUntil,
            Discount = quote.Discount,
            Tax = quote.Tax,
            Notes = quote.Notes,
            SupplierQuoteId = quote.SupplierQuoteId
        };

    public static SaleQuoteLineFormModel ToFormModel(this SaleQuoteItem line)
        => new()
        {
            ItemId = line.ItemId,
            Count = line.Count,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice
        };
}
