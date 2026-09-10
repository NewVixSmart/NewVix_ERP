namespace Silk.Trading.Web.ViewModels.Reports;

public class BalanceSheetLineViewModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class BalanceSheetSectionViewModel
{
    public string Title { get; set; } = string.Empty;
    public List<BalanceSheetLineViewModel> Lines { get; set; } = new();
    public decimal Total { get; set; }
}

public class BalanceSheetReportViewModel
{
    public DateTime AsOf { get; set; }

    public BalanceSheetSectionViewModel Assets { get; set; } = new();
    public BalanceSheetSectionViewModel Liabilities { get; set; } = new();
    public BalanceSheetSectionViewModel Equity { get; set; } = new();

    public decimal NetIncome { get; set; }

    public decimal TotalAssets => Assets.Total;
    public decimal TotalLiabilitiesEquity => Liabilities.Total + Equity.Total + NetIncome;
    public bool IsBalanced => Math.Round(TotalAssets, 2) == Math.Round(TotalLiabilitiesEquity, 2);
}
