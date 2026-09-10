namespace Silk.Trading.Web.ViewModels.Reports;

public class IncomeStatementLineViewModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class IncomeStatementReportViewModel
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }

    public List<IncomeStatementLineViewModel> RevenueLines { get; set; } = new();
    public decimal TotalRevenue { get; set; }

    public List<IncomeStatementLineViewModel> ContraRevenueLines { get; set; } = new();
    public decimal TotalContraRevenue { get; set; }

    public List<IncomeStatementLineViewModel> ExpenseLines { get; set; } = new();
    public decimal TotalExpenses { get; set; }

    public List<IncomeStatementLineViewModel> ContraExpenseLines { get; set; } = new();
    public decimal TotalContraExpenses { get; set; }

    public decimal NetRevenue => TotalRevenue - TotalContraRevenue;
    public decimal NetExpenses => TotalExpenses - TotalContraExpenses;
    public decimal NetIncome => NetRevenue - NetExpenses;
}
