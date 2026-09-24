using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.ViewModels.Core;

namespace NewVixSmart.Web.Services;

public interface ISalesQuotesService
{
    Task<(bool Success, string? Error, SaleQuote? Quote)> CreateAsync(SaleQuote quote, List<SaleQuoteItem> items, string? user, int? branchId = null);
    Task<(bool Success, string? Error, SalesOrder? Order)> ConvertToOrderAsync(int quoteId, string? user, int? branchId = null);
    Task<(bool Success, string? Error)> DeleteAsync(int quoteId);
    Task<(int Converted, int Failed, IReadOnlyList<(int Id, string Error)> Failures)> MassConvertAsync(IEnumerable<int> quoteIds, string? user, int? branchId = null);
}

public sealed class SalesQuotesService : ISalesQuotesService
{
    private const int MaxAttempts = 3;
    private readonly AppDbContext _db;
    private readonly ISalesOrdersService _orders;

    public SalesQuotesService(AppDbContext db, ISalesOrdersService orders)
    {
        _db = db;
        _orders = orders;
    }

    public async Task<(bool Success, string? Error, SaleQuote? Quote)> CreateAsync(SaleQuote quote, List<SaleQuoteItem> items, string? user, int? branchId = null)
    {
        var allItems = items ?? new List<SaleQuoteItem>();
        if (allItems.Any(i => i.Quantity < 0 || i.Count < 0))
            return (false, "الكمية لا يمكن أن تكون سالبة", null);
        if (allItems.Any(i => i.UnitPrice < 0))
            return (false, "السعر لا يمكن أن يكون سالباً", null);

        var valid = allItems.Where(i => i.ItemId > 0 && (i.Count != 0 || i.Quantity != 0)).ToList();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل بالعدد أو الكمية", null);
        if (valid.GroupBy(i => i.ItemId).Any(g => g.Count() > 1))
            return (false, "لا يمكن إضافة الصنف نفسه في أكثر من سطر", null);

        var autoNumber = string.IsNullOrWhiteSpace(quote.QuoteNumber);
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                quote.QuoteNumber = autoNumber ? await NextQuoteNumberAsync() : quote.QuoteNumber;
                quote.TotalAmount = valid.Sum(i => i.Total);
                if (quote.Discount > quote.TotalAmount + quote.Tax)
                    return (false, "الخصم أكبر من إجمالي قيمة العرض مع الضرائب", null);
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

    public async Task<(bool Success, string? Error, SalesOrder? Order)> ConvertToOrderAsync(int quoteId, string? user, int? branchId = null)
    {
        var quote = await _db.SaleQuotes.AsNoTracking().Include(q => q.Items)
            .FirstOrDefaultAsync(q => q.Id == quoteId);
        if (quote == null) return (false, "عرض السعر غير موجود", null);
        if (quote.Status == SaleQuoteStatus.Cancelled) return (false, "لا يمكن تحويل عرض سعر ملغي", null);

        var valid = quote.Items.Where(i => i.ItemId > 0 && (i.Count != 0 || i.Quantity != 0)).ToList();
        if (valid.Count == 0) return (false, "عرض السعر لا يحتوي على أصناف صالحة للتحويل", null);
        if (valid.GroupBy(i => i.ItemId).Any(g => g.Count() > 1))
            return (false, "عرض السعر يحتوي على الصنف نفسه في أكثر من سطر", null);

        var claimed = await _db.SaleQuotes
            .Where(q => q.Id == quoteId && q.Status == SaleQuoteStatus.Draft)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, SaleQuoteStatus.Converting));
        if (claimed == 0)
            return (false, "عرض السعر محوّل إلى أمر بيع بالفعل، أو جارٍ تحويله حالياً", null);

        var order = new SalesOrder
        {
            CustomerId = quote.CustomerId,
            CurrencyId = quote.CurrencyId,
            ExchangeRate = quote.ExchangeRate,
            OrderDate = quote.QuoteDate,
            Notes = quote.Notes,
            SaleQuoteId = quote.Id
        };
        var itemList = valid.Select(l => new SalesOrderItem
        {
            ItemId = l.ItemId,
            Count = l.Count,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice
        }).ToList();

        var (ok, error) = await _orders.CreateOrderAsync(order, itemList, user);
        if (!ok)
        {
            await RollbackConversionAsync(quoteId);
            return (false, error, null);
        }

