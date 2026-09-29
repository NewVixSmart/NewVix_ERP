using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.Services;

public interface IPaymentService
{
    /// <summary>
    /// راية <c>beginOwnTransaction</c> تتبع اصطلاح <c>IInventoryService.CreateSaleAsync</c>:
    /// القيمة الافتراضية تفتح الدفعة معاملتها الخاصة، و<c>false</c> تعني أن المستدعي يفتح
    /// معاملة قائمة وتضم الدفعة إليها.
    /// </summary>
    Task<(bool Success, string? Error, Payment? Payment)> CreatePaymentAsync(Payment payment, string? user, int? branchId = null, bool beginOwnTransaction = true);
    Task<IReadOnlyList<Payment>> GetPaymentsAsync(int page, int pageSize);
    Task<Payment?> GetPaymentAsync(int id);
}
