using Silk.Trading.Web.Models.Accounting;

namespace Silk.Trading.Web.Services;

public interface IPaymentService
{
    Task<(bool Success, string? Error, Payment? Payment)> CreatePaymentAsync(Payment payment, string? user, int? branchId = null);
    Task<IReadOnlyList<Payment>> GetPaymentsAsync(int page, int pageSize);
    Task<Payment?> GetPaymentAsync(int id);
}
