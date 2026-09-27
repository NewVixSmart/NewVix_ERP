using NewVixSmart.Web.Models.Stock;

namespace NewVixSmart.Web.Services;

public readonly record struct ItemAvailability(
    int ItemId,
    string ItemName,
    decimal CurrentQuantity,
    decimal CurrentCount,
    decimal ReservedQuantity,
    decimal ReservedCount)
{
    public decimal AvailableQuantity => CurrentQuantity - ReservedQuantity;
    public decimal AvailableCount => CurrentCount - ReservedCount;
}

public interface IStockReservationsService
{
    Task<IReadOnlyList<ItemAvailability>> GetAvailabilityAsync(IEnumerable<int> itemIds);
    Task<ItemAvailability?> GetAvailabilityAsync(int itemId);
    Task<StockReservation?> GetForOrderAsync(int salesOrderId);
    Task<StockReservation?> GetByIdAsync(int reservationId);
    Task<IReadOnlyList<StockReservation>> GetAllAsync(StockReservationStatus? status = null);
    Task<(bool Success, string? Error)> ReserveOrderAsync(int salesOrderId, string? user, bool beginOwnTransaction = true);
    Task<(bool Success, string? Error, StockReservation? Reservation)> CreateStandaloneAsync(
        StockReservation reservation, List<StockReservationLine> lines, string? user, bool beginOwnTransaction = true);
    Task<(bool Success, string? Error)> ReleaseAsync(int reservationId, string? user, bool beginOwnTransaction = true);
    Task ReleaseForOrderAsync(int salesOrderId, string? user);
    Task<(bool Success, string? Error)> ConsumeForIssuesAsync(
        IReadOnlyCollection<DeliveryIssueItemLine> lines, int? customerId, DateTime date, bool beginOwnTransaction = true);
    Task RecalculateItemReservationsAsync(int itemId);
}

public readonly record struct DeliveryIssueItemLine(
    int ItemId,
    int? SalesOrderItemId,
    decimal Quantity,
    decimal Count);
