using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Models.Forms;

/// <summary>
/// نموذج أمر التسليم المُرسلة من النموذج.
/// <para>
/// رقم الأمر وحالته والمستخدم الذي أنشأه حقول الخادم؛ <c>InventoryService</c> يولّد الرقم
/// ويقرر الانتقال من <c>Draft</c> إلى <c>Issued</c>. كانت مربوطة من الكيان نفسه فصار
/// <c>Status</c> و<code>IssueNumber</code> قابلين للكتابة من المتصفح.
/// هنا للعرض فقط بـ<code>BindNever</code>.
/// </para>
/// <para>
/// هذا النوع ليس كيان EF ولا <c>DbSet</c> يشير إليه، فلا يلتقطه <c>AppDbContext</c>.
/// </para>
/// </summary>
public class DeliveryIssueFormModel
{
    [BindNever]
    [Display(Name = "رقم أمر التسليم")]
    public string IssueNumber { get; set; } = string.Empty;

    [Display(Name = "أذن التسليم")]
    [Range(1, int.MaxValue, ErrorMessage = "اختر أذن التسليم")]
    public int DeliveryOrderId { get; set; }

    [BindNever]
    [Display(Name = "العميل")]
    public int CustomerId { get; set; }

    [Display(Name = "تاريخ الأمر")]
    [DataType(DataType.Date)]
    public DateTime IssueDate { get; set; } = DateTime.Today;

    [BindNever]
    [Display(Name = "حالة الأمر")]
    public DeliveryIssueStatus Status { get; set; }

    [StringLength(100)]
    [Display(Name = "الناقل")]
    public string? Carrier { get; set; }

    [StringLength(100)]
    [Display(Name = "رقم التتبع")]
    public string? TrackingNumber { get; set; }

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}

/// <summary>
/// سطر أمر التسليم في النموذج: الصنف وسطر إذن التسليم المُسلَّم منه والكمية والعدد.
/// <para>
/// <c>DeliveryOrderItemId</c> مدخل حقيقي لا حقل خادم، والخدمة تتحقق أن السطر ينتمي فعلاً
/// إلى إذن التسليم المختار قبل الاعتماد عليه.
/// </para>
/// <para>
/// <c>SalesOrderItemId</c> ليس هنا ولا في النموذج: النموذج لا ترسله، وكان الكيان يقبله
/// فكان المتصفح يقدر يدّعي سطر أمر بيع من أمر آخر. الخدمة تشتقّه من إذن التسليم نفسه.
/// </para>
/// </summary>
public class DeliveryIssueLineFormModel
{
    [Display(Name = "الصنف")]
    public int ItemId { get; set; }

    [Display(Name = "سطر إذن التسليم")]
    [Range(1, int.MaxValue, ErrorMessage = "سطر إذن التسليم غير صحيح")]
    public int DeliveryOrderItemId { get; set; }

    [Range(0, 999999999, ErrorMessage = "الكمية لا يمكن أن تكون سالبة")]
    public decimal Quantity { get; set; }

    [Range(0, 999999999, ErrorMessage = "العدد لا يمكن أن يكون سالباً")]
    public decimal Count { get; set; }

    public DeliveryIssueItem ToEntity() => new()
    {
        ItemId = ItemId,
        DeliveryOrderItemId = DeliveryOrderItemId,
        Quantity = Quantity,
        Count = Count
    };
}
