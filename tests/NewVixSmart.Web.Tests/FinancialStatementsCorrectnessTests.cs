using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class FinancialStatementsCorrectnessTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public FinancialStatementsCorrectnessTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static void SeedChart(AppDbContext db)
    {
        (string Code, string Name, GLAccountType Type, NormalBalance Normal, bool IsActive)[] accounts =
        {
            ("1000", "النقد", GLAccountType.Asset, NormalBalance.Debit, true),
            ("1100", "مخصص الديون المشكوك فيها", GLAccountType.Asset, NormalBalance.Credit, true),
            ("1300", "أصل غير نشط", GLAccountType.Asset, NormalBalance.Debit, false),
            ("1301", "أصل غير نشط بلا حركة", GLAccountType.Asset, NormalBalance.Debit, false),
            ("2000", "الدائنون", GLAccountType.Liability, NormalBalance.Credit, true),
            ("3000", "رأس المال", GLAccountType.Equity, NormalBalance.Credit, true),
            ("3001", "الأرباح المحتجزة", GLAccountType.Equity, NormalBalance.Credit, true),
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit, true),
            ("5000", "مصاريف التشغيل", GLAccountType.Expense, NormalBalance.Debit, true)
        };
        foreach (var (code, name, type, normal, isActive) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = isActive });
        }
        db.SaveChanges();
    }

    private static void AddEntry(AppDbContext db, JournalSource source, DateTime date, params (string Code, decimal Debit, decimal Credit)[] lines)
    {
        var entry = new JournalEntry
        {
            EntryNumber = Guid.NewGuid().ToString("N"),
            Date = date,
            Source = source,
            SourceId = 0,
            IsPosted = true
        };
        foreach (var (code, debit, credit) in lines)
        {
            entry.Lines.Add(new JournalEntryLine { Account = db.GLAccounts.Single(a => a.Code == code), Debit = debit, Credit = credit });
        }
        db.JournalEntries.Add(entry);
    }

    [Fact]
    public async Task BalanceSheet_ContraAsset_NegativeBalance_IsRenderedAndSheetStaysBalanced()
    {
        using var db = CreateContext();
        SeedChart(db);

        AddEntry(db, JournalSource.SaleInvoice, new DateTime(2026, 6, 30),
            ("1000", 1000, 0), ("3000", 0, 1000));
        AddEntry(db, JournalSource.PurchaseInvoice, new DateTime(2026, 6, 30),
            ("5000", 100, 0), ("1100", 0, 100));
        await db.SaveChangesAsync();

        var svc = new FinancialReportService(db);
        var vm = await svc.BalanceSheetAsync(new DateTime(2026, 6, 30));

        var contra = Assert.Single(vm.Assets.Lines, l => l.Code == "1100");
        Assert.Equal(-100m, contra.Amount);
        Assert.Equal(900m, vm.TotalAssets);
        Assert.True(vm.IsBalanced);
        Assert.Equal(vm.TotalAssets, vm.TotalLiabilitiesEquity);
    }

    [Fact]
    public async Task YearEndClose_ExcludedFromIncomeStatementAndNetIncome_WhileEquityKeepsRetainedEarnings()
    {
        using var db = CreateContext();
        SeedChart(db);

        AddEntry(db, JournalSource.SaleInvoice, new DateTime(2026, 1, 5),
            ("1000", 1600, 0), ("4000", 0, 1000), ("3000", 0, 600));
        AddEntry(db, JournalSource.PurchaseInvoice, new DateTime(2026, 6, 30),
            ("5000", 600, 0), ("1000", 0, 600));
        AddEntry(db, JournalSource.YearEndClose, new DateTime(2026, 12, 31),
            ("4000", 1000, 0), ("3001", 0, 1000));
        AddEntry(db, JournalSource.YearEndClose, new DateTime(2026, 12, 31),
            ("3001", 600, 0), ("5000", 0, 600));
        await db.SaveChangesAsync();

        var svc = new FinancialReportService(db);

        var income = await svc.IncomeStatementAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
        Assert.Equal(1000m, income.TotalRevenue);
        Assert.Equal(600m, income.TotalExpenses);
        Assert.Equal(400m, income.NetIncome);

        var bs = await svc.BalanceSheetAsync(new DateTime(2026, 12, 31));
        Assert.Equal(1000m, bs.TotalAssets);
        Assert.Equal(1000m, bs.Equity.Total);
        Assert.Equal(0m, bs.NetIncome);
        Assert.Contains(bs.Equity.Lines, l => l.Code == "3001" && l.Amount == 400m);
        Assert.Equal(1000m, bs.TotalLiabilitiesEquity);
        Assert.True(bs.IsBalanced);
    }

    [Fact]
    public async Task BalanceSheet_AfterClose_ShowsClosedIncomeInRetainedEarnings_AndOnlyOpenYearInNetIncome()
    {
        using var db = CreateContext();
        SeedChart(db);

        AddEntry(db, JournalSource.SaleInvoice, new DateTime(2026, 1, 5),
            ("1000", 1600, 0), ("4000", 0, 1000), ("3000", 0, 600));
        AddEntry(db, JournalSource.PurchaseInvoice, new DateTime(2026, 6, 30),
            ("5000", 600, 0), ("1000", 0, 600));
        AddEntry(db, JournalSource.YearEndClose, new DateTime(2026, 12, 31),
            ("4000", 1000, 0), ("3001", 0, 1000));
        AddEntry(db, JournalSource.YearEndClose, new DateTime(2026, 12, 31),
            ("3001", 600, 0), ("5000", 0, 600));
        AddEntry(db, JournalSource.SaleInvoice, new DateTime(2027, 3, 15),
            ("1000", 500, 0), ("4000", 0, 500));
        await db.SaveChangesAsync();

        var svc = new FinancialReportService(db);
        var bs = await svc.BalanceSheetAsync(new DateTime(2027, 6, 30));

        Assert.Equal(1500m, bs.TotalAssets);
        Assert.Equal(1000m, bs.Equity.Total);
        Assert.Equal(500m, bs.NetIncome);
        Assert.Contains(bs.Equity.Lines, l => l.Code == "3001" && l.Amount == 400m);
        Assert.Equal(1500m, bs.TotalLiabilitiesEquity);
        Assert.True(bs.IsBalanced);
    }

    [Fact]
    public async Task TrialBalance_ShowsInactiveAccountsWithActivity_HidesInactiveWithoutActivity()
    {
        using var db = CreateContext();
        SeedChart(db);

        AddEntry(db, JournalSource.SaleInvoice, new DateTime(2026, 1, 10),
            ("1000", 1200, 0), ("3000", 0, 1200));
        AddEntry(db, JournalSource.OpeningStock, new DateTime(2026, 2, 1),
            ("1300", 50, 0), ("1000", 0, 50));
        await db.SaveChangesAsync();

        var svc = new FinancialReportService(db);
        var vm = await svc.TrialBalanceAsync(new DateTime(2026, 6, 30));

        var row = Assert.Single(vm.Rows, r => r.Code == "1300");
        Assert.Equal(50m, row.Balance);
        Assert.DoesNotContain(vm.Rows, r => r.Code == "1301");
        Assert.Equal(vm.TotalDebit, vm.TotalCredit);
        Assert.True(vm.IsBalanced);
    }

    [Fact]
    public async Task Aging_PostedReturn_ReducesOutstanding_DraftDoesNot()
    {
        using var db = CreateContext();
        SeedChart(db);

        var customer = new Customer { Name = "عميل اختبار" };
        var supplier = new Supplier { Name = "مورد اختبار" };
        db.Customers.Add(customer);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        var today = DateTime.Today;
        var sale = new SaleInvoice
        {
            InvoiceNumber = "S-1",
            CustomerId = customer.Id,
            InvoiceDate = today.AddDays(-30),
            PaymentTerms = InvoicePaymentTerms.OnReceipt,
            DueDate = today.AddDays(-30),
            TotalAmount = 1000,
            NetAmount = 1000,
            PaidAmount = 0,
            IsPaid = false
        };
        db.SaleInvoices.Add(sale);

        var purchase = new PurchaseInvoice
        {
            InvoiceNumber = "P-1",
            SupplierId = supplier.Id,
            InvoiceDate = today.AddDays(-30),
            PaymentTerms = InvoicePaymentTerms.OnReceipt,
            DueDate = today.AddDays(-30),
            TotalAmount = 800,
            NetAmount = 800,
            PaidAmount = 0,
            IsPaid = false
        };
        db.PurchaseInvoices.Add(purchase);
        await db.SaveChangesAsync();

        db.DeliveryOrders.Add(new DeliveryOrder
        {
            DeliveryNumber = $"DLV-FSC-{sale.Id}",
            SaleInvoiceId = sale.Id,
            CustomerId = customer.Id,
            DeliveryDate = today.AddDays(-30),
            Status = DeliveryOrderStatus.Delivered,
            DeliveredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        db.SaleReturns.AddRange(
            new SaleReturn { ReturnNumber = "SR-1-P", CustomerId = customer.Id, SaleInvoiceId = sale.Id, Status = ReturnStatus.Posted, TotalAmount = 400, ReturnDate = today.AddDays(-10) },
            new SaleReturn { ReturnNumber = "SR-2-D", CustomerId = customer.Id, SaleInvoiceId = sale.Id, Status = ReturnStatus.Draft, TotalAmount = 100, ReturnDate = today.AddDays(-5) }
        );
        db.PurchaseReturns.AddRange(
            new PurchaseReturn { ReturnNumber = "PR-1-P", SupplierId = supplier.Id, PurchaseInvoiceId = purchase.Id, Status = ReturnStatus.Posted, TotalAmount = 300, ReturnDate = today.AddDays(-10) },
            new PurchaseReturn { ReturnNumber = "PR-2-D", SupplierId = supplier.Id, PurchaseInvoiceId = purchase.Id, Status = ReturnStatus.Draft, TotalAmount = 50, ReturnDate = today.AddDays(-5) }
        );
        await db.SaveChangesAsync();

        var svc = new ReportService(db, new FinancialReportService(db));
        var vm = await svc.AgingAsync();

        var receivable = Assert.Single(vm.Receivables);
        Assert.Equal(600m, receivable.Total);
        Assert.Equal(600m, vm.ArTotal);

        var payable = Assert.Single(vm.Payables);
        Assert.Equal(500m, payable.Total);
        Assert.Equal(500m, vm.ApTotal);
    }
}