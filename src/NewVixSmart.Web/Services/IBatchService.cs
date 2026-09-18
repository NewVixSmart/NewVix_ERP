using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;

namespace NewVixSmart.Web.Services;

public interface IBatchService
{
    Task<BatchResults> RunSalesBatchAsync(BatchSalesBatchRequest request, string? user, int? branchId = null);
    Task<BatchResults> RunAdjustmentBatchAsync(BatchAdjustmentRequest request, string? user);
    Task<IReadOnlyList<SaleInvoice>> RecentSalesAsync(int take);
    Task<IReadOnlyList<InventoryAdjustment>> RecentAdjustmentsAsync(int take);
}

public sealed record BatchSalesLine(int ItemId, decimal Quantity, decimal Count, decimal UnitPrice, decimal Discount);

public sealed record BatchSalesInvoice(List<BatchSalesLine> Lines);

public sealed record BatchSalesBatchRequest(
    int CustomerId,
    DateTime InvoiceDate,
    InvoicePaymentTerms PaymentTerms,
    int? CurrencyId,
    decimal? ExchangeRate,
    decimal Discount,
    decimal? Discount2,
    decimal? Discount3,
    decimal Tax,
    string? Notes,
    List<BatchSalesInvoice> Invoices);

public sealed record BatchAdjustmentLine(int ItemId, decimal NewCount, decimal NewQuantity);

public sealed record BatchAdjustmentRequest(DateTime AdjustDate, string? Reason, List<BatchAdjustmentLine> Lines);

public sealed record BatchDocumentResult(int Sequence, string DocType, string? DocumentNumber, bool Success, string? Error);

public sealed record BatchResults(string Title, List<BatchDocumentResult> Results)
{
    public int SuccessCount => Results.Count(r => r.Success);
    public int FailCount => Results.Count(r => !r.Success);
}