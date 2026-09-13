using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Models.Sales;

namespace Silk.Trading.Web.Services;

public interface ISalesQuotesService
{
    Task<(bool Success, string? Error, SaleQuote? Quote)> CreateAsync(SaleQuote quote, List<SaleQuoteItem> items, string? user, int? branchId = null);
    Task<(bool Success, string? Error, SaleInvoice? Invoice)> ConvertToInvoiceAsync(int quoteId, string? user, int? branchId = null);
    Task<(bool Success, string? Error)> DeleteAsync(int quoteId);
}

public sealed class SalesQuotesService : ISalesQuotesService
{
    private const int MaxAttempts = 3;
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;

    public SalesQuotesService(AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    public async Task<(bool Success, string? Error, SaleQuote? Quote)> CreateAsync(SaleQuote quote, List<SaleQuoteItem> items, string? user, int? branchId = null)
    {
        var valid = items?.Where(i => i.ItemId > 0 && (i.Count != 0 || i.Quantity != 0)).ToList() ?? new List<SaleQuoteItem>();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل بالعدد أو الكمية", null);

        var autoNumber = string.IsNullOrWhiteSpace(quote.QuoteNumber);
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                quote.QuoteNumber = autoNumber ? await NextQuoteNumberAsync() : quote.QuoteNumber;
                quote.TotalAmount = valid.Sum(i => i.Total);
                quote.NetAmount = quote.TotalAmount - quote.Discount + quote.Tax;
                quote.Status = SaleQuoteStatus.Draft;
                quote.CreatedBy = user;
                quote.CreatedAt = DateTime.UtcNow;
                quote.Items = valid;

                _db.SaleQuotes.Add(quote);
                await _db.SaveChangesAsync();
                return (true, null, quote);
            }
            catch (DbUpdateException)
            {
                DetachAll();
                ResetQuoteKeys(quote);
            }
        }
        return (false, "تعذر حفظ عرض السعر بسبب تعارض في البيانات، حاول مرة أخرى", null);
    }

    public async Task<(bool Success, string? Error, SaleInvoice? Invoice)> ConvertToInvoiceAsync(int quoteId, string? user, int? branchId = null)
    {
        var quote = await _db.SaleQuotes.AsNoTracking().Include(q => q.Items)
            .FirstOrDefaultAsync(q => q.Id == quoteId);
        if (quote == null) return (false, "عرض السعر غير موجود", null);
        if (quote.Status == SaleQuoteStatus.Cancelled) return (false, "لا يمكن تحويل عرض سعر ملغي", null);

        var valid = quote.Items.Where(i => i.ItemId > 0 && (i.Count != 0 || i.Quantity != 0)).ToList();
        if (valid.Count == 0) return (false, "عرض السعر لا يحتوي على أصناف صالحة للتحويل", null);

        var claimed = await _db.SaleQuotes
            .Where(q => q.Id == quoteId && q.Status == SaleQuoteStatus.Draft)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, SaleQuoteStatus.Converting));
        if (claimed == 0)
            return (false, "عرض السعر محوّل إلى فاتورة بالفعل، أو جارٍ تحويله حالياً", null);

        var invoice = new SaleInvoice
        {
            InvoiceNumber = await NextInvoiceNumberAsync(),
            CustomerId = quote.CustomerId,
            CurrencyId = quote.CurrencyId ?? await BaseCurrencyIdAsync(),
            ExchangeRate = quote.ExchangeRate ?? 1m,
            InvoiceDate = quote.QuoteDate,
            PaymentTerms = InvoicePaymentTerms.OnReceipt,
            Notes = quote.Notes,
            TotalAmount = quote.TotalAmount,
            Discount = quote.Discount,
            Tax = quote.Tax,
            NetAmount = quote.NetAmount
        };
        var itemList = valid.Select(l => new SaleInvoiceItem
        {
            ItemId = l.ItemId,
            Count = l.Count,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice
        }).ToList();

        var (ok, error) = await _inventory.CreateSaleAsync(invoice, itemList, user, branchId);
        if (!ok)
        {
            await RollbackConversionAsync(quoteId);
            return (false, error, null);
        }

        var convertedAt = DateTime.UtcNow;
        try
        {
            await _db.SaleQuotes
                .Where(q => q.Id == quoteId && q.Status == SaleQuoteStatus.Converting)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(q => q.Status, SaleQuoteStatus.Converted)
                    .SetProperty(q => q.SaleInvoiceId, invoice.Id)
                    .SetProperty(q => q.ConvertedBy, user)
                    .SetProperty(q => q.ConvertedAt, convertedAt));
        }
        catch
        {
            await RollbackConversionAsync(quoteId);
            throw;
        }

        PatchTrackedQuote(quoteId, q =>
        {
            q.Status = SaleQuoteStatus.Converted;
            q.SaleInvoiceId = invoice.Id;
            q.ConvertedBy = user;
            q.ConvertedAt = convertedAt;
        });

        return (true, null, invoice);
    }

    private async Task RollbackConversionAsync(int quoteId)
    {
        await _db.SaleQuotes
            .Where(q => q.Id == quoteId && q.Status == SaleQuoteStatus.Converting)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, SaleQuoteStatus.Draft));
        PatchTrackedQuote(quoteId, q => q.Status = SaleQuoteStatus.Draft);
    }

    private void PatchTrackedQuote(int quoteId, Action<SaleQuote> apply)
    {
        foreach (var entry in _db.ChangeTracker.Entries<SaleQuote>().ToList())
        {
            if (entry.Entity.Id != quoteId) continue;
            apply(entry.Entity);
            entry.State = EntityState.Unchanged;
        }
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(int quoteId)
    {
        var quote = await _db.SaleQuotes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == quoteId);
        if (quote == null) return (false, "عرض السعر غير موجود");
        if (quote.Status == SaleQuoteStatus.Converted) return (false, "لا يمكن حذف عرض تم تحويله إلى فاتورة");
        if (quote.Status == SaleQuoteStatus.Cancelled) return (false, "لا يمكن حذف عرض سعر ملغي");

        await _db.SaleQuotes.Where(q => q.Id == quoteId).ExecuteDeleteAsync();
        return (true, null);
    }

    private async Task<string> NextQuoteNumberAsync()
    {
        int next = await _db.SaleQuotes.CountAsync() + 1;
        var num = $"SQ-{DateTime.Now:yyyyMMdd}-{next:D3}";
        while (await _db.SaleQuotes.AnyAsync(q => q.QuoteNumber == num))
        {
            next++;
            num = $"SQ-{DateTime.Now:yyyyMMdd}-{next:D3}";
        }
        return num;
    }

    private async Task<string> NextInvoiceNumberAsync()
    {
        var lastInvoice = await _db.SaleInvoices.AsNoTracking().OrderByDescending(s => s.Id).FirstOrDefaultAsync();
        return $"SI-{(lastInvoice == null ? 1 : lastInvoice.Id + 1):D5}";
    }

    private async Task<int?> BaseCurrencyIdAsync()
        => await _db.Currencies.AsNoTracking().Where(c => c.IsActive).OrderByDescending(c => c.IsBase)
            .Select(c => (int?)c.Id).FirstOrDefaultAsync();

    private void DetachAll()
    {
        foreach (var entry in _db.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;
    }

    private static void ResetQuoteKeys(SaleQuote quote)
    {
        quote.Id = 0;
        foreach (var item in quote.Items)
        {
            item.Id = 0;
            item.SaleQuoteId = 0;
        }
    }
}