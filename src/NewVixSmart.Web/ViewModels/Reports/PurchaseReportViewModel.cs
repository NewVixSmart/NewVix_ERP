using NewVixSmart.Web.Models.Purchases;

namespace NewVixSmart.Web.ViewModels.Reports;

public class PurchaseReportViewModel
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<PurchaseInvoice> Invoices { get; set; } = new();
    public int InvoiceCount { get; set; }
    public decimal TotalPurchases { get; set; }
    public decimal TotalDiscounts { get; set; }
    public decimal TotalTax { get; set; }
    public decimal NetPurchases { get; set; }
    public decimal TotalReturns { get; set; }
    public decimal Net => NetPurchases - TotalReturns;
}