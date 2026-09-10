using Silk.Trading.Web.Models.Accounting;

namespace Silk.Trading.Web.ViewModels.Reports;

public class PaymentReportViewModel
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<Payment> Payments { get; set; } = new();
    public int ReceiptCount { get; set; }
    public int DisbursementCount { get; set; }
    public decimal TotalReceipts { get; set; }
    public decimal TotalDisbursements { get; set; }
    public decimal Balance => TotalReceipts - TotalDisbursements;
}