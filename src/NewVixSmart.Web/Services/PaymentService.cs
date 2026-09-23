using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Services;

public sealed class PaymentService : IPaymentService
{
    private readonly AppDbContext _db;
    private readonly IAccountingService? _accounting;

    public PaymentService(AppDbContext db, IAccountingService? accounting = null)
    {
        _db = db;
        _accounting = accounting;
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

        var currency = payment.CurrencyId.HasValue
            ? await _db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == payment.CurrencyId.Value)
            : null;
        if (payment.CurrencyId.HasValue && currency == null)
            return (false, "العملة غير موجودة", null);

        bool foreign = IsForeignPayment(payment, currency);
        if (foreign && !payment.ExchangeRate.HasValue)
            payment.ExchangeRate = 1m;
        payment.BaseAmount = foreign
            ? decimal.Round(payment.Amount * payment.ExchangeRate!.Value, 2)
            : payment.Amount;

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                payment.ReceiptNumber = await NextPaymentNumberAsync(_db.Payments.Select(p => p.ReceiptNumber));

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

                var (remaining, partyBaseReduction, remainingForeign) = await ApplyInvoiceAllocationAsync(payment, foreign);

                var realizedFx = foreign ? decimal.Round(payment.BaseAmount - partyBaseReduction, 2) : 0m;
                var residualAllowed = foreign
                    && remainingForeign <= 0.005m
                    && realizedFx > 0.01m
                    && decimal.Round(remaining, 2) == decimal.Round(realizedFx, 2);

                if (remaining > 0.01m && !residualAllowed)
                {
                    await tx.RollbackAsync();
                    _db.ChangeTracker.Clear();
                    return (false, "المبلغ أكبر من إجمالي المستحق لهذا الطرف", null);
                }

