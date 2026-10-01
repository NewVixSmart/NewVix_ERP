using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Purchases;

namespace NewVixSmart.Web.Models.Forms;

/// <summary>
/// نموذج فاتورة الشراء المُرسلة من النموذج: ما يكتبه المشتري وحده.
/// <para>
/// رقم الفاتورة والإجمالي والصافي والمدفوع للعرض فقط هنا؛
/// <see cref="Services.IInventoryService.CreatePurchaseAsync"/> تولّدها وتلغي أي قيمة وصلت معها،
/// فوجودها في النموذج لا يمنح المتصفح حق كتابتها ولا يترك خطأ تحقق مخفيًا بلا مرئي.
/// </para>
/// <para>
/// هذا النوع ليس كيان EF ولا <c>DbSet</c> يشير إليه، فلا يلتقطه <c>AppDbContext</c>.
/// </para>
/// </summary>
public class PurchaseInvoiceFormModel
{
    [BindNever]
    [Display(Name = "رقم الفاتورة")]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Display(Name = "المورد")]
    [Range(1, int.MaxValue, ErrorMessage = "اختر المورد")]
    public int SupplierId { get; set; }

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

    /// <summary>
    /// للعرض فقط. الخدمة تشتقّها من شروط الدفع وتلغي ما يصل، فحقل مُدخل هنا لا معنى له.
    /// </summary>
    [BindNever]
    [Display(Name = "مدفوعة")]
    public bool IsPaid { get; set; }

    [StringLength(50)]
    [Display(Name = "أمر الشراء")]
    public string? OrderReference { get; set; }

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public PurchaseInvoice ToEntity() => new()
    {
        SupplierId = SupplierId,
        InvoiceDate = InvoiceDate,
        PaymentTerms = PaymentTerms,
        DueDate = DueDate,
        Discount = Discount,
        Discount2 = Discount2,
        Discount3 = Discount3,
        Tax = Tax,
        OrderReference = OrderReference,
        Notes = Notes
    };
}

/// <summary>
/// سطر فاتورة شراء في النموذج؛ الإجمالي والخصم المحسوبان يجلسان على الكيان.
/// </summary>
public class PurchaseInvoiceLineFormModel
{
    [Display(Name = "الصنف")]
    public int ItemId { get; set; }

    [Range(0, 999999999, ErrorMessage = "الكمية لا يمكن أن تكون سالبة")]
    public decimal Quantity { get; set; }

    [Range(0, 999999999, ErrorMessage = "العدد لا يمكن أن يكون سالباً")]
    public decimal Count { get; set; }

    [Range(0, 999999999, ErrorMessage = "سعر الوحدة لا يمكن أن يكون سالباً")]
    public decimal UnitPrice { get; set; }

    public PurchaseInvoiceItem ToEntity() => new()
    {
        ItemId = ItemId,
        Quantity = Quantity,
        Count = Count,
        UnitPrice = UnitPrice
    };
}

/// <summary>
/// يحوّل الكيان المحفوظ إلى حمولة النموذج، فيبقى رقم الفاتورة ظاهرًا بعد فشل الحفظ.
/// </summary>
public static class PurchaseInvoiceFormModelExtensions
{
    public static PurchaseInvoiceFormModel ToFormModel(this PurchaseInvoice invoice)
        => new()
        {
            InvoiceNumber = invoice.InvoiceNumber,
            SupplierId = invoice.SupplierId,
            InvoiceDate = invoice.InvoiceDate,
            PaymentTerms = invoice.PaymentTerms,
            DueDate = invoice.DueDate,
            Discount = invoice.Discount,
            Discount2 = invoice.Discount2,
            Discount3 = invoice.Discount3,
            Tax = invoice.Tax,
            OrderReference = invoice.OrderReference,
            Notes = invoice.Notes
        };

    public static PurchaseInvoiceLineFormModel ToFormModel(this PurchaseInvoiceItem line)
        => new()
        {
            ItemId = line.ItemId,
            Quantity = line.Quantity,
            Count = line.Count,
            UnitPrice = line.UnitPrice
        };
}
