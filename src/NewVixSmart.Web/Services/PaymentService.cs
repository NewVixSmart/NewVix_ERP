using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Services;

public sealed class PaymentService : IPaymentService
{
    private readonly AppDbContext _db;
    private readonly IAccountingService? _accounting;
    private readonly ILogger<PaymentService>? _logger;

    public PaymentService(AppDbContext db, IAccountingService? accounting = null, ILogger<PaymentService>? logger = null)
    {
        _db = db;
        _accounting = accounting;
        _logger = logger;
    }

    public async Task<(bool Success, string? Error, Payment? Payment)> CreatePaymentAsync(Payment payment, string? user, int? branchId = null)
    {
        if (await IsPeriodClosedAsync(payment.PaymentDate))
            return (false, $"السنة المالية {payment.PaymentDate.Year} مغلقة — لا يمكن إدراج قيود فيها", null);

        if (payment.Type == PaymentType.Receipt && !payment.CustomerId.HasValue)
            return (false, "اختر العميل الذي تم القبض منه", null);
        if (payment.Type == PaymentType.Disbursement && !payment.SupplierId.HasValue)
            return (false, "اختر المورد الذي تم الصرف له", null);

        if (payment.Type == PaymentType.Receipt)
            payment.SupplierId = null;
        else
            payment.CustomerId = null;

        payment.DedupeKey = BuildDedupeKey(payment);

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                payment.ReceiptNumber = await NextPaymentNumberAsync();

                if (await HasDuplicatePaymentAsync(payment))
                {
                    await tx.RollbackAsync();
                    _db.ChangeTracker.Clear();
                    return (false, "توجد دفعة مطابقة أُنشئت قبل قليل؛ تخلَّص من الإرسال المكرر", null);
                }

                payment.CreatedBy = user;
                payment.CreatedAt = DateTime.UtcNow;
                payment.BranchId = branchId;
                _db.Payments.Add(payment);
                await _db.SaveChangesAsync();

                var (remaining, allocated) = await ApplyInvoiceAllocationAsync(payment);

                if (remaining > 0m)
                {
                    await tx.RollbackAsync();
                    _db.ChangeTracker.Clear();
                    return (false, "المبلغ أكبر من إجمالي المستحق لهذا الطرف", null);
                }

                if (_accounting != null)
                {
                    if (payment.Type == PaymentType.Receipt && payment.CustomerId.HasValue)
                        await _accounting.RecordReceiptAsync(payment.PaymentDate, payment.Amount, payment.Method, payment.CustomerId.Value, user, branchId);
                    else if (payment.Type == PaymentType.Disbursement && payment.SupplierId.HasValue)
                        await _accounting.RecordDisbursementAsync(payment.PaymentDate, payment.Amount, payment.Method, payment.SupplierId.Value, user, branchId);
                }

                await tx.CommitAsync();
                return (true, null, payment);
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync();
                _db.ChangeTracker.Clear();
                payment.Id = 0;
                payment.ReceiptNumber = await NextPaymentNumberAsync();
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync();
                _db.ChangeTracker.Clear();
                payment.Id = 0;
                if (payment.DedupeKey != null
                    && await _db.Payments.AsNoTracking().AnyAsync(p => p.DedupeKey == payment.DedupeKey))
                {
                    return (false, "توجد دفعة مطابقة أُنشئت قبل قليل؛ تخلَّص من الإرسال المكرر", null);
                }
                payment.ReceiptNumber = await NextPaymentNumberAsync();
            }
            catch (Exception ex) when (ex is not DbUpdateException && ex is not DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync();
                _db.ChangeTracker.Clear();
                _logger?.LogError(ex, "فشلت معالجة الدفعة رقم {Number}", payment.ReceiptNumber);
                return (false, "تعذر معالجة الدفعة بسبب خطأ غير متوقع، حاول مرة أخرى", null);
            }
        }
        return (false, "تعارض في البيانات أثناء الحفظ، يرجى إعادة المحاولة", null);
    }

    public async Task<IReadOnlyList<Payment>> GetPaymentsAsync(int page, int pageSize)
    {
        page = Math.Max(1, page);
        return await _db.Payments
            .Include(p => p.Customer).Include(p => p.Supplier)
            .AsNoTracking()
            .OrderByDescending(p => p.PaymentDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();
    }

    public async Task<Payment?> GetPaymentAsync(int id) =>
        await _db.Payments.Include(p => p.Customer).Include(p => p.Supplier)
            .Include(p => p.SalePaymentAllocations!).ThenInclude(a => a.SaleInvoice)
            .Include(p => p.PurchasePaymentAllocations!).ThenInclude(a => a.PurchaseInvoice)
            .AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);

    private Task<bool> IsPeriodClosedAsync(DateTime date)
        => _db.FiscalPeriods.AnyAsync(fp => fp.Year == date.Year && fp.IsClosed);

    private async Task<string> NextPaymentNumberAsync()
    {
        var taken = await _db.Payments.AsNoTracking()
            .Where(p => p.ReceiptNumber.StartsWith("PAY-"))
            .Select(p => p.ReceiptNumber)
            .ToListAsync();
        int next = taken.Count > 0 ? taken.Select(n => int.TryParse(n.AsSpan(4), out var v) ? v : 0).Max() + 1 : 1;
        return $"PAY-{next:D5}";
    }

    /// <summary>
    /// Applies a payment to that party's open invoices oldest-first. Single currency,
    /// so an allocation is a plain subtraction against the invoice's own NetAmount;
    /// there is no rate to reconcile and no realised exchange difference to book.
    /// </summary>
    /// <remarks>
    /// Everything here is deliberately exact, with no rounding tolerance: the allocation
    /// total must equal <see cref="Payment.Amount"/>, because the journal posts that same
    /// Amount against the receivable. A sub-piastre tolerance would credit the GL without
    /// crediting any invoice, and the AR sub-ledger would silently drift from account 1100.
    /// So any residue is an overpayment and the caller rejects the payment outright.
    /// </remarks>
    private async Task<(decimal Remaining, decimal Allocated)> ApplyInvoiceAllocationAsync(Payment payment)
    {
        decimal remaining = payment.Amount;
        decimal allocated = 0m;
        var saleRows = new List<SalePaymentAllocation>();
        var purchaseRows = new List<PurchasePaymentAllocation>();

        if (payment.Type == PaymentType.Receipt && payment.CustomerId.HasValue && remaining > 0)
        {
            // An invoice becomes collectable through either delivery path:
            //  - a delivered DeliveryOrder (invoice-first flow), or
            //  - a delivery issue that carries the invoice (order-first flow, where the
            //    invoice is created from the issues and DeliveryOrder.SaleInvoiceId must stay
            //    null because CK_DeliveryOrders_SingleSource forbids both sources at once).
            // Missing the second path left those invoices unpayable: the GL carried the
            // receivable but the candidate set was empty, so the receipt was rejected.
            var invoices = await _db.SaleInvoices
                .Where(s => s.CustomerId == payment.CustomerId && s.PaidAmount < s.NetAmount)
                .Where(s => _db.DeliveryOrders.Any(d => d.SaleInvoiceId == s.Id && d.Status == DeliveryOrderStatus.Delivered)
                         || _db.DeliveryIssues.Any(i => i.SaleInvoiceId == s.Id && i.Status == DeliveryIssueStatus.Issued))
                .OrderBy(s => s.InvoiceDate).ThenBy(s => s.Id)
                .ToListAsync();
            foreach (var inv in invoices)
            {
                if (remaining <= 0m) break;
                var outstanding = inv.NetAmount - inv.PaidAmount;
                if (outstanding <= 0m) continue;

                var allocate = Math.Min(remaining, outstanding);
                if (allocate <= 0m) continue;
                inv.PaidAmount += allocate;
                remaining -= allocate;
                allocated += allocate;
                saleRows.Add(new SalePaymentAllocation
                {
                    PaymentId = payment.Id,
                    SaleInvoiceId = inv.Id,
                    AllocatedAmount = allocate
                });
                if (inv.PaidAmount >= inv.NetAmount) inv.IsPaid = true;
            }
        }
        else if (payment.Type == PaymentType.Disbursement && payment.SupplierId.HasValue && remaining > 0)
        {
            var invoices = await _db.PurchaseInvoices
                .Where(p => p.SupplierId == payment.SupplierId && p.PaidAmount < p.NetAmount)
                .OrderBy(p => p.InvoiceDate).ThenBy(p => p.Id)
                .ToListAsync();
            foreach (var inv in invoices)
            {
                if (remaining <= 0m) break;
                var outstanding = inv.NetAmount - inv.PaidAmount;
                if (outstanding <= 0m) continue;

                var allocate = Math.Min(remaining, outstanding);
                if (allocate <= 0m) continue;
                inv.PaidAmount += allocate;
                remaining -= allocate;
                allocated += allocate;
                purchaseRows.Add(new PurchasePaymentAllocation
                {
                    PaymentId = payment.Id,
                    PurchaseInvoiceId = inv.Id,
                    AllocatedAmount = allocate
                });
                if (inv.PaidAmount >= inv.NetAmount) inv.IsPaid = true;
            }
        }

        if (saleRows.Count > 0)
            _db.SalePaymentAllocations.AddRange(saleRows);
        if (purchaseRows.Count > 0)
            _db.PurchasePaymentAllocations.AddRange(purchaseRows);
        if (saleRows.Count > 0 || purchaseRows.Count > 0)
            await _db.SaveChangesAsync();

        return (remaining, allocated);
    }

    private Task<bool> HasDuplicatePaymentAsync(Payment payment)
        => payment.DedupeKey == null
            ? Task.FromResult(false)
            : _db.Payments.AsNoTracking().AnyAsync(p => p.DedupeKey == payment.DedupeKey);

    private static string? BuildDedupeKey(Payment payment)
    {
        if (payment.Amount <= 0) return null;
        var party = payment.CustomerId.HasValue ? $"C{payment.CustomerId}"
            : payment.SupplierId.HasValue ? $"S{payment.SupplierId}" : null;
        if (party == null) return null;
        var date = payment.PaymentDate;
        var day = new DateTime(date.Year, date.Month, date.Day);
        return $"{payment.Type}|{party}|{payment.Amount}|{payment.Method}|{day:yyyyMMdd}";
    }
}
