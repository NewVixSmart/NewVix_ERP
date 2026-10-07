using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.ViewModels.Purchases;

public class SupplierLedgerViewModel
{
    public Supplier Supplier { get; set; } = null!;
    public List<PurchaseInvoice> Invoices { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
    public List<PurchaseReturn> Returns { get; set; } = new();
    public decimal Balance { get; set; }

    /// <summary>
    /// The three header totals, mirroring the customer ledger. Both ledgers used to re-derive these
    /// with LINQ inside Razor - including the "which payment directions count" filter - so the same
    /// word appeared twice in markup and neither copy had a name to test against.
    /// </summary>
    public decimal InvoicesTotal => Invoices.Sum(i => i.NetAmount);

    public decimal DisbursementsTotal => Payments
        .Where(p => p.Type == PaymentType.Disbursement)
        .Sum(p => p.Amount);

    public decimal ReturnsTotal => Returns.Sum(r => decimal.Round(ReturnValuation.ReceivableBase(
        r.TotalAmount, r.PurchaseInvoice?.TotalAmount ?? 0m, r.PurchaseInvoice?.NetAmount ?? 0m), 2));
}
