using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Purchases;
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

    /// <summary>
    /// يسجّل دفعة ويوزعها على فواتير الطرف. مع <c>beginOwnTransaction: false</c> تنضم الدفعة
    /// إلى معاملة المستدعي، فلا يبقى سطر دفعة أو توزيع أو قيد بلا counterpart له.
    /// </summary>
    public async Task<(bool Success, string? Error, Payment? Payment)> CreatePaymentAsync(Payment payment, string? user, int? branchId = null, bool beginOwnTransaction = true)
    {
        if (await IsPeriodClosedAsync(payment.PaymentDate))
        {
            return (false, $"السنة المالية {payment.PaymentDate.Year} مغلقة — لا يمكن إدراج قيود فيها", null);
        }

        if (payment.Type == PaymentType.Receipt && !payment.CustomerId.HasValue)
        {
            return (false, "اختر العميل الذي تم القبض منه", null);
        }

        if (payment.Type == PaymentType.Disbursement && !payment.SupplierId.HasValue)
        {
            return (false, "اختر المورد الذي تم الصرف له", null);
        }

        if (payment.Type == PaymentType.Receipt)
        {
            payment.SupplierId = null;
        }
        else
        {
            payment.CustomerId = null;
        }

        payment.DedupeKey = BuildDedupeKey(payment);
        if (!beginOwnTransaction)
        {
            RequireAmbientTransaction(nameof(CreatePaymentAsync));
        }

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            // A null transaction means the caller's transaction owns this unit of work: EF
            // refuses a second transaction on the same connection, so the method enlists in
            // the ambient one and leaves the rollback to whoever opened it.
            await using var tx = beginOwnTransaction ? await _db.Database.BeginTransactionAsync() : null;
            try
            {
                payment.ReceiptNumber = await NextPaymentNumberAsync();

                if (await HasDuplicatePaymentAsync(payment))
                {
                    await TryRollbackAsync(tx);
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
                    await TryRollbackAsync(tx);
                    _db.ChangeTracker.Clear();
                    return (false, "المبلغ أكبر من إجمالي المستحق لهذا الطرف بعد خصم المرتجعات المرحّلة", null);
                }

                if (_accounting != null)
                {
                    if (payment.Type == PaymentType.Receipt && payment.CustomerId.HasValue)
                    {
                        await _accounting.RecordReceiptAsync(payment.PaymentDate, payment.Amount, payment.Method, payment.CustomerId.Value, user, branchId);
                    }
                    else if (payment.Type == PaymentType.Disbursement && payment.SupplierId.HasValue)
                    {
                        await _accounting.RecordDisbursementAsync(payment.PaymentDate, payment.Amount, payment.Method, payment.SupplierId.Value, user, branchId);
                    }
                }

                if (tx is not null)
                {
                    await tx.CommitAsync();
                }

                return (true, null, payment);
            }
            catch (DbUpdateConcurrencyException)
            {
                await TryRollbackAsync(tx);
                _db.ChangeTracker.Clear();
                if (!beginOwnTransaction)
                {
                    throw;
                }

                payment.Id = 0;
                payment.ReceiptNumber = await NextPaymentNumberAsync();
            }
            catch (DbUpdateException)
            {
                await TryRollbackAsync(tx);
                _db.ChangeTracker.Clear();
                if (!beginOwnTransaction)
                {
                    throw;
                }

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
                await TryRollbackAsync(tx);
                _db.ChangeTracker.Clear();
                if (!beginOwnTransaction)
                {
                    throw;
                }

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

    /// <summary>
    /// مسار استرجاع لا يبتلع الاستثناء الأصلي: فشل التراجع عن معاملة يجب ألا يحلّ محل
    /// الاستثناء الذي تلتقطه الحلقة، وإلا ضاع التشخيص خلف خطأ آخر. وتمرير معاملة فارغة
    /// (معاملة المستدعي) يعني ببساطة أن التراجع من اختصاصه.
    /// </summary>
    private static async Task TryRollbackAsync(IDbContextTransaction? tx)
    {
        if (tx == null)
        {
            return;
        }

        try { await tx.RollbackAsync(); }
        catch (Exception) { }
    }

    /// <summary>
    /// يمنع معنى «بلا معاملة» الصامت: <c>beginOwnTransaction: false</c> بلا معاملة قائمة
    /// يعني دفعة محفوظة بلا توزيع ولا قيد، وهي حالة لا يستطيع أحد التراجع عنها.
    /// </summary>
    private void RequireAmbientTransaction(string operation)
    {
        if (_db.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException(
                $"لا يمكن تنفيذ «{operation}» دون معاملة قائمة؛ ابدأ معاملة قبل الاستدعاء أو اترك القيمة الافتراضية لتفتح العملية معاملتها الخاصة");
        }
    }

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
    /// so an allocation is a plain subtraction against the invoice's open amount;
    /// there is no rate to reconcile and no realised exchange difference to book.
    /// </summary>
    /// <remarks>
    /// Everything here is deliberately exact, with no rounding tolerance: the allocation
    /// total must equal <see cref="Payment.Amount"/>, because the journal posts that same
    /// Amount against the receivable. A sub-piastre tolerance would credit the GL without
    /// crediting any invoice, and the AR sub-ledger would silently drift from account 1100.
    /// So any residue is an overpayment and the caller rejects the payment outright.
    /// What an invoice is still owed is not decided here: the candidate set below prices
    /// every document through <see cref="OpenAmountRule"/>, the same rule the receivable and
    /// payable aging reports use, so a receipt can never be allocated to an invoice the
    /// ledger has already closed with a posted return.
    /// </remarks>
    private async Task<(decimal Remaining, decimal Allocated)> ApplyInvoiceAllocationAsync(Payment payment)
    {
        decimal remaining = payment.Amount;
        decimal allocated = 0m;
        var saleRows = new List<SalePaymentAllocation>();
        var purchaseRows = new List<PurchasePaymentAllocation>();

        if (payment.Type == PaymentType.Receipt && payment.CustomerId.HasValue && remaining > 0)
        {
            foreach (var (inv, outstanding) in await SaleOpenAmountsAsync(payment.CustomerId.Value))
            {
                if (remaining <= 0m)
                {
                    break;
                }

                if (outstanding <= 0m)
                {
                    continue;
                }

                var allocate = Math.Min(remaining, outstanding);
                if (allocate <= 0m)
                {
                    continue;
                }

                inv.PaidAmount += allocate;
                remaining -= allocate;
                allocated += allocate;
                saleRows.Add(new SalePaymentAllocation
                {
                    PaymentId = payment.Id,
                    SaleInvoiceId = inv.Id,
                    AllocatedAmount = allocate
                });
                if (inv.PaidAmount >= inv.NetAmount)
                {
                    inv.IsPaid = true;
                }
            }
        }
        else if (payment.Type == PaymentType.Disbursement && payment.SupplierId.HasValue && remaining > 0)
        {
            foreach (var (inv, outstanding) in await PurchaseOpenAmountsAsync(payment.SupplierId.Value))
            {
                if (remaining <= 0m)
                {
                    break;
                }

                if (outstanding <= 0m)
                {
                    continue;
                }

                var allocate = Math.Min(remaining, outstanding);
                if (allocate <= 0m)
                {
                    continue;
                }

                inv.PaidAmount += allocate;
                remaining -= allocate;
                allocated += allocate;
                purchaseRows.Add(new PurchasePaymentAllocation
                {
                    PaymentId = payment.Id,
                    PurchaseInvoiceId = inv.Id,
                    AllocatedAmount = allocate
                });
                if (inv.PaidAmount >= inv.NetAmount)
                {
                    inv.IsPaid = true;
                }
            }
        }

        if (saleRows.Count > 0)
        {
            _db.SalePaymentAllocations.AddRange(saleRows);
        }

        if (purchaseRows.Count > 0)
        {
            _db.PurchasePaymentAllocations.AddRange(purchaseRows);
        }

        if (saleRows.Count > 0 || purchaseRows.Count > 0)
        {
            await _db.SaveChangesAsync();
        }

        return (remaining, allocated);
    }

    /// <summary>
    /// The customer invoices a receipt may still be applied to, oldest-first, paired with the
    /// amount each is still owed. An invoice becomes collectable through either delivery path:
    ///  - a delivered DeliveryOrder (invoice-first flow), or
    ///  - a delivery issue that carries the invoice (order-first flow, where the
    ///    invoice is created from the issues and DeliveryOrder.SaleInvoiceId must stay
    ///    null because CK_DeliveryOrders_SingleSource forbids both sources at once).
    /// Missing the second path left those invoices unpayable: the GL carried the
    /// receivable but the candidate set was empty, so the receipt was rejected.
    /// Invoices the rule prices at zero or less — fully paid, fully returned, or not yet
    /// fully delivered — are left out, which is what keeps the receipt from landing on a
    /// document account 1200 no longer carries.
    /// </summary>
    private async Task<List<(SaleInvoice Invoice, decimal Open)>> SaleOpenAmountsAsync(int customerId)
    {
        var invoices = await _db.SaleInvoices
            .Include(s => s.Items)
            .Where(s => s.CustomerId == customerId && s.PaidAmount < s.NetAmount)
            .Where(s => _db.DeliveryOrders.Any(d => d.SaleInvoiceId == s.Id && d.Status == DeliveryOrderStatus.Delivered)
                     || _db.DeliveryIssues.Any(i => i.SaleInvoiceId == s.Id && i.Status == DeliveryIssueStatus.Issued))
            .OrderBy(s => s.InvoiceDate).ThenBy(s => s.Id)
            .ToListAsync();
        if (invoices.Count == 0)
        {
            return new List<(SaleInvoice, decimal)>();
        }

        var invoiceIds = invoices.Select(s => s.Id).ToList();

        var deliveredOrders = await _db.DeliveryOrders
            .Include(d => d.Items)
            .Where(d => d.Status == DeliveryOrderStatus.Delivered
                     && d.SaleInvoiceId.HasValue && invoiceIds.Contains(d.SaleInvoiceId!.Value))
            .ToListAsync();
        var ordersByInvoice = deliveredOrders
            .GroupBy(d => d.SaleInvoiceId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var postedReturns = await _db.SaleReturns
            .AsNoTracking()
            .Where(r => r.Status == ReturnStatus.Posted)
            .Select(r => new PostedReturnRow(
                r.SaleInvoiceId,
                r.TotalAmount,
                r.SaleInvoice != null ? r.SaleInvoice.TotalAmount : 0m,
                r.SaleInvoice != null ? r.SaleInvoice.NetAmount : 0m))
            .ToListAsync();
        var returnCredits = OpenAmountRule.ReturnCredits(postedReturns);

        var allocated = await _db.SalePaymentAllocations
            .AsNoTracking()
            .Where(a => invoiceIds.Contains(a.SaleInvoiceId))
            .Select(a => new { a.SaleInvoiceId, a.AllocatedAmount })
            .ToListAsync();
        var allocatedByInvoice = allocated
            .GroupBy(a => a.SaleInvoiceId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.AllocatedAmount));

        var open = new List<(SaleInvoice, decimal)>();
        foreach (var inv in invoices)
        {
            ordersByInvoice.TryGetValue(inv.Id, out var orders);
            var amount = OpenAmountRule.Open(
                OpenAmountRule.SaleDeliveredNet(inv, orders),
                OpenAmountRule.PaidBase(inv.PaidAmount, allocatedByInvoice.GetValueOrDefault(inv.Id)),
                returnCredits.GetValueOrDefault(inv.Id));
            if (amount > 0m)
            {
                open.Add((inv, amount));
            }
        }
        return open;
    }

    /// <summary>
    /// The mirror image of <see cref="SaleOpenAmountsAsync"/> for the payable side: a purchase
    /// invoice is booked at the invoice, so its booked value is the whole net and the only
    /// deductions are the disbursements allocated to it and the purchase returns credited
    /// back through <see cref="OpenAmountRule"/>.
    /// </summary>
    private async Task<List<(PurchaseInvoice Invoice, decimal Open)>> PurchaseOpenAmountsAsync(int supplierId)
    {
        var invoices = await _db.PurchaseInvoices
            .Where(p => p.SupplierId == supplierId && p.PaidAmount < p.NetAmount)
            .OrderBy(p => p.InvoiceDate).ThenBy(p => p.Id)
            .ToListAsync();
        if (invoices.Count == 0)
        {
            return new List<(PurchaseInvoice, decimal)>();
        }

        var invoiceIds = invoices.Select(p => p.Id).ToList();

        var postedReturns = await _db.PurchaseReturns
            .AsNoTracking()
            .Where(r => r.Status == ReturnStatus.Posted)
            .Select(r => new PostedReturnRow(
                r.PurchaseInvoiceId,
                r.TotalAmount,
                r.PurchaseInvoice != null ? r.PurchaseInvoice.TotalAmount : 0m,
                r.PurchaseInvoice != null ? r.PurchaseInvoice.NetAmount : 0m))
            .ToListAsync();
        var returnCredits = OpenAmountRule.ReturnCredits(postedReturns);

        var allocated = await _db.PurchasePaymentAllocations
            .AsNoTracking()
            .Where(a => invoiceIds.Contains(a.PurchaseInvoiceId))
            .Select(a => new { a.PurchaseInvoiceId, a.AllocatedAmount })
            .ToListAsync();
        var allocatedByInvoice = allocated
            .GroupBy(a => a.PurchaseInvoiceId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.AllocatedAmount));

        var open = new List<(PurchaseInvoice, decimal)>();
        foreach (var inv in invoices)
        {
            var amount = OpenAmountRule.Open(
                decimal.Round(inv.NetAmount, 2),
                OpenAmountRule.PaidBase(inv.PaidAmount, allocatedByInvoice.GetValueOrDefault(inv.Id)),
                returnCredits.GetValueOrDefault(inv.Id));
            if (amount > 0m)
            {
                open.Add((inv, amount));
            }
        }
        return open;
    }

    private Task<bool> HasDuplicatePaymentAsync(Payment payment)
        => payment.DedupeKey == null
            ? Task.FromResult(false)
            : _db.Payments.AsNoTracking().AnyAsync(p => p.DedupeKey == payment.DedupeKey);

    private static string? BuildDedupeKey(Payment payment)
    {
        if (payment.Amount <= 0)
        {
            return null;
        }

        var party = payment.CustomerId.HasValue ? $"C{payment.CustomerId}"
            : payment.SupplierId.HasValue ? $"S{payment.SupplierId}" : null;
        if (party == null)
        {
            return null;
        }

        var date = payment.PaymentDate;
        var day = new DateTime(date.Year, date.Month, date.Day);
        return $"{payment.Type}|{party}|{payment.Amount}|{payment.Method}|{day:yyyyMMdd}";
    }
}
