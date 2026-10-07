using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Purchases;

namespace NewVixSmart.Web.Models.Forms;

/// <summary>
/// نموذج أمر الشراء المُرسلة من النموذج.
/// <para>
/// كان <c>PurchaseOrder</c> هو ما يُربط: <c>PublicId</c> و<code>Status</code> و
/// <c>OrderNumber</c> و<code>CreatedAt</code> كلها حقول الخادم، وصورتها مخبوزة في الصفحة،
/// فكان يكفي تعديل الحقل المخبوز ليعيد <c>PublicId</c> غير موجود.
/// </para>
/// <para>
/// <c>Id</c> غير قابل للربط: هوية السجل تُقرأ من مسار العنوان <c>/PurchaseOrders/Edit/{id}</c> لا من
/// جسم الطلب. أما <c>RowVersion</c> فمخبوء للعرض فقط: الحقل نفسه <c>BindNever</c>، ويصل رمز
/// التزامن إلى الإجراء كنص مستقل يجعله القيمة الأصلية في EF على السجل الذي تعيد reloadه، فيرفض
/// الحفظ إن غُيّر الأمر في جلسة أخرى.
/// </para>
/// <para>
/// هذا النوع ليس كيان EF ولا <c>DbSet</c> يشير إليه، فلا يلتقطه <c>AppDbContext</c>.
/// </para>
/// </summary>
public class PurchaseOrderFormModel
{
    [BindNever]
    public int Id { get; set; }

    [BindNever]
    [Display(Name = "رقم أمر الشراء")]
    public string OrderNumber { get; set; } = string.Empty;

    [Display(Name = "المورد")]
    [Range(1, int.MaxValue, ErrorMessage = "اختر المورد")]
    public int SupplierId { get; set; }

    [Display(Name = "تاريخ الأمر")]
    [DataType(DataType.Date)]
    public DateTime OrderDate { get; set; } = DateTime.Today;

    [Display(Name = "التاريخ المتوقع")]
    [DataType(DataType.Date)]
    public DateTime? ExpectedDate { get; set; }

    [BindNever]
    [Display(Name = "الحالة")]
    public PurchaseOrderStatus Status { get; set; }

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [BindNever]
    public byte[]? RowVersion { get; set; }

    public PurchaseOrder ToEntity() => new()
    {
        Id = Id,
        SupplierId = SupplierId,
        OrderDate = OrderDate,
        ExpectedDate = ExpectedDate,
        Notes = Notes
    };
}

/// <summary>
/// سطر أمر الشراء في النموذج: الصنف والعدد والكمية وسعر الوحدة.
/// <para>
/// <c>Id</c> مرجع دائري لتعديل مسودة: النموذج يعرضه، والخدمة تقبله فقط إذا كان سطراً من
/// هذا الأمر، فما جاء من أمر آخر يُسقَط إلى صفر ويُعامل سطراً جديداً.
/// </para>
/// <para>
/// <c>ReceivedQty</c> و<code>ReceivedCount</code> يجمعهما مسار الاستلام ولا وجود لهما هنا،
/// و<code>PurchaseOrderId</code> ملك للخدمة.
/// </para>
/// </summary>
public class PurchaseOrderLineFormModel
{
    [Range(0, int.MaxValue, ErrorMessage = "معرّف السطر غير صحيح")]
    public int Id { get; set; }

    [Display(Name = "الصنف")]
    public int ItemId { get; set; }

    [Range(0, 999999999, ErrorMessage = "الكمية لا يمكن أن تكون سالبة")]
    public decimal Quantity { get; set; }

    [Range(0, 999999999, ErrorMessage = "العدد لا يمكن أن يكون سالباً")]
    public decimal Count { get; set; }

    [Range(0, 999999999, ErrorMessage = "سعر الوحدة لا يمكن أن يكون سالباً")]
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// للعرض فقط: <c>BindNever</c> يمنع ما يرسله العميل من الوصول إلى هنا، و<c>ToEntity()</c>
    /// لا تنقله، والخدمة لا تقرأ رمز السطر أصلًا؛ فالقيمة الأصلية في EF عند الحفظ هي ما حمّله
    /// هذا الطلب نفسه من قاعدة البيانات، فيُكتشف تعارضُ ما بين تحميل الطلب وحفظه فقط، لا ما
    /// جرى والصفحة مفتوحة في المتصفّح. و<c>orderRowVersion</c> يحمي الترويسة لا السطر.
    /// ومن أراد إعادة وصل رمز السطر إلى الحفظ فليبدأ من هذا العقد لا بفرعٍ في الخدمة وحده:
    /// الوصل الناقص يُنتج كودًا ميّتًا، يقع عند أول من يمرّر الرمز فيَختلّ الحفظ.
    /// </summary>
    [BindNever]
    public byte[]? RowVersion { get; set; }

    public PurchaseOrderItem ToEntity() => new()
    {
        Id = Id,
        ItemId = ItemId,
        Quantity = Quantity,
        Count = Count,
        UnitPrice = UnitPrice
    };
}

/// <summary>
/// يحوّل الأوامر المحفوظة إلى حمولة النموذج، فيبقى الرقم والحالة ظاهرين بعد فشل الحفظ.
/// </summary>
public static class PurchaseOrderFormModelExtensions
{
    public static PurchaseOrderFormModel ToFormModel(this PurchaseOrder order)
        => new()
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            SupplierId = order.SupplierId,
            OrderDate = order.OrderDate,
            ExpectedDate = order.ExpectedDate,
            Status = order.Status,
            Notes = order.Notes,
            RowVersion = order.RowVersion
        };

    public static PurchaseOrderLineFormModel ToFormModel(this PurchaseOrderItem line)
        => new()
        {
            Id = line.Id,
            ItemId = line.ItemId,
            Quantity = line.Quantity,
            Count = line.Count,
            UnitPrice = line.UnitPrice,
            RowVersion = line.RowVersion
        };
}
