namespace NewVixSmart.Web.ViewModels.Reports;

public class PendingDeliveryReportViewModel
{
    public List<PendingDeliveryLineViewModel> Lines { get; set; } = new();
    public decimal TotalValue { get; set; }
    public int TotalQty { get; set; }
    public int TotalCount { get; set; }
}

public class PendingDeliveryLineViewModel
{
    public int OrderId { get; set; }
    public Guid OrderPublicId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Count { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DeliveredQty { get; set; }
    public decimal DeliveredCount { get; set; }
    public decimal InvoicedQty { get; set; }
    public decimal InvoicedCount { get; set; }
    public decimal ReservedQty { get; set; }
    public decimal ReservedCount { get; set; }
    public decimal PendingQty { get; set; }
    public decimal PendingCount { get; set; }
    public decimal PendingValue => (PendingQty > 0 ? PendingQty : PendingCount) * UnitPrice;
}
