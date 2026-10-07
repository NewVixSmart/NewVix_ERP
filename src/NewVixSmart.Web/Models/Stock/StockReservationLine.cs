using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Models.Stock;

public class StockReservationLine
{
    [BindNever]
    public int Id { get; set; }

    [BindNever]
    public int StockReservationId { get; set; }

    [BindNever]
    public StockReservation StockReservation { get; set; } = null!;

    [Display(Name = "الصنف")]
    public int ItemId { get; set; }

    [BindNever]
    public Item Item { get; set; } = null!;

    [Display(Name = "سطر أمر البيع")]
    public int? SalesOrderItemId { get; set; }

    [BindNever]
    public SalesOrderItem? SalesOrderItem { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الكمية المحجوزة")]
    [Range(0, 999999999, ErrorMessage = "الكمية لا يمكن أن تكون سالبة")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "العدد المحجوز")]
    [Range(0, 999999999, ErrorMessage = "العدد لا يمكن أن يكون سالباً")]
    public decimal Count { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "الكمية المستهلكة")]
    public decimal ConsumedQuantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "العدد المستهلك")]
    public decimal ConsumedCount { get; set; }

    [BindNever]
    [NotMapped]
    public decimal RemainingQuantity => Quantity - ConsumedQuantity;

    [BindNever]
    [NotMapped]
    public decimal RemainingCount => Count - ConsumedCount;

    [Timestamp]
    [BindNever]
    public byte[]? RowVersion { get; set; }
}
