using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Models.Sales;

namespace Silk.Trading.Web.ViewModels.Sales;

public class CustomerLedgerViewModel
{
    public Customer Customer { get; set; } = null!;
    public List<SaleInvoice> Invoices { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
    public List<SaleReturn> Returns { get; set; } = new();
    public decimal Balance { get; set; }
}