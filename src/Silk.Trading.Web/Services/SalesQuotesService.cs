using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Models.Sales;

namespace Silk.Trading.Web.Services;

public interface ISalesQuotesService
{
    Task<(bool Success, string? Error, SaleQuote? Quote)> CreateAsync(SaleQuote quote, List<SaleQuoteItem> items, string? user, int? branchId = null);
    Task<(bool Success, string? Error, SaleInvoice? Invoice)> ConvertToInvoiceAsync(int quoteId, string? user, int? branchId = null);
    Task<(bool Success, string? Error)> DeleteAsync(int quoteId);
    Task<(int Converted, int Failed, IReadOnlyList<(int Id, string Error)> Failures)> MassConvertAsync(IEnumerable<int> quoteIds, string? user, int? branchId = null);
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

    public async Task<(int Converted, int Failed, IReadOnlyList<(int Id, string Error)> Failures)> MassConvertAsync(IEnumerable<int> quoteIds, string? user, int? branchId = null)
    {
        var failures = new List<(int Id, string Error)>();
        int converted = 0;
        foreach (var id in quoteIds.Distinct().ToList())
        {
            var (ok, error, _) = await ConvertToInvoiceAsync(id, user, branchId);
            if (ok) { converted++; continue; }
            failures.Add((id, error ?? "تعذر تحويل عرض السعر"));
        }
        return (converted, failures.Count, failures);
    }

    public static byte[] RenderQuotePdf(SaleQuote quote)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));
                page.Header().Column(col =>
                {
                    col.Item().AlignCenter().Text("سلك للتجارة").FontSize(18).Bold();
                    col.Item().AlignCenter().Text($"عرض سعر — {quote.QuoteNumber}").FontSize(13).SemiBold();
                    col.Item().PaddingTop(6).LineHorizontal(1);
                });
                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"العميل: {quote.Customer?.Name ?? "—"}");
                        row.RelativeItem().AlignLeft().Text($"التاريخ: {quote.QuoteDate:dd/MM/yyyy}");
                        row.RelativeItem().AlignLeft().Text(quote.ValidUntil.HasValue ? $"صالح حتى: {quote.ValidUntil:dd/MM/yyyy}" : "صالح حتى: —");
                    });
                    if (quote.SupplierQuote != null)
                    {
                        col.Item().PaddingTop(4).Text($"عرض المورد المرجعي: مرتبط بعرض المورد #{quote.SupplierQuote.Id} — {quote.SupplierQuote.Supplier?.Name ?? "—"}");
                    }
                    col.Item().PaddingTop(10).Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.ConstantColumn(70); cd.ConstantColumn(70); cd.ConstantColumn(80); cd.ConstantColumn(80); });
                        t.Header(hd =>
                        {
                            hd.Cell().Element(BoldHeader).Text("الصنف");
                            hd.Cell().Element(BoldHeader).Text("العدد");
                            hd.Cell().Element(BoldHeader).Text("الكمية");
                            hd.Cell().Element(BoldHeader).AlignRight().Text("سعر الوحدة");
                            hd.Cell().Element(BoldHeader).AlignRight().Text("الإجمالي");
                        });
                        foreach (var line in quote.Items)
                        {
                            t.Cell().Text(line.Item?.Name ?? "—");
                            t.Cell().Text(line.Count.ToString("N0"));
                            t.Cell().Text(line.Quantity.ToString("N0"));
                            t.Cell().AlignRight().Text(line.UnitPrice.ToString("N2"));
                            t.Cell().AlignRight().Text(line.Total.ToString("N2"));
                        }
                        t.Cell().Element(BoldFooter).Text("");
                        t.Cell().Element(BoldFooter).Text("");
                        t.Cell().Element(BoldFooter).Text("");
                        t.Cell().Element(BoldFooter).Text("الصافي");
                        t.Cell().Element(BoldFooter).AlignRight().Text(quote.NetAmount.ToString("N2"));
                    });
                    col.Item().PaddingTop(10).Text($"الإجمالي: {quote.TotalAmount.ToString("N2")} — الخصم: {quote.Discount.ToString("N2")} — الضريبة: {quote.Tax.ToString("N2")}");
                    if (!string.IsNullOrWhiteSpace(quote.Notes))
                    {
                        col.Item().PaddingTop(6).Text($"ملاحظات: {quote.Notes}");
                    }
                });
                page.Footer().AlignCenter().Text(x => { x.Span("صفحة "); x.CurrentPageNumber(); x.Span(" من "); x.TotalPages(); });
            });
        }).GeneratePdf();
    }

    private static IContainer BoldHeader(IContainer c) => c.Background(Colors.Grey.Lighten3).BorderBottom(1).Padding(4).DefaultTextStyle(x => x.SemiBold());
    private static IContainer BoldFooter(IContainer c) => c.Background(Colors.Grey.Lighten2).BorderTop(1).Padding(4).DefaultTextStyle(x => x.SemiBold());

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