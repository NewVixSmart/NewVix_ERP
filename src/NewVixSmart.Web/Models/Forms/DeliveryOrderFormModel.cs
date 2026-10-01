using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Models.Forms;

/// <summary>
/// نموذج أذن التسليم المُرسلة من النموذج.
/// <para>
/// رقم الإذن وحالته وأختام الحفظ والتسليم حقول الخادم؛ الخدمة تولّد الرقم وتقرر الحالة.
/// كانت الحقول مربوطة من الكيان نفسه فصار <c>Status</c> قابلًا للكتابة من المتصفح.
/// هنا للعرض فقط بـ<code>BindNever</code>، فلا خطأ تحقق مخفي ولا باب مفتوح.
/// </para>
/// <para>
/// هذا النوع ليس كيان EF ولا <c>DbSet</c> يشير إليه، فلا يلتقطه <c>AppDbContext</c>.
/// </para>
/// </summary>
public class DeliveryOrderFormModel
{
    [BindNever]
    [Display(Name = "رقم الإذن")]
    public string DeliveryNumber { get; set; } = string.Empty;

    [Display(Name = "فاتورة البيع")]
    public int? SaleInvoiceId { get; set; }

    [Display(Name = "أمر البيع المرتبط")]
    public int? SalesOrderId { get; set; }

    [Display(Name = "حجز المخزون المرتبط")]
    public int? StockReservationId { get; set; }

    [Display(Name = "العميل")]
    [Range(1, int.MaxValue, ErrorMessage = "اختر العميل")]
    public int CustomerId { get; set; }

    [Display(Name = "تاريخ التسليم")]
    [DataType(DataType.Date)]
    public DateTime DeliveryDate { get; set; } = DateTime.Today;

    [BindNever]
    [Display(Name = "حالة الإذن")]
    public DeliveryOrderStatus Status { get; set; }

    [StringLength(100)]
    [Display(Name = "الناقل")]
    public string? Carrier { get; set; }

    [StringLength(100)]
    [Display(Name = "رقم التتبع")]
    public string? TrackingNumber { get; set; }

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public DeliveryOrder ToEntity() => new()
    {
        SaleInvoiceId = SaleInvoiceId,
        SalesOrderId = SalesOrderId,
        StockReservationId = StockReservationId,
        CustomerId = CustomerId,
        DeliveryDate = DeliveryDate,
        Carrier = Carrier,
        TrackingNumber = TrackingNumber,
        Notes = Notes
    };
}

/// <summary>
/// سطر أذن التسليم في النموذج: الصنف والعدد والكم المسلَّم فقط.
/// </summary>
public class DeliveryOrderLineFormModel
{
    [Display(Name = "الصنف")]
    public int ItemId { get; set; }

    [Range(0, 999999999, ErrorMessage = "العدد لا يمكن أن يكون سالباً")]
    public decimal Count { get; set; }

    [Range(0, 999999999, ErrorMessage = "الكمية لا يمكن أن تكون سالبة")]
    public decimal Quantity { get; set; }

    public DeliveryOrderItem ToEntity() => new()
    {
        ItemId = ItemId,
        Count = Count,
        Quantity = Quantity
    };
}

/// <summary>
/// يحوّل الكيان المحفوظ إلى حمولة النموذج، فيبقى رقم الإذن ظاهرًا بعد فشل الحفظ.
/// </summary>
public static class DeliveryOrderFormModelExtensions
{
    public static DeliveryOrderFormModel ToFormModel(this DeliveryOrder delivery)
        => new()
        {
            DeliveryNumber = delivery.DeliveryNumber,
            SaleInvoiceId = delivery.SaleInvoiceId,
            SalesOrderId = delivery.SalesOrderId,
            StockReservationId = delivery.StockReservationId,
            CustomerId = delivery.CustomerId,
            DeliveryDate = delivery.DeliveryDate,
            Status = delivery.Status,
            Carrier = delivery.Carrier,
            TrackingNumber = delivery.TrackingNumber,
            Notes = delivery.Notes
        };

    public static DeliveryOrderLineFormModel ToFormModel(this DeliveryOrderItem line)
        => new()
        {
            ItemId = line.ItemId,
            Count = line.Count,
            Quantity = line.Quantity
        };
}
