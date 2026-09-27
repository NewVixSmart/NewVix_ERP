using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.ViewModels.Sales;

public class CustomerLedgerViewModel
{
    public Customer Customer { get; set; } = null!;
    public List<SaleInvoice> Invoices { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
    public List<SaleReturn> Returns { get; set; } = new();
    public decimal Balance { get; set; }
    public List<CustomerPendingLine> PendingLines { get; set; } = new();

    public decimal PendingValueTotal => PendingLines.Sum(l => l.PendingValue);
}

public class CustomerPendingLine
{
    public string OrderNumber { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public Guid OrderPublicId { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
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
    public string DeliveryNoteNumbers { get; set; } = string.Empty;
}
