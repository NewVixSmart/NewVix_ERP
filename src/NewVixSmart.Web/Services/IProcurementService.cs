using NewVixSmart.Web.Models.Purchases;

namespace NewVixSmart.Web.Services;

public interface IProcurementService
{
    Task<IReadOnlyList<PurchaseOrder>> GetOrdersAsync(PurchaseOrderStatus? status = null, int maxRows = 500);
    Task<PurchaseOrder?> GetOrderAsync(int id);
    Task<(bool Success, string? Error)> CreateOrderAsync(PurchaseOrder order, List<PurchaseOrderItem> items, string? user);
    Task<(bool Success, string? Error)> UpdateOrderAsync(PurchaseOrder order, List<PurchaseOrderItem> items, string? user);
    Task<(bool Success, string? Error)> ApproveOrderAsync(int orderId);
    Task<(bool Success, string? Error)> CancelOrderAsync(int orderId);
    Task<(bool Success, string? Error)> ReceiveOrderLineAsync(int orderId, int orderItemId, decimal receiveQty, decimal receiveCount);
    Task<(bool Success, string? Error)> CreateInvoiceFromOrderAsync(int orderId, string? user);
    Task<IReadOnlyList<SupplierQuote>> GetSupplierQuotesAsync();
    Task<(bool Success, string? Error)> SaveSupplierQuoteAsync(SupplierQuote quote);
}