                if (_accounting != null)
                {
                    if (foreign)
                    {
                        var netFx = decimal.Round(payment.BaseAmount - partyBaseReduction, 2);
                        var economicGain = payment.Type == PaymentType.Receipt ? netFx : -netFx;
                        var fxGain = economicGain > 0.01m ? economicGain : 0m;
                        var fxLoss = economicGain < -0.01m ? -economicGain : 0m;
                        if (fxGain == 0m && fxLoss == 0m)
                            partyBaseReduction = payment.BaseAmount;
                        var source = payment.Type == PaymentType.Receipt
                            ? JournalSource.Receipt : JournalSource.Disbursement;
                        var sourceId = payment.Type == PaymentType.Receipt
                            ? payment.CustomerId!.Value : payment.SupplierId!.Value;
                        await _accounting.RecordFxSettlementAsync(payment.PaymentDate, payment.BaseAmount,
                            partyBaseReduction, fxGain, fxLoss, payment.Method, source, sourceId, user, branchId);
                    }
                    else if (payment.Type == PaymentType.Receipt && payment.CustomerId.HasValue)
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
                var lastPayment = await _db.Payments.AsNoTracking().OrderByDescending(p => p.Id).FirstOrDefaultAsync();
                payment.ReceiptNumber = $"PAY-{(lastPayment == null ? 1 : lastPayment.Id + 1):D5}";
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync();
                _db.ChangeTracker.Clear();
                payment.Id = 0;
                var lastPayment = await _db.Payments.AsNoTracking().OrderByDescending(p => p.Id).FirstOrDefaultAsync();
                payment.ReceiptNumber = $"PAY-{(lastPayment == null ? 1 : lastPayment.Id + 1):D5}";
            }
        }
        return (false, "تعارض في البيانات أثناء الحفظ، يرجى إعادة المحاولة", null);
    }

    public async Task<IReadOnlyList<Payment>> GetPaymentsAsync(int page, int pageSize)
    {
        page = Math.Max(1, page);
        return await _db.Payments
            .Include(p => p.Customer).Include(p => p.Supplier).Include(p => p.Currency)
            .AsNoTracking()
            .OrderByDescending(p => p.PaymentDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();
    }

    public async Task<Payment?> GetPaymentAsync(int id) =>
        await _db.Payments.Include(p => p.Customer).Include(p => p.Supplier).Include(p => p.Currency)
            .Include(p => p.PaymentAllocations)
            .AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);

    private Task<bool> IsPeriodClosedAsync(DateTime date)
        => _db.FiscalPeriods.AnyAsync(fp => fp.Year == date.Year && fp.IsClosed);

    private async Task<string> NextPaymentNumberAsync(IQueryable<string> existing)
    {
        int next = await existing.CountAsync() + 1;
        string num = $"PAY-{next:D5}";
        while (await existing.AnyAsync(n => n == num))
        {
            next++;
            num = $"PAY-{next:D5}";
        }
        return num;
    }

    private async Task<(decimal Remaining, decimal PartyBaseReduction, decimal RemainingForeign)> ApplyInvoiceAllocationAsync(Payment payment, bool foreign)
    {
        decimal remaining = payment.BaseAmount;
        decimal remainingForeign = payment.Amount;
        decimal partyBaseReduction = 0m;
        var rows = new List<PaymentAllocation>();

        if (payment.Type == PaymentType.Receipt && payment.CustomerId.HasValue && remaining > 0)
        {
            var invoices = await _db.SaleInvoices
                .Where(s => s.CustomerId == payment.CustomerId && s.PaidAmount < s.NetAmount)
                .Where(s => _db.DeliveryOrders.Any(d => d.SaleInvoiceId == s.Id && d.Status == DeliveryOrderStatus.Delivered))
                .OrderBy(s => s.InvoiceDate).ThenBy(s => s.Id)
                .ToListAsync();
            foreach (var inv in invoices)
            {
                if (remaining <= 0.005m) break;
                var outstanding = inv.NetAmount - inv.PaidAmount;
                if (outstanding <= 0.005m) continue;

                var allocate = remaining;
                if (foreign)
                {
                    if (remainingForeign <= 0.005m) break;
                    var r0 = inv.ExchangeRate ?? 1m;
                    var outstandingBase = decimal.Round(outstanding * r0, 2);

                    if (inv.ExchangeRate.HasValue && inv.ExchangeRate != 1m)
                    {
                        var fCap = Math.Min(remainingForeign, outstanding);
                        if (remaining < outstandingBase)
                        {
                            var baseCapped = decimal.Round(remaining / r0, 2);
                            if (fCap > baseCapped) fCap = baseCapped;
                        }
                        fCap = decimal.Round(fCap, 2);
                        if (fCap < 0.005m) continue;
                        var invoiceBase = decimal.Round(fCap * r0, 2);
                        var paidBase = decimal.Round(fCap * payment.ExchangeRate!.Value, 2);
                        var fxDiff = decimal.Round(paidBase - invoiceBase, 2);
                        inv.PaidAmount += fCap;
                        remaining -= invoiceBase;
                        remainingForeign -= fCap;
                        partyBaseReduction += invoiceBase;
                        rows.Add(new PaymentAllocation
                        {
                            PaymentId = payment.Id,
                            InvoiceType = PaymentAllocationInvoiceType.Sales,
                            InvoiceId = inv.Id,
                            AllocatedBaseAmount = invoiceBase,
                            ExchangeRateAtSettlement = payment.ExchangeRate,
                            FxGain = fxDiff > 0 ? fxDiff : 0m,
                            FxLoss = fxDiff < 0 ? -fxDiff : 0m
                        });
                    }
                    else
                    {
                        if (allocate > outstanding) allocate = outstanding;
                        if (allocate < 0.005m) continue;
                        var consumedForeign = decimal.Round(allocate / payment.ExchangeRate!.Value, 2);
                        if (consumedForeign > remainingForeign + 0.005m)
                        {
                            consumedForeign = remainingForeign;
                            allocate = decimal.Round(consumedForeign * payment.ExchangeRate.Value, 2);
                            if (allocate < 0.005m) continue;
                        }
                        inv.PaidAmount += allocate;
                        remaining -= allocate;
                        remainingForeign -= consumedForeign;
                        partyBaseReduction += allocate;
                        rows.Add(new PaymentAllocation
                        {
                            PaymentId = payment.Id,
                            InvoiceType = PaymentAllocationInvoiceType.Sales,
                            InvoiceId = inv.Id,
                            AllocatedBaseAmount = allocate,
                            ExchangeRateAtSettlement = payment.ExchangeRate,
                            FxGain = 0m,
                            FxLoss = 0m
                        });
                    }
                }
                else
                {
                    var baseR0 = inv.ExchangeRate ?? 1m;
                    var outstandingBase = decimal.Round(outstanding * baseR0, 2);
                    if (allocate > outstandingBase) allocate = outstandingBase;
                    if (allocate < 0.005m) continue;
                    var foreignApplied = decimal.Round(allocate / baseR0, 2);
                    inv.PaidAmount += foreignApplied;
                    remaining -= allocate;
                    partyBaseReduction += allocate;
                    rows.Add(new PaymentAllocation
                    {
                        PaymentId = payment.Id,
                        InvoiceType = PaymentAllocationInvoiceType.Sales,
                        InvoiceId = inv.Id,
                        AllocatedBaseAmount = allocate,
                        ExchangeRateAtSettlement = null,
                        FxGain = 0m,
                        FxLoss = 0m
                    });
                }
                if (inv.PaidAmount >= inv.NetAmount - 0.005m) inv.IsPaid = true;
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
                if (remaining <= 0.005m) break;
                var outstanding = inv.NetAmount - inv.PaidAmount;
                if (outstanding <= 0.005m) continue;

                var allocate = remaining;
                if (foreign)
                {
                    if (remainingForeign <= 0.005m) break;
                    var r0 = inv.ExchangeRate ?? 1m;
                    var outstandingBase = decimal.Round(outstanding * r0, 2);

                    if (inv.ExchangeRate.HasValue && inv.ExchangeRate != 1m)
                    {
                        var fCap = Math.Min(remainingForeign, outstanding);
                        if (remaining < outstandingBase)
                        {
                            var baseCapped = decimal.Round(remaining / r0, 2);
                            if (fCap > baseCapped) fCap = baseCapped;
                        }
                        fCap = decimal.Round(fCap, 2);
                        if (fCap < 0.005m) continue;
                        var invoiceBase = decimal.Round(fCap * r0, 2);
                        var paidBase = decimal.Round(fCap * payment.ExchangeRate!.Value, 2);
                        var fxDiff = decimal.Round(paidBase - invoiceBase, 2);
                        inv.PaidAmount += fCap;
                        remaining -= invoiceBase;
                        remainingForeign -= fCap;
                        partyBaseReduction += invoiceBase;
                        rows.Add(new PaymentAllocation
                        {
                            PaymentId = payment.Id,
                            InvoiceType = PaymentAllocationInvoiceType.Purchases,
                            InvoiceId = inv.Id,
                            AllocatedBaseAmount = invoiceBase,
                            ExchangeRateAtSettlement = payment.ExchangeRate,
                            FxGain = fxDiff < 0 ? -fxDiff : 0m,
                            FxLoss = fxDiff > 0 ? fxDiff : 0m
                        });
                    }
                    else
                    {
                        if (allocate > outstanding) allocate = outstanding;
                        if (allocate < 0.005m) continue;
                        var consumedForeign = decimal.Round(allocate / payment.ExchangeRate!.Value, 2);
                        if (consumedForeign > remainingForeign + 0.005m)
                        {
                            consumedForeign = remainingForeign;
                            allocate = decimal.Round(consumedForeign * payment.ExchangeRate.Value, 2);
                            if (allocate < 0.005m) continue;
                        }
                        inv.PaidAmount += allocate;
                        remaining -= allocate;
                        remainingForeign -= consumedForeign;
                        partyBaseReduction += allocate;
                        rows.Add(new PaymentAllocation
                        {
                            PaymentId = payment.Id,
                            InvoiceType = PaymentAllocationInvoiceType.Purchases,
                            InvoiceId = inv.Id,
                            AllocatedBaseAmount = allocate,
                            ExchangeRateAtSettlement = payment.ExchangeRate,
                            FxGain = 0m,
                            FxLoss = 0m
                        });
                    }
                }
                else
                {
                    var baseR0 = inv.ExchangeRate ?? 1m;
                    var outstandingBase = decimal.Round(outstanding * baseR0, 2);
                    if (allocate > outstandingBase) allocate = outstandingBase;
                    if (allocate < 0.005m) continue;
                    var foreignApplied = decimal.Round(allocate / baseR0, 2);
                    inv.PaidAmount += foreignApplied;
                    remaining -= allocate;
                    partyBaseReduction += allocate;
                    rows.Add(new PaymentAllocation
                    {
                        PaymentId = payment.Id,
                        InvoiceType = PaymentAllocationInvoiceType.Purchases,
                        InvoiceId = inv.Id,
                        AllocatedBaseAmount = allocate,
                        ExchangeRateAtSettlement = null,
                        FxGain = 0m,
                        FxLoss = 0m
                    });
                }
                if (inv.PaidAmount >= inv.NetAmount - 0.005m) inv.IsPaid = true;
            }
        }

        if (rows.Count > 0)
        {
            _db.PaymentAllocations.AddRange(rows);
            await _db.SaveChangesAsync();
        }

        return (remaining, partyBaseReduction, remainingForeign);
    }

    private static bool IsForeignPayment(Payment payment, Currency? currency)
        => payment.CurrencyId.HasValue && currency != null && !currency.IsBase;

    private async Task<bool> HasDuplicatePaymentAsync(Payment payment)
    {
        var window = DateTime.UtcNow.AddMinutes(-2);
        var q = _db.Payments.AsNoTracking()
            .Where(p => p.CreatedAt >= window && p.Amount == payment.Amount && p.Type == payment.Type
                && p.CurrencyId == payment.CurrencyId && p.ExchangeRate == payment.ExchangeRate);
        if (payment.Type == PaymentType.Receipt && payment.CustomerId.HasValue)
            q = q.Where(p => p.CustomerId == payment.CustomerId);
        else if (payment.Type == PaymentType.Disbursement && payment.SupplierId.HasValue)
            q = q.Where(p => p.SupplierId == payment.SupplierId);
        return await q.AnyAsync();
    }
}
