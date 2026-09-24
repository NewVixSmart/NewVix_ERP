using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.ViewModels.Reports;

public class CashFlowMethodTotal
{
    public PaymentMethod Method { get; set; }
    public decimal Receipts { get; set; }
    public decimal Disbursements { get; set; }
    public decimal Net => Receipts - Disbursements;
}

public sealed record CashFlowDetailLine(
    DateTime Date,
    string Doc,
    PaymentType Type,
    string Party,
    PaymentMethod Method,
    string Reference,
    decimal Amount);

public class CashFlowReportViewModel
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }

    public decimal OpeningBalance { get; set; }
    public decimal TotalReceipts { get; set; }
    public decimal TotalDisbursements { get; set; }
    public decimal NetCashFlow => TotalReceipts - TotalDisbursements;
    public decimal ClosingBalance => OpeningBalance + NetCashFlow;

    public List<CashFlowMethodTotal> ByMethod { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
    public List<CashFlowDetailLine> Details { get; set; } = new();
}