using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.ViewModels.Sales;

public class CustomerLedgerViewModel
{
    public Customer Customer { get; set; } = null!;
    public List<SaleInvoice> Invoices { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
    public List<SaleReturn> Returns { get; set; } = new();
    public decimal Balance { get; set; }
    public List<CustomerPendingLine> PendingLines { get; set; } = new();

    /// <summary>
    /// The three header totals. The customer ledger and the supplier ledger each used to re-derive
    /// these with LINQ inside Razor - including the "which payment directions count" filter - so the
    /// same word appeared twice in markup and neither copy had a name to test against.
    /// </summary>
    public decimal InvoicesTotal => Invoices.Sum(i => i.NetAmount);

    public decimal ReceiptsTotal => Payments
        .Where(p => p.Type == PaymentType.Receipt)
        .Sum(p => p.Amount);

    public decimal ReturnsTotal => Returns.Sum(r => decimal.Round(ReturnValuation.ReceivableBase(
        r.TotalAmount, r.SaleInvoice?.TotalAmount ?? 0m, r.SaleInvoice?.NetAmount ?? 0m), 2));

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
