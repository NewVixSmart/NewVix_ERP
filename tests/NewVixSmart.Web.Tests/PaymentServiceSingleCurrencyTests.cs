using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Api.Dtos;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// Single-currency (EGP) settlement invariants. These replace the former
/// milestone-M9 foreign-exchange suite: the ledger has no exchange rate, so a
/// receipt is always exactly its amount in EGP and no FX gain/loss leg exists.
/// </summary>
public sealed class PaymentServiceSingleCurrencyTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public PaymentServiceSingleCurrencyTests()
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
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
        };
        foreach (var (code, name, type, normal) in accounts)
        {
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        }
        db.SaveChanges();
    }

    private async Task<int> SeedCustomerAsync(AppDbContext db)
    {
        db.Customers.Add(new Customer { Name = "عميل اختبار" });
        await db.SaveChangesAsync();
        return (await db.Customers.SingleAsync()).Id;
    }

    private static async Task MarkDeliveredAsync(AppDbContext db, int invoiceId, int customerId)
    {
        db.DeliveryOrders.Add(new DeliveryOrder
        {
            DeliveryNumber = $"DLV-EGP-{invoiceId}",
            SaleInvoiceId = invoiceId,
            CustomerId = customerId,
            DeliveryDate = DateTime.Today,
            Status = DeliveryOrderStatus.Delivered,
            DeliveredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<SaleInvoice> SeedSaleAsync(AppDbContext db, int customerId, decimal net)
    {
        var inv = new SaleInvoice
        {
            InvoiceNumber = $"SI-EGP-{Guid.NewGuid():N}".Substring(0, 16),
            CustomerId = customerId,
            NetAmount = net,
            TotalAmount = net,
            CreatedAt = DateTime.UtcNow
        };
        db.SaleInvoices.Add(inv);
        await db.SaveChangesAsync();
        await MarkDeliveredAsync(db, inv.Id, customerId);
        return inv;
    }

    /// <summary>
    /// The invoice-after-delivery flow: the invoice is produced from a sales order, so its
    /// DeliveryOrder belongs to the order and deliberately keeps SaleInvoiceId null
    /// (CK_DeliveryOrders_SingleSource forbids both sources at once). The invoice's link to
    /// the delivery is the DeliveryIssue that carries it. This is the shape that the old
    /// DeliveryOrders-only lookup could not see, so the invoice was collectible in the GL
    /// but unreachable by both the receipt allocation and the AR aging report.
    /// </summary>
    private static async Task<SaleInvoice> SeedOrderBackedSaleAsync(AppDbContext db, int customerId, decimal net)
    {
        var inv = new SaleInvoice
        {
            InvoiceNumber = $"SI-ORD-{Guid.NewGuid():N}".Substring(0, 16),
            CustomerId = customerId,
            NetAmount = net,
            TotalAmount = net,
            PostingMode = SalesPostingMode.AtInvoice,
            CreatedAt = DateTime.UtcNow
        };
        db.SaleInvoices.Add(inv);
        await db.SaveChangesAsync();

        var order = new DeliveryOrder
        {
            DeliveryNumber = $"DLV-ORD-{Guid.NewGuid():N}".Substring(0, 16),
            CustomerId = customerId,
            DeliveryDate = DateTime.Today,
            Status = DeliveryOrderStatus.Delivered,
            DeliveredAt = DateTime.UtcNow
        };
        db.DeliveryOrders.Add(order);
        await db.SaveChangesAsync();

        db.DeliveryIssues.Add(new DeliveryIssue
        {
            IssueNumber = $"ISS-{Guid.NewGuid():N}".Substring(0, 16),
            DeliveryOrderId = order.Id,
            CustomerId = customerId,
            SaleInvoiceId = inv.Id,
            IssueDate = DateTime.Today,
            Status = DeliveryIssueStatus.Issued,
            IssuedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return inv;
    }

    [Fact]
    public async Task Receipt_SettlesInvoiceIssuedThroughDeliveryIssue_NotDeliveryOrder()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var cust = await SeedCustomerAsync(db);
        var inv = await SeedOrderBackedSaleAsync(db, cust, 7500m);

        // Precondition: the order-first shape really has no invoice-linked DeliveryOrder,
        // otherwise this test would pass for the wrong reason.
        Assert.False(await db.DeliveryOrders.AnyAsync(d => d.SaleInvoiceId == inv.Id));
        Assert.True(await db.DeliveryIssues.AnyAsync(i => i.SaleInvoiceId == inv.Id && i.Status == DeliveryIssueStatus.Issued));

        var svc = new PaymentService(db, new AccountingService(db));
        var (ok, error, payment) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-ORD-1",
            Type = PaymentType.Receipt,
            CustomerId = cust,
            Amount = 7500m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "test");

        Assert.True(ok, error);
        Assert.Equal(7500m, payment!.Amount);
        var settled = await db.SaleInvoices.SingleAsync(i => i.Id == inv.Id);
        Assert.Equal(7500m, settled.PaidAmount);
        Assert.True(settled.IsPaid);
        Assert.Equal(7500m, (await db.SalePaymentAllocations.SingleAsync()).AllocatedAmount);
    }

    [Fact]
    public async Task Receipt_RejectsOverpayment_AndLeavesNoJournalEntry()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var cust = await SeedCustomerAsync(db);
        await SeedSaleAsync(db, cust, 1000m);
        var svc = new PaymentService(db, new AccountingService(db));

        var (ok, error, payment) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-R-OVER",
            Type = PaymentType.Receipt,
            CustomerId = cust,
            Amount = 1000.5m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "test");

        Assert.False(ok);
        Assert.Null(payment);
        Assert.Contains("أكبر من إجمالي المستحق", error);
        Assert.False(await db.Payments.AnyAsync());
        Assert.False(await db.JournalEntries.AnyAsync());
    }

    // (1) A receipt settles its invoice exactly: amount in, same amount out, no FX leg.
    [Fact]
    public async Task Receipt_SettlesInvoice_Exactly_NoFxLegs()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var cust = await SeedCustomerAsync(db);
        await SeedSaleAsync(db, cust, 1000m);
        var svc = new PaymentService(db, new AccountingService(db));

        var (ok, error, payment) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-R-1",
            Type = PaymentType.Receipt,
            CustomerId = cust,
            Amount = 1000m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "test");

        Assert.True(ok, error);
        Assert.Equal(1000m, payment!.Amount);
        var inv = await db.SaleInvoices.SingleAsync();
        Assert.Equal(1000m, inv.PaidAmount);
        Assert.True(inv.IsPaid);

        var allocation = await db.SalePaymentAllocations.SingleAsync();
        Assert.Equal(1000m, allocation.AllocatedAmount);

        var entry = await db.JournalEntries.SingleAsync();
        Assert.Equal(JournalSource.Receipt, entry.Source);
        var lines = await db.JournalEntryLines.Include(l => l.Account).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Account!.Code == "1000" && l.Debit == 1000m);
        Assert.Contains(lines, l => l.Account!.Code == "1200" && l.Credit == 1000m);
        Assert.Equal(1000m, lines.Sum(l => l.Debit));
        Assert.Equal(1000m, lines.Sum(l => l.Credit));
    }

    // (2) Overpayment is rejected and fully rolled back.
    [Fact]
    public async Task Receipt_OverPayment_Rejected_NoSideEffects()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var cust = await SeedCustomerAsync(db);
        await SeedSaleAsync(db, cust, 1000m);
        var svc = new PaymentService(db, new AccountingService(db));

        var (ok, error, payment) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-R-2",
            Type = PaymentType.Receipt,
            CustomerId = cust,
            Amount = 1100m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "test");

        Assert.False(ok);
        Assert.Contains("المستحق", error);
        Assert.Null(payment);
        Assert.Equal(0, await db.Payments.CountAsync());
        Assert.Equal(0, await db.JournalEntries.CountAsync());
        Assert.Equal(0, await db.SalePaymentAllocations.CountAsync() + await db.PurchasePaymentAllocations.CountAsync());
        Assert.Equal(0m, (await db.SaleInvoices.SingleAsync()).PaidAmount);
    }

    // (3) Allocations are retrievable through the payment navigation.
    [Fact]
    public async Task Payment_IncludesPaymentAllocations_OnLoad()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var cust = await SeedCustomerAsync(db);
        await SeedSaleAsync(db, cust, 1000m);
        var svc = new PaymentService(db, new AccountingService(db));

        var (ok, error, payment) = await svc.CreatePaymentAsync(new Payment
        {
            ReceiptNumber = "PAY-R-3",
            Type = PaymentType.Receipt,
            CustomerId = cust,
            Amount = 400m,
            Method = PaymentMethod.Cash,
            PaymentDate = DateTime.Today
        }, "test");
        Assert.True(ok, error);

        var loaded = await svc.GetPaymentAsync(payment!.Id);
        Assert.NotNull(loaded);
        var allocations = loaded!.SalePaymentAllocations!.ToList();
        Assert.Single(allocations);
        Assert.Equal(400m, allocations[0].AllocatedAmount);
        Assert.Equal("عميل اختبار", loaded.Customer!.Name);
    }

    // (4) The API maps the EGP amount straight through to the service.
    [Fact]
    public async Task ApiCreatePayment_MapsAmount_AndSettles()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var cust = await SeedCustomerAsync(db);
        await SeedSaleAsync(db, cust, 1000m);
        var svc = new PaymentService(db, new AccountingService(db));

        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var controller = new Api.PaymentsController(svc, http)
        {
            ControllerContext = new ControllerContext { HttpContext = http.HttpContext! }
        };

        var actionResult = await controller.CreatePayment(new CreatePaymentRequest
        {
            Type = "receipt",
            CustomerId = cust,
            Amount = 400m,
            Method = "Cash"
        });

        var objectResult = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<ApiResponse<PaymentResponse>>(objectResult.Value);
        Assert.True(response.Success);
        Assert.Equal(400m, response.Data!.Amount);
        var inv = await db.SaleInvoices.SingleAsync();
        Assert.Equal(400m, inv.PaidAmount);
        Assert.False(inv.IsPaid);
        Assert.Equal(400m, (await db.SalePaymentAllocations.SingleAsync()).AllocatedAmount);
    }
}
