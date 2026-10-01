using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Models.Forms;

/// <summary>
/// نموذج النموذج الخاص بأمر البيع: ما يستطيع العميل إدخاله وحده.
/// <para>
/// كائن <c>POST</c> لا يجب أن يكون كيان EF. ربط <see cref="SalesOrder"/> مباشرة كان يجعل
/// <c>OrderNumber</c> و<code>Status</code> و<code>RowVersion</code> وأالكميات المحسوبة داخل
/// <c>ModelState</c>: خطأ تحقق لا يستطيع المستخدم إصلاحه ولا يراه، و<code>Status</code> قابل للكتابة
/// من المتصفح. هذا النوع يحسم الأمرين معًا، فما ليس عليه اسم هنا لا يُربط أصلًا.
/// </para>
/// <para>
/// ما ليس هنا مقصود: ترقيم المستند و<code>Status</code> و<code>PublicId</code> و<code>CreatedAt</code>
/// و<code>CreatedBy</code> و<code>RowVersion</code>. الثابت - أن لكل أمر رقمًا - يبقى على الكيان
/// <see cref="SalesOrder"/>؛ هنا فقط لا مكان له في الطلب.
/// </para>
/// <para>
/// هذا النوع ليس كيان EF ولا يُربط بـ<code>DbContext</code>: لا <c>DbSet</c> يشير إليه ولا خاصية
/// تنقلية من أي كيان، فلا يلتقطه <c>AppDbContext</c> ولا يتغير به شيء في لقطة الهجرات.
/// </para>
/// </summary>
public class SalesOrderFormModel
{
    /// <summary>صفر عند الإنشاء، ومعرّف الأمر عند التعديل. التعديل لا يصل إلى سجل غير هذا.</summary>
    public int Id { get; set; }

    [Display(Name = "مرجع عرض السعر")]
    public int? SaleQuoteId { get; set; }

    [Display(Name = "العميل")]
    [Range(1, int.MaxValue, ErrorMessage = "اختر العميل")]
    public int CustomerId { get; set; }

    [Display(Name = "تاريخ الأمر")]
    [DataType(DataType.Date)]
    public DateTime OrderDate { get; set; } = DateTime.Today;

    [Display(Name = "تاريخ التسليم المتوقع")]
    [DataType(DataType.Date)]
    public DateTime? ExpectedDate { get; set; }

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    /// <summary>
    /// للعرض فقط عند التعديل. <c>BindNever</c> يمنع أخذه من الطلب، فوجوده هنا لا يجعل
    /// الحالة قابلة للكتابة، والقيمة هذه تقرأ من الكيان المحفوظ لا من العميل.
    /// </summary>
    [BindNever]
    public SalesOrderStatus Status { get; set; }

    /// <summary>
    /// كيان جديد للخدمة. لم يُنسخ أي حقل يملكه الخادم - توليد رقم الأمر وحالته وتخزينهما على
    /// <see cref="SalesOrdersService"/>.
    /// </summary>
    public SalesOrder ToEntity() => new()
    {
        CustomerId = CustomerId,
        OrderDate = OrderDate,
        ExpectedDate = ExpectedDate,
        Notes = Notes,
        SaleQuoteId = SaleQuoteId,
        Status = Status
    };
}

/// <summary>
/// سطر في نموذج أمر البيع. الكميات المحسوبة (المفوتَرة والمحجوزة والمسلَّمة) ليست جزءًا من الطلب،
/// فوجودها هنا كان يجعل العميل يكتبها.
/// </summary>
public class SalesOrderLineFormModel
{
    /// <summary>معرّف السطر المحفوظ عند التعديل؛ صفر لسطر جديد.</summary>
    public int Id { get; set; }

    [Display(Name = "الصنف")]
    public int ItemId { get; set; }

    [Display(Name = "الكمية المطلوبة")]
    [Range(0, 999999999, ErrorMessage = "الكمية لا يمكن أن تكون سالبة")]
    public decimal Quantity { get; set; }

    [Display(Name = "العدد المطلوب")]
    [Range(0, 999999999, ErrorMessage = "العدد لا يمكن أن يكون سالباً")]
    public decimal Count { get; set; }

    [Display(Name = "سعر الوحدة")]
    [Range(0, 999999999, ErrorMessage = "السعر لا يمكن أن يكون سالباً")]
    public decimal UnitPrice { get; set; }

    public SalesOrderItem ToEntity() => new()
    {
        Id = Id,
        ItemId = ItemId,
        Quantity = Quantity,
        Count = Count,
        UnitPrice = UnitPrice
    };
}

/// <summary>
/// يحوّل الكيان المحفوظ إلى حمولة النموذج، فيعود التعديل بالقيم المخزَّنة دون أن يمرّ الكيان
/// نفسه على المُرابط أبدًا.
/// </summary>
public static class SalesOrderFormModelExtensions
{
    public static SalesOrderFormModel ToFormModel(this SalesOrder order)
        => new()
        {
            Id = order.Id,
            SaleQuoteId = order.SaleQuoteId,
            CustomerId = order.CustomerId,
            OrderDate = order.OrderDate,
            ExpectedDate = order.ExpectedDate,
            Notes = order.Notes,
            Status = order.Status
        };

    public static SalesOrderLineFormModel ToFormModel(this SalesOrderItem line)
        => new()
        {
            Id = line.Id,
            ItemId = line.ItemId,
            Quantity = line.Quantity,
            Count = line.Count,
            UnitPrice = line.UnitPrice
        };
}