        var convertedAt = DateTime.UtcNow;
        try
        {
            var updated = await _db.SaleQuotes
                .Where(q => q.Id == quoteId && q.Status == SaleQuoteStatus.Converting)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(q => q.Status, SaleQuoteStatus.Converted)
                    .SetProperty(q => q.SalesOrderId, order.Id)
                    .SetProperty(q => q.ConvertedBy, user)
                    .SetProperty(q => q.ConvertedAt, convertedAt));
            if (updated == 0)
            {
                await DeleteCreatedOrderAsync(order.Id);
                await RollbackConversionAsync(quoteId);
                return (false, "تعذر تحديث حالة عرض السعر أثناء التحويل، حاول مرة أخرى", null);
            }
        }
        catch
        {
            await DeleteCreatedOrderAsync(order.Id);
            await RollbackConversionAsync(quoteId);
            throw;
        }

        PatchTrackedQuote(quoteId, q =>
        {
            q.Status = SaleQuoteStatus.Converted;
            q.SalesOrderId = order.Id;
            q.ConvertedBy = user;
            q.ConvertedAt = convertedAt;
        });

        return (true, null, order);
    }

    private async Task RollbackConversionAsync(int quoteId)
    {
        await _db.SaleQuotes
            .Where(q => q.Id == quoteId && q.Status == SaleQuoteStatus.Converting)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, SaleQuoteStatus.Draft));
        PatchTrackedQuote(quoteId, q => q.Status = SaleQuoteStatus.Draft);
    }

    private async Task DeleteCreatedOrderAsync(int orderId)
    {
        _db.ChangeTracker.Clear();
        await _db.SalesOrderItems.Where(i => i.SalesOrderId == orderId).ExecuteDeleteAsync();
        await _db.SalesOrders.Where(o => o.Id == orderId).ExecuteDeleteAsync();
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
        if (quote.Status == SaleQuoteStatus.Converted) return (false, "لا يمكن حذف عرض تم تحويله إلى أمر بيع");
        if (quote.Status == SaleQuoteStatus.Cancelled) return (false, "لا يمكن حذف عرض سعر ملغي");
        if (quote.Status == SaleQuoteStatus.Converting) return (false, "عرض السعر جارٍ تحويله حالياً، أعد المحاولة بعد لحظات");

        await _db.SaleQuotes.Where(q => q.Id == quoteId).ExecuteDeleteAsync();
        return (true, null);
    }

    public async Task<(int Converted, int Failed, IReadOnlyList<(int Id, string Error)> Failures)> MassConvertAsync(IEnumerable<int> quoteIds, string? user, int? branchId = null)
    {
        var failures = new List<(int Id, string Error)>();
        int converted = 0;
        foreach (var id in quoteIds.Distinct().ToList())
        {
            var (ok, error, _) = await ConvertToOrderAsync(id, user, branchId);
            if (ok) { converted++; continue; }
            failures.Add((id, error ?? "تعذر تحويل عرض السعر"));
        }
        return (converted, failures.Count, failures);
    }

    public static byte[] RenderQuotePdf(SaleQuote quote)
    {
        var layout = PrintPdfBuilder.ResolveLayout(PrintGroup.SalesQuote);
        return PrintPdfBuilder.Render(PrintGroup.SalesQuote, layout, $"عرض سعر — {quote.QuoteNumber}", page =>
            PrintPdfBuilder.DocumentBody(page, layout, col =>
            {
                PrintPdfBuilder.InfoRow(col, new[]
                {
                    $"العميل: {quote.Customer?.Name ?? "—"}",
                    $"التاريخ: {quote.QuoteDate:dd/MM/yyyy}",
                    quote.ValidUntil.HasValue ? $"صالح حتى: {quote.ValidUntil:dd/MM/yyyy}" : "صالح حتى: —"
                });
                if (quote.SupplierQuote != null)
                {
                    col.Item().PaddingTop(4).Text($"عرض المورد المرجعي: مرتبط بعرض المورد #{quote.SupplierQuote.Id} — {quote.SupplierQuote.Supplier?.Name ?? "—"}");
                }
                var rows = quote.Items
                    .Select(l => PrintPdfBuilder.LineRow(layout, l.Item?.Name ?? "—", l.Item?.Code, l.Item?.Barcode, l.Count, l.Quantity, l.UnitPrice, 0m, l.Total))
                    .ToList();
                PrintPdfBuilder.LineTable(col, layout, PrintPdfBuilder.InvoiceColumns(layout, true), rows);
                PrintPdfBuilder.DrawTotals(col, layout, new PdfTotals(
                    PrintPdfBuilder.Fmt(quote.TotalAmount, layout.Decimals),
                    PrintPdfBuilder.Fmt(quote.Discount, layout.Decimals),
                    PrintPdfBuilder.Fmt(quote.Tax, layout.Decimals),
                    PrintPdfBuilder.Fmt(quote.NetAmount, layout.Decimals),
                    null, null,
                    quote.CreatedBy,
                    quote.Notes,
                    layout.ShowAmountInWords ? PrintPdfBuilder.AmountInWords(quote.NetAmount) : null));
            }));
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