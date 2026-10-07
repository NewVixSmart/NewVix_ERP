using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.Tests.Fakes;

public sealed class ThrowingAccountingService : IAccountingService
{
    private static InvalidOperationException Fail(string method) =>
        new($"ThrowingAccountingService: {method} was called. This test must not depend on ledger posting; pass a real AccountingService with seeded GL accounts instead.");

    public Task RecordSaleInvoiceAsync(DateTime entryDate, int customerId, decimal netAmount, decimal costAmount, string? user, int? branchId = null)
        => throw Fail(nameof(RecordSaleInvoiceAsync));

    public Task RecordSaleInvoiceRevenueAsync(DateTime entryDate, int customerId, decimal netAmount, decimal taxAmount, string? user, int? branchId = null, int? invoiceId = null)
        => throw Fail(nameof(RecordSaleInvoiceRevenueAsync));

    public Task RecordSaleIssueCostAsync(DateTime entryDate, int issueId, decimal costAmount, string? user, int? branchId = null)
        => throw Fail(nameof(RecordSaleIssueCostAsync));

    public Task RecordSaleDeliveryAsync(DateTime entryDate, int customerId, decimal value, decimal cost, string? user, int? branchId = null, int? deliveryId = null, decimal taxAmount = 0m)
        => throw Fail(nameof(RecordSaleDeliveryAsync));

    public Task RecordPurchaseInvoiceAsync(DateTime entryDate, int supplierId, decimal netAmount, string? user, int? branchId = null)
        => throw Fail(nameof(RecordPurchaseInvoiceAsync));

    public Task RecordReceiptAsync(DateTime entryDate, decimal amount, PaymentMethod method, int customerId, string? user, int? branchId = null)
        => throw Fail(nameof(RecordReceiptAsync));

    public Task RecordDisbursementAsync(DateTime entryDate, decimal amount, PaymentMethod method, int supplierId, string? user, int? branchId = null)
        => throw Fail(nameof(RecordDisbursementAsync));

    public Task RecordSaleReturnAsync(DateTime entryDate, decimal amount, string? user, int? branchId = null)
        => throw Fail(nameof(RecordSaleReturnAsync));

    public Task RecordPurchaseReturnAsync(DateTime entryDate, decimal amount, string? user, int? branchId = null)
        => throw Fail(nameof(RecordPurchaseReturnAsync));

    public Task RecordSaleReturnWithCostAsync(DateTime entryDate, int sourceId, int customerId, decimal valueAmount, decimal costAmount, string? user, int? branchId = null, decimal taxAmount = 0m)
        => throw Fail(nameof(RecordSaleReturnWithCostAsync));

    public Task RecordPurchaseReturnWithCostAsync(DateTime entryDate, int sourceId, int supplierId, decimal valueAmount, decimal costAmount, string? user, int? branchId = null)
        => throw Fail(nameof(RecordPurchaseReturnWithCostAsync));

    public Task RecordOpeningStockAsync(int itemId, decimal qty, decimal count, decimal cost, string? user, int? branchId = null, DateTime? date = null)
        => throw Fail(nameof(RecordOpeningStockAsync));

    public Task RecordStockWriteDownAsync(int itemId, decimal qty, decimal count, decimal cost, string? user, int? branchId = null, DateTime? date = null, int? adjustmentId = null)
        => throw Fail(nameof(RecordStockWriteDownAsync));

    public Task RecordStockVarianceUpAsync(int itemId, decimal qty, decimal count, decimal cost, string? user, int? branchId = null, DateTime? date = null, int? adjustmentId = null)
        => throw Fail(nameof(RecordStockVarianceUpAsync));

    public Task PostAsync(JournalSource source, int sourceId, DateTime date, string description, JournalLine[] lines, string? user, int? branchId = null, string? entryNumber = null, int? sourceDocumentId = null)
        => throw Fail(nameof(PostAsync));

    public Task<JournalEntry?> GetEntryForSourceAsync(JournalSource source, int sourceId)
        => throw Fail(nameof(GetEntryForSourceAsync));
}
