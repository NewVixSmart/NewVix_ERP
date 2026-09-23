using System.Text;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models.Accounting;

namespace NewVixSmart.Web.Services;

public class ReportExportService
{
    private const int MaxExportRows = 50_000;
    private readonly AppDbContext _db;
    public ReportExportService(AppDbContext db) => _db = db;

    public async Task<byte[]> SalesToCsv(DateTime from, DateTime to)
    {
        var invoices = await _db.SaleInvoices
            .AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Currency)
            .Where(s => s.InvoiceDate >= from && s.InvoiceDate <= to)
            .OrderByDescending(s => s.InvoiceDate)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("الفاتورة,العميل,التاريخ,الإجمالي,الخصم,الضريبة,الصافي,العملة,سعر الصرف");

        foreach (var i in invoices)
        {
            sb.AppendLine(string.Join(",",
                CsvField(i.InvoiceNumber),
                CsvField(i.Customer?.Name ?? "—"),
                CsvField(i.InvoiceDate.ToString("dd/MM/yyyy")),
                CsvField(i.TotalAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
                CsvField(i.Discount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
                CsvField(i.Tax.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
                CsvField(i.NetAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
                CsvField(i.Currency?.Code ?? "—"),
                CsvField(i.ExchangeRate?.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) ?? "—")));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return bytes;
    }

    public async Task<byte[]> PurchasesToCsv(DateTime from, DateTime to)
    {
        var invoices = await _db.PurchaseInvoices
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Currency)
            .Where(p => p.InvoiceDate >= from && p.InvoiceDate <= to)
            .OrderByDescending(p => p.InvoiceDate)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("الفاتورة,المورد,التاريخ,الإجمالي,الخصم,الضريبة,الصافي,العملة,سعر الصرف");

        foreach (var i in invoices)
        {
            sb.AppendLine(string.Join(",",
                CsvField(i.InvoiceNumber),
                CsvField(i.Supplier?.Name ?? "—"),
                CsvField(i.InvoiceDate.ToString("dd/MM/yyyy")),
                CsvField(i.TotalAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
                CsvField(i.Discount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
                CsvField(i.Tax.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
                CsvField(i.NetAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
                CsvField(i.Currency?.Code ?? "—"),
                CsvField(i.ExchangeRate?.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) ?? "—")));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return bytes;
    }

    public async Task<byte[]> PaymentsToCsv(DateTime from, DateTime to)
    {
        var payments = await _db.Payments
            .AsNoTracking()
            .Include(p => p.Customer)
            .Include(p => p.Supplier)
            .Include(p => p.Currency)
            .Where(p => p.PaymentDate >= from && p.PaymentDate <= to)
            .OrderByDescending(p => p.PaymentDate)
            .Take(MaxExportRows)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("الإيصال,النوع,العميل/المورد,المبلغ,المبلغ بالأساس,العملة,سعر الصرف,الطريقة,التاريخ");

        foreach (var p in payments)
        {
            var typeLabel = p.Type == PaymentType.Receipt ? "قبض" : "صرف";
            var partyName = p.Customer?.Name ?? p.Supplier?.Name ?? "—";
            var methodLabel = p.Method.GetDisplayName();

            sb.AppendLine(string.Join(",",
                CsvField(p.ReceiptNumber),
                CsvField(typeLabel),
                CsvField(partyName),
                CsvField(p.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
                CsvField(p.BaseAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
                CsvField(p.Currency?.Code ?? "—"),
                CsvField(p.ExchangeRate?.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) ?? "—"),
                CsvField(methodLabel),
                CsvField(p.PaymentDate.ToString("dd/MM/yyyy"))));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return bytes;
    }

    private static string CsvField(string value)
    {
        value ??= string.Empty;
        if (value.Length > 0 && "=+-\t@\r\n".IndexOf(value[0]) >= 0)
            value = "'" + value;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
