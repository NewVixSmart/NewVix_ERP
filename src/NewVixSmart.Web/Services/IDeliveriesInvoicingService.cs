using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Services;

public interface IDeliveriesInvoicingService
{
    Task<(bool Success, string? Error, SaleInvoice? Invoice)> CreateInvoiceFromIssuesAsync(
        IReadOnlyList<int> issueIds, SaleInvoice invoice, string? user, int? branchId = null);

    Task<IReadOnlyList<int>> GetOutstandingIssueIdsAsync(int salesOrderId);
}
