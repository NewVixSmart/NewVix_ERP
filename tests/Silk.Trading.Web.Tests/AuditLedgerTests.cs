using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Controllers;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Services;
using Silk.Trading.Web.ViewModels.Reports;
using Xunit;

namespace Silk.Trading.Web.Tests;

public sealed class AuditLedgerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AuditLedgerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
        SeedChartOfAccounts(db);
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static void SeedChartOfAccounts(AppDbContext db)
    {
        (string Code, string Name, GLAccountType Type, NormalBalance Normal)[] accounts =
        {
            ("1000", "النقد / الصندوق", GLAccountType.Asset, NormalBalance.Debit),
            ("1200", "المدينون (العملاء)", GLAccountType.Asset, NormalBalance.Debit),
            ("1300", "المخزون", GLAccountType.Asset, NormalBalance.Debit),
            ("2000", "الدائنون (الموردون)", GLAccountType.Liability, NormalBalance.Credit),
            ("3000", "رأس المال", GLAccountType.Equity, NormalBalance.Credit),
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.SaveChanges();
    }

    private static ReportsController CreateController(AppDbContext db) =>
        new(db, new ReportExportService(db), new FinancialReportService(db), new ReportService(db, new FinancialReportService(db)));

    private static async Task SeedEntriesAsync(AppDbContext db)
    {
        var accounting = new AccountingService(db);
        await accounting.RecordSaleInvoiceAsync(new DateTime(2026, 1, 5), 1, 500m, 0m, null, null, "auditor");
        await accounting.RecordReceiptAsync(new DateTime(2026, 1, 7), 300m, PaymentMethod.Cash, 1, "auditor");
        await accounting.RecordPurchaseInvoiceAsync(new DateTime(2026, 1, 9), 2, 200m, null, null, "auditor");
    }

    private static (DateTime from, DateTime to) FullYear => (new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));

    private static AuditLedgerViewModel Extract(IActionResult result)
    {
        var view = Assert.IsType<ViewResult>(result);
        return Assert.IsType<AuditLedgerViewModel>(view.Model);
    }

    [Fact]
    public async Task AuditLedger_ReturnsPostedEntries_OrderedEachBalanced_WithAccountNames()
    {
        using var db = CreateContext();
        await SeedEntriesAsync(db);

        var (from, to) = FullYear;
        var vm = Extract(await CreateController(db).AuditLedger(from, to, null, null, 1));

        Assert.Equal(3, vm.Entries.Count);
        Assert.NotNull(vm.Entries[0].EntryNumber);

        var ordered = vm.Entries.Select(e => e.EntryNumber).OrderBy(x => x).ToArray();
        Assert.Equal(ordered, vm.Entries.Select(e => e.EntryNumber).ToArray());

        Assert.All(vm.Entries, e =>
        {
            Assert.Equal(0m, e.Balance);
            Assert.Equal(e.TotalDebit, e.TotalCredit);
            Assert.NotEmpty(e.Lines);
            Assert.All(e.Lines, l =>
            {
                Assert.False(string.IsNullOrWhiteSpace(l.AccountCode));
                Assert.False(string.IsNullOrWhiteSpace(l.AccountName));
            });
        });
    }

    [Fact]
    public async Task AuditLedger_FilterByAccountId_ReturnsOnlyEntriesTouchingThatAccount()
    {
        using var db = CreateContext();
        await SeedEntriesAsync(db);

        var (from, to) = FullYear;
        var inventoryAccountId = await db.GLAccounts.Where(a => a.Code == "1300").Select(a => a.Id).SingleAsync();
        var vm = Extract(await CreateController(db).AuditLedger(from, to, inventoryAccountId, null, 1));

        Assert.Single(vm.Entries);
        Assert.Equal(JournalSource.PurchaseInvoice, vm.Entries[0].Source);
        Assert.Contains(vm.Entries[0].Lines, l => l.AccountCode == "1300");
    }

    [Fact]
    public async Task AuditLedger_FilterBySource_ReturnsOnlyThatSource()
    {
        using var db = CreateContext();
        await SeedEntriesAsync(db);

        var (from, to) = FullYear;
        var vm = Extract(await CreateController(db).AuditLedger(from, to, null, JournalSource.Receipt, 1));

        Assert.Single(vm.Entries);
        Assert.Equal(JournalSource.Receipt, vm.Entries[0].Source);
        Assert.Equal("قبض", vm.Entries[0].SourceDisplayName);
    }

    [Fact]
    public async Task ExportAuditLedgerXlsxAsync_ProducesNonEmptyWorkbook()
    {
        using var db = CreateContext();
        await SeedEntriesAsync(db);

        var (from, to) = FullYear;
        var report = new ReportService(db, new FinancialReportService(db));
        var bytes = await report.ExportAuditLedgerXlsxAsync(from, to, null, null);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        Assert.Equal(0x50, bytes[0]); // 'P'
        Assert.Equal(0x4B, bytes[1]); // 'K'

        // 3 posted entries, 2 lines each → title(1) + header(3) + 6 data rows = last row 9.
        using var ms = new MemoryStream(bytes, writable: false);
        using var wb = new ClosedXML.Excel.XLWorkbook(ms);
        var ws = wb.Worksheets.First();
        Assert.Equal("سجل التدقيق", ws.Name);
        Assert.Equal("رقم القيد", ws.Cell(3, 1).Value.ToString());
        Assert.Equal(9, ws.LastRowUsed()!.RowNumber());
        Assert.True(ws.Cell(4, 8).GetDouble() > 0.0); // first line debit present
    }
}