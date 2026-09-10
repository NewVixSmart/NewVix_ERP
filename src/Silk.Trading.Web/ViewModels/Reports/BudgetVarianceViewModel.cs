using Silk.Trading.Web.Models.Accounting;

namespace Silk.Trading.Web.ViewModels.Reports;

public sealed record BudgetLineRecord(GLAccount Account, decimal Amount);

public class VarianceRow
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public GLAccountType Type { get; set; }
    public decimal Budget { get; set; }
    public decimal Actual { get; set; }
    public decimal Variance { get; set; }
    public decimal VariancePct { get; set; }
}

public class BudgetVarianceViewModel
{
    public int Year { get; set; }
    public List<VarianceRow> Rows { get; set; } = new();
    public decimal TotalBudgetRevenue { get; set; }
    public decimal TotalActualRevenue { get; set; }
    public decimal TotalBudgetExpense { get; set; }
    public decimal TotalActualExpense { get; set; }
    public List<int> Revenues { get; set; } = new();
}
