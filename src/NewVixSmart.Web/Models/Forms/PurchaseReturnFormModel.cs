using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Purchases;

namespace NewVixSmart.Web.Models.Forms;

/// <summary>
/// نموذج مرتجع الشراء المُرسلة من النموذج.
/// <para>
/// رقم المرتجع وحالته وإجماليه وأختام الحفظ حقول الخادم؛
/// <see cref="Services.IInventoryService.CreatePurchaseReturnDraftAsync"/> تولّد الرقم وتحتسب
/// الإجمالي وتقرر الحالة، فوجودها هنا للعرض فقط بـ<code>BindNever</code>: لا خطأ تحقق مخفي،
/// ولا حق كتابة من المتصفح.
/// </para>
/// <para>
/// هذا النوع ليس كيان EF ولا <c>DbSet</c> يشير إليه، فلا يلتقطه <c>AppDbContext</c>.
/// </para>
/// </summary>
public class PurchaseReturnFormModel
{
    [BindNever]
    [Display(Name = "رقم المرتجع")]
    public string ReturnNumber { get; set; } = string.Empty;

    [Display(Name = "المورد")]
    [Range(1, int.MaxValue, ErrorMessage = "اختر المورد")]
    public int SupplierId { get; set; }

    [Display(Name = "فاتورة الشراء الأصلية (اختيارية)")]
    public int? PurchaseInvoiceId { get; set; }

    [Display(Name = "تاريخ المرتجع")]
    [DataType(DataType.Date)]
    public DateTime ReturnDate { get; set; } = DateTime.Today;

    [BindNever]
    [Display(Name = "الإجمالي")]
    public decimal TotalAmount { get; set; }

    [StringLength(500)]
    [Display(Name = "سبب المرتجع")]
    public string? Reason { get; set; }

    public PurchaseReturn ToEntity() => new()
    {
        SupplierId = SupplierId,
        PurchaseInvoiceId = PurchaseInvoiceId,
        ReturnDate = ReturnDate,
        Reason = Reason
    };
}

/// <summary>
/// سطر مرتجع شراء في النموذج؛ الإجمالي المحسوب يجلس على الكيان.
/// </summary>
public class PurchaseReturnLineFormModel
{
    [Display(Name = "الصنف")]
    public int ItemId { get; set; }

    [Range(0, 999999999, ErrorMessage = "الكمية لا يمكن أن تكون سالبة")]
    public decimal Quantity { get; set; }

    [Range(0, 999999999, ErrorMessage = "العدد لا يمكن أن يكون سالباً")]
    public decimal Count { get; set; }

    [Range(0, 999999999, ErrorMessage = "سعر الوحدة لا يمكن أن يكون سالباً")]
    public decimal UnitPrice { get; set; }

    public PurchaseReturnItem ToEntity() => new()
    {
        ItemId = ItemId,
        Quantity = Quantity,
        Count = Count,
        UnitPrice = UnitPrice
    };
}

/// <summary>
/// يحوّل الكيان المحفوظ إلى حمولة النموذج، فيبقى رقم المرتجع ظاهرًا بعد فشل الحفظ.
/// </summary>
public static class PurchaseReturnFormModelExtensions
{
    public static PurchaseReturnFormModel ToFormModel(this PurchaseReturn purchaseReturn)
        => new()
        {
            ReturnNumber = purchaseReturn.ReturnNumber,
            SupplierId = purchaseReturn.SupplierId,
            PurchaseInvoiceId = purchaseReturn.PurchaseInvoiceId,
            ReturnDate = purchaseReturn.ReturnDate,
            Reason = purchaseReturn.Reason
        };

    public static PurchaseReturnLineFormModel ToFormModel(this PurchaseReturnItem line)
        => new()
        {
            ItemId = line.ItemId,
            Quantity = line.Quantity,
            Count = line.Count,
            UnitPrice = line.UnitPrice
        };
}
