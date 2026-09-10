using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Silk.Trading.Web.Models.Purchases;
using Silk.Trading.Web.Models.Sales;

namespace Silk.Trading.Web.Services;

public static class PdfInvoiceService
{
    public static byte[] RenderSalePdf(SaleInvoice invoice)
    {
        return QuestPDF.Fluent.Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));
                page.Header().Column(col =>
                {
                    col.Item().AlignCenter().Text("سلك للتجارة").FontSize(18).Bold();
                    col.Item().AlignCenter().Text($"فاتورة بيع — {invoice.InvoiceNumber}").FontSize(13).SemiBold();
                    col.Item().PaddingTop(6).LineHorizontal(1);
                });
                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"العميل: {invoice.Customer?.Name ?? "—"}");
                        row.RelativeItem().AlignLeft().Text($"التاريخ: {invoice.InvoiceDate:dd/MM/yyyy}");
                    });
                    col.Item().PaddingTop(10).Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(); cd.ConstantColumn(80); cd.ConstantColumn(80); });
                        t.Header(hd =>
                        {
                            hd.Cell().Element(BoldHeader).Text("الصنف");
                            hd.Cell().Element(BoldHeader).Text("الوحدة");
                            hd.Cell().Element(BoldHeader).AlignRight().Text("سعر الوحدة");
                            hd.Cell().Element(BoldHeader).AlignRight().Text("الإجمالي");
                        });
                        foreach (var line in invoice.Items)
                        {
                            t.Cell().Text(line.Item?.Name ?? "—");
                            t.Cell().Text(line.Quantity > 0 ? line.Quantity.ToString("N0") : line.Count.ToString("N0"));
                            t.Cell().AlignRight().Text(line.UnitPrice.ToString("N2"));
                            t.Cell().AlignRight().Text(line.Total.ToString("N2"));
                        }
                        t.Cell().Element(BoldFooter).Text("");
                        t.Cell().Element(BoldFooter).Text("");
                        t.Cell().Element(BoldFooter).Text("الصافي");
                        t.Cell().Element(BoldFooter).AlignRight().Text(invoice.NetAmount.ToString("N2"));
                    });
                    col.Item().PaddingTop(10).Text($"المدفوع: {invoice.PaidAmount.ToString("N2")}");
                    col.Item().Text($"المتبقي: {(invoice.NetAmount - invoice.PaidAmount).ToString("N2")}");
                });
                page.Footer().AlignCenter().Text(x => { x.Span("صفحة "); x.CurrentPageNumber(); x.Span(" من "); x.TotalPages(); });
            });
        }).GeneratePdf();
    }

    public static byte[] RenderPurchasePdf(PurchaseInvoice invoice)
    {
        return QuestPDF.Fluent.Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));
                page.Header().Column(col =>
                {
                    col.Item().AlignCenter().Text("سلك للتجارة").FontSize(18).Bold();
                    col.Item().AlignCenter().Text($"فاتورة شراء — {invoice.InvoiceNumber}").FontSize(13).SemiBold();
                    col.Item().PaddingTop(6).LineHorizontal(1);
                });
                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"المورد: {invoice.Supplier?.Name ?? "—"}");
                        row.RelativeItem().AlignLeft().Text($"التاريخ: {invoice.InvoiceDate:dd/MM/yyyy}");
                    });
                    col.Item().PaddingTop(10).Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(); cd.ConstantColumn(80); cd.ConstantColumn(80); });
                        t.Header(hd =>
                        {
                            hd.Cell().Element(BoldHeader).Text("الصنف");
                            hd.Cell().Element(BoldHeader).Text("الوحدة");
                            hd.Cell().Element(BoldHeader).AlignRight().Text("سعر الوحدة");
                            hd.Cell().Element(BoldHeader).AlignRight().Text("الإجمالي");
                        });
                        foreach (var line in invoice.Items)
                        {
                            t.Cell().Text(line.Item?.Name ?? "—");
                            t.Cell().Text(line.Quantity > 0 ? line.Quantity.ToString("N0") : line.Count.ToString("N0"));
                            t.Cell().AlignRight().Text(line.UnitPrice.ToString("N2"));
                            t.Cell().AlignRight().Text(line.Total.ToString("N2"));
                        }
                        t.Cell().Element(BoldFooter).Text("");
                        t.Cell().Element(BoldFooter).Text("");
                        t.Cell().Element(BoldFooter).Text("الصافي");
                        t.Cell().Element(BoldFooter).AlignRight().Text(invoice.NetAmount.ToString("N2"));
                    });
                    col.Item().PaddingTop(10).Text($"المدفوع: {invoice.PaidAmount.ToString("N2")}");
                    col.Item().Text($"المتبقي: {(invoice.NetAmount - invoice.PaidAmount).ToString("N2")}");
                });
                page.Footer().AlignCenter().Text(x => { x.Span("صفحة "); x.CurrentPageNumber(); x.Span(" من "); x.TotalPages(); });
            });
        }).GeneratePdf();
    }

    private static IContainer BoldHeader(IContainer c) => c.Background(Colors.Grey.Lighten3).BorderBottom(1).Padding(4).DefaultTextStyle(x => x.SemiBold());
    private static IContainer BoldFooter(IContainer c) => c.Background(Colors.Grey.Lighten2).BorderTop(1).Padding(4).DefaultTextStyle(x => x.SemiBold());
}
