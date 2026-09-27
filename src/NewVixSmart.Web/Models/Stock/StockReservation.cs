using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Models.Stock;

public class StockReservation
{
    [BindNever]
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "رقم الحجز مطلوب")]
    [StringLength(50)]
    [Display(Name = "رقم الحجز")]
    public string ReservationNumber { get; set; } = string.Empty;

    [Display(Name = "أمر البيع المرتبط")]
    public int? SalesOrderId { get; set; }

    [BindNever]
    public SalesOrder? SalesOrder { get; set; }

    [Display(Name = "العميل")]
    public int? CustomerId { get; set; }

    [BindNever]
    public Sales.Customer? Customer { get; set; }

    [Display(Name = "حالة الحجز")]
    public StockReservationStatus Status { get; set; } = StockReservationStatus.Active;

    [StringLength(500)]
    [Display(Name = "السبب")]
    public string? Reason { get; set; }

    [StringLength(500)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "أنشئ بواسطة")]
    [BindNever]
    public string? CreatedBy { get; set; }

    [Display(Name = "تاريخ الإنشاء")]
    [BindNever]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "حرَّر بواسطة")]
    [BindNever]
    public string? ReleasedBy { get; set; }

    [Display(Name = "تاريخ التحرير")]
    [BindNever]
    public DateTime? ReleasedAt { get; set; }

    [Timestamp]
    [BindNever]
    public byte[]? RowVersion { get; set; }

    [BindNever]
    public ICollection<StockReservationLine> Items { get; set; } = new List<StockReservationLine>();

    [BindNever]
    public decimal TotalQuantity => Items.Sum(i => i.Quantity);

    [BindNever]
    public decimal TotalCount => Items.Sum(i => i.Count);
}
