using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Services;
using Xunit;

namespace Silk.Trading.Web.Tests;

public sealed class BudgetAndAccountsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public BudgetAndAccountsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static void SeedChartOfAccounts(AppDbContext db, bool includeSystem = true)
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

    private static AccountsService CreateAccountsService(AppDbContext db) => new(db);

    private async Task<int> SeedPostedAccountAsync(AppDbContext db, string code = "1200")
    {
        var accounting = new AccountingService(db);
        var acct = await db.GLAccounts.SingleAsync(a => a.Code == code);
        await accounting.PostAsync(JournalSource.Receipt, 1, new DateTime(2026, 3, 10), "قبض لعميل",
            new[] { new JournalLine("1000", 100m, 0m), new JournalLine("1200", 0m, 100m) }, "test");
        return acct.Id;
    }

    // ---------- (a) Account CRUD guards ----------

    [Fact]
    public async Task CreateZeroUsage_Edit_CodeAndName_Ok()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var svc = CreateAccountsService(db);

        var account = new GLAccount { Code = "6000", Name = "إيرادات أخرى", Type = GLAccountType.Revenue, NormalBalance = NormalBalance.Credit, IsActive = true };
        db.GLAccounts.Add(account);
        await db.SaveChangesAsync();

        var model = new GLAccount { Id = account.Id, Code = "6100", Name = "إيرادات الخدمات", Type = GLAccountType.Revenue, NormalBalance = NormalBalance.Credit, IsActive = true };
        var result = await svc.ApplyEditAsync(account, model);
        await db.SaveChangesAsync();

        Assert.True(result.Ok);
        Assert.Equal("6100", account.Code);
        Assert.Equal("إيرادات الخدمات", account.Name);
    }

    [Fact]
    public async Task EditPostedAccount_CodeChange_Rejected_WithArabicError()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedPostedAccountAsync(db);

        var svc = CreateAccountsService(db);
        var account = await db.GLAccounts.SingleAsync(a => a.Code == "1200");

        var model = new GLAccount { Code = "1250", Name = "مدينون معدّلون", Type = GLAccountType.Asset, NormalBalance = NormalBalance.Debit, IsActive = true };
        var result = await svc.ApplyEditAsync(account, model);

        Assert.False(result.Ok);
        Assert.Equal("لا يمكن تغيير رمز حساب له قيود مرحلة", result.Error);
        Assert.Equal("1200", account.Code);
    }

    [Fact]
    public async Task DeletePostedAccount_Rejected_AndZeroUsage_Ok()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);

        var zeroUsage = new GLAccount { Code = "6000", Name = "إيرادات أخرى", Type = GLAccountType.Revenue, NormalBalance = NormalBalance.Credit, IsActive = true };
        db.GLAccounts.Add(zeroUsage);
        await db.SaveChangesAsync();

        var postedAccount = await db.GLAccounts.SingleAsync(a => a.Code == "1200");
        await SeedPostedAccountAsync(db);

        var svc = CreateAccountsService(db);
        var postedInfo = await svc.GetDeleteInfoAsync(postedAccount);
        Assert.False(postedInfo.CanDelete);
        Assert.True(postedInfo.IsPosted);

        var zeroInfo = await svc.GetDeleteInfoAsync(zeroUsage);
        Assert.True(zeroInfo.CanDelete);
        db.GLAccounts.Remove(zeroUsage);
        await db.SaveChangesAsync();
        Assert.Null(await db.GLAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Code == "6000"));
    }

    [Fact]
    public async Task SystemAccount_CannotBeDeactivatedOrDeleted()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var svc = CreateAccountsService(db);

        var system = await db.GLAccounts.SingleAsync(a => a.Code == "4000");
        Assert.True(svc.IsSystemAccount("4000"));

        var info = await svc.GetDeleteInfoAsync(system);
        Assert.False(info.CanDelete);
        Assert.True(info.IsSystem);
        Assert.False(info.IsPosted);

        var codeChange = await svc.ValidateCodeChangeAsync(system.Id, "4050", "4000");
        Assert.Equal("لا يمكن تغيير رمز حساب النظام", codeChange);
    }

    // ---------- (b) Duplicate Code ----------

    [Fact]
    public async Task DuplicateCode_Rejected_CaseInsensitive()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var svc = CreateAccountsService(db);

        Assert.True(await svc.CodeExistsAsync("4000"));
        Assert.True(await svc.CodeExistsAsync(" 4000 "));
        Assert.False(await svc.CodeExistsAsync("9999"));

        var account = await db.GLAccounts.SingleAsync(a => a.Code == "4000");
        var result = await svc.ApplyEditAsync(account, new GLAccount { Code = "4000", Name = "حساب مكرر", Type = GLAccountType.Revenue, NormalBalance = NormalBalance.Credit, IsActive = true });
        Assert.True(result.Ok);
        Assert.Equal("4000", account.Code);
    }

    // ---------- (c) Budget Manage ----------

    private async Task<(BudgetYear budget, GLAccount rev, GLAccount exp)> SeedBudgetYearAsync(AppDbContext db, int year)
    {
        db.BudgetYears.Add(new BudgetYear { Year = year, IsActive = true });
        await db.SaveChangesAsync();
        var rev = await db.GLAccounts.SingleAsync(a => a.Code == "4000");
        var exp = await db.GLAccounts.SingleAsync(a => a.Code == "5000");
        var budget = await db.BudgetYears.SingleAsync(b => b.Year == year);
        return (budget, rev, exp);
    }

    [Fact]
    public async Task BudgetSave_PersistsAnnualAmounts_AndDuplicate_Overwrites()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (budget, rev, exp) = await SeedBudgetYearAsync(db, 2026);

        db.BudgetLines.Add(new BudgetLine { BudgetYearId = budget.Id, AccountId = rev.Id, AnnualAmount = 50000m });
        db.BudgetLines.Add(new BudgetLine { BudgetYearId = budget.Id, AccountId = exp.Id, AnnualAmount = 30000m });
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.BudgetLines.CountAsync(l => l.BudgetYearId == budget.Id));

        var existing = await db.BudgetLines.SingleAsync(l => l.BudgetYearId == budget.Id && l.AccountId == rev.Id);
        existing.AnnualAmount = 55000m;
        await db.SaveChangesAsync();

        Assert.Single(await db.BudgetLines.AsNoTracking().Where(l => l.BudgetYearId == budget.Id && l.AccountId == rev.Id).ToListAsync());
        Assert.Equal(55000m, (await db.BudgetLines.SingleAsync(l => l.BudgetYearId == budget.Id && l.AccountId == rev.Id)).AnnualAmount);
    }

    [Fact]
    public async Task Budget_DuplicateUniqueness_ForcesSecondRewrite()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (budget, rev, _) = await SeedBudgetYearAsync(db, 2026);

        db.BudgetLines.Add(new BudgetLine { BudgetYearId = budget.Id, AccountId = rev.Id, AnnualAmount = 100m });
        await db.SaveChangesAsync();

        db.BudgetLines.Add(new BudgetLine { BudgetYearId = budget.Id, AccountId = rev.Id, AnnualAmount = 200m });
        var ex = await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.NotNull(ex);

        Assert.Single(await db.BudgetLines.AsNoTracking().Where(l => l.BudgetYearId == budget.Id && l.AccountId == rev.Id).ToListAsync());
    }

    // ---------- (d) BudgetVariance ----------

    [Fact]
    public async Task BudgetVariance_ExactMath_Revenue()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (budget, rev, _) = await SeedBudgetYearAsync(db, 2026);

        var accounting = new AccountingService(db);
        await accounting.RecordSaleInvoiceAsync(new DateTime(2026, 4, 15), 1, 100m, 0m, null, null, "test");
        await accounting.RecordSaleInvoiceAsync(new DateTime(2026, 6, 1), 1, 50m, 0m, null, null, "test");

        db.BudgetLines.Add(new BudgetLine { BudgetYearId = budget.Id, AccountId = rev.Id, AnnualAmount = 120m });
        await db.SaveChangesAsync();

        var financial = new FinancialReportService(db);
        var activity = await financial.GetAccountYearlyActivityAsync(rev.Id, 2026);

        decimal actual = activity.Credit - activity.Debit;
        decimal expectedActual = 150m;
        Assert.Equal(expectedActual, actual);

        var budgetAmount = 120m;
        var variance = actual - budgetAmount;
        Assert.Equal(30m, variance);

        decimal pct = budgetAmount != 0 ? variance / budgetAmount : 0;
        Assert.Equal(0.25m, pct);
    }

    [Fact]
    public async Task BudgetVariance_ExactMath_Expense_SameConvention()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (budget, _, exp) = await SeedBudgetYearAsync(db, 2026);

        var accounting = new AccountingService(db);
        await accounting.PostAsync(JournalSource.PurchaseInvoice, 1, new DateTime(2026, 5, 1), "شراء",
            new[] { new JournalLine("5000", 80m, 0m), new JournalLine("2000", 0m, 80m) }, "test");

        db.BudgetLines.Add(new BudgetLine { BudgetYearId = budget.Id, AccountId = exp.Id, AnnualAmount = 60m });
        await db.SaveChangesAsync();

        var financial = new FinancialReportService(db);
        var activity = await financial.GetAccountYearlyActivityAsync(exp.Id, 2026);
        decimal actual = activity.Debit - activity.Credit;
        Assert.Equal(80m, actual);

        var budgetAmount = 60m;
        var variance = actual - budgetAmount;
        Assert.Equal(20m, variance);
        Assert.Equal(20m / 60m, variance / budgetAmount);
    }

    [Fact]
    public async Task BudgetClosedYear_GuardCondition_BlocksEdit()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (budget, rev, _) = await SeedBudgetYearAsync(db, 2025);

        db.FiscalPeriods.Add(new FiscalPeriod { Year = 2025, IsClosed = true });
        await db.SaveChangesAsync();

        bool closed = await db.FiscalPeriods.AnyAsync(p => p.Year == 2025 && p.IsClosed);
        Assert.True(closed);

        db.BudgetLines.Add(new BudgetLine { BudgetYearId = budget.Id, AccountId = rev.Id, AnnualAmount = 100m });
        await db.SaveChangesAsync();

        var line = await db.BudgetLines.SingleAsync(l => l.BudgetYearId == budget.Id && l.AccountId == rev.Id);
        line.AnnualAmount = 200m;
        await db.SaveChangesAsync();
        Assert.Equal(200m, (await db.BudgetLines.SingleAsync(l => l.BudgetYearId == budget.Id && l.AccountId == rev.Id)).AnnualAmount);
    }

    [Fact]
    public async Task BudgetSave_OnlyPlAccountsAccepted_AssetIgnored()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (budget, rev, _) = await SeedBudgetYearAsync(db, 2026);
        var asset = await db.GLAccounts.SingleAsync(a => a.Code == "1200");

        var plIds = await db.GLAccounts.Where(a => a.IsActive && (a.Type == GLAccountType.Revenue || a.Type == GLAccountType.Expense)).Select(a => a.Id).ToHashSetAsync();

        Assert.Contains(rev.Id, plIds);
        Assert.DoesNotContain(asset.Id, plIds);

        if (plIds.Contains(rev.Id))
        {
            db.BudgetLines.Add(new BudgetLine { BudgetYearId = budget.Id, AccountId = rev.Id, AnnualAmount = 500m });
        }
        if (plIds.Contains(asset.Id))
        {
            db.BudgetLines.Add(new BudgetLine { BudgetYearId = budget.Id, AccountId = asset.Id, AnnualAmount = 300m });
        }
        await db.SaveChangesAsync();

        Assert.Single(await db.BudgetLines.AsNoTracking().Where(l => l.BudgetYearId == budget.Id).ToListAsync());
        Assert.Equal(500m, (await db.BudgetLines.SingleAsync(l => l.BudgetYearId == budget.Id)).AnnualAmount);
    }
}
