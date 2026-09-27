using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.Models.Sales;

public class SalesOrder
{
    [BindNever]
    public int Id { get; set; }

    public Guid PublicId { get; set; } = Guid.NewGuid();

    [Required(ErrorMessage = "ط±ظ‚ظ… ط§ظ„ط£ظ…ط± ظ…ط·ظ„ظˆط¨")]
    [StringLength(50)]
    [Display(Name = "ط±ظ‚ظ… ط§ظ„ط£ظ…ط±")]
    public string OrderNumber { get; set; } = string.Empty;

    [Display(Name = "ط§ظ„ط¹ظ…ظٹظ„")]
    public int CustomerId { get; set; }

    [BindNever]
    public Customer Customer { get; set; } = null!;

    [Display(Name = "طھط§ط±ظٹط® ط§ظ„ط£ظ…ط±")]
    [DataType(DataType.Date)]
    public DateTime OrderDate { get; set; } = DateTime.Today;

    [Display(Name = "طھط§ط±ظٹط® ط§ظ„طھظ†ظپظٹط° ط§ظ„ظ…طھظˆظ‚ط¹")]
    [DataType(DataType.Date)]
    public DateTime? ExpectedDate { get; set; }

    [Display(Name = "ط­ط§ظ„ط© ط§ظ„ط£ظ…ط±")]
    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Draft;

    [StringLength(500)]
    [Display(Name = "ظ…ظ„ط§ط­ط¸ط§طھ")]
    public string? Notes { get; set; }

    [Display(Name = "ط¹ط±ط¶ ط§ظ„ط³ط¹ط± ط§ظ„ظ…طµط¯ط±")]
    public int? SaleQuoteId { get; set; }

    [BindNever]
    public SaleQuote? SaleQuote { get; set; }

    [Display(Name = "ط£ظ†ط´ط¦ ط¨ظˆط§ط³ط·ط©")]
    [BindNever]
    public string? CreatedBy { get; set; }

    [Display(Name = "طھط§ط±ظٹط® ط§ظ„ط¥ظ†ط´ط§ط،")]
    [BindNever]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BindNever]
    public ICollection<SalesOrderItem> Items { get; set; } = new List<SalesOrderItem>();

    [BindNever]
    public ICollection<SaleInvoice> Invoices { get; set; } = new List<SaleInvoice>();

    [BindNever]
    public ICollection<Stock.StockReservation> Reservations { get; set; } = new List<Stock.StockReservation>();

    [BindNever]
    public ICollection<DeliveryOrder> DeliveryOrders { get; set; } = new List<DeliveryOrder>();

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
