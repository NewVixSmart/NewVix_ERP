using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class AccountingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AccountingServiceTests()
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
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("4100", "مرتجعات البيع", GLAccountType.Revenue, NormalBalance.Credit),
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
            ("5100", "مرتجعات الشراء", GLAccountType.Expense, NormalBalance.Debit),
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.SaveChanges();
    }

    private async Task<(decimal deb, decimal cred, int lineCount)> BalancesAsync(AppDbContext db, string entryNumber)
    {
        var lines = await db.JournalEntryLines
            .Where(l => l.JournalEntry!.EntryNumber == entryNumber)
            .ToListAsync();
        return (lines.Sum(l => l.Debit), lines.Sum(l => l.Credit), lines.Count);
    }

    private async Task<string> LastEntryNumberAsync(AppDbContext db)
        => await db.JournalEntries.OrderByDescending(j => j.Id).Select(j => j.EntryNumber).FirstAsync();

    [Fact]
    public async Task SaleInvoice_PostsBalanced_DebitReceivable_CreditRevenue()
    {
        using var db = CreateContext();
        var svc = new AccountingService(db);
        await svc.RecordSaleInvoiceAsync(new DateTime(2026, 1, 5), 1, 500m, 0m, "test");

        var entryNo = await LastEntryNumberAsync(db);
        var (deb, cred, count) = await BalancesAsync(db, entryNo);

        Assert.Equal(deb, cred);
        Assert.Equal(2, count);

        var lines = await db.JournalEntryLines.Where(l => l.JournalEntry!.EntryNumber == entryNo).Include(l => l.Account).ToListAsync();
        Assert.Contains(lines, l => l.Account!.Code == "1200" && l.Debit == 500m);
        Assert.Contains(lines, l => l.Account!.Code == "4000" && l.Credit == 500m);
    }

    [Fact]
    public async Task PurchaseInvoice_PostsBalanced()
    {
        using var db = CreateContext();
        var svc = new AccountingService(db);
        await svc.RecordPurchaseInvoiceAsync(new DateTime(2026, 1, 6), 2, 300m, "test");

        var entryNo = await LastEntryNumberAsync(db);
        var (deb, cred, _) = await BalancesAsync(db, entryNo);
        Assert.Equal(deb, cred);

        var lines = await db.JournalEntryLines.Where(l => l.JournalEntry!.EntryNumber == entryNo).Include(l => l.Account).ToListAsync();
        Assert.Contains(lines, l => l.Account!.Code == "1300" && l.Debit == 300m);
        Assert.Contains(lines, l => l.Account!.Code == "2000" && l.Credit == 300m);
    }

    [Fact]
    public async Task Receipt_Cash_PostsBalanced()
    {
        using var db = CreateContext();
        var svc = new AccountingService(db);
        await svc.RecordReceiptAsync(new DateTime(2026, 1, 7), 200m, PaymentMethod.Cash, 1, "test");

        var entryNo = await LastEntryNumberAsync(db);
        var (deb, cred, _) = await BalancesAsync(db, entryNo);
        Assert.Equal(deb, cred);

        var lines = await db.JournalEntryLines.Where(l => l.JournalEntry!.EntryNumber == entryNo).Include(l => l.Account).ToListAsync();
        Assert.Contains(lines, l => l.Account!.Code == "1000" && l.Debit == 200m);
        Assert.Contains(lines, l => l.Account!.Code == "1200" && l.Credit == 200m);
    }

    [Fact]
    public async Task Receipt_BankTransfer_PostsToBankAccount()
    {
        using var db = CreateContext();
        var svc = new AccountingService(db);
        await svc.RecordReceiptAsync(new DateTime(2026, 1, 8), 250m, PaymentMethod.BankTransfer, 1, "test");

        var entryNo = await LastEntryNumberAsync(db);
        var lines = await db.JournalEntryLines.Where(l => l.JournalEntry!.EntryNumber == entryNo).Include(l => l.Account).ToListAsync();
        Assert.Contains(lines, l => l.Account!.Code == "1100" && l.Debit == 250m);
    }

    [Fact]
    public async Task Disbursement_PostsBalanced()
    {
        using var db = CreateContext();
        var svc = new AccountingService(db);
        await svc.RecordDisbursementAsync(new DateTime(2026, 1, 9), 150m, PaymentMethod.Cash, 2, "test");

        var entryNo = await LastEntryNumberAsync(db);
        var (deb, cred, _) = await BalancesAsync(db, entryNo);
        Assert.Equal(deb, cred);

        var lines = await db.JournalEntryLines.Where(l => l.JournalEntry!.EntryNumber == entryNo).Include(l => l.Account).ToListAsync();
        Assert.Contains(lines, l => l.Account!.Code == "2000" && l.Debit == 150m);
        Assert.Contains(lines, l => l.Account!.Code == "1000" && l.Credit == 150m);
    }

    [Fact]
    public async Task OpeningStock_PostsBalanced_OnlyWhenPositive()
    {
        using var db = CreateContext();
        var svc = new AccountingService(db);
        await svc.RecordOpeningStockAsync(1, qty: 10, count: 0, cost: 20m, user: "test");

        var entryNo = await LastEntryNumberAsync(db);
        var (deb, cred, _) = await BalancesAsync(db, entryNo);
        Assert.Equal(deb, cred);

        var lines = await db.JournalEntryLines.Where(l => l.JournalEntry!.EntryNumber == entryNo).Include(l => l.Account).ToListAsync();
        Assert.Contains(lines, l => l.Account!.Code == "1300" && l.Debit == 200m);
        Assert.Contains(lines, l => l.Account!.Code == "3000" && l.Credit == 200m);

        // zero-cost → no entry
        var before = await db.JournalEntries.CountAsync();
        await svc.RecordOpeningStockAsync(1, qty: 5, count: 0, cost: 0m, user: "test");
        Assert.Equal(before, await db.JournalEntries.CountAsync());
    }

    [Fact]
    public async Task SaleInvoice_WithCost_PostsCogsLegs()
    {
        using var db = CreateContext();
        var svc = new AccountingService(db);
        await svc.RecordSaleInvoiceAsync(new DateTime(2026, 1, 10), 1, 100m, 40m, "test");

        var entryNo = await LastEntryNumberAsync(db);
        var lines = await db.JournalEntryLines.Where(l => l.JournalEntry!.EntryNumber == entryNo).Include(l => l.Account).ToListAsync();
        Assert.Equal(4, lines.Count);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
        Assert.Contains(lines, l => l.Account!.Code == "1200" && l.Debit == 100m);
        Assert.Contains(lines, l => l.Account!.Code == "4000" && l.Credit == 100m);
        Assert.Contains(lines, l => l.Account!.Code == "5000" && l.Debit == 40m);
        Assert.Contains(lines, l => l.Account!.Code == "1300" && l.Credit == 40m);
    }

    [Fact]
    public async Task SaleInvoice_NoCost_PostsOnlyValueLines()
    {
        using var db = CreateContext();
        var svc = new AccountingService(db);
        await svc.RecordSaleInvoiceAsync(new DateTime(2026, 1, 11), 1, 100m, 0m, "test");

        var (deb, cred, count) = await BalancesAsync(db, await LastEntryNumberAsync(db));
        Assert.Equal(deb, cred);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task SaleInvoice_PostsValueAndCostInTheSameCurrency()
    {
        // The ledger is single-currency, so the value and the cost legs are posted at the
        // same face amount - there is no conversion step that could split them.
        using var db = CreateContext();
        var svc = new AccountingService(db);

        await svc.RecordSaleInvoiceAsync(new DateTime(2026, 1, 12), 1, 100m, 40m, "test");

        var entryNo = await LastEntryNumberAsync(db);
        var lines = await db.JournalEntryLines.Where(l => l.JournalEntry!.EntryNumber == entryNo).Include(l => l.Account).ToListAsync();
        Assert.Equal(4, lines.Count);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
        Assert.Contains(lines, l => l.Account!.Code == "1200" && l.Debit == 100m);
        Assert.Contains(lines, l => l.Account!.Code == "4000" && l.Credit == 100m);
        Assert.Contains(lines, l => l.Account!.Code == "5000" && l.Debit == 40m);
        Assert.Contains(lines, l => l.Account!.Code == "1300" && l.Credit == 40m);
    }

    [Fact]
    public async Task PurchaseInvoice_PostsAtFaceAmount()
    {
        using var db = CreateContext();
        var svc = new AccountingService(db);

        await svc.RecordPurchaseInvoiceAsync(new DateTime(2026, 1, 13), 2, 300m, "test");

        var entryNo = await LastEntryNumberAsync(db);
        var lines = await db.JournalEntryLines.Where(l => l.JournalEntry!.EntryNumber == entryNo).Include(l => l.Account).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
        Assert.Contains(lines, l => l.Account!.Code == "1300" && l.Debit == 300m);
        Assert.Contains(lines, l => l.Account!.Code == "2000" && l.Credit == 300m);
    }

    [Fact]
    public async Task EntryNumbers_AreUnique()
    {
        using var db = CreateContext();
        var svc = new AccountingService(db);
        for (int i = 0; i < 5; i++)
        {
            await svc.RecordSaleInvoiceAsync(DateTime.UtcNow, 1, 100m + i, 0m, "test");
        }
        var numbers = await db.JournalEntries.Select(j => j.EntryNumber).ToListAsync();
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
    }
}
