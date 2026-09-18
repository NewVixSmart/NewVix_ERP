using Silk.Trading.Web.Models.Purchases;
using Silk.Trading.Web.Models.Sales;
using Silk.Trading.Web.ViewModels.Core;

namespace Silk.Trading.Web.Services;

public static class PdfInvoiceService
{
    public static IServiceProvider? Services { get; set; }

    public static void ConfigureServices(IServiceProvider services) => Services = services;

    public static byte[] RenderPreviewPdf(PrintGroup group, PrintLayoutOptions options)
        => PrintPdfBuilder.RenderPreviewPdf(group, options);

    public static byte[] RenderSalePdf(SaleInvoice invoice)
    {
        var layout = PrintPdfBuilder.ResolveLayout(PrintGroup.SalesInvoice);
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
                    PrintPdfBuilder.Fmt(invoice.NetAmount - invoice.PaidAmount, layout.Decimals),
                    invoice.CreatedBy,
                    invoice.Notes,
                    layout.ShowAmountInWords ? PrintPdfBuilder.AmountInWords(invoice.NetAmount) : null));
            }));
    }

    public static byte[] RenderPurchasePdf(PurchaseInvoice invoice)
    {
        var layout = PrintPdfBuilder.ResolveLayout(PrintGroup.PurchaseInvoice);
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
                    PrintPdfBuilder.Fmt(invoice.NetAmount - invoice.PaidAmount, layout.Decimals),
                    invoice.CreatedBy,
                    invoice.Notes,
                    layout.ShowAmountInWords ? PrintPdfBuilder.AmountInWords(invoice.NetAmount) : null));
            }));
    }
}