using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Stock;

namespace NewVixSmart.Web.Models.Forms;

/// <summary>
/// نموذج تحويل المخزون المُرسلة من النموذج.
/// <para>
/// كان <c>StockTransfer</c> هو ما يُربط: رقم التحويل حقل خادم يولّده
/// <c>InventoryService.NextTransferNumberAsync</c>، بينما <c>[Required]</c> على الكيان كان
/// يترك خطأ تحقق واقفاً في <c>ModelState</c> لكل إرسال دون أن يقرأه أحد.
/// </para>
/// <para>
/// هنا <c>TransferNumber</c> للعرض فقط بـ<code>BindNever</code>: لا باب مفتوح ولا خطأ وهمي.
/// </para>
/// <para>
/// هذا النوع ليس كيان EF ولا <c>DbSet</c> يشير إليه، فلا يلتقطه <c>AppDbContext</c>.
/// </para>
/// </summary>
public class StockTransferFormModel
{
    [BindNever]
    [Display(Name = "رقم التحويل")]
    public string TransferNumber { get; set; } = string.Empty;

    [Display(Name = "المستودع المصدر")]
    [Range(1, int.MaxValue, ErrorMessage = "اختر المستودع المصدر")]
    public int SourceWarehouseId { get; set; }

    [Display(Name = "المستودع الوجهة")]
    [Range(1, int.MaxValue, ErrorMessage = "اختر المستودع الوجهة")]
    public int TargetWarehouseId { get; set; }

    [Display(Name = "تاريخ التحويل")]
    [DataType(DataType.Date)]
    public DateTime TransferDate { get; set; } = DateTime.Today;

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public StockTransfer ToEntity() => new()
    {
        SourceWarehouseId = SourceWarehouseId,
        TargetWarehouseId = TargetWarehouseId,
        TransferDate = TransferDate,
        Notes = Notes
    };
}

/// <summary>
/// سطر تحويل المخزون في النموذج: الصنف والعدد والكمية وتكلفة الوحدة وتاريخ الاستلام.
/// <para>
/// <c>Id</c> و<code>StockTransferId</code> لا وجود لهما هنا. كانا قابلين للربط فكان يقدر
/// المتصفح يرسل <c>Id</c> لسطر تحويل قائم فيستبدله، والخدمة تضيف السطور إلى تحويل جديد
/// فتصبح قاعدة البيانات غير متسقة.
/// </para>
/// </summary>
public class StockTransferLineFormModel
{
    [Display(Name = "الصنف")]
    public int ItemId { get; set; }

    [Range(0, 999999999, ErrorMessage = "العدد لا يمكن أن يكون سالباً")]
    public decimal Count { get; set; }

    [Range(0, 999999999, ErrorMessage = "الكمية لا يمكن أن تكون سالبة")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,6)")]
    [Display(Name = "تكلفة الوحدة")]
    public decimal UnitCost { get; set; }

    [Display(Name = "تاريخ الاستلام الأصلي")]
    [DataType(DataType.Date)]
    public DateTime DateReceived { get; set; }

    public StockTransferItem ToEntity() => new()
    {
        ItemId = ItemId,
        Count = Count,
        Quantity = Quantity,
        UnitCost = UnitCost,
        DateReceived = DateReceived
    };
}
