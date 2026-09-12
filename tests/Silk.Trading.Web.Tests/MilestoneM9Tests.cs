using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Api;
using Silk.Trading.Web.Api.Dtos;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Models.Purchases;
using Silk.Trading.Web.Models.Sales;
using Silk.Trading.Web.Services;
using Xunit;

namespace Silk.Trading.Web.Tests;

public sealed class MilestoneM9Tests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public MilestoneM9Tests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    private static void SeedChartOfAccounts(AppDbContext db)
    {
        (string Code, string Name, GLAccountType Type, NormalBalance Normal)[] accounts =
        {
            ("1000", "النقد / الصندوق", GLAccountType.Asset, NormalBalance.Debit),
            ("1100", "البنك", GLAccountType.Asset, NormalBalance.Debit),
            ("1200", "المدينون (العملاء)", GLAccountType.Asset, NormalBalance.Debit),
            ("1300", "المخزون", GLAccountType.Asset, NormalBalance.Debit),
            ("2000", "الدائنون (الموردون)", GLAccountType.Liability, NormalBalance.Credit),
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("4400", "خسائر فروقات العملة (عملة أجنبية)", GLAccountType.Expense, NormalBalance.Debit),
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
            ("8400", "أرباح فروقات العملة (عملة أجنبية)", GLAccountType.Revenue, NormalBalance.Credit),
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.SaveChanges();
    }

    private async Task SeedCurrenciesAsync(AppDbContext db)
    {
        db.Currencies.Add(new Currency { Code = "SDG", Name = "جنيه سوداني", Symbol = "ج.س", ExchangeRate = 1m, IsBase = true, IsActive = true });
        db.Currencies.Add(new Currency { Code = "USD", Name = "دولار أمريكي", Symbol = "$", ExchangeRate = 500m, IsBase = false, IsActive = true });
        await db.SaveChangesAsync();
    }

    private async Task<int> SeedCustomerAsync(AppDbContext db)
    {
        db.Customers.Add(new Customer { Name = "عميل فروقات" });
        await db.SaveChangesAsync();
        return (await db.Customers.SingleAsync()).Id;
    }

    private async Task<int> SeedSupplierAsync(AppDbContext db)
    {
        db.Suppliers.Add(new Supplier { Name = "مورد فروقات" });
        await db.SaveChangesAsync();
        return (await db.Suppliers.SingleAsync()).Id;
    }

    private static async Task<SaleInvoice> SeedForeignSaleAsync(AppDbContext db, int customerId, int usdId, decimal rate, decimal net)
    {
        var inv = new SaleInvoice
        {
            InvoiceNumber = $"SI-FX-{Guid.NewGuid():N}".Substring(0, 16),
            CustomerId = customerId,
            CurrencyId = usdId,
            ExchangeRate = rate,
            NetAmount = net,
            TotalAmount = net,
            CreatedAt = DateTime.UtcNow
        };
        db.SaleInvoices.Add(inv);
        await db.SaveChangesAsync();
        return inv;
    }

    private static async Task<Models.Purchases.PurchaseInvoice> SeedForeignPurchaseAsync(AppDbContext db, int supplierId, int usdId, decimal rate, decimal net)
    {
        var inv = new Models.Purchases.PurchaseInvoice
        {
            InvoiceNumber = $"PI-FX-{Guid.NewGuid():N}".Substring(0, 16),
            SupplierId = supplierId,
            CurrencyId = usdId,
            ExchangeRate = rate,
            NetAmount = net,
            TotalAmount = net,
            CreatedAt = DateTime.UtcNow
        };
        db.PurchaseInvoices.Add(inv);
        await db.SaveChangesAsync();
        return inv;
    }

    private static async Task<PaymentService> NewPaymentServiceAsync(AppDbContext db)
    {
        await Task.CompletedTask;
        return new PaymentService(db, new AccountingService(db));
    }

    private static async Task<bool> HasFxLineAsync(AppDbContext db, string code) =>
        await db.JournalEntryLines.AnyAsync(l => l.Account!.Code == code);

    // (a1) Base payment: settlement with the base currency posts a plain receipt, no FX legs.
    [Fact]
    public async Task BaseReceipt_NoForeignCurrency_NoFxLegs()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var cust = await SeedCustomerAsync(db);
        db.SaleInvoices.Add(new SaleInvoice { InvoiceNumber = "SI-1000", CustomerId = cust, NetAmount = 1000m, TotalAmount = 1000m, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var svc = await NewPaymentServiceAsync(db);

        var (ok, error, payment) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-R-1", Type = PaymentType.Receipt, CustomerId = cust,
            Amount = 1000m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today
        }, "test");

        Assert.True(ok, error);
        Assert.Equal(1000m, payment!.BaseAmount);
        var inv = await db.SaleInvoices.SingleAsync();
        Assert.Equal(1000m, inv.PaidAmount);
        Assert.True(inv.IsPaid);

        var allocation = await db.PaymentAllocations.SingleAsync();
        Assert.Equal(0m, allocation.FxGain);
        Assert.Equal(0m, allocation.FxLoss);
        Assert.Null(allocation.ExchangeRateAtSettlement);

        var entry = await db.JournalEntries.SingleAsync();
        Assert.Equal(JournalSource.Receipt, entry.Source);
        var lines = await db.JournalEntryLines.ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.False(await HasFxLineAsync(db, "4400"));
        Assert.False(await HasFxLineAsync(db, "8400"));
        Assert.Equal(1000m, lines.Single(l => l.Debit > 0).Debit);
        Assert.Equal(1000m, lines.Single(l => l.Credit > 0).Credit);
    }

    // (a2) Base payment exceeding outstanding is rejected and fully rolled back.
    [Fact]
    public async Task BasePayment_OverPayment_Rejected_NoSideEffects()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var cust = await SeedCustomerAsync(db);
        db.SaleInvoices.Add(new SaleInvoice { InvoiceNumber = "SI-1001", CustomerId = cust, NetAmount = 1000m, TotalAmount = 1000m, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var svc = await NewPaymentServiceAsync(db);

        var (ok, error, payment) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-R-2", Type = PaymentType.Receipt, CustomerId = cust,
            Amount = 1100m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today
        }, "test");

        Assert.False(ok);
        Assert.Contains("المستحق", error);
        Assert.Null(payment);
        Assert.Equal(0, await db.Payments.CountAsync());
        Assert.Equal(0, await db.JournalEntries.CountAsync());
        Assert.Equal(0, await db.PaymentAllocations.CountAsync());
        Assert.Equal(0m, (await db.SaleInvoices.SingleAsync()).PaidAmount);
    }

    // (b) Foreign payment at the same rate books no FX gain/loss.
    [Fact]
    public async Task Foreign_SameRate_NoFxLegs_ButBaseAmountAndAllocationPersist()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedCurrenciesAsync(db);
        var cust = await SeedCustomerAsync(db);
        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        await SeedForeignSaleAsync(db, cust, usd.Id, 500m, 1000m);
        var svc = await NewPaymentServiceAsync(db);

        var (ok, error, payment) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-R-3", Type = PaymentType.Receipt, CustomerId = cust,
            Amount = 2m, CurrencyId = usd.Id, ExchangeRate = 500m,
            Method = PaymentMethod.BankTransfer, PaymentDate = DateTime.Today
        }, "test");

        Assert.True(ok, error);
        Assert.Equal(1000m, payment!.BaseAmount);
        Assert.Equal(1000m, payment.Amount * payment.ExchangeRate!.Value);

        var allocation = await db.PaymentAllocations.SingleAsync();
        Assert.Equal(500m, allocation.ExchangeRateAtSettlement);
        Assert.Equal(0m, allocation.FxGain);
        Assert.Equal(0m, allocation.FxLoss);
        Assert.Equal(1000m, allocation.AllocatedBaseAmount);

        Assert.Equal(2, await db.JournalEntryLines.CountAsync());
        Assert.False(await HasFxLineAsync(db, "4400"));
        Assert.False(await HasFxLineAsync(db, "8400"));
    }

    // (c) Foreign receipt at a higher rate realizes a gain (Cr 8400).
    [Fact]
    public async Task Receipt_HigherRate_PostsFxGain8400()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedCurrenciesAsync(db);
        var cust = await SeedCustomerAsync(db);
        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        var invoice = await SeedForeignSaleAsync(db, cust, usd.Id, 500m, 1000m);
        var svc = await NewPaymentServiceAsync(db);

        var (ok, error, payment) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-R-4", Type = PaymentType.Receipt, CustomerId = cust,
            Amount = 2m, CurrencyId = usd.Id, ExchangeRate = 520m,
            Method = PaymentMethod.Cash, PaymentDate = DateTime.Today
        }, "test");

        Assert.True(ok, error);
        Assert.Equal(1040m, payment!.BaseAmount);

        var allocation = await db.PaymentAllocations.SingleAsync();
        Assert.Equal(PaymentAllocationInvoiceType.Sales, allocation.InvoiceType);
        Assert.Equal(invoice.Id, allocation.InvoiceId);
        Assert.Equal(40m, allocation.FxGain);
        Assert.Equal(0m, allocation.FxLoss);

        var lines = await db.JournalEntryLines.Include(l => l.Account).ToListAsync();
        Assert.Equal(3, lines.Count);
        var gainLine = lines.Single(l => l.Account!.Code == "8400");
        Assert.Equal(40m, gainLine.Credit);
        Assert.Equal(0m, gainLine.Debit);
        var cashLine = lines.Single(l => l.Account!.Code == "1000");
        Assert.Equal(1040m, cashLine.Debit);
        var arLine = lines.Single(l => l.Account!.Code == "1200");
        Assert.Equal(1000m, arLine.Credit);
        Assert.Equal(1040m, lines.Sum(l => l.Debit));
        Assert.Equal(1040m, lines.Sum(l => l.Credit));
    }

    // (d) Foreign disbursement at a higher rate realizes a loss (Dr 4400).
    [Fact]
    public async Task Disbursement_HigherRate_PostsFxLoss4400()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedCurrenciesAsync(db);
        var sup = await SeedSupplierAsync(db);
        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        var invoice = await SeedForeignPurchaseAsync(db, sup, usd.Id, 500m, 1000m);
        var svc = await NewPaymentServiceAsync(db);

        var (ok, error, payment) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-P-1", Type = PaymentType.Disbursement, SupplierId = sup,
            Amount = 2m, CurrencyId = usd.Id, ExchangeRate = 520m,
            Method = PaymentMethod.Cash, PaymentDate = DateTime.Today
        }, "test");

        Assert.True(ok, error);
        Assert.Equal(1040m, payment!.BaseAmount);

        var allocation = await db.PaymentAllocations.SingleAsync();
        Assert.Equal(PaymentAllocationInvoiceType.Purchases, allocation.InvoiceType);
        Assert.Equal(invoice.Id, allocation.InvoiceId);
        Assert.Equal(0m, allocation.FxGain);
        Assert.Equal(40m, allocation.FxLoss);

        var lines = await db.JournalEntryLines.Include(l => l.Account).ToListAsync();
        Assert.Equal(3, lines.Count);
        var lossLine = lines.Single(l => l.Account!.Code == "4400");
        Assert.Equal(40m, lossLine.Debit);
        var apLine = lines.Single(l => l.Account!.Code == "2000");
        Assert.Equal(1000m, apLine.Debit);
        var cashLine = lines.Single(l => l.Account!.Code == "1000");
        Assert.Equal(1040m, cashLine.Credit);
        Assert.Equal(1040m, lines.Sum(l => l.Debit));
        Assert.Equal(1040m, lines.Sum(l => l.Credit));
    }

    // (e) Payment allocations are retrievable through the payment navigation.
    [Fact]
    public async Task Payment_IncludesPaymentAllocations_OnLoad()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedCurrenciesAsync(db);
        var cust = await SeedCustomerAsync(db);
        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        await SeedForeignSaleAsync(db, cust, usd.Id, 500m, 1000m);
        var svc = await NewPaymentServiceAsync(db);

        var (ok, _, payment) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-R-5", Type = PaymentType.Receipt, CustomerId = cust,
            Amount = 2m, CurrencyId = usd.Id, ExchangeRate = 520m,
            Method = PaymentMethod.Cash, PaymentDate = DateTime.Today
        }, "test");
        Assert.True(ok);

        var loaded = await svc.GetPaymentAsync(payment!.Id);
        Assert.NotNull(loaded);
        var allocations = loaded!.PaymentAllocations!.ToList();
        Assert.Single(allocations);
        Assert.Equal(40m, allocations[0].FxGain);
        Assert.Equal(PaymentAllocationInvoiceType.Sales, allocations[0].InvoiceType);
        Assert.NotNull(loaded.Currency);
        Assert.Equal("USD", loaded.Currency!.Code);
    }

    // (f) API CreatePayment maps currency fields through to the service.
    [Fact]
    public async Task ApiCreatePayment_WithCurrency_MapsAndSettles()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedCurrenciesAsync(db);
        var cust = await SeedCustomerAsync(db);
        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        await SeedForeignSaleAsync(db, cust, usd.Id, 500m, 1000m);
        var svc = new PaymentService(db, new AccountingService(db));

        var controller = new Api.PaymentsController(svc)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var actionResult = await controller.CreatePayment(new CreatePaymentRequest
        {
            Type = "receipt",
            CustomerId = cust,
            Amount = 2m,
            CurrencyId = usd.Id,
            ExchangeRate = 520m,
            Method = "Cash"
        });

        var objectResult = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<ApiResponse<Payment>>(objectResult.Value);
        Assert.True(response.Success);
        Assert.Equal(usd.Id, response.Data!.CurrencyId);
        Assert.Equal(520m, response.Data.ExchangeRate);
        Assert.Equal(1040m, response.Data.BaseAmount);
        Assert.Equal(2m, (await db.SaleInvoices.SingleAsync()).PaidAmount); // paid in invoice's own (foreign) units
        Assert.False((await db.SaleInvoices.SingleAsync()).IsPaid); // only 2 of 1000 USD settled
        Assert.Equal(40m, (await db.PaymentAllocations.SingleAsync()).FxGain);
    }

    // (g) Base payment against a foreign invoice is applied in invoice (foreign) units — no unit mixing.
    [Fact]
    public async Task BasePayment_OnForeignInvoice_AppliesInInvoiceUnits()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedCurrenciesAsync(db);
        var cust = await SeedCustomerAsync(db);
        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        await SeedForeignSaleAsync(db, cust, usd.Id, 500m, 1000m);
        var svc = await NewPaymentServiceAsync(db);

        var (ok, error, payment) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-R-6", Type = PaymentType.Receipt, CustomerId = cust,
            Amount = 1000m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today
        }, "test");

        Assert.True(ok, error);
        var inv = await db.SaleInvoices.SingleAsync();
        Assert.Equal(2m, inv.PaidAmount); // 1000 SDG @500 covers 2 of 1000 USD
        Assert.False(inv.IsPaid);
        var allocation = await db.PaymentAllocations.SingleAsync();
        Assert.Equal(1000m, allocation.AllocatedBaseAmount);
        Assert.Null(allocation.ExchangeRateAtSettlement);
        Assert.Equal(0m, allocation.FxGain);
    }

    // (h) Full base coverage of a foreign invoice pays it off using invoice units.
    [Fact]
    public async Task BasePayment_FullCoverage_OfForeignInvoice_MarksPaid()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedCurrenciesAsync(db);
        var cust = await SeedCustomerAsync(db);
        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        await SeedForeignSaleAsync(db, cust, usd.Id, 500m, 1000m);
        var svc = await NewPaymentServiceAsync(db);

        var (ok, error, _) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-R-7", Type = PaymentType.Receipt, CustomerId = cust,
            Amount = 500000m, Method = PaymentMethod.Cash, PaymentDate = DateTime.Today
        }, "test");

        Assert.True(ok, error);
        var inv = await db.SaleInvoices.SingleAsync();
        Assert.Equal(1000m, inv.PaidAmount);
        Assert.True(inv.IsPaid);
        Assert.Equal(500000m, (await db.PaymentAllocations.SingleAsync()).AllocatedBaseAmount);
    }

    // (i) Partial foreign payments accumulate in invoice foreign units across steps.
    [Fact]
    public async Task ForeignPayment_PartialSteps_AccumulateInInvoiceUnits()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        await SeedCurrenciesAsync(db);
        var cust = await SeedCustomerAsync(db);
        var usd = await db.Currencies.SingleAsync(c => c.Code == "USD");
        await SeedForeignSaleAsync(db, cust, usd.Id, 500m, 1000m);
        var svc = await NewPaymentServiceAsync(db);

        var (ok1, err1, _) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-R-8", Type = PaymentType.Receipt, CustomerId = cust,
            Amount = 1m, CurrencyId = usd.Id, ExchangeRate = 500m,
            Method = PaymentMethod.Cash, PaymentDate = DateTime.Today
        }, "test");
        Assert.True(ok1, err1);
        var inv = await db.SaleInvoices.SingleAsync();
        Assert.Equal(1m, inv.PaidAmount);
        Assert.False(inv.IsPaid);

        var (ok2, err2, _) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-R-9", Type = PaymentType.Receipt, CustomerId = cust,
            Amount = 2m, CurrencyId = usd.Id, ExchangeRate = 500m,
            Method = PaymentMethod.Cash, PaymentDate = DateTime.Today
        }, "test");
        Assert.True(ok2, err2);
        Assert.Equal(3m, (await db.SaleInvoices.SingleAsync()).PaidAmount);

        var (ok3, err3, _) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-R-10", Type = PaymentType.Receipt, CustomerId = cust,
            Amount = 997m, CurrencyId = usd.Id, ExchangeRate = 500m,
            Method = PaymentMethod.Cash, PaymentDate = DateTime.Today
        }, "test");
        Assert.True(ok3, err3);
        var paid = await db.SaleInvoices.SingleAsync();
        Assert.Equal(1000m, paid.PaidAmount);
        Assert.True(paid.IsPaid);
    }
}