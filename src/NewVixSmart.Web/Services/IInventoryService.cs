using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;

namespace NewVixSmart.Web.Services;

public interface IInventoryService
{
    Task<(bool Success, string? Error)> CreateSaleAsync(SaleInvoice invoice, List<SaleInvoiceItem> items, string? user, int? branchId = null, bool beginOwnTransaction = true);
    Task<(bool Success, string? Error)> CreateDeliveryOrderAsync(DeliveryOrder delivery, List<DeliveryOrderItem> items, string? user);
    Task<(bool Success, string? Error, DeliveryOrder? Delivery)> CreateSalesDeliveryNoteAsync(
        int? salesOrderId, int? saleInvoiceId, int? customerId, List<DeliveryOrderItem> items, string? user,
        DateTime? deliveryDate = null, string? notes = null);
    Task<(bool Success, string? Error, DeliveryIssue? Issue)> CreateDeliveryIssueAsync(
        int deliveryId, List<DeliveryIssueItem> items, string? user, DateTime? issueDate = null, string? notes = null,
        string? carrier = null, string? trackingNumber = null);
    Task<(bool Success, string? Error)> IssueDeliveryAsync(int issueId, string? user, int? branchId = null);
    Task<(bool Success, string? Error)> CancelDeliveryIssueAsync(int issueId, string? user);
    Task<(bool Success, string? Error)> DeliverDeliveryOrderAsync(int deliveryId, string? user, int? branchId = null);
    Task<(bool Success, string? Error)> CancelDeliveryOrderAsync(int deliveryId, string? user);
    Task<(bool Success, string? Error)> CreatePurchaseAsync(PurchaseInvoice invoice, List<PurchaseInvoiceItem> items, string? user, int? branchId = null, bool beginOwnTransaction = true);
    Task<(bool Success, string? Error)> CreateSaleReturnAsync(SaleReturn saleReturn, List<SaleReturnItem> items, string? user);
    Task<(bool Success, string? Error)> CreatePurchaseReturnAsync(PurchaseReturn purchaseReturn, List<PurchaseReturnItem> items, string? user);
    Task<(bool Success, string? Error, int ReturnId)> CreateSaleReturnDraftAsync(SaleReturn saleReturn, List<SaleReturnItem> items, string? user);
    Task<(bool Success, string? Error)> PostSaleReturnAsync(int saleReturnId, string? user);
    Task<(bool Success, string? Error, int ReturnId)> CreatePurchaseReturnDraftAsync(PurchaseReturn purchaseReturn, List<PurchaseReturnItem> items, string? user);
    Task<(bool Success, string? Error)> PostPurchaseReturnAsync(int purchaseReturnId, string? user);
    Task<(bool Success, string? Error)> CreateAdjustmentAsync(InventoryAdjustment adjustment, string? user);
    Task<(bool Success, string? Error)> DeleteAdjustmentAsync(int adjustmentId, string? user);
    Task<ConsumedCostResult?> GetConsumedCostAsync(int itemId, IReadOnlyCollection<StockLine> lines);
    Task<IReadOnlyList<StockTransfer>> GetTransfersAsync();
    Task<(bool Success, string? Error)> CreateTransferAsync(StockTransfer transfer, List<StockTransferItem> items, string? user);
    Task<IReadOnlyList<StockSnapshotItem>> GetStockSnapshotAsync(int? warehouseId);
}

public sealed record StockSnapshotItem(
    int ItemId,
    string ItemName,
    decimal TotalCount,
    decimal TotalQuantity,
    decimal WarehouseCount,
    decimal WarehouseQuantity);
