using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Services;

public interface ISalesOrdersService
{
    Task<IReadOnlyList<SalesOrder>> GetOrdersAsync(SalesOrderStatus? status = null);
    Task<SalesOrder?> GetOrderAsync(int id);
    Task<(bool Success, string? Error)> CreateOrderAsync(SalesOrder order, List<SalesOrderItem> items, string? user);
    Task<(bool Success, string? Error)> UpdateOrderAsync(SalesOrder order, List<SalesOrderItem> items, string? user);
    Task<(bool Success, string? Error)> ApproveOrderAsync(int orderId);
    Task<(bool Success, string? Error)> CancelOrderAsync(int orderId);
    Task<(bool Success, string? Error)> CreateInvoiceFromOrderAsync(int orderId, string? user);
    Task<(bool Success, string? Error, SaleInvoice? Invoice)> InvoiceOutstandingDeliveriesAsync(
        int orderId, string? user, int? branchId = null);
}