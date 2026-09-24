using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class ReportExportServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public ReportExportServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static string Body(byte[] bytes) =>
        Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

    [Fact]
    public async Task SalesToCsv_UnderLimit_ReturnsUntruncatedCsvWithHeader()
    {
        using var db = CreateContext();
        var customer = new Customer { Name = "عميل تصدير" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var today = new DateTime(2026, 8, 10);
        db.SaleInvoices.AddRange(
            new SaleInvoice { InvoiceNumber = "S-1", CustomerId = customer.Id, InvoiceDate = today, TotalAmount = 100m, NetAmount = 100m },
            new SaleInvoice { InvoiceNumber = "S-2", CustomerId = customer.Id, InvoiceDate = today.AddDays(-1), TotalAmount = 50m, Discount = 5m, NetAmount = 45m });
        await db.SaveChangesAsync();

        var svc = new ReportExportService(db);
        var result = await svc.SalesToCsv(today.AddDays(-10), today.AddDays(10));

        Assert.False(result.Truncated);
        Assert.Equal(2, result.TotalRows);
        var body = Body(result.Bytes);
        Assert.StartsWith("الفاتورة,العميل,التاريخ,الإجمالي", body);
        Assert.Contains("S-1", body);
        Assert.Contains("S-2", body);
    }

    [Fact]
    public async Task SalesToCsv_OverBounded_FlagsTruncatedAndLimitsRows()
    {
        using var db = CreateContext();
        var customer = new Customer { Name = "عميل تصدير" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var today = new DateTime(2026, 8, 10);
        db.SaleInvoices.AddRange(
            new SaleInvoice { InvoiceNumber = "S-1", CustomerId = customer.Id, InvoiceDate = today, TotalAmount = 10m, NetAmount = 10m },
            new SaleInvoice { InvoiceNumber = "S-2", CustomerId = customer.Id, InvoiceDate = today, TotalAmount = 10m, NetAmount = 10m },
            new SaleInvoice { InvoiceNumber = "S-3", CustomerId = customer.Id, InvoiceDate = today, TotalAmount = 10m, NetAmount = 10m });
        await db.SaveChangesAsync();

        var svc = new ReportExportService(db);
        var result = await svc.SalesToCsv(today.AddDays(-10), today.AddDays(10), maxRows: 2);

        Assert.True(result.Truncated);
        Assert.Equal(3, result.TotalRows);
        var lines = Body(result.Bytes).Trim().Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.True(lines.Skip(1).All(l => l.StartsWith("S-")), "كل سطر بيانات يجب أن يكون رقم فاتورة بيع.");
        var exported = lines.Skip(1).Select(l => l.Split(',')[0]).ToHashSet();
        Assert.Equal(2, exported.Count);
    }

    [Fact]
    public async Task PurchasesToCsv_UnderLimit_ReturnsUntruncatedCsv()
    {
        using var db = CreateContext();
        var supplier = new Supplier { Name = "مورد تصدير" };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        var today = new DateTime(2026, 8, 10);
        db.PurchaseInvoices.Add(new PurchaseInvoice { InvoiceNumber = "P-1", SupplierId = supplier.Id, InvoiceDate = today, TotalAmount = 200m, NetAmount = 200m });
        await db.SaveChangesAsync();

        var svc = new ReportExportService(db);
        var result = await svc.PurchasesToCsv(today.AddDays(-1), today.AddDays(1));

        Assert.False(result.Truncated);
        Assert.Equal(1, result.TotalRows);
        Assert.StartsWith("الفاتورة,المورد,التاريخ,الإجمالي", Body(result.Bytes));
        Assert.Contains("P-1", Body(result.Bytes));
    }

    [Fact]
    public async Task PaymentsToCsv_UnderLimit_ReturnsUntruncatedCsv()
    {
        using var db = CreateContext();
        var customer = new Customer { Name = "عميل مدفوعات" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var today = new DateTime(2026, 8, 10);
        db.Payments.AddRange(
            new Payment { ReceiptNumber = "PY-1", Type = PaymentType.Receipt, PaymentDate = today, Amount = 10m, CustomerId = customer.Id },
            new Payment { ReceiptNumber = "PY-2", Type = PaymentType.Disbursement, PaymentDate = today, Amount = 20m });
        await db.SaveChangesAsync();

        var svc = new ReportExportService(db);
        var result = await svc.PaymentsToCsv(today.AddDays(-1), today.AddDays(1));

        Assert.False(result.Truncated);
        Assert.Equal(2, result.TotalRows);
        Assert.StartsWith("الإيصال,النوع,العميل/المورد", Body(result.Bytes));
        Assert.Contains("قبض", Body(result.Bytes));
        Assert.Contains("صرف", Body(result.Bytes));
    }
}