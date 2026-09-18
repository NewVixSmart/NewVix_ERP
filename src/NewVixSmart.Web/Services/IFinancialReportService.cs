using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.ViewModels.Reports;

namespace NewVixSmart.Web.Services;

public readonly record struct PlAccountActivity(string Code, string Name, GLAccountType Type, NormalBalance NormalBalance, decimal Debit, decimal Credit);

public interface IFinancialReportService
{
    Task<TrialBalanceReportViewModel> TrialBalanceAsync(DateTime asOf);
    Task<IncomeStatementReportViewModel> IncomeStatementAsync(DateTime from, DateTime to);
    Task<BalanceSheetReportViewModel> BalanceSheetAsync(DateTime asOf);
    Task<IReadOnlyList<PlAccountActivity>> GetYearlyPlActivityAsync(int year);
    Task<(decimal Debit, decimal Credit)> GetAccountYearlyActivityAsync(int accountId, int year);
}