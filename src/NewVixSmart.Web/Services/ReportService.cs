using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.ViewModels.Core;
using NewVixSmart.Web.ViewModels.Reports;

namespace NewVixSmart.Web.Services;

public class ReportService : IReportService
{
    private readonly AppDbContext _db;
    private readonly IFinancialReportService _financial;

    public ReportService(AppDbContext db, IFinancialReportService financial)
    {
        _db = db;
        _financial = financial;
    }

    // ---------- IReportService ----------

    public Task<TrialBalanceReportViewModel> TrialBalanceAsync(DateTime asOf) => _financial.TrialBalanceAsync(asOf);

    public Task<IncomeStatementReportViewModel> IncomeStatementAsync(DateTime from, DateTime to) => _financial.IncomeStatementAsync(from, to);

    public Task<BalanceSheetReportViewModel> BalanceSheetAsync(DateTime asOf) => _financial.BalanceSheetAsync(asOf);

    public async Task<DashboardReportViewModel> GetDashboardAsync()
    {
        var today = DateTime.Today;
        var vm = new DashboardReportViewModel { AsOf = today };

        var receivables = await _db.SaleInvoices
            .AsNoTracking()
            .Include(s => s.Customer)
            .Where(s => (s.DueDate ?? s.InvoiceDate).Date < today
                && s.PaidAmount < s.NetAmount)
            .ToListAsync();

        vm.OverdueReceivables = receivables.Select(s =>
        {
            var due = s.DueDate ?? s.InvoiceDate;
            return new OverdueInvoiceViewModel
            {
                Id = s.Id,
                InvoiceNumber = s.InvoiceNumber,
                PartyName = s.Customer?.Name ?? "—",
                InvoiceDate = s.InvoiceDate,
                DueDate = due,
                NetAmount = s.NetAmount,
                PaidAmount = s.PaidAmount
            };
        }).OrderByDescending(x => x.DaysOverdue).ToList();

        var payables = await _db.PurchaseInvoices
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Where(p => (p.DueDate ?? p.InvoiceDate).Date < today
                && p.PaidAmount < p.NetAmount)
            .ToListAsync();

        vm.OverduePayables = payables.Select(p =>
        {
            var due = p.DueDate ?? p.InvoiceDate;
            return new OverdueInvoiceViewModel
            {
                Id = p.Id,
                InvoiceNumber = p.InvoiceNumber,
                PartyName = p.Supplier?.Name ?? "—",
                InvoiceDate = p.InvoiceDate,
                DueDate = due,
                NetAmount = p.NetAmount,
                PaidAmount = p.PaidAmount
            };
        }).OrderByDescending(x => x.DaysOverdue).ToList();

        var lowStock = await _db.Items
            .AsNoTracking()
            .Where(i => i.IsActive
                && ((i.CountUnitId.HasValue && i.MinCount > 0 && i.CurrentCount <= i.MinCount)
                 || (i.QuantityUnitId.HasValue && i.MinQuantity > 0 && i.CurrentQuantity <= i.MinQuantity)))
            .Include(i => i.Category)
            .OrderBy(i => i.Name)
            .ToListAsync();

        vm.LowStockItems = lowStock.Select(i => new LowStockItemViewModel
        {
            Id = i.Id,
            Name = i.Name,
            Code = i.Code,
            Barcode = i.Barcode,
            CategoryName = i.Category?.Name ?? "—",
            CurrentCount = i.CurrentCount,
            CurrentQuantity = i.CurrentQuantity,
            MinCount = i.MinCount,
            MinQuantity = i.MinQuantity
        }).ToList();

        return vm;
    }

    // ---------- XLSX exports ----------

