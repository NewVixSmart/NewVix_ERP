using Silk.Trading.Web.ViewModels.Reports;

namespace Silk.Trading.Web.Services;

public interface IReportService
{
    Task<DashboardReportViewModel> GetDashboardAsync();
    Task<TrialBalanceReportViewModel> TrialBalanceAsync(DateTime asOf);
    Task<IncomeStatementReportViewModel> IncomeStatementAsync(DateTime from, DateTime to);
    Task<BalanceSheetReportViewModel> BalanceSheetAsync(DateTime asOf);

    Task<byte[]> ExportTrialBalancePdfAsync(DateTime asOf);
    Task<byte[]> ExportTrialBalanceXlsxAsync(DateTime asOf);
    Task<byte[]> ExportIncomeStatementPdfAsync(DateTime from, DateTime to);
    Task<byte[]> ExportIncomeStatementXlsxAsync(DateTime from, DateTime to);
    Task<byte[]> ExportBalanceSheetPdfAsync(DateTime asOf);
    Task<byte[]> ExportBalanceSheetXlsxAsync(DateTime asOf);
    Task<byte[]> ExportItemsXlsxAsync();
    Task<byte[]> ExportStockXlsxAsync(bool lowOnly);
    Task<byte[]> ExportSalesXlsxAsync(DateTime from, DateTime to);
    Task<byte[]> ExportPurchasesXlsxAsync(DateTime from, DateTime to);
    Task<byte[]> ExportPaymentsXlsxAsync(DateTime from, DateTime to);

    Task<byte[]> ExportAuditLedgerXlsxAsync(DateTime? from, DateTime? to, int? accountId, Models.Accounting.JournalSource? source);
    Task<byte[]> ExportBudgetVarianceXlsxAsync(int year, IReadOnlyList<(string Code, string Name, decimal Budget, decimal Actual, decimal Variance, decimal VariancePct)> rows);
}