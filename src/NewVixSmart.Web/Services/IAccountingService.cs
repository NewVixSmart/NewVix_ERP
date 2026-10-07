using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.Services;

public readonly record struct JournalLine(string Code, decimal Debit, decimal Credit, string? Description = null);

public interface IAccountingService
{
    Task RecordSaleInvoiceAsync(DateTime entryDate, int customerId, decimal netAmount, decimal costAmount, string? user, int? branchId = null);
    Task RecordSaleInvoiceRevenueAsync(DateTime entryDate, int customerId, decimal netAmount, decimal taxAmount, string? user, int? branchId = null, int? invoiceId = null);
    Task RecordSaleIssueCostAsync(DateTime entryDate, int issueId, decimal costAmount, string? user, int? branchId = null);
    Task RecordSaleDeliveryAsync(DateTime entryDate, int customerId, decimal value, decimal cost, string? user, int? branchId = null, int? deliveryId = null, decimal taxAmount = 0m);
    Task RecordPurchaseInvoiceAsync(DateTime entryDate, int supplierId, decimal netAmount, string? user, int? branchId = null);
    Task RecordReceiptAsync(DateTime entryDate, decimal amount, PaymentMethod method, int customerId, string? user, int? branchId = null);
    Task RecordDisbursementAsync(DateTime entryDate, decimal amount, PaymentMethod method, int supplierId, string? user, int? branchId = null);
    Task RecordSaleReturnAsync(DateTime entryDate, decimal amount, string? user, int? branchId = null);
    Task RecordPurchaseReturnAsync(DateTime entryDate, decimal amount, string? user, int? branchId = null);
    Task RecordSaleReturnWithCostAsync(DateTime entryDate, int sourceId, int customerId, decimal valueAmount, decimal costAmount, string? user, int? branchId = null, decimal taxAmount = 0m);
    Task RecordPurchaseReturnWithCostAsync(DateTime entryDate, int sourceId, int supplierId, decimal valueAmount, decimal costAmount, string? user, int? branchId = null);
    Task RecordOpeningStockAsync(int itemId, decimal qty, decimal count, decimal cost, string? user, int? branchId = null, DateTime? date = null);
    Task RecordStockWriteDownAsync(int itemId, decimal qty, decimal count, decimal cost, string? user, int? branchId = null, DateTime? date = null, int? adjustmentId = null);
    Task RecordStockVarianceUpAsync(int itemId, decimal qty, decimal count, decimal cost, string? user, int? branchId = null, DateTime? date = null, int? adjustmentId = null);
    /// <summary>
    /// يرحّل قيدًا مزدوجًا متوازنًا. <paramref name="entryNumber"/> اختياري: عند تركه فارغًا
    /// يولّد النظام رقم القيد من سلسلة دفتر الأستاذ، وعند تمريره يُحفظ الرقم كما هو لأن هوية
    /// المستند من ملكية المستدعي (استيراد قيد مثلًا) ولا يجوز أن يغيّرها النظام.
    /// <para>
    /// <paramref name="sourceDocumentId"/> اختياري: مفتاحُ المستندِ المنشئِ للقيدِ بالضبط.
    /// <c>SourceId</c> مفتاحٌ أوسعُ (صنفٌ في تسويةِ المخزون مثلًا)، فلا يكفي لحمايةِ حذفِ
    /// مستندٍ خاصٍّ إذا تعددت على نفسِ المفتاحِ مستنداتٌ، بينما <c>SourceDocumentId</c>
    /// يخصُّ المستندَ بعينه فيسمح للحارسِ بالتمييز.
    /// </para>
    /// </summary>
    Task PostAsync(JournalSource source, int sourceId, DateTime date, string description, JournalLine[] lines, string? user, int? branchId = null, string? entryNumber = null, int? sourceDocumentId = null);
    Task<JournalEntry?> GetEntryForSourceAsync(JournalSource source, int sourceId);
}
