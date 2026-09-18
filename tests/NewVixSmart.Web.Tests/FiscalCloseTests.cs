using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class FiscalCloseTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public FiscalCloseTests()
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
            ("1100", "البنوك", GLAccountType.Asset, NormalBalance.Debit),
            ("1200", "المدينون (العملاء)", GLAccountType.Asset, NormalBalance.Debit),
            ("1300", "المخزون", GLAccountType.Asset, NormalBalance.Debit),
            ("2000", "الدائنون (الموردون)", GLAccountType.Liability, NormalBalance.Credit),
            ("3000", "رأس المال", GLAccountType.Equity, NormalBalance.Credit),
            ("3001", "الأرباح المحتجزة / تجميع الإقفال", GLAccountType.Equity, NormalBalance.Credit),
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("4100", "مرتجعات البيع (قديم)", GLAccountType.Revenue, NormalBalance.Credit),
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
            ("5100", "مرتجعات الشراء (قديم)", GLAccountType.Expense, NormalBalance.Debit),
            ("5101", "مرتجعات المبيعات", GLAccountType.Expense, NormalBalance.Debit),
            ("5102", "مرتجعات المشتريات", GLAccountType.Expense, NormalBalance.Debit)
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.SaveChanges();
    }

    private static FiscalService CreateFiscalService(AppDbContext db)
        => new(db, new AccountingService(db), new FinancialReportService(db));

    private async Task CloseYearAsync(AppDbContext db, string? user = "admin")
    {
        var fiscal = CreateFiscalService(db);
        await fiscal.EnsurePeriodAsync(2026);
        await fiscal.CloseYearAsync(2026, user);
    }

    [Fact]
    public async Task CloseYear_ZeroesPlAccounts_AndTransfersNetIncome_To3001()
    {
        using var db = CreateContext();
        var accounting = new AccountingService(db);
        var fiscal = CreateFiscalService(db);

        await fiscal.EnsurePeriodAsync(2026);

        await accounting.RecordSaleInvoiceAsync(new DateTime(2026, 3, 10), 1, 100m, 0m, null, null, "test");
        await accounting.PostAsync(JournalSource.PurchaseInvoice, 2, new DateTime(2026, 3, 11), "فاتورة شراء",
            new[] { new JournalLine("5000", 40m, 0m), new JournalLine("2000", 0m, 40m) }, "test");

        var summary = await fiscal.CloseYearAsync(2026, "admin");

        Assert.Equal(2, summary.AccountsCleared);
        Assert.Equal(60m, summary.NetIncomeToRetainedEarnings);

        var period = await db.FiscalPeriods.SingleAsync(p => p.Year == 2026);
        Assert.True(period.IsClosed);
        Assert.Equal("admin", period.ClosedById);
        Assert.NotNull(period.ClosedAt);

        var closeEntries = await db.JournalEntries.Where(j => j.Source == JournalSource.YearEndClose).ToListAsync();
        Assert.Equal(2, closeEntries.Count);
        Assert.All(closeEntries, e => Assert.Equal(period.Id, e.SourceId));

        var r3001 = await db.GLAccounts.SingleAsync(a => a.Code == "3001");
        var lines3001 = await db.JournalEntryLines.Where(l => l.AccountId == r3001.Id).ToListAsync();
        Assert.Equal(40m, lines3001.Sum(l => l.Debit));
        Assert.Equal(100m, lines3001.Sum(l => l.Credit));

        var financial = new FinancialReportService(db);
        var income = await financial.IncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
        Assert.Equal(100m, income.TotalRevenue);
        Assert.Equal(40m, income.TotalExpenses);

        var tb = await financial.TrialBalanceAsync(new DateTime(2026, 12, 31));
        Assert.Equal(0m, tb.Rows.Single(r => r.Code == "4000").Balance);
        Assert.Equal(0m, tb.Rows.Single(r => r.Code == "5000").Balance);
        Assert.Equal(60m, tb.Rows.Single(r => r.Code == "3001").Balance);
    }

    [Fact]
    public async Task CloseYear_EmptyYear_Closes_WithoutPosting()
    {
        using var db = CreateContext();
        var fiscal = CreateFiscalService(db);

        await fiscal.EnsurePeriodAsync(2026);
        var summary = await fiscal.CloseYearAsync(2026, "admin");

        Assert.Equal(0, summary.AccountsCleared);
        Assert.Equal(0m, summary.NetIncomeToRetainedEarnings);
        Assert.True((await db.FiscalPeriods.SingleAsync(p => p.Year == 2026)).IsClosed);
        Assert.Equal(0, await db.JournalEntries.CountAsync(j => j.Source == JournalSource.YearEndClose));
    }

    [Fact]
    public async Task CloseYear_ContraRevenue_ReversesOn3001()
    {
        using var db = CreateContext();
        var accounting = new AccountingService(db);
        var fiscal = CreateFiscalService(db);

        await fiscal.EnsurePeriodAsync(2026);
        await accounting.RecordSaleInvoiceAsync(new DateTime(2026, 2, 1), 1, 100m, 0m, null, null, "test");
        await accounting.RecordSaleReturnAsync(new DateTime(2026, 2, 2), 30m, "test");

        var summary = await fiscal.CloseYearAsync(2026, "admin");

        Assert.Equal(2, summary.AccountsCleared);
        Assert.Equal(70m, summary.NetIncomeToRetainedEarnings);

        var a5101 = await db.GLAccounts.SingleAsync(a => a.Code == "5101");
        var close5101 = await db.JournalEntryLines.Where(l => l.AccountId == a5101.Id).ToListAsync();
        Assert.Equal(30m, close5101.Sum(l => l.Debit));
        Assert.Equal(30m, close5101.Sum(l => l.Credit));

        var r3001 = await db.GLAccounts.SingleAsync(a => a.Code == "3001");
        var lines3001 = await db.JournalEntryLines.Where(l => l.AccountId == r3001.Id).ToListAsync();
        Assert.Equal(30m, lines3001.Sum(l => l.Debit));
        Assert.Equal(100m, lines3001.Sum(l => l.Credit));
    }

    [Fact]
    public async Task CloseYear_IncludesInactivePlAccount_WithPostedActivity()
    {
        using var db = CreateContext();
        var accounting = new AccountingService(db);
        var fiscal = CreateFiscalService(db);

        await fiscal.EnsurePeriodAsync(2026);
        await accounting.RecordSaleInvoiceAsync(new DateTime(2026, 3, 10), 1, 100m, 0m, null, null, "test");

        var financing = await db.GLAccounts.SingleAsync(a => a.Code == "4000");
        financing.IsActive = false;
        await db.SaveChangesAsync();

        var financial = new FinancialReportService(db);
        var beforeClose = await financial.IncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
        Assert.Equal(100m, beforeClose.TotalRevenue);

        var summary = await fiscal.CloseYearAsync(2026, "admin");

        Assert.Equal(1, summary.AccountsCleared);
        Assert.Equal(100m, summary.NetIncomeToRetainedEarnings);

        var r3001 = await db.GLAccounts.SingleAsync(a => a.Code == "3001");
        Assert.Equal(100m, (await db.JournalEntryLines.Where(l => l.AccountId == r3001.Id).ToListAsync()).Sum(l => l.Credit));
    }

    [Fact]
    public async Task Guard_PostingInClosedYear_Throws()
    {
        using var db = CreateContext();
        var accounting = new AccountingService(db);

        await CloseYearAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            accounting.RecordSaleInvoiceAsync(new DateTime(2026, 6, 1), 1, 50m, 0m, null, null, "test"));
        Assert.Contains("مغلقة", ex.Message);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            accounting.RecordPurchaseInvoiceAsync(new DateTime(2026, 6, 1), 2, 20m, null, null, "test"));
    }

    [Fact]
    public async Task Guard_PostingInOpenYear_StillAllowed()
    {
        using var db = CreateContext();
        var accounting = new AccountingService(db);

        await CloseYearAsync(db);

        await accounting.RecordSaleInvoiceAsync(new DateTime(2027, 1, 5), 1, 50m, 0m, null, null, "test");
        Assert.Equal(1, await db.JournalEntries.CountAsync(j => j.Date.Year == 2027));
    }

    [Fact]
    public async Task Guard_CreatePurchaseInClosedYear_ReturnsFalse()
    {
        using var db = CreateContext();
        var inventory = new InventoryService(db);

        await CloseYearAsync(db);

        var invoice = new PurchaseInvoice { SupplierId = 1, InvoiceDate = new DateTime(2026, 6, 1) };
        var items = new List<PurchaseInvoiceItem> { new() { ItemId = 1, Quantity = 1, UnitPrice = 10 } };

        var (ok, error) = await inventory.CreatePurchaseAsync(invoice, items, "test");
        Assert.False(ok);
        Assert.Contains("مغلقة", error);
    }

    [Fact]
    public async Task Guard_CreateSaleInClosedYear_ReturnsFalse()
    {
        using var db = CreateContext();
        var inventory = new InventoryService(db);

        await CloseYearAsync(db);

        var invoice = new NewVixSmart.Web.Models.Sales.SaleInvoice { CustomerId = 1, InvoiceDate = new DateTime(2026, 6, 1) };
        var items = new List<NewVixSmart.Web.Models.Sales.SaleInvoiceItem> { new() { ItemId = 1, Quantity = 1, UnitPrice = 10 } };

        var (ok, error) = await inventory.CreateSaleAsync(invoice, items, "test");
        Assert.False(ok);
        Assert.Contains("مغلقة", error);
    }

    [Fact]
    public async Task Guard_CreateAdjustmentInClosedYear_ReturnsFalse()
    {
        using var db = CreateContext();
        var inventory = new InventoryService(db);

        await CloseYearAsync(db);

        var adjustment = new InventoryAdjustment
        {
            ItemId = 1,
            NewCount = 5,
            NewQuantity = 5,
            AdjustmentDate = new DateTime(2026, 7, 1)
        };

        var (ok, error) = await inventory.CreateAdjustmentAsync(adjustment, "test");
        Assert.False(ok);
        Assert.Contains("مغلقة", error);
    }

    [Fact]
    public async Task Guard_CreatePaymentInClosedYear_ReturnsFalse()
    {
        using var db = CreateContext();
        var payments = new PaymentService(db);

        await CloseYearAsync(db);

        var payment = new Payment
        {
            Type = PaymentType.Receipt,
            CustomerId = 1,
            Amount = 10,
            PaymentDate = new DateTime(2026, 8, 1)
        };

        var (ok, error, created) = await payments.CreatePaymentAsync(payment, "test");
        Assert.False(ok);
        Assert.Contains("مغلقة", error);
        Assert.Null(created);
    }

    [Fact]
    public async Task ReopenYear_RemovesCloseEntries_AndRestoresPlBalances()
    {
        using var db = CreateContext();
        var accounting = new AccountingService(db);
        var financial = new FinancialReportService(db);
        var fiscal = CreateFiscalService(db);

        await fiscal.EnsurePeriodAsync(2026);
        await accounting.RecordSaleInvoiceAsync(new DateTime(2026, 3, 10), 1, 100m, 0m, null, null, "test");
        await accounting.PostAsync(JournalSource.PurchaseInvoice, 2, new DateTime(2026, 3, 11), "فاتورة شراء",
            new[] { new JournalLine("5000", 40m, 0m), new JournalLine("2000", 0m, 40m) }, "test");

        var closed = await fiscal.CloseYearAsync(2026, "admin");
        Assert.Equal(60m, closed.NetIncomeToRetainedEarnings);

        var reopened = await fiscal.ReopenYearAsync(2026, "admin");
        Assert.Equal(2, reopened.EntriesRemoved);
        Assert.Equal(60m, reopened.RestoredNetIncome);

        var period = await db.FiscalPeriods.SingleAsync(p => p.Year == 2026);
        Assert.False(period.IsClosed);
        Assert.Null(period.ClosedById);
        Assert.Null(period.ClosedAt);

        Assert.Equal(0, await db.JournalEntries.CountAsync(j => j.Source == JournalSource.YearEndClose));

        var tb = await financial.TrialBalanceAsync(new DateTime(2026, 12, 31));
        Assert.Equal(100m, tb.Rows.Single(r => r.Code == "4000").Balance);
        Assert.Equal(40m, tb.Rows.Single(r => r.Code == "5000").Balance);
        Assert.Equal(0m, tb.Rows.Single(r => r.Code == "3001").Balance);
    }

    [Fact]
    public async Task ReopenYear_WhenOpen_Throws()
    {
        using var db = CreateContext();
        var fiscal = CreateFiscalService(db);

        await fiscal.EnsurePeriodAsync(2026);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => fiscal.ReopenYearAsync(2026, "admin"));
        Assert.Contains("غير مغلقة", ex.Message);
    }

    [Fact]
    public async Task ReopenYear_WhenMissing_Throws()
    {
        using var db = CreateContext();
        var fiscal = CreateFiscalService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => fiscal.ReopenYearAsync(2026, "admin"));
        Assert.Contains("غير موجودة", ex.Message);
    }

    [Fact]
    public async Task CloseYear_OlderYear_WhenNewerExists_Throws()
    {
        using var db = CreateContext();
        var fiscal = CreateFiscalService(db);

        await fiscal.EnsurePeriodAsync(2025);
        await fiscal.EnsurePeriodAsync(2026);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => fiscal.CloseYearAsync(2025, "admin"));
        Assert.Contains("أحدث", ex.Message);
    }

    [Fact]
    public async Task CloseYear_WhenPeriodMissing_Throws()
    {
        using var db = CreateContext();
        var fiscal = CreateFiscalService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => fiscal.CloseYearAsync(2026, "admin"));
        Assert.Contains("غير موجودة", ex.Message);
    }

    [Fact]
    public async Task CloseYear_WhenAlreadyClosed_Throws()
    {
        using var db = CreateContext();
        var fiscal = CreateFiscalService(db);

        await CloseYearAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => fiscal.CloseYearAsync(2026, "admin"));
        Assert.Contains("مغلقة بالفعل", ex.Message);
    }

    [Fact]
    public async Task EnsurePeriodAsync_IsIdempotent_AndNamesDefault()
    {
        using var db = CreateContext();
        var fiscal = CreateFiscalService(db);

        await fiscal.EnsurePeriodAsync(2026);
        await fiscal.EnsurePeriodAsync(2026);
        await fiscal.EnsurePeriodAsync(2026);

        Assert.Equal(1, await db.FiscalPeriods.CountAsync(p => p.Year == 2026));
        Assert.Equal("سنة 2026", (await db.FiscalPeriods.SingleAsync()).Name);
    }

    [Fact]
    public async Task IsClosedAsync_ReflectsPeriodState()
    {
        using var db = CreateContext();
        var fiscal = CreateFiscalService(db);

        await fiscal.EnsurePeriodAsync(2026);
        Assert.False(await fiscal.IsClosedAsync(new DateTime(2026, 6, 1)));

        await fiscal.CloseYearAsync(2026, "admin");
        Assert.True(await fiscal.IsClosedAsync(new DateTime(2026, 6, 1)));
        Assert.False(await fiscal.IsClosedAsync(new DateTime(2027, 6, 1)));
    }

    [Fact]
    public async Task ValidateBudgetWriteAsync_ClosedYear_Throws()
    {
        using var db = CreateContext();
        await CloseYearAsync(db);
        var fiscal = CreateFiscalService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => fiscal.ValidateBudgetWriteAsync(2026));
        Assert.Contains("مغلقة", ex.Message);
    }

    [Fact]
    public async Task ValidateBudgetWriteAsync_OpenYear_DoesNotThrow()
    {
        using var db = CreateContext();
        var fiscal = CreateFiscalService(db);
        await fiscal.EnsurePeriodAsync(2026);

        await fiscal.ValidateBudgetWriteAsync(2026);
    }
}