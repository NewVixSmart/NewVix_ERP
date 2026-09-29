using Microsoft.Extensions.DependencyInjection;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.ViewModels.Core;

namespace NewVixSmart.Web.Services;

public static class PdfInvoiceService
{
    public static IServiceProvider? Services { get; set; }

    public static void ConfigureServices(IServiceProvider services) => Services = services;

    /// <summary>
    /// What a sale invoice is still owed, priced through <see cref="OpenAmountRule"/>: the value
    /// the delivery booked, less what receipts allocated to it, less what posted returns credited
    /// back. This is the figure the "المتبقي" line prints, so the customer's paper copy agrees
    /// with the aging report and the receivable sub-ledger instead of a stale
    /// <c>NetAmount - PaidAmount</c>.
    /// </summary>
    public static decimal SaleRemainingAmount(AppDbContext db, SaleInvoice invoice)
    {
        var open = OpenAmountRule.SaleOpenByInvoiceAsync(db, new[] { invoice.Id }).GetAwaiter().GetResult();
        return open.TryGetValue(invoice.Id, out var value) ? value : 0m;
    }

    /// <summary>The payable mirror of <see cref="SaleRemainingAmount"/>.</summary>
    public static decimal PurchaseRemainingAmount(AppDbContext db, PurchaseInvoice invoice)
    {
        var open = OpenAmountRule.PurchaseOpenByInvoiceAsync(db, new[] { invoice.Id }).GetAwaiter().GetResult();
        return open.TryGetValue(invoice.Id, out var value) ? value : 0m;
    }

    public static byte[] RenderPreviewPdf(PrintGroup group, PrintLayoutOptions options)
        => PrintPdfBuilder.RenderPreviewPdf(group, options);

    public static byte[] RenderSalePdf(SaleInvoice invoice)
    {
        var layout = PrintPdfBuilder.ResolveLayout(PrintGroup.SalesInvoice);
        var remaining = SaleRemaining(invoice, layout);
        return PrintPdfBuilder.Render(PrintGroup.SalesInvoice, layout, $"فاتورة بيع — {invoice.InvoiceNumber}", page =>
            PrintPdfBuilder.DocumentBody(page, layout, col =>
            {
                PrintPdfBuilder.InfoRow(col, new[]
                {
                    $"العميل: {invoice.Customer?.Name ?? "—"}",
                    $"التاريخ: {invoice.InvoiceDate:dd/MM/yyyy}"
                });
                var rows = invoice.Items
                    .Select(l => PrintPdfBuilder.LineRow(layout, l.Item?.Name ?? "—", l.Item?.Code, l.Item?.Barcode, l.Count, l.Quantity, l.UnitPrice, l.Discount, l.Total))
                    .ToList();
                PrintPdfBuilder.LineTable(col, layout, PrintPdfBuilder.InvoiceColumns(layout, true), rows);
                PrintPdfBuilder.DrawTotals(col, layout, new PdfTotals(
                    PrintPdfBuilder.Fmt(invoice.TotalAmount, layout.Decimals),
                    PrintPdfBuilder.Fmt(invoice.Discount + (invoice.Discount2 ?? 0m) + (invoice.Discount3 ?? 0m), layout.Decimals),
                    PrintPdfBuilder.Fmt(invoice.Tax, layout.Decimals),
                    PrintPdfBuilder.Fmt(invoice.NetAmount, layout.Decimals),
                    PrintPdfBuilder.Fmt(invoice.PaidAmount, layout.Decimals),
                    remaining,
                    invoice.CreatedBy,
                    invoice.Notes,
                    layout.ShowAmountInWords ? PrintPdfBuilder.AmountInWords(invoice.NetAmount) : null));
            }));
    }

    public static byte[] RenderPurchasePdf(PurchaseInvoice invoice)
    {
        var layout = PrintPdfBuilder.ResolveLayout(PrintGroup.PurchaseInvoice);
        var remaining = PurchaseRemaining(invoice, layout);
        return PrintPdfBuilder.Render(PrintGroup.PurchaseInvoice, layout, $"فاتورة شراء — {invoice.InvoiceNumber}", page =>
            PrintPdfBuilder.DocumentBody(page, layout, col =>
            {
                PrintPdfBuilder.InfoRow(col, new[]
                {
                    $"المورد: {invoice.Supplier?.Name ?? "—"}",
                    $"التاريخ: {invoice.InvoiceDate:dd/MM/yyyy}"
                });
                var rows = invoice.Items
                    .Select(l => PrintPdfBuilder.LineRow(layout, l.Item?.Name ?? "—", l.Item?.Code, l.Item?.Barcode, l.Count, l.Quantity, l.UnitPrice, l.Discount, l.Total))
                    .ToList();
                PrintPdfBuilder.LineTable(col, layout, PrintPdfBuilder.InvoiceColumns(layout, true), rows);
                PrintPdfBuilder.DrawTotals(col, layout, new PdfTotals(
                    PrintPdfBuilder.Fmt(invoice.TotalAmount, layout.Decimals),
                    PrintPdfBuilder.Fmt(invoice.Discount + (invoice.Discount2 ?? 0m) + (invoice.Discount3 ?? 0m), layout.Decimals),
                    PrintPdfBuilder.Fmt(invoice.Tax, layout.Decimals),
                    PrintPdfBuilder.Fmt(invoice.NetAmount, layout.Decimals),
                    PrintPdfBuilder.Fmt(invoice.PaidAmount, layout.Decimals),
                    remaining,
                    invoice.CreatedBy,
                    invoice.Notes,
                    layout.ShowAmountInWords ? PrintPdfBuilder.AmountInWords(invoice.NetAmount) : null));
            }));
    }

    /// <summary>
    /// The "المتبقي" line for a sale invoice, or null when the ledger cannot be reached. Omitting
    /// the line is honest; printing a stale one on the copy the customer keeps is not.
    /// </summary>
    private static string? SaleRemaining(SaleInvoice invoice, PrintLayoutOptions layout)
        => Remaining(db => SaleRemainingAmount(db, invoice), layout);

    /// <summary>The purchase mirror of <see cref="SaleRemaining"/>.</summary>
    private static string? PurchaseRemaining(PurchaseInvoice invoice, PrintLayoutOptions layout)
        => Remaining(db => PurchaseRemainingAmount(db, invoice), layout);

    private static string? Remaining(Func<AppDbContext, decimal> price, PrintLayoutOptions layout)
    {
        var factory = Services?.GetService<IServiceScopeFactory>();
        if (factory == null)
        {
            return null;
        }

        try
        {
            using var scope = factory.CreateScope();
            var db = scope.ServiceProvider.GetService<AppDbContext>();
            if (db == null)
            {
                return null;
            }

            return PrintPdfBuilder.Fmt(price(db), layout.Decimals);
        }
        catch
        {
            return null;
        }
    }
}
