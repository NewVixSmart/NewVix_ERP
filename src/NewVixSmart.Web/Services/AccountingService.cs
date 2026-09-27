using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.Services;

public class AccountingService : IAccountingService
{
    private const int MaxAttempts = 3;
    private readonly AppDbContext _db;

    public AccountingService(AppDbContext db)
    {
        _db = db;
    }

    public async Task RecordSaleInvoiceAsync(DateTime entryDate, int customerId, decimal netAmount, decimal costAmount,
        int? currencyId, decimal? exchangeRate, string? user, int? branchId = null)
    {
        var localValue = decimal.Round(netAmount * (exchangeRate ?? 1m), 2);
        var localCost = decimal.Round(costAmount, 2);
        var lines = new List<JournalLine>();
        if (localValue > 0)
        {
            lines.Add(new JournalLine("1200", localValue, 0));
            lines.Add(new JournalLine("4000", 0, localValue));
        }
        if (localCost > 0)
        {
            lines.Add(new JournalLine("5000", localCost, 0));
            lines.Add(new JournalLine("1300", 0, localCost));
        }
        if (lines.Count == 0) throw new InvalidOperationException("فاتورة البيع بلا قيمة أو تكلفة");
        await PostAsync(JournalSource.SaleInvoice, customerId, entryDate, "فاتورة بيع", lines.ToArray(), user, branchId);
    }

    public async Task RecordSaleInvoiceRevenueAsync(DateTime entryDate, int customerId, decimal netAmount, decimal taxAmount,
        int? currencyId, decimal? exchangeRate, string? user, int? branchId = null, int? invoiceId = null)
    {
        var localValue = decimal.Round(netAmount * (exchangeRate ?? 1m), 2);
        var localTax = decimal.Round(taxAmount * (exchangeRate ?? 1m), 2);
        if (localTax < 0.005m) localTax = 0m;
        if (localTax > localValue - 0.005m) localTax = 0m;
        if (localValue <= 0) throw new InvalidOperationException("فاتورة البيع بلا قيمة");
        var lines = new List<JournalLine>
        {
            new("1200", localValue, 0),
            new("4000", 0, decimal.Round(localValue - localTax, 2))
        };
        if (localTax > 0) lines.Add(new JournalLine("2055", 0, localTax));
        await PostAsync(JournalSource.SaleInvoice, invoiceId ?? customerId, entryDate, "فاتورة بيع", lines.ToArray(), user, branchId);
    }

    public async Task RecordSaleIssueCostAsync(DateTime entryDate, int issueId, decimal costAmount,
        int? currencyId, decimal? exchangeRate, string? user, int? branchId = null)
    {
        var localCost = decimal.Round(costAmount * (exchangeRate ?? 1m), 2);
        if (localCost <= 0) return;
        await PostAsync(JournalSource.SaleDeliveryIssue, issueId, entryDate, "تكلفة تسليم بيع",
            new[] { new JournalLine("5000", localCost, 0), new JournalLine("1300", 0, localCost) }, user, branchId);
    }

    public async Task RecordSaleDeliveryAsync(DateTime entryDate, int customerId, decimal value, decimal cost,
        int? currencyId, decimal? exchangeRate, string? user, int? branchId = null, int? deliveryId = null, decimal taxAmount = 0m)
    {
        var localValue = decimal.Round(value, 2);
        var localCost = decimal.Round(cost, 2);
        var localTax = decimal.Round(taxAmount, 2);
        if (localTax < 0.005m) localTax = 0m;
        if (localTax > localValue - 0.005m) localTax = 0m;
        var lines = new List<JournalLine>();
        if (localValue > 0)
        {
            lines.Add(new JournalLine("1200", localValue, 0));
            lines.Add(new JournalLine("4000", 0, decimal.Round(localValue - localTax, 2)));
            if (localTax > 0)
                lines.Add(new JournalLine("2055", 0, localTax));
        }
        if (localCost > 0)
        {
            lines.Add(new JournalLine("5000", localCost, 0));
            lines.Add(new JournalLine("1300", 0, localCost));
        }
        if (lines.Count == 0) throw new InvalidOperationException("أذن التسليم بلا قيمة أو تكلفة");
        await PostAsync(JournalSource.SaleDeliveryOrder, deliveryId ?? customerId, entryDate, "أذن تسليم بيع", lines.ToArray(), user, branchId);
    }

