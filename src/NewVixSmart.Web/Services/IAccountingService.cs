using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.Services;

public readonly record struct JournalLine(string Code, decimal Debit, decimal Credit, string? Description = null);

public interface IAccountingService
{
    Task RecordSaleInvoiceAsync(DateTime entryDate, int customerId, decimal netAmount, decimal costAmount, int? currencyId, decimal? exchangeRate, string? user, int? branchId = null);
    Task RecordSaleDeliveryAsync(DateTime entryDate, int customerId, decimal value, decimal cost, int? currencyId, decimal? exchangeRate, string? user, int? branchId = null, int? deliveryId = null, decimal taxAmount = 0m);
    Task RecordPurchaseInvoiceAsync(DateTime entryDate, int supplierId, decimal netAmount, int? currencyId, decimal? exchangeRate, string? user, int? branchId = null);
    Task RecordReceiptAsync(DateTime entryDate, decimal amount, PaymentMethod method, int customerId, string? user, int? branchId = null);
    Task RecordDisbursementAsync(DateTime entryDate, decimal amount, PaymentMethod method, int supplierId, string? user, int? branchId = null);
    Task RecordFxSettlementAsync(DateTime entryDate, decimal cashAmount, decimal receivablePayableReduction, decimal fxGain, decimal fxLoss, PaymentMethod method, JournalSource source, int sourceId, string? user, int? branchId = null);
    Task RecordSaleReturnAsync(DateTime entryDate, decimal amount, string? user, int? branchId = null);
    Task RecordPurchaseReturnAsync(DateTime entryDate, decimal amount, string? user, int? branchId = null);
    Task RecordSaleReturnWithCostAsync(DateTime entryDate, int sourceId, int customerId, decimal valueAmount, decimal costAmount, int? currencyId, decimal? exchangeRate, string? user, int? branchId = null, decimal taxAmount = 0m);
    Task RecordPurchaseReturnWithCostAsync(DateTime entryDate, int sourceId, int supplierId, decimal valueAmount, decimal costAmount, int? currencyId, decimal? exchangeRate, string? user, int? branchId = null);
    Task RecordOpeningStockAsync(int itemId, decimal qty, decimal count, decimal cost, string? user, int? branchId = null);
    Task RecordStockWriteDownAsync(int itemId, decimal qty, decimal count, decimal cost, string? user, int? branchId = null);
    Task PostAsync(JournalSource source, int sourceId, DateTime date, string description, JournalLine[] lines, string? user, int? branchId = null);
}