using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Purchases;

namespace NewVixSmart.Web.ViewModels.Purchases;

public class SupplierLedgerViewModel
{
    public Supplier Supplier { get; set; } = null!;
    public List<PurchaseInvoice> Invoices { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
    public List<PurchaseReturn> Returns { get; set; } = new();
    public decimal Balance { get; set; }
}