    public async Task RecordPurchaseInvoiceAsync(DateTime entryDate, int supplierId, decimal netAmount,
        int? currencyId, decimal? exchangeRate, string? user, int? branchId = null)
    {
        if (netAmount <= 0) throw new InvalidOperationException("فاتورة الشراء بلا قيمة");
        var localValue = decimal.Round(netAmount * (exchangeRate ?? 1m), 2);
        await PostAsync(JournalSource.PurchaseInvoice, supplierId, entryDate, "فاتورة شراء",
            new[] { new JournalLine("1300", localValue, 0), new JournalLine("2000", 0, localValue) }, user, branchId);
    }

    public async Task RecordReceiptAsync(DateTime entryDate, decimal amount, PaymentMethod method, int customerId, string? user, int? branchId = null)
    {
        var debitCode = method == PaymentMethod.Cash ? "1000" : "1100";
        await PostAsync(JournalSource.Receipt, customerId, entryDate, "قبض من عميل",
            new[] { new JournalLine(debitCode, amount, 0), new JournalLine("1200", 0, amount) }, user, branchId);
    }

    public async Task RecordDisbursementAsync(DateTime entryDate, decimal amount, PaymentMethod method, int supplierId, string? user, int? branchId = null)
    {
        var creditCode = method == PaymentMethod.Cash ? "1000" : "1100";
        await PostAsync(JournalSource.Disbursement, supplierId, entryDate, "صرف لمورد",
            new[] { new JournalLine("2000", amount, 0), new JournalLine(creditCode, 0, amount) }, user, branchId);
    }

    public async Task RecordFxSettlementAsync(DateTime entryDate, decimal cashAmount, decimal receivablePayableReduction,
        decimal fxGain, decimal fxLoss, PaymentMethod method, JournalSource source, int sourceId, string? user, int? branchId = null)
    {
        var cashCode = method == PaymentMethod.Cash ? "1000" : "1100";
        var lines = new List<JournalLine>();
        if (source == JournalSource.Receipt)
        {
            lines.Add(new JournalLine(cashCode, cashAmount, 0));
            lines.Add(new JournalLine("1200", 0, receivablePayableReduction));
            if (fxGain > 0.01m) lines.Add(new JournalLine("8400", 0, fxGain));
            if (fxLoss > 0.01m) lines.Add(new JournalLine("4400", fxLoss, 0));
        }
        else
        {
            lines.Add(new JournalLine("2000", receivablePayableReduction, 0));
            lines.Add(new JournalLine(cashCode, 0, cashAmount));
            if (fxLoss > 0.01m) lines.Add(new JournalLine("4400", fxLoss, 0));
            if (fxGain > 0.01m) lines.Add(new JournalLine("8400", 0, fxGain));
        }
        await PostAsync(source, sourceId, entryDate, "فروقات عملة أجنبية عند التسوية", lines.ToArray(), user, branchId);
    }

    public async Task RecordSaleReturnAsync(DateTime entryDate, decimal amount, string? user, int? branchId = null)
        => await PostAsync(JournalSource.SaleReturn, 0, entryDate, "مرتجع بيع",
            new[] { new JournalLine("5101", amount, 0), new JournalLine("1200", 0, amount) }, user, branchId);

    public async Task RecordPurchaseReturnAsync(DateTime entryDate, decimal amount, string? user, int? branchId = null)
        => await PostAsync(JournalSource.PurchaseReturn, 0, entryDate, "مرتجع شراء",
            new[] { new JournalLine("2000", amount, 0), new JournalLine("5102", 0, amount) }, user, branchId);

