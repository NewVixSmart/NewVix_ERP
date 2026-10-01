using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Models.Forms;

/// <summary>
/// نموذج فاتورة البيع المُرسلة من النموذج: ما يكتبه المحاسب وحده.
/// <para>
/// رقم الفاتورة والإجمالي والصافي والمدفوع لا يحتاجها أحد أن يكتبها؛
/// <see cref="Services.IInventoryService.CreateSaleAsync"/> تولّدها من الأصناف وتلغي أي قيمة
/// وصلت معها. لذلك هي للعرض فقط هنا: <c>BindNever</c> يمنعها من <c>ModelState</c> أصلًا،
/// فلا يوجد خطأ تحقق مخفي لا يراه المستخدم، ولا يستطيع المتصفح أن يكتب رقم فاتورة.
/// </para>
/// <para>
/// هذا النوع ليس كيان EF ولا <c>DbSet</c> يشير إليه، فلا يلتقطه <c>AppDbContext</c>.
/// </para>
/// </summary>
public class SaleInvoiceFormModel
{
    [BindNever]
    [Display(Name = "رقم الفاتورة")]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Display(Name = "العميل")]
    [Range(1, int.MaxValue, ErrorMessage = "اختر العميل")]
    public int CustomerId { get; set; }

    [Display(Name = "تاريخ الفاتورة")]
    [DataType(DataType.Date)]
    public DateTime InvoiceDate { get; set; } = DateTime.Today;

    [Display(Name = "شروط الدفع")]
    public InvoicePaymentTerms PaymentTerms { get; set; } = InvoicePaymentTerms.OnReceipt;

    [Display(Name = "تاريخ الاستحقاق")]
    [DataType(DataType.Date)]
    public DateTime? DueDate { get; set; }

    [BindNever]
    [Display(Name = "المبلغ الإجمالي")]
    public decimal TotalAmount { get; set; }

    [Display(Name = "الخصم")]
    [Range(0, 999999999, ErrorMessage = "الخصم لا يمكن أن يكون سالباً")]
    public decimal Discount { get; set; }

    [Display(Name = "خصم إضافي 2")]
    [Range(0, 999999999, ErrorMessage = "الخصم الإضافي لا يمكن أن يكون سالباً")]
    public decimal? Discount2 { get; set; }

    [Display(Name = "خصم إضافي 3")]
    [Range(0, 999999999, ErrorMessage = "الخصم الإضافي لا يمكن أن يكون سالباً")]
    public decimal? Discount3 { get; set; }

    [Display(Name = "الضريبة")]
    [Range(0, 999999999, ErrorMessage = "الضريبة لا يمكن أن تكون سالبة")]
    public decimal Tax { get; set; }

    [BindNever]
    [Display(Name = "المبلغ الصافي")]
    public decimal NetAmount { get; set; }

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    /// <summary>
    /// كيان جديد للخدمة. ما ليس مكتوبًا هنا لا وجود له عندها: لا رقم ولا إجمالي ولا صافي
    /// ولا مدفوع ولا فرع ولا أختام.
    /// </summary>
    public SaleInvoice ToEntity() => new()
    {
        CustomerId = CustomerId,
        InvoiceDate = InvoiceDate,
        PaymentTerms = PaymentTerms,
        DueDate = DueDate,
        Discount = Discount,
        Discount2 = Discount2,
        Discount3 = Discount3,
        Tax = Tax,
        Notes = Notes
    };
}

/// <summary>
/// سطر فاتورة بيع في النموذج. الخصم المحسوب للسطر (<c>Total</c> و<code>Gross</code>) ليس
/// جزءًا من الطلب، والإجمالي نفسه تحسبه الخدمة من الكميات والأسعار.
/// </summary>
public class SaleInvoiceLineFormModel
{
    [Display(Name = "الصنف")]
    public int ItemId { get; set; }

    [Range(0, 999999999, ErrorMessage = "العدد لا يمكن أن يكون سالباً")]
    public decimal Count { get; set; }

    [Range(0, 999999999, ErrorMessage = "الكمية لا يمكن أن تكون سالبة")]
    public decimal Quantity { get; set; }

    [Range(0, 999999999, ErrorMessage = "سعر الوحدة لا يمكن أن يكون سالباً")]
    public decimal UnitPrice { get; set; }

    public SaleInvoiceItem ToEntity() => new()
    {
        ItemId = ItemId,
        Count = Count,
        Quantity = Quantity,
        UnitPrice = UnitPrice
    };
}

/// <summary>
/// يحوّل الكيان المحفوظ إلى حمولة النموذج، فيبقى رقم الفاتورة ظاهرًا بعد فشل الحفظ.
/// </summary>
public static class SaleInvoiceFormModelExtensions
{
    public static SaleInvoiceFormModel ToFormModel(this SaleInvoice invoice)
        => new()
        {
            InvoiceNumber = invoice.InvoiceNumber,
            CustomerId = invoice.CustomerId,
            InvoiceDate = invoice.InvoiceDate,
            PaymentTerms = invoice.PaymentTerms,
            DueDate = invoice.DueDate,
            Discount = invoice.Discount,
            Discount2 = invoice.Discount2,
            Discount3 = invoice.Discount3,
            Tax = invoice.Tax,
            Notes = invoice.Notes
        };

    public static SaleInvoiceLineFormModel ToFormModel(this SaleInvoiceItem line)
        => new()
        {
            ItemId = line.ItemId,
            Count = line.Count,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice
        };
}
