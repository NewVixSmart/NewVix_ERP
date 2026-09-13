using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Accounting;

namespace Silk.Trading.Web.Services;

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
        var localCost = decimal.Round(costAmount * (exchangeRate ?? 1m), 2);
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

    public async Task RecordSaleReturnWithCostAsync(DateTime entryDate, int sourceId, int customerId, decimal valueAmount, decimal costAmount, int? currencyId, decimal? exchangeRate, string? user, int? branchId = null)
    {
        var localValue = decimal.Round(valueAmount * (exchangeRate ?? 1m), 2);
        await PostAsync(JournalSource.SaleReturn, sourceId, entryDate, "مرتجع بيع",
            new[]
            {
                new JournalLine("5101", localValue, 0),
                new JournalLine("1200", 0, localValue),
                new JournalLine("1300", costAmount, 0),
                new JournalLine("5000", 0, costAmount)
            }, user, branchId);
    }

    // Purchase return (mirror): Dr 2000 (AP) V / Cr 5102 (contra-purchases) V, and stock out at cost:
    // Cr 1300 (Inventory) C / Dr 5000 (COGS reversal) C. Balanced: Dr(V+C) == Cr(V+C).
    public async Task RecordPurchaseReturnWithCostAsync(DateTime entryDate, int sourceId, int supplierId, decimal valueAmount, decimal costAmount, int? currencyId, decimal? exchangeRate, string? user, int? branchId = null)
    {
        var localValue = decimal.Round(valueAmount * (exchangeRate ?? 1m), 2);
        await PostAsync(JournalSource.PurchaseReturn, sourceId, entryDate, "مرتجع شراء",
            new[]
            {
                new JournalLine("2000", localValue, 0),
                new JournalLine("5102", 0, localValue),
                new JournalLine("1300", 0, costAmount),
                new JournalLine("5000", costAmount, 0)
            }, user, branchId);
    }

    public async Task RecordOpeningStockAsync(int itemId, decimal qty, decimal count, decimal cost, string? user, int? branchId = null)
    {
        decimal amount = (qty + count) * cost;
        if (amount <= 0) return;
        await PostAsync(JournalSource.OpeningStock, itemId, DateTime.UtcNow, "جرد افتتاحي",
            new[] { new JournalLine("1300", amount, 0), new JournalLine("3000", 0, amount) }, user, branchId);
    }

    public async Task RecordStockWriteDownAsync(int itemId, decimal qty, decimal count, decimal cost, string? user, int? branchId = null)
    {
        decimal amount = (qty + count) * cost;
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
                _db.ChangeTracker.Clear();
            }
        }
        throw new InvalidOperationException("تعذر حفظ القيد بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    private async Task<string> NextEntryNumberAsync()
    {
        int next = await _db.JournalEntries.CountAsync() + 1;
        string num = $"GL-{DateTime.Now:yyyyMMdd}-{next:D4}";
        while (await _db.JournalEntries.AnyAsync(j => j.EntryNumber == num))
        {
            next++;
            num = $"GL-{DateTime.Now:yyyyMMdd}-{next:D4}";
        }
        return num;
    }
}