    // Sale return = exact proportional mirror of the booking the source invoice produced
    // (Dr 1200 net / Cr 4000 (net - tax) / Cr 2055 tax), so for a returned fraction f:
    //   Dr 5101 (contra revenue) f * (net - tax)
    //   Dr 2055 (tax)           f * tax
    //   Cr 1200 (AR)           f * (net - tax) + f * tax  == f * net
    // The tax is NEVER folded into the value leg: valueAmount is the contra-revenue amount
    // (already net of tax) and taxAmount the tax amount, so the receivable credit is their sum.
    public async Task RecordSaleReturnWithCostAsync(DateTime entryDate, int sourceId, int customerId, decimal valueAmount, decimal costAmount, int? currencyId, decimal? exchangeRate, string? user, int? branchId = null, decimal taxAmount = 0m)
    {
        var rate = exchangeRate ?? 1m;
        var contraValue = NonNegative(decimal.Round(valueAmount * rate, 2));
        var tax = NonNegative(decimal.Round(taxAmount * rate, 2));
        if (tax < 0.005m) tax = 0m;
        var cost = NonNegative(decimal.Round(costAmount, 2));

        var lines = new List<JournalLine>();
        // The receivable credit is the largest leg, so it is derived as the balancing figure
        // (total debits minus the cost credit) and the entry always balances to the cent.
        var totalDebits = decimal.Round(contraValue + tax + cost, 2);
        var receivable = decimal.Round(NonNegative(totalDebits - cost), 2);
        if (contraValue > 0m) lines.Add(new JournalLine("5101", contraValue, 0));
        if (receivable > 0m) lines.Add(new JournalLine("1200", 0, receivable));
        if (tax > 0m) lines.Add(new JournalLine("2055", tax, 0));
        if (cost > 0m)
        {
            lines.Add(new JournalLine("1300", cost, 0));
            lines.Add(new JournalLine("5000", 0, cost));
        }
        if (lines.Count == 0) throw new InvalidOperationException("مرتجع البيع بلا قيمة أو تكلفة");
        await PostAsync(JournalSource.SaleReturn, sourceId, entryDate, "مرتجع بيع", lines.ToArray(), user, branchId);
    }

    // Purchase return = exact proportional mirror of RecordPurchaseInvoiceAsync, which books
    // Dr 1300 (Inventory) net / Cr 2000 (AP) net with no separate tax leg. So for fraction f:
    //   Dr 2000 (AP)   f * net
    //   Dr 5102 (contra-purchases) f * net
    //   Cr 1300 (Inventory) cost / Dr 5000 (COGS reversal) cost
    // valueAmount is therefore the full net (tax included), not a tax-exclusive amount.
    public async Task RecordPurchaseReturnWithCostAsync(DateTime entryDate, int sourceId, int supplierId, decimal valueAmount, decimal costAmount, int? currencyId, decimal? exchangeRate, string? user, int? branchId = null)
    {
        var localValue = NonNegative(decimal.Round(valueAmount * (exchangeRate ?? 1m), 2));
        var cost = NonNegative(decimal.Round(costAmount, 2));
        if (localValue <= 0m && cost <= 0m)
            throw new InvalidOperationException("مرتجع الشراء بلا قيمة أو تكلفة");

        var lines = new List<JournalLine>();
        // The payable debit is the largest leg, so it is the balancing figure (total credits
        // minus the cost debit) and the entry always balances to the cent despite per-leg rounding.
        var totalCredits = decimal.Round(localValue + cost, 2);
        var payable = decimal.Round(NonNegative(totalCredits - cost), 2);
        if (payable > 0m) lines.Add(new JournalLine("2000", payable, 0));
        if (localValue > 0m) lines.Add(new JournalLine("5102", 0, localValue));
        if (cost > 0m)
        {
            lines.Add(new JournalLine("1300", 0, cost));
            lines.Add(new JournalLine("5000", cost, 0));
        }
        await PostAsync(JournalSource.PurchaseReturn, sourceId, entryDate, "مرتجع شراء", lines.ToArray(), user, branchId);
    }

    private static decimal NonNegative(decimal amount) => amount < 0m ? 0m : amount;

    public async Task RecordOpeningStockAsync(int itemId, decimal qty, decimal count, decimal cost, string? user, int? branchId = null)
    {
        decimal amount = (qty > 0 ? qty : count) * cost;
        if (amount <= 0) return;
        await PostAsync(JournalSource.OpeningStock, itemId, DateTime.UtcNow, "جرد افتتاحي",
            new[] { new JournalLine("1300", amount, 0), new JournalLine("3000", 0, amount) }, user, branchId);
    }

