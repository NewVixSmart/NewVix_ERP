using Silk.Trading.Web.Models.Accounting;

namespace Silk.Trading.Web.Services;

public sealed record FiscalCloseSummary(int Year, int AccountsCleared, decimal NetIncomeToRetainedEarnings, int EntriesPosted);

public sealed record FiscalReopenSummary(int Year, int EntriesRemoved, decimal RestoredNetIncome);

public interface IFiscalService
{
    Task<FiscalPeriod?> GetPeriodAsync(int year);
    Task EnsurePeriodAsync(int year);
    Task<bool> IsClosedAsync(DateTime date);
    Task ValidateBudgetWriteAsync(int year);
    Task<FiscalCloseSummary> CloseYearAsync(int year, string? user);
    Task<FiscalReopenSummary> ReopenYearAsync(int year, string? user);
}