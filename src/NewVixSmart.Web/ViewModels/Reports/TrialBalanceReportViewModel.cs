namespace NewVixSmart.Web.ViewModels.Reports;

public class TrialBalanceRowViewModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance { get; set; }
    public bool IsDebitBalance => Balance > 0;
    public bool IsCreditBalance => Balance < 0;
}

public class TrialBalanceReportViewModel
{
    public DateTime AsOf { get; set; }
    public List<TrialBalanceRowViewModel> Rows { get; set; } = new();
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public bool IsBalanced => Math.Round(TotalDebit, 2) == Math.Round(TotalCredit, 2);
}
