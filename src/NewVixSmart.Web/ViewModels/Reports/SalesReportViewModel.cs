using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.ViewModels.Reports;

public class SalesReportViewModel
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<SaleInvoice> Invoices { get; set; } = new();
    public int InvoiceCount { get; set; }
    public decimal TotalSales { get; set; }
    public decimal TotalDiscounts { get; set; }
    public decimal TotalTax { get; set; }
    public decimal NetSales { get; set; }
    public decimal TotalReturns { get; set; }
    public decimal Net => NetSales - TotalReturns;
}
