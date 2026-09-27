using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.ViewModels.Core;
using System.Globalization;

namespace NewVixSmart.Web.Services;

public static class PrintPdfBuilder
{
    public static byte[] Render(PrintGroup group, PrintLayoutOptions layout, Action<PageDescriptor> content)
        => Render(group, layout, GroupTitle(group), content);

    public static byte[] Render(PrintGroup group, PrintLayoutOptions layout, string title, Action<PageDescriptor> content)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var profile = ResolveProfile();
        var scale = FontScale(layout);
        var accent = AccentColor(layout);
        var size = PageSizeOf(layout);
        var margin = MarginOf(layout);
        return QuestPDF.Fluent.Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(size);
                page.Margin(margin);
                page.DefaultTextStyle(x => x.FontSize((float)(10 * scale)));
                BuildHeader(page, layout, profile, title, scale, accent);
                content(page);
                BuildFooter(page, layout, profile);
            });
        }).GeneratePdf();
    }

    public static PrintLayoutOptions ResolveLayout(PrintGroup group)
    {
        var factory = PdfInvoiceService.Services?.GetService<IServiceScopeFactory>();
        if (factory != null)
        {
            try
            {
                using var scope = factory.CreateScope();
                var settings = scope.ServiceProvider.GetService<IPrintSettingsService>();
                if (settings != null)
                {
                    var layout = settings.GetLayoutAsync(group).GetAwaiter().GetResult();
                    if (layout != null) return layout;
                }
            }
            catch
            {
            }
        }
        return FallbackLayout(group);
    }

    public static CompanyProfile ResolveProfile()
    {
        var factory = PdfInvoiceService.Services?.GetService<IServiceScopeFactory>();
        if (factory != null)
        {
            try
            {
                using var scope = factory.CreateScope();
                var branding = scope.ServiceProvider.GetService<IBrandingService>();
                if (branding != null)
                {
                    var profile = branding.LoadAsync().GetAwaiter().GetResult().Profile;
                    if (profile != null) return profile;
                }
            }
            catch
            {
            }
        }
        return new CompanyProfile { CompanyName = "NewVix" };
    }

    public static decimal Round2(decimal v, int decimals)
    {
        var d = Math.Clamp(decimals, 0, 6);
        return Math.Round(v, d, MidpointRounding.AwayFromZero);
    }

    public static string Fmt(decimal v, int decimals)
        => Round2(v, Math.Clamp(decimals, 0, 6)).ToString("N" + Math.Clamp(decimals, 0, 6).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    public static string AccentColor(PrintLayoutOptions layout) => NormalizeHex(layout.AccentColor, "#2e6fd8");

    public static string TableHeaderBg(PrintLayoutOptions layout) => NormalizeHex(layout.TableHeaderBg, "#eaf3fc");

    public static string TableHeaderText(PrintLayoutOptions layout) => NormalizeHex(layout.TableHeaderText, "#0d1b35");

    public static double FontScale(PrintLayoutOptions layout)
        => double.IsNaN(layout.FontScale) ? 1.0 : Math.Clamp(layout.FontScale, 0.8, 1.3);

    public static IContainer HeaderCell(IContainer c, PrintLayoutOptions layout)
    {
        return c.Background(Color.FromHex(TableHeaderBg(layout)))
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(4)
            .DefaultTextStyle(x => x.SemiBold().FontColor(Color.FromHex(TableHeaderText(layout))));
    }

    public static IContainer FooterCell(IContainer c, PrintLayoutOptions layout)
    {
        return c.Background(Color.FromHex(ColorUtil.Lighten(TableHeaderBg(layout), 0.35)))
            .BorderTop(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(4)
            .DefaultTextStyle(x => x.SemiBold().FontColor(Color.FromHex(TableHeaderText(layout))));
    }

    public static IReadOnlyList<PdfColumn> InvoiceColumns(PrintLayoutOptions layout, bool showCost)
    {
        var cols = new List<PdfColumn> { new("الصنف", false, 2f) };
        if (layout.ShowItemCode) cols.Add(new PdfColumn("الكود", false, 1f));
        if (layout.ShowBarcode) cols.Add(new PdfColumn("الباركود", false, 1f));
        if (layout.ShowCount) cols.Add(new PdfColumn("العدد", true, 1f));
        if (layout.ShowQuantity) cols.Add(new PdfColumn("الكمية", true, 1f));
        if (layout.ShowUnitPrice && showCost) cols.Add(new PdfColumn("سعر الوحدة", true, 1f));
        if (layout.ShowDiscountColumn && showCost) cols.Add(new PdfColumn("الخصم", true, 1f));
        if (layout.ShowSubtotal && showCost) cols.Add(new PdfColumn("الإجمالي", true, 1f));
        return cols;
    }

    public static IReadOnlyList<PdfColumn> TransferColumns(PrintLayoutOptions layout)
    {
        var cols = new List<PdfColumn> { new("الصنف", false, 2f) };
        if (layout.ShowItemCode) cols.Add(new PdfColumn("الكود", false, 1f));
        if (layout.ShowBarcode) cols.Add(new PdfColumn("الباركود", false, 1f));
        if (layout.ShowCount) cols.Add(new PdfColumn("العدد", true, 1f));
        if (layout.ShowQuantity) cols.Add(new PdfColumn("الكمية", true, 1f));
        if (layout.ShowUnitPrice) cols.Add(new PdfColumn("تكلفة الوحدة", true, 1f));
        return cols;
    }

    public static IReadOnlyList<string?> LineRow(PrintLayoutOptions layout, string name, string? code, string? barcode, decimal count, decimal quantity, decimal unitPrice, decimal discount, decimal subtotal)
    {
        var row = new List<string?> { name };
        if (layout.ShowItemCode) row.Add(code);
        if (layout.ShowBarcode) row.Add(barcode);
        if (layout.ShowCount) row.Add(Fmt(count, layout.Decimals));
        if (layout.ShowQuantity) row.Add(Fmt(quantity, layout.Decimals));
        if (layout.ShowUnitPrice) row.Add(Fmt(unitPrice, layout.Decimals));
        if (layout.ShowDiscountColumn) row.Add(Fmt(discount, layout.Decimals));
        if (layout.ShowSubtotal) row.Add(Fmt(subtotal, layout.Decimals));
        return row;
    }

    public static IReadOnlyList<string?> TransferRow(PrintLayoutOptions layout, string name, string? code, string? barcode, decimal count, decimal quantity, decimal unitCost)
    {
        var row = new List<string?> { name };
        if (layout.ShowItemCode) row.Add(code);
        if (layout.ShowBarcode) row.Add(barcode);
        if (layout.ShowCount) row.Add(Fmt(count, layout.Decimals));
        if (layout.ShowQuantity) row.Add(Fmt(quantity, layout.Decimals));
        if (layout.ShowUnitPrice) row.Add(Fmt(unitCost, layout.Decimals));
        return row;
    }

    public static void InfoRow(ColumnDescriptor col, IReadOnlyList<string> fields)
    {
        col.Item().Row(row =>
        {
            for (int i = 0; i < fields.Count; i++)
            {
                if (i > 0)
                    row.RelativeItem().AlignLeft().Text(fields[i]);
                else
                    row.RelativeItem().Text(fields[i]);
            }
        });
    }

    public static void LineTable(ColumnDescriptor container, PrintLayoutOptions layout, IReadOnlyList<PdfColumn> columns, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        container.Item().PaddingTop(10).Table(t => DrawLineTable(t, layout, columns, rows));
    }

    public static void DrawLineTable(TableDescriptor t, PrintLayoutOptions layout, IReadOnlyList<PdfColumn> columns, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        t.ColumnsDefinition(cd =>
        {
            foreach (var c in columns) cd.RelativeColumn(c.Weight);
        });
        t.Header(hd =>
        {
            foreach (var c in columns)
            {
                if (c.Right)
                    hd.Cell().Element(x => HeaderCell(x, layout)).AlignRight().Text(c.Header);
                else
                    hd.Cell().Element(x => HeaderCell(x, layout)).Text(c.Header);
            }
        });
        foreach (var row in rows)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                if (columns[i].Right)
                    t.Cell().AlignRight().Text(row.Count > i ? row[i] ?? "" : "");
                else
                    t.Cell().Text(row.Count > i ? row[i] ?? "" : "");
            }
        }
    }

    public static void DrawTotals(ColumnDescriptor col, PrintLayoutOptions layout, PdfTotals totals)
    {
        if (layout.ShowSubtotal && totals.Total != null) TotalRow(col, layout, "الإجمالي", totals.Total, false);
        if (layout.ShowTotalDiscount && totals.TotalDiscount != null) TotalRow(col, layout, "الخصم", totals.TotalDiscount, false);
        if (layout.ShowTotalTax && totals.TotalTax != null) TotalRow(col, layout, "الضريبة", totals.TotalTax, false);
        if (layout.ShowGrandTotal && totals.GrandTotal != null) TotalRow(col, layout, "الصافي", totals.GrandTotal, true);
        if (layout.ShowPaidBadge && totals.Paid != null && totals.Remaining != null)
        {
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text($"المدفوع: {totals.Paid}");
                row.RelativeItem().Text($"المتبقي: {totals.Remaining}");
            });
        }
        if (!string.IsNullOrWhiteSpace(totals.Notes))
            col.Item().PaddingTop(6).Text($"ملاحظات: {totals.Notes}").FontSize(9);
        if (layout.ShowCreatedBy && !string.IsNullOrWhiteSpace(totals.CreatedBy))
            col.Item().PaddingTop(4).Text($"أنشئ بواسطة: {totals.CreatedBy}").FontSize(8).FontColor(Colors.Grey.Darken1);
        if (layout.ShowAmountInWords && totals.AmountInWords != null)
            col.Item().PaddingTop(6).Text($"فقط: {totals.AmountInWords}").FontSize(9).SemiBold();
    }

    public static void DocumentBody(PageDescriptor page, PrintLayoutOptions layout, Action<ColumnDescriptor> body)
    {
        page.Content().PaddingTop(10).Column(col =>
        {
            body(col);
            if (layout.ShowSignatureLines)
            {
                col.Item().PaddingTop(24).Row(row =>
                {
                    row.RelativeItem().Text(string.IsNullOrWhiteSpace(layout.SignatureOne) ? "التوقيع" : layout.SignatureOne);
                    row.RelativeItem().Text(string.IsNullOrWhiteSpace(layout.SignatureTwo) ? "التوقيع" : layout.SignatureTwo);
                });
            }
        });
    }

    public static string AmountInWords(decimal value)
    {
        var amount = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        var negative = amount < 0m;
        amount = Math.Abs(amount);
        var whole = Math.Truncate(amount);
        var frac = Math.Round(Math.Abs(amount - whole) * 100m, MidpointRounding.AwayFromZero);
        var w = (long)Math.Abs(whole);
        var parts = new List<string> { w == 0 ? "صفر" : IntegerWords(w) };
        var fracValue = (int)Math.Min(frac, 99);
        if (fracValue > 0) parts.Add(UnderHundred(fracValue) + " من مائة");
        parts.Add("فقط");
        var result = string.Join(" و ", parts);
        return negative && w > 0 ? "ناقص " + result : result;
    }

    public static byte[] RenderSaleReturnPdf(SaleReturn entity)
    {
        var layout = ResolveLayout(PrintGroup.SaleReturn);
        return Render(PrintGroup.SaleReturn, layout, $"مرتجع بيع — {entity.ReturnNumber}", page =>
            DocumentBody(page, layout, col =>
            {
                InfoRow(col, new[]
                {
                    $"العميل: {entity.Customer?.Name ?? "—"}",
                    $"التاريخ: {entity.ReturnDate:dd/MM/yyyy}",
                    entity.SaleInvoiceId.HasValue ? $"الفاتورة الأصلية: #{entity.SaleInvoiceId}" : "الفاتورة الأصلية: —"
                });
                var rows = entity.Items
                    .Select(i => LineRow(layout, i.Item?.Name ?? "—", i.Item?.Code, i.Item?.Barcode, i.Count, i.Quantity, i.UnitPrice, 0m, i.Total))
                    .ToList();
                LineTable(col, layout, InvoiceColumns(layout, true), rows);
                DrawTotals(col, layout, new PdfTotals(
                    null, null, null,
                    Fmt(entity.TotalAmount, layout.Decimals),
                    null, null,
                    entity.CreatedBy,
                    entity.Reason,
                    layout.ShowAmountInWords ? AmountInWords(entity.TotalAmount) : null));
            }));
    }

    public static byte[] RenderPurchaseReturnPdf(PurchaseReturn entity)
    {
        var layout = ResolveLayout(PrintGroup.PurchaseReturn);
        return Render(PrintGroup.PurchaseReturn, layout, $"مرتجع شراء — {entity.ReturnNumber}", page =>
            DocumentBody(page, layout, col =>
            {
                InfoRow(col, new[]
                {
                    $"المورد: {entity.Supplier?.Name ?? "—"}",
                    $"التاريخ: {entity.ReturnDate:dd/MM/yyyy}",
                    entity.PurchaseInvoiceId.HasValue ? $"الفاتورة الأصلية: #{entity.PurchaseInvoiceId}" : "الفاتورة الأصلية: —"
                });
                var rows = entity.Items
                    .Select(i => LineRow(layout, i.Item?.Name ?? "—", i.Item?.Code, i.Item?.Barcode, i.Count, i.Quantity, i.UnitPrice, 0m, i.Total))
                    .ToList();
                LineTable(col, layout, InvoiceColumns(layout, true), rows);
                DrawTotals(col, layout, new PdfTotals(
                    null, null, null,
                    Fmt(entity.TotalAmount, layout.Decimals),
                    null, null,
                    entity.CreatedBy,
                    entity.Reason,
                    layout.ShowAmountInWords ? AmountInWords(entity.TotalAmount) : null));
            }));
    }

    public static byte[] RenderPurchaseOrderPdf(PurchaseOrder entity)
    {
        var layout = ResolveLayout(PrintGroup.PurchaseOrder);
        return Render(PrintGroup.PurchaseOrder, layout, $"أمر شراء — {entity.OrderNumber}", page =>
            DocumentBody(page, layout, col =>
            {
                InfoRow(col, new[]
                {
                    $"المورد: {entity.Supplier?.Name ?? "—"}",
                    $"التاريخ: {entity.OrderDate:dd/MM/yyyy}",
                    entity.ExpectedDate.HasValue ? $"متوقع: {entity.ExpectedDate:dd/MM/yyyy}" : "متوقع: —",
                    $"الحالة: {entity.Status.GetDisplayName()}"
                });
                var rows = entity.Items
                    .Select(i => LineRow(layout, i.Item?.Name ?? "—", i.Item?.Code, i.Item?.Barcode, i.Count, i.Quantity, i.UnitPrice, 0m, i.Total))
                    .ToList();
                LineTable(col, layout, InvoiceColumns(layout, true), rows);
                var total = entity.Items.Sum(i => i.Total);
                DrawTotals(col, layout, new PdfTotals(
                    null, null, null,
                    Fmt(total, layout.Decimals),
                    null, null,
                    entity.CreatedBy,
                    entity.Notes,
                    layout.ShowAmountInWords ? AmountInWords(total) : null));
            }));
    }

    public static byte[] RenderStockTransferPdf(StockTransfer entity)
    {
        var layout = ResolveLayout(PrintGroup.StockTransfer);
        return Render(PrintGroup.StockTransfer, layout, $"تحويل مخزون — {entity.TransferNumber}", page =>
            DocumentBody(page, layout, col =>
            {
                InfoRow(col, new[]
                {
                    $"من: {entity.SourceWarehouse?.Name ?? "—"}",
                    $"إلى: {entity.TargetWarehouse?.Name ?? "—"}",
                    $"التاريخ: {entity.TransferDate:dd/MM/yyyy}"
                });
                var rows = entity.Items
                    .Select(i => TransferRow(layout, i.Item?.Name ?? "—", i.Item?.Code, i.Item?.Barcode, i.Count, i.Quantity, i.UnitCost))
                    .ToList();
                LineTable(col, layout, TransferColumns(layout), rows);
                DrawTotals(col, layout, new PdfTotals(
                    null, null, null, null, null, null,
                    entity.CreatedBy,
                    entity.Notes,
                    null));
            }));
    }

    public static byte[] RenderCustomerStatementPdf(string partyName, DateTime from, DateTime to, decimal opening, IReadOnlyList<StatementLine> lines, decimal closing, decimal? pendingValue = null)
    {
        var layout = ResolveLayout(PrintGroup.CustomerStatement);
        return Render(PrintGroup.CustomerStatement, layout, $"كشف حساب — {partyName}", page =>
            DocumentBody(page, layout, col =>
            {
                InfoRow(col, new[] { $"{partyName}", $"من: {from:dd/MM/yyyy}", $"إلى: {to:dd/MM/yyyy}" });
                StatementTable(col, layout, opening, lines, closing);
                if (pendingValue.HasValue)
                    TotalRow(col, layout, "تسليمات معلّقة (غير محسوبة في الرصيد)", Fmt(pendingValue.Value, layout.Decimals), false);
            }));
    }

    public static byte[] RenderSupplierStatementPdf(string partyName, DateTime from, DateTime to, decimal opening, IReadOnlyList<StatementLine> lines, decimal closing)
    {
        var layout = ResolveLayout(PrintGroup.SupplierStatement);
        return Render(PrintGroup.SupplierStatement, layout, $"كشف حساب — {partyName}", page =>
            DocumentBody(page, layout, col =>
            {
                InfoRow(col, new[] { $"{partyName}", $"من: {from:dd/MM/yyyy}", $"إلى: {to:dd/MM/yyyy}" });
                StatementTable(col, layout, opening, lines, closing);
            }));
    }

    public static byte[] RenderPreviewPdf(PrintGroup group, PrintLayoutOptions layout)
    {
        switch (group)
        {
            case PrintGroup.ItemLabel:
                return Render(group, layout, "ملصق الصنف", page =>
                    DocumentBody(page, layout, col =>
                    {
                        col.Item().PaddingTop(8).Border(1).BorderColor(Color.FromHex(AccentColor(layout))).Padding(12).Column(item =>
                        {
                            item.Item().Text("ITM-001").FontSize(10).FontColor(Colors.Grey.Darken1);
                            item.Item().PaddingTop(2).Text("منتج تجريبي أ").FontSize(16).Bold();
                            item.Item().PaddingTop(4).Text("الباركود: 6291041500213");
                            item.Item().PaddingTop(4).Text($"سعر البيع: {Fmt(150m, layout.Decimals)}");
                            item.Item().PaddingTop(2).Text($"سعر الشراء: {Fmt(110m, layout.Decimals)}");
                        });
                    }));
            case PrintGroup.FinancialReports:
                return Render(group, layout, "تقرير مالي تجريبي", page =>
                    page.Content().PaddingTop(10).Column(col =>
                    {
                        col.Item().Text("الإيرادات").FontSize(12).SemiBold();
                        SampleFinancialTable(col, layout, new (string Code, string Name, decimal Amount)[]
                        {
                            ("4101", "مبيعات المنتجات", 12500m),
                            ("4102", "الخدمات", 3200m),
                            ("4103", "مرتجعات المبيعات", -800m)
                        }, 14900m);
                        col.Item().PaddingTop(10).Text("المصروفات").FontSize(12).SemiBold();
                        SampleFinancialTable(col, layout, new (string Code, string Name, decimal Amount)[]
                        {
                            ("5101", "الرواتب", 6000m),
                            ("5102", "الإيجار", 2000m),
                            ("5103", "مصاريف تشغيل", 1200m)
                        }, 9200m);
                        col.Item().PaddingTop(12).Table(t =>
                        {
                            t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.ConstantColumn(90); });
                            t.Cell().Element(x => FooterCell(x, layout)).Text("صافي الدخل");
                            t.Cell().Element(x => FooterCell(x, layout)).AlignRight().Text(Fmt(5700m, layout.Decimals));
                        });
                    }));
            case PrintGroup.CustomerStatement:
            case PrintGroup.SupplierStatement:
                return RenderStatementPreview(group, layout);
            default:
                return RenderInvoicePreview(group, layout);
        }
    }

    private static byte[] RenderInvoicePreview(PrintGroup group, PrintLayoutOptions layout)
    {
        var party = group is PrintGroup.PurchaseInvoice or PrintGroup.PurchaseReturn or PrintGroup.PurchaseOrder ? "المورد" : "العميل";
        return Render(group, layout, GroupTitle(group) + " — تجريبي", page =>
            DocumentBody(page, layout, col =>
            {
                if (group == PrintGroup.StockTransfer)
                {
                    InfoRow(col, new[] { "من: مستودع المصدر", "إلى: مستودع الوجهة", "التاريخ: " + DateTime.Today.ToString("dd/MM/yyyy") });
                    var rows = new List<IReadOnlyList<string?>>
                    {
                        TransferRow(layout, "منتج تجريبي أ", "ITM-001", "6291041500213", 2m, 5.5m, 110m),
                        TransferRow(layout, "منتج تجريبي ب", "ITM-002", "6291041500220", 1m, 2m, 80m),
                        TransferRow(layout, "منتج تجريبي ج", "ITM-003", "6291041500237", 3m, 0m, 40m)
                    };
                    LineTable(col, layout, TransferColumns(layout), rows);
                    DrawTotals(col, layout, new PdfTotals(null, null, null, null, null, null, "أمجد", null, null));
                }
                else
                {
                    InfoRow(col, new[] { $"{party}: جهة تجريبية", "التاريخ: " + DateTime.Today.ToString("dd/MM/yyyy") });
                    var rows = new List<IReadOnlyList<string?>>
                    {
                        LineRow(layout, "منتج تجريبي أ", "ITM-001", "6291041500213", 2m, 5.5m, 150m, 0m, 300m),
                        LineRow(layout, "منتج تجريبي ب", "ITM-002", "6291041500220", 1m, 2m, 80m, 5m, 75m),
                        LineRow(layout, "منتج تجريبي ج", "ITM-003", "6291041500237", 3m, 0m, 40m, 0m, 120m)
                    };
                    LineTable(col, layout, InvoiceColumns(layout, true), rows);
                    DrawTotals(col, layout, PreviewTotals(layout, group));
                }
            }));
    }

    private static byte[] RenderStatementPreview(PrintGroup group, PrintLayoutOptions layout)
    {
        var party = group == PrintGroup.CustomerStatement ? "عميل تجريبي" : "مورد تجريبي";
        var lines = new List<StatementLine>
        {
            new(DateTime.Today.AddDays(-7), "فاتورة تجريبية", 900m, 0m),
            new(DateTime.Today.AddDays(-5), "قبض تجريبي", 0m, 300m),
            new(DateTime.Today.AddDays(-2), "مرتجع تجريبي", 0m, 100m)
        };
        return Render(group, layout, "كشف حساب — تجريبي", page =>
            DocumentBody(page, layout, col =>
            {
                InfoRow(col, new[]
                {
                    party,
                    "من: " + DateTime.Today.AddDays(-30).ToString("dd/MM/yyyy"),
                    "إلى: " + DateTime.Today.ToString("dd/MM/yyyy")
                });
                StatementTable(col, layout, 200m, lines, 700m);
            }));
    }

    private static PdfTotals PreviewTotals(PrintLayoutOptions layout, PrintGroup group)
    {
        var d = layout.Decimals;
        switch (group)
        {
            case PrintGroup.SaleReturn:
            case PrintGroup.PurchaseReturn:
                return new PdfTotals(null, null, null, Fmt(495m, d), null, null, "أمجد", "معاينة مرتجع", layout.ShowAmountInWords ? AmountInWords(495m) : null);
            case PrintGroup.PurchaseOrder:
                return new PdfTotals(null, null, null, Fmt(495m, d), null, null, "أمجد", "معاينة أمر شراء", layout.ShowAmountInWords ? AmountInWords(495m) : null);
            case PrintGroup.SalesQuote:
                return new PdfTotals(Fmt(495m, d), Fmt(25m, d), Fmt(23.5m, d), Fmt(493.5m, d), null, null, "أمجد", "معاينة عرض سعر", layout.ShowAmountInWords ? AmountInWords(493.5m) : null);
            default:
                return new PdfTotals(Fmt(495m, d), Fmt(25m, d), Fmt(23.5m, d), Fmt(493.5m, d), Fmt(200m, d), Fmt(293.5m, d), "أمجد", "معاينة فاتورة", layout.ShowAmountInWords ? AmountInWords(493.5m) : null);
        }
    }

    private static void SampleFinancialTable(ColumnDescriptor col, PrintLayoutOptions layout, IReadOnlyList<(string Code, string Name, decimal Amount)> rows, decimal total)
    {
        col.Item().PaddingTop(4).Table(t =>
        {
            t.ColumnsDefinition(cd => { cd.ConstantColumn(70); cd.RelativeColumn(2); cd.ConstantColumn(90); });
            t.Header(hd =>
            {
                hd.Cell().Element(x => HeaderCell(x, layout)).Text("الرمز");
                hd.Cell().Element(x => HeaderCell(x, layout)).Text("البند");
                hd.Cell().Element(x => HeaderCell(x, layout)).AlignRight().Text("المبلغ");
            });
            foreach (var r in rows)
            {
                t.Cell().Text(r.Code);
                t.Cell().Text(r.Name);
                t.Cell().AlignRight().Text(Fmt(r.Amount, layout.Decimals));
            }
            t.Cell().Element(x => FooterCell(x, layout)).Text("");
            t.Cell().Element(x => FooterCell(x, layout)).Text("الإجمالي");
            t.Cell().Element(x => FooterCell(x, layout)).AlignRight().Text(Fmt(total, layout.Decimals));
        });
    }

    private static void StatementTable(ColumnDescriptor col, PrintLayoutOptions layout, decimal opening, IReadOnlyList<StatementLine> lines, decimal closing)
    {
        col.Item().PaddingTop(8).Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                cd.RelativeColumn(1.1f);
                cd.RelativeColumn(2.1f);
                cd.RelativeColumn(1);
                cd.RelativeColumn(1);
                cd.RelativeColumn(1);
            });
            t.Header(hd =>
            {
                var headers = new (string Label, bool Right)[] { ("التاريخ", false), ("البيان", false), ("مدين", true), ("دائن", true), ("الرصيد", true) };
                foreach (var (label, right) in headers)
                {
                    if (right)
                        hd.Cell().Element(x => HeaderCell(x, layout)).AlignRight().Text(label);
                    else
                        hd.Cell().Element(x => HeaderCell(x, layout)).Text(label);
                }
            });
            t.Cell().Text("الرصيد الافتتاحي");
            t.Cell().Text("");
            t.Cell().AlignRight().Text("");
            t.Cell().AlignRight().Text("");
            t.Cell().AlignRight().Text(Fmt(opening, layout.Decimals));
            var running = opening;
            foreach (var line in lines)
            {
                running += line.Debit - line.Credit;
                t.Cell().Text(line.Date.ToString("dd/MM/yyyy"));
                t.Cell().Text(line.Description);
                t.Cell().AlignRight().Text(Fmt(line.Debit, layout.Decimals));
                t.Cell().AlignRight().Text(Fmt(line.Credit, layout.Decimals));
                t.Cell().AlignRight().Text(Fmt(running, layout.Decimals));
            }
            t.Cell().Element(x => FooterCell(x, layout)).Text("الرصيد الختامي");
            t.Cell().Element(x => FooterCell(x, layout)).Text("");
            t.Cell().Element(x => FooterCell(x, layout)).AlignRight().Text("");
            t.Cell().Element(x => FooterCell(x, layout)).AlignRight().Text("");
            t.Cell().Element(x => FooterCell(x, layout)).AlignRight().Text(Fmt(closing, layout.Decimals));
        });
    }

    private static void TotalRow(ColumnDescriptor col, PrintLayoutOptions layout, string label, string value, bool footer)
    {
        col.Item().PaddingTop(footer ? 3 : 2).Table(t =>
        {
            t.ColumnsDefinition(cd => { cd.RelativeColumn(3); cd.ConstantColumn(90); });
            if (footer)
            {
                t.Cell().Element(x => FooterCell(x, layout)).Text(label);
                t.Cell().Element(x => FooterCell(x, layout)).AlignRight().Text(value);
            }
            else
            {
                t.Cell().Text(label);
                t.Cell().AlignRight().Text(value);
            }
        });
    }

    private static void BuildHeader(PageDescriptor page, PrintLayoutOptions layout, CompanyProfile profile, string title, double scale, string accent)
    {
        page.Header().Column(col =>
        {
            if (layout.ShowLogo && profile.HasLogo && profile.LogoData is { Length: > 0 })
                col.Item().AlignCenter().Element(c => c.Height((float)(64 * layout.LogoScalePercent / 100d)).Image(profile.LogoData).FitHeight());
            if (layout.ShowCompanyName && !string.IsNullOrWhiteSpace(profile.CompanyName))
                col.Item().AlignCenter().Text(profile.CompanyName).FontSize((float)(18 * scale)).Bold();
            if (layout.ShowTagline && !string.IsNullOrWhiteSpace(profile.Tagline))
                col.Item().AlignCenter().Text(profile.Tagline).FontSize((float)(10 * scale));
            var contact = BuildContact(layout, profile);
            if (contact.Length > 0)
                col.Item().AlignCenter().Text(contact).FontSize((float)(8 * scale)).FontColor(Colors.Grey.Darken1);
            if (layout.ShowDocTitle)
            {
                col.Item().PaddingTop(4).AlignCenter().Text(title).FontSize((float)(13 * scale)).SemiBold().FontColor(Color.FromHex(accent));
                col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Color.FromHex(accent));
            }
            else
            {
                col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            }
        });
    }

    private static void BuildFooter(PageDescriptor page, PrintLayoutOptions layout, CompanyProfile profile)
    {
        if (!layout.ShowFooter && !layout.ShowPageNumbers) return;
        page.Footer().Column(col =>
        {
            if (layout.ShowFooter)
            {
                var note = string.IsNullOrWhiteSpace(layout.FooterNoteText)
                    ? (string.IsNullOrWhiteSpace(profile.CompanyName) ? "شكراً لتعاملكم معنا" : $"شكراً لتعاملكم معنا — {profile.CompanyName}")
                    : layout.FooterNoteText;
                col.Item().AlignCenter().Text(note).FontSize(8).FontColor(Colors.Grey.Darken1);
            }
            if (layout.ShowPageNumbers)
            {
                col.Item().AlignCenter().Text(x =>
                {
                    x.Span("صفحة ").FontSize(8);
                    x.CurrentPageNumber().FontSize(8);
                    x.Span(" من ").FontSize(8);
                    x.TotalPages().FontSize(8);
                });
            }
        });
    }

    private static string BuildContact(PrintLayoutOptions layout, CompanyProfile profile)
    {
        var contact = new List<string>(4);
        if (layout.ShowCompanyContact)
        {
            if (!string.IsNullOrWhiteSpace(profile.Address)) contact.Add(profile.Address);
            if (!string.IsNullOrWhiteSpace(profile.Phone)) contact.Add(profile.Phone);
            if (!string.IsNullOrWhiteSpace(profile.Email)) contact.Add(profile.Email);
        }
        if (layout.ShowTaxNumber && !string.IsNullOrWhiteSpace(profile.TaxNumber))
            contact.Add($"الرقم الضريبي: {profile.TaxNumber}");
        return string.Join(" • ", contact);
    }

    private static PageSize PageSizeOf(PrintLayoutOptions layout)
    {
        var size = layout.PageSize switch
        {
            PrintPageSize.A5 => PageSizes.A5,
            PrintPageSize.Letter => PageSizes.Letter,
            _ => PageSizes.A4
        };
        return layout.Orientation == PrintOrientation.Landscape ? size.Landscape() : size.Portrait();
    }

    private static float MarginOf(PrintLayoutOptions layout) => layout.Margin switch
    {
        PrintMarginSize.Narrow => 18f,
        PrintMarginSize.Wide => 45f,
        _ => 30f
    };

    private static PrintLayoutOptions FallbackLayout(PrintGroup group)
    {
        var o = new PrintLayoutOptions();
        switch (group)
        {
            case PrintGroup.SalesInvoice:
            case PrintGroup.PurchaseInvoice:
                o.ShowTagline = false;
                o.ShowCompanyContact = false;
                o.ShowDiscountColumn = false;
                break;
            case PrintGroup.SalesQuote:
                o.ShowBarcode = false;
                o.ShowTagline = false;
                o.ShowCompanyContact = false;
                break;
            case PrintGroup.ItemLabel:
            case PrintGroup.FinancialReports:
                o.ShowBarcode = false;
                o.ShowUnitPrice = false;
                o.ShowDiscountColumn = false;
                o.ShowCount = false;
                o.ShowQuantity = false;
                break;
            case PrintGroup.SaleReturn:
            case PrintGroup.PurchaseReturn:
                o.ShowTagline = false;
                o.ShowCompanyContact = false;
                break;
            case PrintGroup.PurchaseOrder:
                o.ShowTagline = false;
                o.ShowCompanyContact = false;
                o.ShowDiscountColumn = false;
                break;
            case PrintGroup.StockTransfer:
            case PrintGroup.CustomerStatement:
            case PrintGroup.SupplierStatement:
                o.ShowBarcode = false;
                o.ShowUnitPrice = false;
                o.ShowDiscountColumn = false;
                o.ShowCount = false;
                o.ShowQuantity = false;
                break;
        }
        return o;
    }

    private static string GroupTitle(PrintGroup group) => group switch
    {
        PrintGroup.SalesInvoice => "فاتورة بيع",
        PrintGroup.PurchaseInvoice => "فاتورة شراء",
        PrintGroup.SalesQuote => "عرض سعر",
        PrintGroup.ItemLabel => "ملصق الصنف",
        PrintGroup.SaleReturn => "مرتجع بيع",
        PrintGroup.PurchaseReturn => "مرتجع شراء",
        PrintGroup.PurchaseOrder => "أمر شراء",
        PrintGroup.StockTransfer => "تحويل مخزون",
        PrintGroup.CustomerStatement => "كشف حساب عميل",
        PrintGroup.SupplierStatement => "كشف حساب مورد",
        _ => "تقرير"
    };

    private static string NormalizeHex(string? hex, string fallback)
    {
        var value = (hex ?? "").Trim();
        if (value.Length == 0) return fallback;
        try
        {
            _ = ColorUtil.HexToRgb(value);
            return value.StartsWith('#') ? value : '#' + value;
        }
        catch
        {
            return fallback;
        }
    }

    private static string IntegerWords(long n)
    {
        var parts = new List<string>();
        long billions = n / 1000000000;
        long millions = (n / 1000000) % 1000;
        long thousands = (n / 1000) % 1000;
        long rest = n % 1000;
        if (billions > 0) parts.Add(UnderThousand((int)billions) + (billions == 1 ? " مليار" : billions == 2 ? " ملياران" : " مليارات"));
        if (millions > 0) parts.Add(UnderThousand((int)millions) + (millions == 1 ? " مليون" : millions == 2 ? " مليونان" : " ملايين"));
        if (thousands > 0) parts.Add(UnderThousand((int)thousands) + (thousands == 1 ? " ألف" : thousands == 2 ? " ألفان" : " ألفاً"));
        if (rest > 0) parts.Add(UnderThousand((int)rest));
        return string.Join(" و ", parts);
    }

    private static string UnderThousand(int n)
    {
        if (n <= 0) return "";
        if (n < 100) return UnderHundred(n);
        int hundreds = n / 100;
        int rest = n % 100;
        return rest == 0 ? ArabicHundreds[hundreds] : ArabicHundreds[hundreds] + " و" + UnderHundred(rest);
    }

    private static string UnderHundred(int n)
    {
        if (n < 0 || n > 99) return "";
        if (n < 20) return ArabicOnes[n];
        int ones = n % 10;
        int tens = n / 10;
        return ones == 0 ? ArabicTens[tens] : ArabicOnes[ones] + " و" + ArabicTens[tens];
    }

    private static readonly string[] ArabicOnes =
    [
        "", "واحد", "اثنان", "ثلاثة", "أربعة", "خمسة", "ستة", "سبعة", "ثمانية", "تسعة",
        "عشرة", "أحد عشر", "اثنا عشر", "ثلاثة عشر", "أربعة عشر", "خمسة عشر", "ستة عشر", "سبعة عشر", "ثمانية عشر", "تسعة عشر"
    ];

    private static readonly string[] ArabicTens = ["", "", "عشرون", "ثلاثون", "أربعون", "خمسون", "ستون", "سبعون", "ثمانون", "تسعون"];

    private static readonly string[] ArabicHundreds = ["", "مائة", "مائتان", "ثلاثمائة", "أربعمائة", "خمسمائة", "ستمائة", "سبعمائة", "ثمانمائة", "تسعمائة"];
}

public sealed record PdfColumn(string Header, bool Right, float Weight);

public sealed record PdfTotals(string? Total, string? TotalDiscount, string? TotalTax, string? GrandTotal, string? Paid, string? Remaining, string? CreatedBy, string? Notes, string? AmountInWords);

public sealed record StatementLine(DateTime Date, string Description, decimal Debit, decimal Credit);