    public async Task RecordStockWriteDownAsync(int itemId, decimal qty, decimal count, decimal cost, string? user, int? branchId = null)
    {
        decimal amount = (qty > 0 ? qty : count) * cost;
        if (amount <= 0) return;
        await PostAsync(JournalSource.OpeningStock, itemId, DateTime.UtcNow, "جرد تخفيض",
            new[] { new JournalLine("3000", amount, 0), new JournalLine("1300", 0, amount) }, user, branchId);
    }

    public async Task PostAsync(JournalSource source, int sourceId, DateTime date, string description, JournalLine[] lines, string? user, int? branchId = null)
    {
        var validLines = new List<(int AccountId, decimal Debit, decimal Credit, string? Desc)>();
        decimal totalDebit = 0, totalCredit = 0;

        foreach (var line in lines)
        {
            if ((line.Debit > 0) == (line.Credit > 0))
                throw new InvalidOperationException("كل سطر في القيد يجب أن يكون مدينًا أو دائنًا وليس كلاهما");
            if (line.Debit > 0 && line.Credit > 0)
                throw new InvalidOperationException("لا يمكن أن يكون السطر مدينًا ودائنًا في نفس الوقت");
            if (line.Debit < 0 || line.Credit < 0)
                throw new InvalidOperationException("لا يمكن أن يكون المبلغ سالبًا");

            var account = await _db.GLAccounts.FirstOrDefaultAsync(a => a.Code == line.Code);
            if (account == null)
                throw new InvalidOperationException($"الحساب برمز {line.Code} غير موجود في مخطط الحسابات");

            validLines.Add((account.Id, line.Debit, line.Credit, line.Description));
            totalDebit += line.Debit;
            totalCredit += line.Credit;
        }

        if (validLines.Count == 0)
            throw new InvalidOperationException("لا يمكن إنشاء قيد بلا أسطر");
        if (decimal.Round(totalDebit, 2) != decimal.Round(totalCredit, 2))
            throw new InvalidOperationException("مجموع المدين لا يساوي مجموع الدائن في القيد");

        if (await _db.FiscalPeriods.AnyAsync(fp => fp.Year == date.Year && fp.IsClosed))
            throw new InvalidOperationException($"السنة المالية {date.Year} مغلقة — لا يمكن إدراج قيود فيها");

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var entry = new JournalEntry
            {
                EntryNumber = await NextEntryNumberAsync(),
                Date = date,
                Description = description,
                Source = source,
                SourceId = sourceId,
                CreatedBy = user,
                CreatedAt = DateTime.UtcNow,
                IsPosted = true,
                BranchId = branchId,
                Lines = validLines.Select(l => new JournalEntryLine
                {
                    AccountId = l.AccountId,
                    Debit = l.Debit,
                    Credit = l.Credit,
                    Description = l.Desc,
                    BranchId = branchId
                }).ToList()
            };

            _db.JournalEntries.Add(entry);
            try
            {
                await _db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException)
            {
                if (_db.Database.CurrentTransaction != null) throw;
                _db.ChangeTracker.Clear();
            }
        }
        throw new InvalidOperationException("تعذر حفظ القيد بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    public async Task<JournalEntry?> GetEntryForSourceAsync(JournalSource source, int sourceId)
        => await _db.JournalEntries
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Source == source && e.SourceId == sourceId);

    private static async Task<int> MaxSeriesValueAsync(IQueryable<string> existing, string seriesPrefix)
    {
        var values = await existing.Where(n => n.StartsWith(seriesPrefix)).ToListAsync();
        int max = 0;
        foreach (var value in values)
        {
            if (value == null || value.Length <= seriesPrefix.Length) continue;
            if (int.TryParse(value.AsSpan(seriesPrefix.Length), out var parsed) && parsed > max)
                max = parsed;
        }
        return max;
    }

    private async Task<string> NextEntryNumberAsync()
    {
        var seriesPrefix = $"GL-{DateTime.Now:yyyyMMdd}-";
        int next = await MaxSeriesValueAsync(
            _db.JournalEntries.Select(j => j.EntryNumber), seriesPrefix) + 1;
        string num = $"{seriesPrefix}{next:D4}";
        while (await _db.JournalEntries.AnyAsync(j => j.EntryNumber == num))
        {
            next++;
            num = $"{seriesPrefix}{next:D4}";
        }
        return num;
    }
}