    public async Task<byte[]> ExportTrialBalanceXlsxAsync(DateTime asOf)
    {
        var vm = await TrialBalanceAsync(asOf);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("ميزان المراجعة");
        WriteReportHeading(ws, 1, $"ميزان المراجعة — حتى {asOf:dd/MM/yyyy}");
        ws.Range(3, 1, 3, 4).Style.Font.Bold = true;
        ws.Cell(3, 1).Value = "الرمز";
        ws.Cell(3, 2).Value = "الحساب";
        ws.Cell(3, 3).Value = "مدين";
        ws.Cell(3, 4).Value = "دائن";

        int row = 4;
        foreach (var r in vm.Rows)
        {
            ws.Cell(row, 1).Value = r.Code;
            ws.Cell(row, 2).Value = r.Name;
            ws.Cell(row, 3).Value = (double)r.Debit;
            ws.Cell(row, 4).Value = (double)r.Credit;
            row++;
        }
        ws.Cell(row, 2).Value = "الإجمالي";
        ws.Cell(row, 3).Value = (double)vm.TotalDebit;
        ws.Cell(row, 4).Value = (double)vm.TotalCredit;
        ws.Range(row, 1, row, 4).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportIncomeStatementXlsxAsync(DateTime from, DateTime to)
    {
        var vm = await IncomeStatementAsync(from, to);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("قائمة الدخل");
        WriteReportHeading(ws, 1, $"قائمة الدخل — من {from:dd/MM/yyyy} إلى {to:dd/MM/yyyy}");
        ws.Range(3, 1, 3, 3).Style.Font.Bold = true;
        ws.Cell(3, 1).Value = "الرمز";
        ws.Cell(3, 2).Value = "البند";
        ws.Cell(3, 3).Value = "المبلغ";

        int row = 4;
        foreach (var l in vm.RevenueLines) { ws.Cell(row, 1).Value = l.Code; ws.Cell(row, 2).Value = l.Name; ws.Cell(row, 3).Value = (double)l.Amount; row++; }
        foreach (var l in vm.ContraRevenueLines) { ws.Cell(row, 1).Value = l.Code; ws.Cell(row, 2).Value = l.Name; ws.Cell(row, 3).Value = -1 * (double)l.Amount; row++; }
        ws.Cell(row, 2).Value = "صافي الإيرادات";
        ws.Cell(row, 3).Value = (double)vm.NetRevenue;
        row += 2;
        foreach (var l in vm.ExpenseLines) { ws.Cell(row, 1).Value = l.Code; ws.Cell(row, 2).Value = l.Name; ws.Cell(row, 3).Value = (double)l.Amount; row++; }
        foreach (var l in vm.ContraExpenseLines) { ws.Cell(row, 1).Value = l.Code; ws.Cell(row, 2).Value = l.Name; ws.Cell(row, 3).Value = -1 * (double)l.Amount; row++; }
        ws.Cell(row, 2).Value = "صافي المصروفات";
        ws.Cell(row, 3).Value = (double)vm.NetExpenses;
        row += 2;
        ws.Cell(row, 2).Value = "صافي الدخل";
        ws.Cell(row, 3).Value = (double)vm.NetIncome;
        ws.Range(row, 1, row, 3).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportBalanceSheetXlsxAsync(DateTime asOf)
    {
        var vm = await BalanceSheetAsync(asOf);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("الميزانية العمومية");
        WriteReportHeading(ws, 1, $"الميزانية العمومية — حتى {asOf:dd/MM/yyyy}");
        ws.Range(3, 1, 3, 3).Style.Font.Bold = true;
        ws.Cell(3, 1).Value = "الرمز";
        ws.Cell(3, 2).Value = "الحساب";
        ws.Cell(3, 3).Value = "المبلغ";

        int row = 4;
        foreach (var l in vm.Assets.Lines) { ws.Cell(row, 1).Value = l.Code; ws.Cell(row, 2).Value = l.Name; ws.Cell(row, 3).Value = (double)l.Amount; row++; }
        ws.Cell(row, 2).Value = "إجمالي الأصول";
        ws.Cell(row, 3).Value = (double)vm.Assets.Total;
        row += 2;
        foreach (var l in vm.Liabilities.Lines) { ws.Cell(row, 1).Value = l.Code; ws.Cell(row, 2).Value = l.Name; ws.Cell(row, 3).Value = (double)l.Amount; row++; }
        ws.Cell(row, 2).Value = "إجمالي الخصوم";
        ws.Cell(row, 3).Value = (double)vm.Liabilities.Total;
        row += 2;
        foreach (var l in vm.Equity.Lines) { ws.Cell(row, 1).Value = l.Code; ws.Cell(row, 2).Value = l.Name; ws.Cell(row, 3).Value = (double)l.Amount; row++; }
        ws.Cell(row, 2).Value = "صافي الدخل (الفترة)";
        ws.Cell(row, 3).Value = (double)vm.NetIncome;
        row++;
        ws.Cell(row, 2).Value = "إجمالي حقوق الملكية";
        ws.Cell(row, 3).Value = (double)(vm.Equity.Total + vm.NetIncome);
        row++;
        ws.Cell(row, 2).Value = "إجمالي الخصوم + حقوق الملكية";
        ws.Cell(row, 3).Value = (double)(vm.Liabilities.Total + vm.Equity.Total + vm.NetIncome);
        row++;
        ws.Cell(row, 2).Value = "إجمالي الأصول";
        ws.Cell(row, 3).Value = (double)vm.Assets.Total;
        ws.Range(row - 1, 1, row, 3).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportItemsXlsxAsync()
    {
        var items = await _db.Items
            .AsNoTracking()
            .Include(i => i.Category)
            .Include(i => i.CountUnit)
            .Include(i => i.QuantityUnit)
            .Where(i => i.IsActive)
            .OrderBy(i => i.Name)
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("الأصناف وأرصدتها");
        ws.Range(1, 1, 1, 8).Style.Font.Bold = true;
        ws.Cell(1, 1).Value = "الكود";
        ws.Cell(1, 2).Value = "الاسم";
        ws.Cell(1, 3).Value = "التصنيف";
        ws.Cell(1, 4).Value = "الباركود";
        ws.Cell(1, 5).Value = "الرصيد (عدد)";
        ws.Cell(1, 6).Value = "الرصيد (كمية)";
        ws.Cell(1, 7).Value = "سعر البيع";
        ws.Cell(1, 8).Value = "سعر الشراء";

        int row = 2;
        foreach (var i in items)
        {
            ws.Cell(row, 1).Value = i.Code ?? "";
            ws.Cell(row, 2).Value = i.Name;
            ws.Cell(row, 3).Value = i.Category?.Name ?? "";
            ws.Cell(row, 4).Value = i.Barcode ?? "";
            ws.Cell(row, 5).Value = (double)i.CurrentCount;
            ws.Cell(row, 6).Value = (double)i.CurrentQuantity;
            ws.Cell(row, 7).Value = (double)i.SalePrice;
            ws.Cell(row, 8).Value = (double)i.PurchasePrice;
            row++;
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportStockXlsxAsync(bool lowOnly)
    {
        var items = await _db.Items
            .AsNoTracking()
            .Include(i => i.Category)
            .Include(i => i.CountUnit)
            .Include(i => i.QuantityUnit)
            .Where(i => i.IsActive)
            .ToListAsync();

        if (lowOnly)
            items = items.Where(i => (i.CountUnitId.HasValue && i.MinCount > 0 && i.CurrentCount <= i.MinCount)
                || (i.QuantityUnitId.HasValue && i.MinQuantity > 0 && i.CurrentQuantity <= i.MinQuantity)).ToList();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(lowOnly ? "انخفاض المخزون" : "لقطة المخزون");
        ws.Range(1, 1, 1, 8).Style.Font.Bold = true;
        ws.Cell(1, 1).Value = "الكود";
        ws.Cell(1, 2).Value = "الاسم";
        ws.Cell(1, 3).Value = "التصنيف";
        ws.Cell(1, 4).Value = "الرصيد (عدد)";
        ws.Cell(1, 5).Value = "الحد الأدنى (عدد)";
        ws.Cell(1, 6).Value = "الرصيد (كمية)";
        ws.Cell(1, 7).Value = "الحد الأدنى (كمية)";
        ws.Cell(1, 8).Value = "قيمة المخزون (سعر الشراء)";

        int row = 2;
        foreach (var i in items)
        {
            decimal value = (i.QuantityUnitId.HasValue || i.CurrentQuantity > 0)
                ? i.CurrentQuantity * i.PurchasePrice
                : i.CurrentCount * i.PurchasePrice;
            ws.Cell(row, 1).Value = i.Code ?? "";
            ws.Cell(row, 2).Value = i.Name;
            ws.Cell(row, 3).Value = i.Category?.Name ?? "";
            ws.Cell(row, 4).Value = (double)i.CurrentCount;
            ws.Cell(row, 5).Value = (double)i.MinCount;
            ws.Cell(row, 6).Value = (double)i.CurrentQuantity;
            ws.Cell(row, 7).Value = (double)i.MinQuantity;
            ws.Cell(row, 8).Value = (double)value;
            row++;
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportSalesXlsxAsync(DateTime from, DateTime to)
    {
        var invoices = await _db.SaleInvoices
            .AsNoTracking()
            .Include(s => s.Customer)
            .Where(s => s.InvoiceDate >= from && s.InvoiceDate <= to)
            .OrderByDescending(s => s.InvoiceDate)
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("المبيعات");
        ws.Range(1, 1, 1, 7).Style.Font.Bold = true;
        ws.Cell(1, 1).Value = "الفاتورة";
        ws.Cell(1, 2).Value = "العميل";
        ws.Cell(1, 3).Value = "التاريخ";
        ws.Cell(1, 4).Value = "الإجمالي";
        ws.Cell(1, 5).Value = "الخصم";
        ws.Cell(1, 6).Value = "الضريبة";
        ws.Cell(1, 7).Value = "الصافي";

        int row = 2;
        foreach (var i in invoices)
        {
            ws.Cell(row, 1).Value = i.InvoiceNumber;
            ws.Cell(row, 2).Value = i.Customer?.Name ?? "";
            ws.Cell(row, 3).Value = i.InvoiceDate.ToString("dd/MM/yyyy");
            ws.Cell(row, 4).Value = (double)i.TotalAmount;
            ws.Cell(row, 5).Value = (double)(i.Discount + (i.Discount2 ?? 0) + (i.Discount3 ?? 0));
            ws.Cell(row, 6).Value = (double)i.Tax;
            ws.Cell(row, 7).Value = (double)i.NetAmount;
            row++;
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportPurchasesXlsxAsync(DateTime from, DateTime to)
    {
        var invoices = await _db.PurchaseInvoices
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Where(p => p.InvoiceDate >= from && p.InvoiceDate <= to)
            .OrderByDescending(p => p.InvoiceDate)
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("المشتريات");
        ws.Range(1, 1, 1, 7).Style.Font.Bold = true;
        ws.Cell(1, 1).Value = "الفاتورة";
        ws.Cell(1, 2).Value = "المورد";
        ws.Cell(1, 3).Value = "التاريخ";
        ws.Cell(1, 4).Value = "الإجمالي";
        ws.Cell(1, 5).Value = "الخصم";
        ws.Cell(1, 6).Value = "الضريبة";
        ws.Cell(1, 7).Value = "الصافي";

        int row = 2;
        foreach (var i in invoices)
        {
            ws.Cell(row, 1).Value = i.InvoiceNumber;
            ws.Cell(row, 2).Value = i.Supplier?.Name ?? "";
            ws.Cell(row, 3).Value = i.InvoiceDate.ToString("dd/MM/yyyy");
            ws.Cell(row, 4).Value = (double)i.TotalAmount;
            ws.Cell(row, 5).Value = (double)(i.Discount + (i.Discount2 ?? 0) + (i.Discount3 ?? 0));
            ws.Cell(row, 6).Value = (double)i.Tax;
            ws.Cell(row, 7).Value = (double)i.NetAmount;
            row++;
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportPaymentsXlsxAsync(DateTime from, DateTime to)
    {
        var payments = await _db.Payments
            .AsNoTracking()
            .Include(p => p.Customer)
            .Include(p => p.Supplier)
            .Where(p => p.PaymentDate >= from && p.PaymentDate <= to)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("المدفوعات");
        ws.Range(1, 1, 1, 5).Style.Font.Bold = true;
        ws.Cell(1, 1).Value = "الإيصال";
        ws.Cell(1, 2).Value = "النوع";
        ws.Cell(1, 3).Value = "العميل/المورد";
        ws.Cell(1, 4).Value = "المبلغ";
        ws.Cell(1, 5).Value = "التاريخ";

        int row = 2;
        foreach (var p in payments)
        {
            ws.Cell(row, 1).Value = p.ReceiptNumber;
            ws.Cell(row, 2).Value = p.Type == Models.Accounting.PaymentType.Receipt ? "قبض" : "صرف";
            ws.Cell(row, 3).Value = p.Customer?.Name ?? p.Supplier?.Name ?? "";
            ws.Cell(row, 4).Value = (double)p.Amount;
            ws.Cell(row, 5).Value = p.PaymentDate.ToString("dd/MM/yyyy");
            row++;
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportAuditLedgerXlsxAsync(DateTime? from, DateTime? to, int? accountId, JournalSource? source)
    {
        var today = DateTime.Today;
        from = (from ?? new DateTime(today.Year, today.Month, 1)).Date;
        to = (to ?? today).Date;

        var entryQuery = _db.JournalEntries
            .AsNoTracking()
            .Include(j => j.Lines)
            .ThenInclude(l => l.Account)
            .Where(j => j.IsPosted && j.Date >= from.Value && j.Date <= to.Value);
        if (source is not null)
            entryQuery = entryQuery.Where(j => j.Source == source);
        if (accountId is not null)
            entryQuery = entryQuery.Where(j => j.Lines.Any(l => l.AccountId == accountId));

        var entries = await entryQuery.OrderBy(j => j.EntryNumber).ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("سجل التدقيق");
        WriteReportHeading(ws, 1, $"سجل التدقيق المركزي — من {from:dd/MM/yyyy} إلى {to:dd/MM/yyyy}");
        ws.Range(3, 1, 3, 11).Style.Font.Bold = true;
        ws.Cell(3, 1).Value = "رقم القيد";
        ws.Cell(3, 2).Value = "التاريخ";
        ws.Cell(3, 3).Value = "المصدر";
        ws.Cell(3, 4).Value = "معرف المستند";
        ws.Cell(3, 5).Value = "البيان";
        ws.Cell(3, 6).Value = "رمز الحساب";
        ws.Cell(3, 7).Value = "اسم الحساب";
        ws.Cell(3, 8).Value = "مدين";
        ws.Cell(3, 9).Value = "دائن";
        ws.Cell(3, 10).Value = "الفرع";
        ws.Cell(3, 11).Value = "أنشئ بواسطة";

        int row = 4;
        foreach (var e in entries)
        {
            foreach (var l in e.Lines)
            {
                ws.Cell(row, 1).Value = e.EntryNumber;
                ws.Cell(row, 2).Value = e.Date.ToString("dd/MM/yyyy");
                ws.Cell(row, 3).Value = e.Source.GetDisplayName();
                ws.Cell(row, 4).Value = e.SourceId;
                ws.Cell(row, 5).Value = e.Description;
                ws.Cell(row, 6).Value = l.Account?.Code ?? "";
                ws.Cell(row, 7).Value = l.Account?.Name ?? "";
                ws.Cell(row, 8).Value = (double)l.Debit;
                ws.Cell(row, 9).Value = (double)l.Credit;
                ws.Cell(row, 10).Value = e.BranchId?.ToString() ?? "";
                ws.Cell(row, 11).Value = e.CreatedBy ?? "";
                row++;
            }
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ---------- PDF exports ----------

    public async Task<byte[]> ExportTrialBalancePdfAsync(DateTime asOf)
    {
        var vm = await TrialBalanceAsync(asOf);
        var layout = PrintPdfBuilder.ResolveLayout(PrintGroup.FinancialReports);
        return BuildFinancialPdf($"ميزان المراجعة — حتى {asOf:dd/MM/yyyy}", layout, doc =>
        {
            doc.Column(c =>
            {
                c.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.ConstantColumn(70); cd.RelativeColumn(2); cd.ConstantColumn(90); cd.ConstantColumn(90); });
                    t.Header(hd =>
                    {
                        hd.Cell().Element(x => BoldHeader(x, layout)).Text("الرمز");
                        hd.Cell().Element(x => BoldHeader(x, layout)).Text("الحساب");
                        hd.Cell().Element(x => BoldHeader(x, layout)).Text("مدين");
                        hd.Cell().Element(x => BoldHeader(x, layout)).Text("دائن");
                    });
                    foreach (var r in vm.Rows)
                    {
                        t.Cell().Text(r.Code);
                        t.Cell().Text(r.Name);
                        t.Cell().AlignRight().Text(PrintPdfBuilder.Fmt(r.Debit, layout.Decimals));
                        t.Cell().AlignRight().Text(PrintPdfBuilder.Fmt(r.Credit, layout.Decimals));
                    }
                    t.Cell().Element(x => BoldFooter(x, layout)).Text("الإجمالي");
                    t.Cell().Element(x => BoldFooter(x, layout)).Text("");
                    t.Cell().Element(x => BoldFooter(x, layout)).AlignRight().Text(PrintPdfBuilder.Fmt(vm.TotalDebit, layout.Decimals));
                    t.Cell().Element(x => BoldFooter(x, layout)).AlignRight().Text(PrintPdfBuilder.Fmt(vm.TotalCredit, layout.Decimals));
                });
            });
        });
    }

    public async Task<byte[]> ExportIncomeStatementPdfAsync(DateTime from, DateTime to)
    {
        var vm = await IncomeStatementAsync(from, to);
        var layout = PrintPdfBuilder.ResolveLayout(PrintGroup.FinancialReports);
        return BuildFinancialPdf($"قائمة الدخل — من {from:dd/MM/yyyy} إلى {to:dd/MM/yyyy}", layout, doc =>
        {
            doc.Column(c =>
            {
                c.Item().Text("الايرادات").FontSize(12).SemiBold();
                c.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.ConstantColumn(70); cd.RelativeColumn(2); cd.ConstantColumn(90); });
                    t.Header(hd =>
                    {
                        hd.Cell().Element(x => BoldHeader(x, layout)).Text("الرمز");
                        hd.Cell().Element(x => BoldHeader(x, layout)).Text("البند");
                        hd.Cell().Element(x => BoldHeader(x, layout)).AlignRight().Text("المبلغ");
                    });
                    foreach (var l in vm.RevenueLines) { t.Cell().Text(l.Code); t.Cell().Text(l.Name); t.Cell().AlignRight().Text(PrintPdfBuilder.Fmt(l.Amount, layout.Decimals)); }
                    foreach (var l in vm.ContraRevenueLines) { t.Cell().Text(l.Code); t.Cell().Text(l.Name + " (خصم)"); t.Cell().AlignRight().Text(PrintPdfBuilder.Fmt(-l.Amount, layout.Decimals)); }
                    t.Cell().Element(x => BoldFooter(x, layout)).Text("");
                    t.Cell().Element(x => BoldFooter(x, layout)).Text("صافي الإيرادات");
                    t.Cell().Element(x => BoldFooter(x, layout)).AlignRight().Text(PrintPdfBuilder.Fmt(vm.NetRevenue, layout.Decimals));
                });
                c.Item().PaddingTop(12).Text("المصروفات").FontSize(12).SemiBold();
                c.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.ConstantColumn(70); cd.RelativeColumn(2); cd.ConstantColumn(90); });
                    t.Header(hd =>
                    {
                        hd.Cell().Element(x => BoldHeader(x, layout)).Text("الرمز");
                        hd.Cell().Element(x => BoldHeader(x, layout)).Text("البند");
                        hd.Cell().Element(x => BoldHeader(x, layout)).AlignRight().Text("المبلغ");
                    });
                    foreach (var l in vm.ExpenseLines) { t.Cell().Text(l.Code); t.Cell().Text(l.Name); t.Cell().AlignRight().Text(PrintPdfBuilder.Fmt(l.Amount, layout.Decimals)); }
                    foreach (var l in vm.ContraExpenseLines) { t.Cell().Text(l.Code); t.Cell().Text(l.Name + " (خصم)"); t.Cell().AlignRight().Text(PrintPdfBuilder.Fmt(-l.Amount, layout.Decimals)); }
                    t.Cell().Element(x => BoldFooter(x, layout)).Text("");
                    t.Cell().Element(x => BoldFooter(x, layout)).Text("صافي المصروفات");
                    t.Cell().Element(x => BoldFooter(x, layout)).AlignRight().Text(PrintPdfBuilder.Fmt(vm.NetExpenses, layout.Decimals));
                });
                c.Item().PaddingTop(12).Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.ConstantColumn(90); });
                    t.Cell().Element(x => BoldFooter(x, layout)).Text("صافي الدخل");
                    t.Cell().Element(x => BoldFooter(x, layout)).AlignRight().Text(PrintPdfBuilder.Fmt(vm.NetIncome, layout.Decimals));
                });
            });
        });
    }

    public async Task<byte[]> ExportBalanceSheetPdfAsync(DateTime asOf)
    {
        var vm = await BalanceSheetAsync(asOf);
        var layout = PrintPdfBuilder.ResolveLayout(PrintGroup.FinancialReports);
        return BuildFinancialPdf($"الميزانية العمومية — حتى {asOf:dd/MM/yyyy}", layout, doc =>
        {
            doc.Column(c =>
            {
                c.Item().Text("الأصول").FontSize(12).SemiBold();
                c.Item().Table(t => BalanceSheetSection(t, layout, vm.Assets, vm.Assets.Total));
                c.Item().PaddingTop(12).Text("الخصوم").FontSize(12).SemiBold();
                c.Item().Table(t => BalanceSheetSection(t, layout, vm.Liabilities, vm.Liabilities.Total));
                c.Item().PaddingTop(12).Text("حقوق الملكية").FontSize(12).SemiBold();
                c.Item().Table(t => BalanceSheetSection(t, layout, vm.Equity, vm.Equity.Total));
                c.Item().PaddingTop(12).Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.ConstantColumn(90); });
                    t.Cell().Element(x => BoldFooter(x, layout)).Text("صافي الدخل (الفترة)");
                    t.Cell().Element(x => BoldFooter(x, layout)).AlignRight().Text(PrintPdfBuilder.Fmt(vm.NetIncome, layout.Decimals));
                });
                c.Item().PaddingTop(6).Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.ConstantColumn(90); });
                    t.Cell().Element(x => BoldFooter(x, layout)).Text("إجمالي الخصوم + حقوق الملكية");
                    t.Cell().Element(x => BoldFooter(x, layout)).AlignRight().Text(PrintPdfBuilder.Fmt(vm.TotalLiabilitiesEquity, layout.Decimals));
                });
            });
        });
    }

    private static void BalanceSheetSection(TableDescriptor t, PrintLayoutOptions layout, BalanceSheetSectionViewModel section, decimal total)
    {
        t.ColumnsDefinition(cd => { cd.ConstantColumn(70); cd.RelativeColumn(2); cd.ConstantColumn(90); });
        t.Header(hd =>
        {
            hd.Cell().Element(x => BoldHeader(x, layout)).Text("الرمز");
            hd.Cell().Element(x => BoldHeader(x, layout)).Text("الحساب");
            hd.Cell().Element(x => BoldHeader(x, layout)).AlignRight().Text("المبلغ");
        });
        foreach (var l in section.Lines)
        {
            t.Cell().Text(l.Code);
            t.Cell().Text(l.Name);
            t.Cell().AlignRight().Text(PrintPdfBuilder.Fmt(l.Amount, layout.Decimals));
        }
        t.Cell().Element(x => BoldFooter(x, layout)).Text("");
        t.Cell().Element(x => BoldFooter(x, layout)).Text("الإجمالي");
        t.Cell().Element(x => BoldFooter(x, layout)).AlignRight().Text(PrintPdfBuilder.Fmt(total, layout.Decimals));
    }

    private byte[] BuildFinancialPdf(string title, PrintLayoutOptions layout, Action<IContainer> content)
    {
        return PrintPdfBuilder.Render(PrintGroup.FinancialReports, layout, title, page =>
            page.Content().PaddingTop(10).Element(content));
    }

    private static IContainer BoldHeader(IContainer c, PrintLayoutOptions layout) => PrintPdfBuilder.HeaderCell(c, layout);
    private static IContainer BoldFooter(IContainer c, PrintLayoutOptions layout) => PrintPdfBuilder.FooterCell(c, layout);

    private static void WriteReportHeading(IXLWorksheet ws, int row, string title)
    {
        ws.Cell(row, 1).Value = title;
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 14;
    }

    public async Task<byte[]> ExportBudgetVarianceXlsxAsync(int year, IReadOnlyList<(string Code, string Name, decimal Budget, decimal Actual, decimal Variance, decimal VariancePct)> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("واريانس الميزانية");
        WriteReportHeading(ws, 1, $"واريانس الميزانية — سنة {year}");
        ws.Range(3, 1, 3, 6).Style.Font.Bold = true;
        ws.Cell(3, 1).Value = "رمز الحساب";
        ws.Cell(3, 2).Value = "اسم الحساب";
        ws.Cell(3, 3).Value = "الميزانية";
        ws.Cell(3, 4).Value = "الفعلي";
        ws.Cell(3, 5).Value = "الواريانس";
        ws.Cell(3, 6).Value = "النسبة %";

        int row = 4;
        foreach (var r in rows)
        {
            ws.Cell(row, 1).Value = r.Code;
            ws.Cell(row, 2).Value = r.Name;
            ws.Cell(row, 3).Value = (double)r.Budget;
            ws.Cell(row, 4).Value = (double)r.Actual;
            ws.Cell(row, 5).Value = (double)r.Variance;
            ws.Cell(row, 6).Value = (double)r.VariancePct;
            ws.Cell(row, 6).Style.NumberFormat.Format = "0.0%";
            row++;
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return await Task.FromResult(ms.ToArray());
    }

    public async Task<AgingReportViewModel> AgingAsync()
    {
        var today = DateTime.Today;
        var vm = new AgingReportViewModel { AsOf = today };

        var saleInvoices = await _db.SaleInvoices
            .AsNoTracking()
            .Include(s => s.Customer)
            .Where(s => s.PaidAmount < s.NetAmount)
            .Where(s => _db.DeliveryOrders.Any(d => d.SaleInvoiceId == s.Id && d.Status == NewVixSmart.Web.Models.Sales.DeliveryOrderStatus.Delivered))
            .ToListAsync();

        var saleReturns = await _db.SaleReturns
            .AsNoTracking()
            .Where(r => r.Status == ReturnStatus.Posted)
            .Select(r => new { r.SaleInvoiceId, r.TotalAmount, r.ExchangeRate })
            .ToListAsync();
        var returnsBySaleInvoice = saleReturns
            .Where(r => r.SaleInvoiceId.HasValue)
            .GroupBy(r => r.SaleInvoiceId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(r => decimal.Round(r.TotalAmount * (r.ExchangeRate ?? 1m), 2)));

        var saleAllocations = await _db.PaymentAllocations
            .AsNoTracking()
            .Where(a => a.InvoiceType == PaymentAllocationInvoiceType.Sales)
            .Select(a => new { a.InvoiceId, a.AllocatedBaseAmount })
            .ToListAsync();
        var allocatedBySale = saleAllocations
            .GroupBy(a => a.InvoiceId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.AllocatedBaseAmount));

        var receivableLines = new List<(int PartyId, string Name, DateTime Due, decimal Amount)>();
        foreach (var s in saleInvoices)
        {
            var rate = s.ExchangeRate ?? 1m;
            var paidBase = allocatedBySale.GetValueOrDefault(s.Id);
            if (paidBase <= 0m && s.PaidAmount > 0m)
                paidBase = decimal.Round(s.PaidAmount * rate, 2);
            var outstanding = decimal.Round(s.NetAmount * rate, 2)
                - paidBase
                - returnsBySaleInvoice.GetValueOrDefault(s.Id);
            if (outstanding > 0.005m)
                receivableLines.Add((s.CustomerId, s.Customer?.Name ?? "—", s.DueDate ?? s.InvoiceDate, outstanding));
        }

        var standaloneSaleReturns = await _db.SaleReturns
            .AsNoTracking()
            .Include(r => r.Customer)
            .Where(r => r.Status == ReturnStatus.Posted && r.SaleInvoiceId == null)
            .ToListAsync();
        receivableLines.AddRange(standaloneSaleReturns.Select(r =>
            (r.CustomerId, r.Customer?.Name ?? "—", r.ReturnDate, -decimal.Round(r.TotalAmount * (r.ExchangeRate ?? 1m), 2))));

        var customerOpenings = await _db.Customers.AsNoTracking()
            .Where(c => c.OpeningBalance != 0m)
            .Select(c => new { c.Id, c.Name, c.OpeningBalance })
            .ToListAsync();
        receivableLines.AddRange(customerOpenings.Select(o =>
            (o.Id, string.IsNullOrWhiteSpace(o.Name) ? "—" : o.Name, DateTime.MinValue, o.OpeningBalance)));

        vm.Receivables = receivableLines
            .GroupBy(x => x.PartyId)
            .Select(g => BuildAgingRow(g.First().Name, g.Select(x => (x.Due, x.Amount)), today))
            .Where(r => r.Total > 0.005m)
            .OrderByDescending(r => r.Total)
            .ToList();

        var purchaseInvoices = await _db.PurchaseInvoices
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Where(p => p.PaidAmount < p.NetAmount)
            .ToListAsync();

        var purchaseReturns = await _db.PurchaseReturns
            .AsNoTracking()
            .Where(r => r.Status == ReturnStatus.Posted)
            .Select(r => new { r.PurchaseInvoiceId, r.TotalAmount, r.ExchangeRate })
            .ToListAsync();
        var returnsByPurchaseInvoice = purchaseReturns
            .Where(r => r.PurchaseInvoiceId.HasValue)
            .GroupBy(r => r.PurchaseInvoiceId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(r => decimal.Round(r.TotalAmount * (r.ExchangeRate ?? 1m), 2)));

        var purchaseAllocations = await _db.PaymentAllocations
            .AsNoTracking()
            .Where(a => a.InvoiceType == PaymentAllocationInvoiceType.Purchases)
            .Select(a => new { a.InvoiceId, a.AllocatedBaseAmount })
            .ToListAsync();
        var allocatedByPurchase = purchaseAllocations
            .GroupBy(a => a.InvoiceId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.AllocatedBaseAmount));

        var payableLines = new List<(int PartyId, string Name, DateTime Due, decimal Amount)>();
        foreach (var p in purchaseInvoices)
        {
            var rate = p.ExchangeRate ?? 1m;
            var paidBase = allocatedByPurchase.GetValueOrDefault(p.Id);
            if (paidBase <= 0m && p.PaidAmount > 0m)
                paidBase = decimal.Round(p.PaidAmount * rate, 2);
            var outstanding = decimal.Round(p.NetAmount * rate, 2)
                - paidBase
                - returnsByPurchaseInvoice.GetValueOrDefault(p.Id);
            if (outstanding > 0.005m)
                payableLines.Add((p.SupplierId, p.Supplier?.Name ?? "—", p.DueDate ?? p.InvoiceDate, outstanding));
        }

        var standalonePurchaseReturns = await _db.PurchaseReturns
            .AsNoTracking()
            .Include(r => r.Supplier)
            .Where(r => r.Status == ReturnStatus.Posted && r.PurchaseInvoiceId == null)
            .ToListAsync();
        payableLines.AddRange(standalonePurchaseReturns.Select(r =>
            (r.SupplierId, r.Supplier?.Name ?? "—", r.ReturnDate, -decimal.Round(r.TotalAmount * (r.ExchangeRate ?? 1m), 2))));

        var supplierOpenings = await _db.Suppliers.AsNoTracking()
            .Where(s => s.OpeningBalance != 0m)
            .Select(s => new { s.Id, s.Name, s.OpeningBalance })
            .ToListAsync();
        payableLines.AddRange(supplierOpenings.Select(o =>
            (o.Id, string.IsNullOrWhiteSpace(o.Name) ? "—" : o.Name, DateTime.MinValue, o.OpeningBalance)));

        vm.Payables = payableLines
            .GroupBy(x => x.PartyId)
            .Select(g => BuildAgingRow(g.First().Name, g.Select(x => (x.Due, x.Amount)), today))
            .Where(r => r.Total > 0.005m)
            .OrderByDescending(r => r.Total)
            .ToList();

        return vm;
    }

    private static AgingBucketRow BuildAgingRow(string name, IEnumerable<(DateTime Due, decimal Amount)> items, DateTime asOf)
    {
        var row = new AgingBucketRow { PartyName = name };
        foreach (var (due, amount) in items)
        {
            var days = (asOf.Date - due.Date).TotalDays;
            if (days <= 0) row.Current += amount;
            else if (days <= 30) row.Days1To30 += amount;
            else if (days <= 60) row.Days31To60 += amount;
            else if (days <= 90) row.Days61To90 += amount;
            else row.Days90Plus += amount;
        }
        return row;
    }

    public async Task<byte[]> ExportAgingXlsxAsync()
    {
        var vm = await AgingAsync();
        using var wb = new XLWorkbook();

        var ar = wb.Worksheets.Add("ذمم العملاء");
        WriteReportHeading(ar, 1, $"القائمة العمرية — ذمم العملاء حتى {vm.AsOf:dd/MM/yyyy}");
        ar.Range(3, 1, 3, 7).Style.Font.Bold = true;
        ar.Cell(3, 1).Value = "العميل";
        ar.Cell(3, 2).Value = "لم يستحق";
        ar.Cell(3, 3).Value = "1-30 يوم";
        ar.Cell(3, 4).Value = "31-60 يوم";
        ar.Cell(3, 5).Value = "61-90 يوم";
        ar.Cell(3, 6).Value = "أكثر من 90";
        ar.Cell(3, 7).Value = "الإجمالي";
        int arRow = 4;
        foreach (var row in vm.Receivables)
        {
            ar.Cell(arRow, 1).Value = row.PartyName;
            ar.Cell(arRow, 2).Value = (double)row.Current;
            ar.Cell(arRow, 3).Value = (double)row.Days1To30;
            ar.Cell(arRow, 4).Value = (double)row.Days31To60;
            ar.Cell(arRow, 5).Value = (double)row.Days61To90;
            ar.Cell(arRow, 6).Value = (double)row.Days90Plus;
            ar.Cell(arRow, 7).Value = (double)row.Total;
            arRow++;
        }
        ar.Cell(arRow, 1).Value = "الإجمالي";
        ar.Cell(arRow, 1).Style.Font.Bold = true;
        ar.Cell(arRow, 2).Value = (double)vm.ArCurrent;
        ar.Cell(arRow, 3).Value = (double)vm.ArDays1To30;
        ar.Cell(arRow, 4).Value = (double)vm.ArDays31To60;
        ar.Cell(arRow, 5).Value = (double)vm.ArDays61To90;
        ar.Cell(arRow, 6).Value = (double)vm.ArDays90Plus;
        ar.Cell(arRow, 7).Value = (double)vm.ArTotal;
        ar.Cell(arRow, 7).Style.Font.Bold = true;
        ar.Columns().AdjustToContents();

        var ap = wb.Worksheets.Add("ذمم الموردين");
        WriteReportHeading(ap, 1, $"القائمة العمرية — ذمم الموردين حتى {vm.AsOf:dd/MM/yyyy}");
        ap.Range(3, 1, 3, 7).Style.Font.Bold = true;
        ap.Cell(3, 1).Value = "المورد";
        ap.Cell(3, 2).Value = "لم يستحق";
        ap.Cell(3, 3).Value = "1-30 يوم";
        ap.Cell(3, 4).Value = "31-60 يوم";
        ap.Cell(3, 5).Value = "61-90 يوم";
        ap.Cell(3, 6).Value = "أكثر من 90";
        ap.Cell(3, 7).Value = "الإجمالي";
        int apRow = 4;
        foreach (var row in vm.Payables)
        {
            ap.Cell(apRow, 1).Value = row.PartyName;
            ap.Cell(apRow, 2).Value = (double)row.Current;
            ap.Cell(apRow, 3).Value = (double)row.Days1To30;
            ap.Cell(apRow, 4).Value = (double)row.Days31To60;
            ap.Cell(apRow, 5).Value = (double)row.Days61To90;
            ap.Cell(apRow, 6).Value = (double)row.Days90Plus;
            ap.Cell(apRow, 7).Value = (double)row.Total;
            apRow++;
        }
        ap.Cell(apRow, 1).Value = "الإجمالي";
        ap.Cell(apRow, 1).Style.Font.Bold = true;
        ap.Cell(apRow, 2).Value = (double)vm.ApCurrent;
        ap.Cell(apRow, 3).Value = (double)vm.ApDays1To30;
        ap.Cell(apRow, 4).Value = (double)vm.ApDays31To60;
        ap.Cell(apRow, 5).Value = (double)vm.ApDays61To90;
        ap.Cell(apRow, 6).Value = (double)vm.ApDays90Plus;
        ap.Cell(apRow, 7).Value = (double)vm.ApTotal;
        ap.Cell(apRow, 7).Style.Font.Bold = true;
        ap.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<CashFlowReportViewModel> CashFlowAsync(DateTime from, DateTime to)
    {
        var fromDate = from.Date;
        var toDate = to.Date;

        var payments = await _db.Payments
            .AsNoTracking()
            .Include(p => p.Customer)
            .Include(p => p.Supplier)
            .OrderBy(p => p.PaymentDate).ThenBy(p => p.Id)
            .ToListAsync();

        var onReceiptPurchases = (await _db.PurchaseInvoices
            .AsNoTracking()
            .Where(p => p.PaymentTerms == InvoicePaymentTerms.OnReceipt && p.PaidAmount > 0)
            .Select(p => new { p.InvoiceDate, p.NetAmount, p.ExchangeRate })
            .ToListAsync())
            .Select(p => new { p.InvoiceDate, Base = decimal.Round(p.NetAmount * (p.ExchangeRate ?? 1m), 2) })
            .ToList();

        var vm = new CashFlowReportViewModel { From = fromDate, To = toDate };

        foreach (var p in payments)
        {
            if (p.PaymentDate >= fromDate) break;
            var amount = p.BaseAmount > 0 ? p.BaseAmount : p.Amount;
            vm.OpeningBalance += p.Type == PaymentType.Receipt ? amount : -amount;
        }
        foreach (var s in onReceiptPurchases)
        {
            if (s.InvoiceDate < fromDate) vm.OpeningBalance -= s.Base;
        }

        var period = payments.Where(p => p.PaymentDate >= fromDate && p.PaymentDate <= toDate).ToList();
        vm.Payments = period;
        foreach (var p in period)
        {
            var amount = p.BaseAmount > 0 ? p.BaseAmount : p.Amount;
            if (p.Type == PaymentType.Receipt) vm.TotalReceipts += amount;
            else vm.TotalDisbursements += amount;
        }
        foreach (var p in onReceiptPurchases)
        {
            if (p.InvoiceDate >= fromDate && p.InvoiceDate <= toDate) vm.TotalDisbursements += p.Base;
        }

        var byMethod = period
            .GroupBy(p => p.Method)
            .Select(g => new CashFlowMethodTotal
            {
                Method = g.Key,
                Receipts = g.Where(p => p.Type == PaymentType.Receipt).Sum(p => p.BaseAmount > 0 ? p.BaseAmount : p.Amount),
                Disbursements = g.Where(p => p.Type == PaymentType.Disbursement).Sum(p => p.BaseAmount > 0 ? p.BaseAmount : p.Amount)
            })
            .ToList();

        var periodPurchases = onReceiptPurchases.Where(p => p.InvoiceDate >= fromDate && p.InvoiceDate <= toDate).Sum(p => p.Base);
        if (periodPurchases > 0)
        {
            var cashRow = byMethod.FirstOrDefault(m => m.Method == PaymentMethod.Cash);
            if (cashRow != null)
            {
                cashRow.Disbursements += periodPurchases;
            }
            else
            {
                byMethod.Add(new CashFlowMethodTotal
                {
                    Method = PaymentMethod.Cash,
                    Receipts = 0m,
                    Disbursements = periodPurchases
                });
            }
        }
        byMethod = byMethod.OrderBy(m => m.Method).ToList();

        vm.ByMethod = byMethod;
        return vm;
    }

    public async Task<byte[]> ExportCashFlowXlsxAsync(DateTime from, DateTime to)
    {
        var vm = await CashFlowAsync(from, to);
        using var wb = new XLWorkbook();

        var ws = wb.Worksheets.Add("التدفق النقدي");
        WriteReportHeading(ws, 1, $"كشف التدفق النقدي — من {vm.From:dd/MM/yyyy} إلى {vm.To:dd/MM/yyyy}");
        ws.Cell(3, 1).Value = "رصيد افتتاحي";
        ws.Cell(3, 2).Value = (double)vm.OpeningBalance;
        ws.Cell(4, 1).Value = "المقبوضات";
        ws.Cell(4, 2).Value = (double)vm.TotalReceipts;
        ws.Cell(5, 1).Value = "المصروفات";
        ws.Cell(5, 2).Value = (double)vm.TotalDisbursements;
        ws.Cell(6, 1).Value = "صافي التدفق";
        ws.Cell(6, 2).Value = (double)vm.NetCashFlow;
        ws.Cell(7, 1).Value = "رصيد ختامي";
        ws.Cell(7, 2).Value = (double)vm.ClosingBalance;
        ws.Cell(7, 2).Style.Font.Bold = true;

        ws.Cell(9, 1).Value = "التوزيع حسب طريقة الدفع";
        ws.Cell(9, 1).Style.Font.Bold = true;
        ws.Range(10, 1, 10, 4).Style.Font.Bold = true;
        ws.Cell(10, 1).Value = "طريقة الدفع";
        ws.Cell(10, 2).Value = "مقبوضات";
        ws.Cell(10, 3).Value = "مصروفات";
        ws.Cell(10, 4).Value = "الصافي";
        int row = 11;
        foreach (var m in vm.ByMethod)
        {
            ws.Cell(row, 1).Value = m.Method.GetDisplayName();
            ws.Cell(row, 2).Value = (double)m.Receipts;
            ws.Cell(row, 3).Value = (double)m.Disbursements;
            ws.Cell(row, 4).Value = (double)m.Net;
            row++;
        }

        ws.Cell(row + 1, 1).Value = "تفاصيل الحركات";
        ws.Cell(row + 1, 1).Style.Font.Bold = true;
        ws.Range(row + 2, 1, row + 2, 6).Style.Font.Bold = true;
        ws.Cell(row + 2, 1).Value = "التاريخ";
        ws.Cell(row + 2, 2).Value = "الإيصال";
        ws.Cell(row + 2, 3).Value = "النوع";
        ws.Cell(row + 2, 4).Value = "الطرف";
        ws.Cell(row + 2, 5).Value = "الطريقة";
        ws.Cell(row + 2, 6).Value = "المبلغ";
        int detailRow = row + 3;
        foreach (var p in vm.Payments)
        {
            var amount = p.BaseAmount > 0 ? p.BaseAmount : p.Amount;
            ws.Cell(detailRow, 1).Value = p.PaymentDate.ToString("dd/MM/yyyy");
            ws.Cell(detailRow, 2).Value = p.ReceiptNumber;
            ws.Cell(detailRow, 3).Value = p.Type == PaymentType.Receipt ? "قبض" : "صرف";
            ws.Cell(detailRow, 4).Value = p.Customer?.Name ?? p.Supplier?.Name ?? "";
            ws.Cell(detailRow, 5).Value = p.Method.GetDisplayName();
            ws.Cell(detailRow, 6).Value = (double)amount;
            detailRow++;
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportCustomerStatementXlsxAsync(int customerId)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId);
        if (customer == null) return Array.Empty<byte>();

        var invoices = await _db.SaleInvoices.AsNoTracking().Where(s => s.CustomerId == customerId).OrderBy(s => s.InvoiceDate).ThenBy(s => s.Id).ToListAsync();
        var returns = await _db.SaleReturns.AsNoTracking().Where(r => r.CustomerId == customerId && r.Status == ReturnStatus.Posted).OrderBy(r => r.ReturnDate).ThenBy(r => r.Id).ToListAsync();
        var receipts = await _db.Payments.AsNoTracking()
            .Where(p => p.CustomerId == customerId && p.Type == PaymentType.Receipt)
            .OrderBy(p => p.PaymentDate).ThenBy(p => p.Id).ToListAsync();

        var lines = new List<(DateTime Date, string Desc, string Doc, decimal Debit, decimal Credit)>();
        foreach (var inv in invoices)
        {
            lines.Add((inv.InvoiceDate, "فاتورة بيع", inv.InvoiceNumber, decimal.Round(inv.NetAmount * (inv.ExchangeRate ?? 1m), 2), 0));
            if (inv.PaymentTerms == InvoicePaymentTerms.OnReceipt && inv.PaidAmount > 0)
                lines.Add((inv.InvoiceDate, "مدفوع عند الاستلام", inv.InvoiceNumber, 0, decimal.Round(inv.PaidAmount * (inv.ExchangeRate ?? 1m), 2)));
        }
        foreach (var r in returns) lines.Add((r.ReturnDate, "مرتجع بيع", r.ReturnNumber, 0, decimal.Round(r.TotalAmount * (r.ExchangeRate ?? 1m), 2)));
        foreach (var r in receipts) lines.Add((r.PaymentDate, "قبض", r.ReceiptNumber, 0, r.BaseAmount > 0 ? r.BaseAmount : r.Amount));

        return BuildStatementWorkbook($"كشف حساب — {customer.Name}", customer.OpeningBalance, lines);
    }

    public async Task<byte[]> ExportSupplierStatementXlsxAsync(int supplierId)
    {
        var supplier = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId);
        if (supplier == null) return Array.Empty<byte>();

        var invoices = await _db.PurchaseInvoices.AsNoTracking().Where(p => p.SupplierId == supplierId).OrderBy(p => p.InvoiceDate).ThenBy(p => p.Id).ToListAsync();
        var returns = await _db.PurchaseReturns.AsNoTracking().Where(r => r.SupplierId == supplierId && r.Status == ReturnStatus.Posted).OrderBy(r => r.ReturnDate).ThenBy(r => r.Id).ToListAsync();
        var disbursements = await _db.Payments.AsNoTracking()
            .Where(p => p.SupplierId == supplierId && p.Type == PaymentType.Disbursement)
            .OrderBy(p => p.PaymentDate).ThenBy(p => p.Id).ToListAsync();

        var lines = new List<(DateTime Date, string Desc, string Doc, decimal Debit, decimal Credit)>();
        foreach (var inv in invoices)
        {
            lines.Add((inv.InvoiceDate, "فاتورة شراء", inv.InvoiceNumber, decimal.Round(inv.NetAmount * (inv.ExchangeRate ?? 1m), 2), 0));
            if (inv.PaymentTerms == InvoicePaymentTerms.OnReceipt && inv.PaidAmount > 0)
                lines.Add((inv.InvoiceDate, "مدفوع عند الاستلام", inv.InvoiceNumber, 0, decimal.Round(inv.PaidAmount * (inv.ExchangeRate ?? 1m), 2)));
        }
        foreach (var r in returns) lines.Add((r.ReturnDate, "مرتجع شراء", r.ReturnNumber, 0, decimal.Round(r.TotalAmount * (r.ExchangeRate ?? 1m), 2)));
        foreach (var d in disbursements) lines.Add((d.PaymentDate, "صرف", d.ReceiptNumber, 0, d.BaseAmount > 0 ? d.BaseAmount : d.Amount));

        return BuildStatementWorkbook($"كشف حساب — {supplier.Name}", supplier.OpeningBalance, lines);
    }

    private static byte[] BuildStatementWorkbook(string title, decimal openingBalance, List<(DateTime Date, string Desc, string Doc, decimal Debit, decimal Credit)> unordered)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("كشف حساب");
        WriteReportHeading(ws, 1, title);
        ws.Cell(2, 1).Value = $"الرصيد الافتتاحي: {openingBalance:N2}";
        ws.Range(3, 1, 3, 5).Style.Font.Bold = true;
        ws.Cell(3, 1).Value = "التاريخ";
        ws.Cell(3, 2).Value = "البيان";
        ws.Cell(3, 3).Value = "المستند";
        ws.Cell(3, 4).Value = "مدين";
        ws.Cell(3, 5).Value = "دائن";

        int row = 4;
        decimal running = openingBalance;
        foreach (var (date, desc, doc, debit, credit) in unordered.OrderBy(l => l.Date).ThenBy(l => l.Doc))
        {
            running += debit - credit;
            ws.Cell(row, 1).Value = date.ToString("dd/MM/yyyy");
            ws.Cell(row, 2).Value = desc;
            ws.Cell(row, 3).Value = doc;
            ws.Cell(row, 4).Value = (double)debit;
            ws.Cell(row, 5).Value = (double)credit;
            row++;
        }

        ws.Cell(row + 1, 1).Value = "الرصيد الختامي";
        ws.Cell(row + 1, 1).Style.Font.Bold = true;
        ws.Cell(row + 1, 5).Value = (double)running;
        ws.Cell(row + 1, 5).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
