using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Models.Purchases;

namespace Silk.Trading.Web.ViewModels.Purchases;

public class SupplierLedgerViewModel
{
    public Supplier Supplier { get; set; } = null!;
    public List<PurchaseInvoice> Invoices { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
    public List<PurchaseReturn> Returns { get; set; } = new();
    public decimal Balance { get; set; }
}