using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Models.Forms;

/// <summary>
/// نموذج مرتجع البيع المُرسلة من النموذج.
/// <para>
/// رقم المرتجع وحالته وإجماليه وأختامه حقول الخادم؛
/// <see cref="Services.IInventoryService.CreateSaleReturnDraftAsync"/> تولّد الرقم وتحتسب الإجمالي
/// وتقرر الحالة، فوجودها هنا للعرض فقط بـ<code>BindNever</code>: لا خطأ تحقق مخفي، ولا حق
/// كتابة من المتصفح.
/// </para>
/// <para>
/// هذا النوع ليس كيان EF ولا <c>DbSet</c> يشير إليه، فلا يلتقطه <c>AppDbContext</c>.
/// </para>
/// </summary>
public class SaleReturnFormModel
{
    [BindNever]
    [Display(Name = "رقم المرتجع")]
    public string ReturnNumber { get; set; } = string.Empty;

    [Display(Name = "العميل")]
    [Range(1, int.MaxValue, ErrorMessage = "اختر العميل")]
    public int CustomerId { get; set; }

    [Display(Name = "فاتورة البيع الأصلية (اختيارية)")]
    public int? SaleInvoiceId { get; set; }

    [Display(Name = "تاريخ المرتجع")]
    [DataType(DataType.Date)]
    public DateTime ReturnDate { get; set; } = DateTime.Today;

    [BindNever]
    [Display(Name = "الإجمالي")]
    public decimal TotalAmount { get; set; }

    [StringLength(500)]
    [Display(Name = "سبب المرتجع")]
    public string? Reason { get; set; }

    public SaleReturn ToEntity() => new()
    {
        CustomerId = CustomerId,
        SaleInvoiceId = SaleInvoiceId,
        ReturnDate = ReturnDate,
        Reason = Reason
    };
}

/// <summary>
/// سطر مرتجع بيع في النموذج؛ الإجمالي المحسوب يجلس على الكيان.
/// </summary>
public class SaleReturnLineFormModel
{
    [Display(Name = "الصنف")]
    public int ItemId { get; set; }

    [Range(0, 999999999, ErrorMessage = "العدد لا يمكن أن يكون سالباً")]
    public decimal Count { get; set; }

    [Range(0, 999999999, ErrorMessage = "الكمية لا يمكن أن تكون سالبة")]
    public decimal Quantity { get; set; }

    [Range(0, 999999999, ErrorMessage = "سعر الوحدة لا يمكن أن يكون سالباً")]
    public decimal UnitPrice { get; set; }

    public SaleReturnItem ToEntity() => new()
    {
        ItemId = ItemId,
        Count = Count,
        Quantity = Quantity,
        UnitPrice = UnitPrice
    };
}

/// <summary>
/// يحوّل الكيان المحفوظ إلى حمولة النموذج، فيبقى رقم المرتجع ظاهرًا بعد فشل الحفظ.
/// </summary>
public static class SaleReturnFormModelExtensions
{
    public static SaleReturnFormModel ToFormModel(this SaleReturn saleReturn)
        => new()
        {
            ReturnNumber = saleReturn.ReturnNumber,
            CustomerId = saleReturn.CustomerId,
            SaleInvoiceId = saleReturn.SaleInvoiceId,
            ReturnDate = saleReturn.ReturnDate,
            Reason = saleReturn.Reason
        };

    public static SaleReturnLineFormModel ToFormModel(this SaleReturnItem line)
        => new()
        {
            ItemId = line.ItemId,
            Count = line.Count,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice
        };
}
