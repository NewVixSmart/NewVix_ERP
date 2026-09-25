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
            .Include(p => p.Customer).Include(p => p.Supplier).Include(p => p.Currency)
            .AsNoTracking()
            .OrderByDescending(p => p.PaymentDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();
    }

    public async Task<Payment?> GetPaymentAsync(int id) =>
        await _db.Payments.Include(p => p.Customer).Include(p => p.Supplier).Include(p => p.Currency)
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

    private async Task<(decimal Remaining, decimal PartyBaseReduction, decimal RemainingForeign)> ApplyInvoiceAllocationAsync(Payment payment, bool foreign)
    {
        decimal remaining = payment.BaseAmount;
        decimal remainingForeign = payment.Amount;
        decimal partyBaseReduction = 0m;
        var saleRows = new List<SalePaymentAllocation>();
        var purchaseRows = new List<PurchasePaymentAllocation>();

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
                        saleRows.Add(new SalePaymentAllocation
                        {
                            PaymentId = payment.Id,
                            SaleInvoiceId = inv.Id,
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
                        saleRows.Add(new SalePaymentAllocation
                        {
                            PaymentId = payment.Id,
                            SaleInvoiceId = inv.Id,
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
                    saleRows.Add(new SalePaymentAllocation
                    {
                        PaymentId = payment.Id,
                        SaleInvoiceId = inv.Id,
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
                        purchaseRows.Add(new PurchasePaymentAllocation
                        {
                            PaymentId = payment.Id,
                            PurchaseInvoiceId = inv.Id,
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
                        purchaseRows.Add(new PurchasePaymentAllocation
                        {
                            PaymentId = payment.Id,
                            PurchaseInvoiceId = inv.Id,
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
                    purchaseRows.Add(new PurchasePaymentAllocation
                    {
                        PaymentId = payment.Id,
                        PurchaseInvoiceId = inv.Id,
                        AllocatedBaseAmount = allocate,
                        ExchangeRateAtSettlement = null,
                        FxGain = 0m,
                        FxLoss = 0m
                    });
                }
                if (inv.PaidAmount >= inv.NetAmount - 0.005m) inv.IsPaid = true;
            }
        }

        if (saleRows.Count > 0)
            _db.SalePaymentAllocations.AddRange(saleRows);
        if (purchaseRows.Count > 0)
            _db.PurchasePaymentAllocations.AddRange(purchaseRows);
        if (saleRows.Count > 0 || purchaseRows.Count > 0)
            await _db.SaveChangesAsync();

        return (remaining, partyBaseReduction, remainingForeign);
    }

    private static bool IsForeignPayment(Payment payment, Currency? currency)
        => payment.CurrencyId.HasValue && currency != null && !currency.IsBase;

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
        return $"{payment.Type}|{party}|{payment.Amount}|{payment.CurrencyId}|{payment.ExchangeRate}|{payment.Method}|{day:yyyyMMdd}";
    }
}
