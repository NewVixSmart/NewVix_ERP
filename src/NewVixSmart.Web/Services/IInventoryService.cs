using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;

namespace NewVixSmart.Web.Services;

public interface IInventoryService
{
    Task<(bool Success, string? Error)> CreateSaleAsync(SaleInvoice invoice, List<SaleInvoiceItem> items, string? user, int? branchId = null);
    Task<(bool Success, string? Error)> CreatePurchaseAsync(PurchaseInvoice invoice, List<PurchaseInvoiceItem> items, string? user, int? branchId = null);
    Task<(bool Success, string? Error)> CreateSaleReturnAsync(SaleReturn saleReturn, List<SaleReturnItem> items, string? user);
    Task<(bool Success, string? Error)> CreatePurchaseReturnAsync(PurchaseReturn purchaseReturn, List<PurchaseReturnItem> items, string? user);
    Task<(bool Success, string? Error, int ReturnId)> CreateSaleReturnDraftAsync(SaleReturn saleReturn, List<SaleReturnItem> items, string? user);
    Task<(bool Success, string? Error)> PostSaleReturnAsync(int saleReturnId, string? user);
    Task<(bool Success, string? Error, int ReturnId)> CreatePurchaseReturnDraftAsync(PurchaseReturn purchaseReturn, List<PurchaseReturnItem> items, string? user);
    Task<(bool Success, string? Error)> PostPurchaseReturnAsync(int purchaseReturnId, string? user);
    Task<(bool Success, string? Error)> CreateAdjustmentAsync(InventoryAdjustment adjustment, string? user);
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
