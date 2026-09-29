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

/// <summary>
/// A posted return credits account 1200/2000 through the journal but never rewrites the source
/// invoice's own <c>NetAmount</c>/<c>PaidAmount</c> columns. So <c>PaidAmount &lt; NetAmount</c>
/// is NOT a collectable test on its own: after a full return it still holds, the aging report
/// already treats the invoice as closed, and a receipt that trusted the column would allocate
/// 1:1 and leave a credit balance on the receivable that no document explains.
/// <para>
/// These tests pin the candidate set to the same open-amount rule the aging reports use — the
/// delivered/booked value, less allocations, less the <see cref="ReturnValuation"/> credit the
/// return actually posted — and prove the arithmetic stays exact (a one-piastre residue is
/// still refused, never absorbed by a tolerance).
/// </para>
/// </summary>
public sealed class PaymentReturnAllocationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public PaymentReturnAllocationTests()
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
            ("2055", "الضريبة المستحقة", GLAccountType.Liability, NormalBalance.Credit),
            ("4000", "إيرادات المبيعات", GLAccountType.Revenue, NormalBalance.Credit),
            ("5000", "تكلفة البضاعة", GLAccountType.Expense, NormalBalance.Debit),
            ("5101", "مرتجعات المبيعات", GLAccountType.Expense, NormalBalance.Debit),
            ("5102", "مرتجعات المشتريات", GLAccountType.Expense, NormalBalance.Debit),
        };
        foreach (var (code, name, type, normal) in accounts)
            db.GLAccounts.Add(new GLAccount { Code = code, Name = name, Type = type, NormalBalance = normal, IsActive = true });
        db.SaveChanges();
    }

    private static async Task<(int customerId, int supplierId, int itemA, int itemB)> SeedBaseAsync(AppDbContext db)
    {
        var category = new ItemCategory { Name = "تصنيف اختبار" };
        var type = new ItemType { Name = "نوع اختبار" };
        var unit = new Unit { Name = "قطعة" };
        db.ItemCategories.Add(category);
        db.ItemTypes.Add(type);
        db.Units.Add(unit);

        var itemA = new Item
        {
            Name = "صنف أ", Category = category, ItemType = type,
            CountUnit = unit, QuantityUnit = unit, SalePrice = 100m
        };
        var itemB = new Item
        {
            Name = "صنف ب", Category = category, ItemType = type,
            CountUnit = unit, QuantityUnit = unit, SalePrice = 50m
        };
        db.Items.AddRange(itemA, itemB);

        var customer = new Customer { Name = "عميل المرتجعات" };
        var supplier = new Supplier { Name = "مورد المرتجعات" };
        db.Customers.Add(customer);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        return (customer.Id, supplier.Id, itemA.Id, itemB.Id);
    }

    /// <summary>Builds an invoice from its lines, then books it exactly as InventoryService does.</summary>
    private static async Task<SaleInvoice> SeedSaleAsync(AppDbContext db, int customerId, string number,
        DateTime invoiceDate, int itemA, int itemB, (int ItemId, decimal Qty)[] lines,
        decimal discount = 0m, decimal tax = 0m, int? paymentTerms = null)
    {
        var invoice = new SaleInvoice
        {
            InvoiceNumber = number,
            CustomerId = customerId,
            InvoiceDate = invoiceDate,
            PaymentTerms = paymentTerms.HasValue ? (InvoicePaymentTerms)paymentTerms.Value : InvoicePaymentTerms.Net30,
            Discount = discount,
            Tax = tax,
            CreatedAt = DateTime.UtcNow
        };
        foreach (var (itemId, qty) in lines)
        {
            var unitPrice = itemId == itemA ? 100m : 50m;
            invoice.Items.Add(new SaleInvoiceItem
            {
                ItemId = itemId, Quantity = qty, Count = 0m, UnitPrice = unitPrice
            });
        }
        invoice.TotalAmount = decimal.Round(invoice.Items.Sum(i => (i.Quantity > 0 ? i.Quantity : i.Count) * i.UnitPrice), 2);
        invoice.NetAmount = invoice.TotalAmount - discount + tax;
        db.SaleInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    /// <summary>
    /// Delivers (part of) the invoice and posts the same prorated value the delivery path
    /// posts, so the receivable carried by account 1200 is the figure the aging report derives.
    /// </summary>
    private static async Task<DeliveryOrder> DeliverAsync(AppDbContext db, SaleInvoice invoice, string number,
        (int ItemId, decimal Qty)[] lines)
    {
        var order = new DeliveryOrder
        {
            DeliveryNumber = number,
            SaleInvoiceId = invoice.Id,
            CustomerId = invoice.CustomerId,
            DeliveryDate = invoice.InvoiceDate,
            Status = DeliveryOrderStatus.Delivered,
            DeliveredAt = DateTime.UtcNow
        };
        db.DeliveryOrders.Add(order);
        await db.SaveChangesAsync();

        decimal rawValue = 0m;
        foreach (var (itemId, qty) in lines)
        {
            db.DeliveryOrderItems.Add(new DeliveryOrderItem
            {
                DeliveryOrderId = order.Id, ItemId = itemId, Quantity = qty
            });
            rawValue += qty * invoice.Items.First(i => i.ItemId == itemId).UnitPrice;
        }
        await db.SaveChangesAsync();

        decimal value = 0m;
        decimal taxShare = 0m;
        if (invoice.TotalAmount > 0m && invoice.NetAmount >= 0m)
        {
            var share = rawValue / invoice.TotalAmount;
            value = invoice.NetAmount * share;
            if (invoice.Tax > 0m) taxShare = invoice.Tax * share;
        }

        await new AccountingService(db).RecordSaleDeliveryAsync(invoice.InvoiceDate, invoice.CustomerId,
            decimal.Round(value, 2), 0m, "test", null, order.Id, decimal.Round(taxShare, 2));
        return order;
    }

    /// <summary>Posts a sale return through the very mirror the return posting path uses.</summary>
    private static async Task PostSaleReturnAsync(AppDbContext db, SaleInvoice invoice, int itemId, decimal qty)
    {
        var unitPrice = invoice.Items.First(i => i.ItemId == itemId).UnitPrice;
        var saleReturn = new SaleReturn
        {
            ReturnNumber = $"SR-{invoice.InvoiceNumber}-{qty:0.##}",
            CustomerId = invoice.CustomerId,
            SaleInvoiceId = invoice.Id,
            ReturnDate = invoice.InvoiceDate,
            Status = ReturnStatus.Posted,
            TotalAmount = decimal.Round(qty * unitPrice, 2)
        };
        saleReturn.Items.Add(new SaleReturnItem { ItemId = itemId, Quantity = qty, Count = 0m, UnitPrice = unitPrice });
        db.SaleReturns.Add(saleReturn);
        await db.SaveChangesAsync();

        var mirror = ReturnMirror.ProratedAgainst(saleReturn.TotalAmount,
            invoice.TotalAmount, invoice.NetAmount, invoice.Tax);
        await new AccountingService(db).RecordSaleReturnWithCostAsync(saleReturn.ReturnDate, saleReturn.Id,
            saleReturn.CustomerId, mirror.ContraValue, 0m, "test", null, mirror.Tax);
    }

    private static async Task<PurchaseInvoice> SeedPurchaseAsync(AppDbContext db, int supplierId, string number,
        DateTime invoiceDate, decimal total, decimal discount, decimal tax)
    {
        var invoice = new PurchaseInvoice
        {
            InvoiceNumber = number,
            SupplierId = supplierId,
            InvoiceDate = invoiceDate,
            PaymentTerms = InvoicePaymentTerms.Net30,
            TotalAmount = total,
            Discount = discount,
            Tax = tax,
            NetAmount = total - discount + tax,
            CreatedAt = DateTime.UtcNow
        };
        db.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();
        await new AccountingService(db).RecordPurchaseInvoiceAsync(invoiceDate, supplierId, invoice.NetAmount, "test");
        return invoice;
    }

    private static async Task PostPurchaseReturnAsync(AppDbContext db, PurchaseInvoice invoice, decimal returnedGross)
    {
        var purchaseReturn = new PurchaseReturn
        {
            ReturnNumber = $"PR-{invoice.InvoiceNumber}-{returnedGross:0.##}",
            SupplierId = invoice.SupplierId,
            PurchaseInvoiceId = invoice.Id,
            ReturnDate = invoice.InvoiceDate,
            Status = ReturnStatus.Posted,
            TotalAmount = returnedGross
        };
        db.PurchaseReturns.Add(purchaseReturn);
        await db.SaveChangesAsync();

        var mirror = ReturnMirror.ProratedAgainst(purchaseReturn.TotalAmount,
            invoice.TotalAmount, invoice.NetAmount, invoice.Tax);
        await new AccountingService(db).RecordPurchaseReturnWithCostAsync(purchaseReturn.ReturnDate,
            purchaseReturn.Id, purchaseReturn.SupplierId, mirror.Receivable, 0m, "test");
    }

    private static Task<decimal> ArControlAsync(AppDbContext db)
        => db.JournalEntryLines.AsNoTracking().Include(l => l.Account)
            .Where(l => l.Account!.Code == "1200")
            .SumAsync(l => l.Debit - l.Credit);

    private static Task<decimal> ApControlAsync(AppDbContext db)
        => db.JournalEntryLines.AsNoTracking().Include(l => l.Account)
            .Where(l => l.Account!.Code == "2000")
            .SumAsync(l => l.Credit - l.Debit);

    private static Payment Receipt(int customerId, decimal amount, string number, DateTime date)
        => new()
        {
            ReceiptNumber = number,
            Type = PaymentType.Receipt,
            CustomerId = customerId,
            Amount = amount,
            Method = PaymentMethod.Cash,
            PaymentDate = date
        };

    private static Payment Disbursement(int supplierId, decimal amount, string number, DateTime date)
        => new()
        {
            ReceiptNumber = number,
            Type = PaymentType.Disbursement,
            SupplierId = supplierId,
            Amount = amount,
            Method = PaymentMethod.Cash,
            PaymentDate = date
        };

    // ------------------------------------------------------------------
    // (1) A fully returned invoice is not a payment candidate at all.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Receipt_AgainstFullyReturnedInvoice_IsRejected_AndLeavesNoAllocationAndNoJournal()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (customerId, _, itemA, itemB) = await SeedBaseAsync(db);
        var today = DateTime.Today;

        var invoice = await SeedSaleAsync(db, customerId, "S-FULL", today.AddDays(-20), itemA, itemB,
            new[] { (itemA, 10m), (itemB, 4m) }, discount: 200m);
        Assert.Equal(1200m, invoice.TotalAmount);
        Assert.Equal(1000m, invoice.NetAmount);
        await DeliverAsync(db, invoice, "DLV-S-FULL", new[] { (itemA, 10m), (itemB, 4m) });

        // A return for the whole gross credits exactly the net the delivery booked.
        Assert.Equal(1000m, ReturnValuation.ReceivableBase(1200m, invoice.TotalAmount, invoice.NetAmount));
        await PostSaleReturnAsync(db, invoice, itemA, 12m);

        // Precondition: the ledger has already closed the receivable on this invoice...
        Assert.Equal(0m, await ArControlAsync(db));
        // ...while the invoice's own columns still read as open. That gap is the whole bug.
        var stored = await db.SaleInvoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
        Assert.Equal(0m, stored.PaidAmount);
        Assert.False(stored.IsPaid);
        Assert.True(stored.PaidAmount < stored.NetAmount);

        var svc = new PaymentService(db, new AccountingService(db));
        var (ok, error, payment) = await svc.CreatePaymentAsync(
            Receipt(customerId, 1000m, "PAY-FULL", today), "test");

        Assert.False(ok);
        Assert.Null(payment);
        Assert.NotNull(error);
        Assert.Contains("أكبر من إجمالي المستحق", error);
        Assert.Contains("المرتجعات", error);

        Assert.False(await db.Payments.AnyAsync());
        Assert.False(await db.SalePaymentAllocations.AnyAsync());
        Assert.False(await db.PurchasePaymentAllocations.AnyAsync());
        Assert.False(await db.JournalEntries.AnyAsync(e => e.Source == JournalSource.Receipt));
        // The receivable control is still closed: no credit balance left with no document.
        Assert.Equal(0m, await ArControlAsync(db));

        var untouched = await db.SaleInvoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
        Assert.Equal(0m, untouched.PaidAmount);
        Assert.False(untouched.IsPaid);
    }

    // ------------------------------------------------------------------
    // (2) A partially returned invoice is collectable for the net open
    //     amount only, and one piastre more is still refused.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Receipt_AgainstPartiallyReturnedInvoice_AllocatesTheNetOpenAmountAfterTaxAndDiscount()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (customerId, _, itemA, itemB) = await SeedBaseAsync(db);
        var today = DateTime.Today;

        // Total 1200, header discount 150, tax 100 => net 1150.
        var invoice = await SeedSaleAsync(db, customerId, "S-PART", today.AddDays(-20), itemA, itemB,
            new[] { (itemA, 10m), (itemB, 4m) }, discount: 150m, tax: 100m);
        Assert.Equal(1200m, invoice.TotalAmount);
        Assert.Equal(1150m, invoice.NetAmount);
        await DeliverAsync(db, invoice, "DLV-S-PART", new[] { (itemA, 10m), (itemB, 4m) });

        // Half the gross comes back. The credit is f * net = 0.5 * 1150 = 575, NOT the 600 gross:
        // 150 of the discount is never receivable, so returning it must not credit AR with it.
        var mirror = ReturnMirror.ProratedAgainst(600m, invoice.TotalAmount, invoice.NetAmount, invoice.Tax);
        Assert.Equal(525m, mirror.ContraValue);
        Assert.Equal(50m, mirror.Tax);
        Assert.Equal(575m, mirror.Receivable);
        Assert.Equal(575m, ReturnValuation.ReceivableBase(600m, invoice.TotalAmount, invoice.NetAmount));

        await PostSaleReturnAsync(db, invoice, itemA, 6m);
        Assert.Equal(575m, await ArControlAsync(db));

        var svc = new PaymentService(db, new AccountingService(db));

        // The old candidate set offered the raw NetAmount - PaidAmount = 1150. It is gone.
        var (tooBig, bigError, _) = await svc.CreatePaymentAsync(
            Receipt(customerId, 1150m, "PAY-PART-BIG", today), "test");
        Assert.False(tooBig);
        Assert.Contains("أكبر من إجمالي المستحق", bigError);
        Assert.False(await db.SalePaymentAllocations.AnyAsync());
        Assert.False(await db.JournalEntries.AnyAsync(e => e.Source == JournalSource.Receipt));

        // One piastre over the net open amount is a residue the GL cannot absorb: refused.
        var (pennyOver, pennyError, _) = await svc.CreatePaymentAsync(
            Receipt(customerId, 575.01m, "PAY-PART-PENNY", today), "test");
        Assert.False(pennyOver);
        Assert.Contains("أكبر من إجمالي المستحق", pennyError);

        // The net open amount is accepted, and it settles account 1200 to exactly zero.
        var (ok, error, payment) = await svc.CreatePaymentAsync(
            Receipt(customerId, 575m, "PAY-PART-OK", today), "test");
        Assert.True(ok, error);
        Assert.Equal(575m, payment!.Amount);

        var allocation = await db.SalePaymentAllocations.AsNoTracking().SingleAsync();
        Assert.Equal(invoice.Id, allocation.SaleInvoiceId);
        Assert.Equal(575m, allocation.AllocatedAmount);

        var settled = await db.SaleInvoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
        Assert.Equal(575m, settled.PaidAmount);

        var entry = await db.JournalEntries.AsNoTracking().SingleAsync(e => e.Source == JournalSource.Receipt);
        var lines = await db.JournalEntryLines.AsNoTracking().Include(l => l.Account)
            .Where(l => l.JournalEntryId == entry.Id).ToListAsync();
        Assert.Contains(lines, l => l.Account!.Code == "1000" && l.Debit == 575m);
        Assert.Contains(lines, l => l.Account!.Code == "1200" && l.Credit == 575m);
        Assert.Equal(0m, await ArControlAsync(db));

        // Nothing further is collectable: the invoice is closed in the ledger.
        var (extra, _, _) = await svc.CreatePaymentAsync(
            Receipt(customerId, 0.01m, "PAY-PART-EXTRA", today), "test");
        Assert.False(extra);
        Assert.Equal(0m, await ArControlAsync(db));
    }

    // ------------------------------------------------------------------
    // (3) Aging and the payment candidate set agree on the same fixture.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Aging_AndPaymentCandidateSet_Agree_InvoiceAppearsInAgingIffItIsCollectable()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (customerId, _, itemA, itemB) = await SeedBaseAsync(db);
        var today = DateTime.Today;
        var svc = new PaymentService(db, new AccountingService(db));
        var reports = new ReportService(db, new FinancialReportService(db));

        // E is settled first, while it is the only invoice on file, so the later oldest-first
        // sweep of this fixture is not stolen by it.
        var settled = await SeedSaleAsync(db, customerId, "S-PAID", today.AddDays(-40), itemA, itemB,
            new[] { (itemA, 10m) });
        await DeliverAsync(db, settled, "DLV-S-PAID", new[] { (itemA, 10m) });
        var (paidOk, paidErr, _) = await svc.CreatePaymentAsync(
            Receipt(customerId, 1000m, "PAY-E", today.AddDays(-1)), "test");
        Assert.True(paidOk, paidErr);

        // A — delivered in full, nothing returned: collectable 1000.
        var open = await SeedSaleAsync(db, customerId, "S-OPEN", today.AddDays(-35), itemA, itemB,
            new[] { (itemA, 10m) });
        await DeliverAsync(db, open, "DLV-S-OPEN", new[] { (itemA, 10m) });

        // B — delivered in full, then fully returned: the ledger has nothing left on it.
        var returned = await SeedSaleAsync(db, customerId, "S-RETURNED", today.AddDays(-30), itemA, itemB,
            new[] { (itemA, 10m) });
        await DeliverAsync(db, returned, "DLV-S-RETURNED", new[] { (itemA, 10m) });
        await PostSaleReturnAsync(db, returned, itemA, 10m);

        // C — delivered in full, half returned: collectable 500.
        var half = await SeedSaleAsync(db, customerId, "S-HALF", today.AddDays(-25), itemA, itemB,
            new[] { (itemA, 10m) });
        await DeliverAsync(db, half, "DLV-S-HALF", new[] { (itemA, 10m) });
        await PostSaleReturnAsync(db, half, itemA, 5m);

        // D — never delivered: account 1200 never carried it, so it is nobody's receivable.
        await SeedSaleAsync(db, customerId, "S-UNDELIVERED", today.AddDays(-20), itemA, itemB,
            new[] { (itemA, 10m) });

        // F — delivered only halfway: only the delivered half was ever booked, so only that
        // half is collectable even though NetAmount is untouched.
        var partDelivered = await SeedSaleAsync(db, customerId, "S-PARTDELIV", today.AddDays(-15), itemA, itemB,
            new[] { (itemA, 10m) });
        await DeliverAsync(db, partDelivered, "DLV-S-PARTDELIV", new[] { (itemA, 5m) });

        // A + C + F = 1000 + 500 + 500. B, D and E are not in the report.
        var aging = await reports.AgingAsync();
        var row = Assert.Single(aging.Receivables);
        Assert.Equal("عميل المرتجعات", row.PartyName);
        Assert.Equal(2000m, row.Total);
        Assert.Equal(2000m, aging.ArTotal);
        // The same figure the ledger carries on account 1200 for this customer.
        Assert.Equal(2000m, await ArControlAsync(db));

        // One piastre past what aging shows is not collectable, and it leaves nothing behind:
        // the only allocation and the only receipt entry are still E's.
        var (over, overError, _) = await svc.CreatePaymentAsync(
            Receipt(customerId, 2000.01m, "PAY-OVER", today), "test");
        Assert.False(over);
        Assert.Contains("أكبر من إجمالي المستحق", overError);
        var beforeAllocations = await db.SalePaymentAllocations.AsNoTracking().ToListAsync();
        Assert.Single(beforeAllocations);
        Assert.Equal(settled.Id, beforeAllocations[0].SaleInvoiceId);
        Assert.Single(await db.JournalEntries.AsNoTracking()
            .Where(e => e.Source == JournalSource.Receipt).ToListAsync());

        // Exactly what aging shows is collectable, oldest first, and nothing else.
        var (ok, error, payment) = await svc.CreatePaymentAsync(
            Receipt(customerId, 2000m, "PAY-ALL", today), "test");
        Assert.True(ok, error);
        Assert.Equal(2000m, payment!.Amount);

        var allocations = await db.SalePaymentAllocations.AsNoTracking()
            .Where(a => a.PaymentId == payment.Id).ToListAsync();
        Assert.Equal(3, allocations.Count);
        Assert.Equal(2000m, allocations.Sum(a => a.AllocatedAmount));
        var byInvoice = allocations.ToDictionary(a => a.SaleInvoiceId, a => a.AllocatedAmount);
        Assert.Equal(1000m, byInvoice[open.Id]);
        Assert.Equal(500m, byInvoice[half.Id]);
        Assert.Equal(500m, byInvoice[partDelivered.Id]);
        Assert.DoesNotContain(returned.Id, byInvoice.Keys);

        var closed = await reports.AgingAsync();
        Assert.Empty(closed.Receivables);
        Assert.Equal(0m, closed.ArTotal);
        // Every document the ledger carried is now fully settled, to the cent.
        Assert.Equal(0m, await ArControlAsync(db));

        // The returned invoice never comes back as a candidate.
        var (extra, _, _) = await svc.CreatePaymentAsync(
            Receipt(customerId, 1m, "PAY-AFTER", today), "test");
        Assert.False(extra);
        Assert.Equal(0m, await ArControlAsync(db));
    }

    // ------------------------------------------------------------------
    // The payable side gets the mirror treatment.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Disbursement_AgainstFullyReturnedPurchaseInvoice_IsRejected_AndLeavesNoJournal()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (_, supplierId, _, _) = await SeedBaseAsync(db);
        var today = DateTime.Today;

        var invoice = await SeedPurchaseAsync(db, supplierId, "P-FULL", today.AddDays(-20), 1200m, 200m, 0m);
        Assert.Equal(1000m, invoice.NetAmount);
        Assert.Equal(1000m, await ApControlAsync(db));

        Assert.Equal(1000m, ReturnValuation.ReceivableBase(1200m, invoice.TotalAmount, invoice.NetAmount));
        await PostPurchaseReturnAsync(db, invoice, 1200m);
        Assert.Equal(0m, await ApControlAsync(db));

        var stored = await db.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
        Assert.Equal(0m, stored.PaidAmount);
        Assert.True(stored.PaidAmount < stored.NetAmount);

        var svc = new PaymentService(db, new AccountingService(db));
        var (ok, error, payment) = await svc.CreatePaymentAsync(
            Disbursement(supplierId, 1000m, "PAY-PD-FULL", today), "test");

        Assert.False(ok);
        Assert.Null(payment);
        Assert.NotNull(error);
        Assert.Contains("أكبر من إجمالي المستحق", error);
        Assert.Contains("المرتجعات", error);
        Assert.False(await db.Payments.AnyAsync());
        Assert.False(await db.PurchasePaymentAllocations.AnyAsync());
        Assert.False(await db.JournalEntries.AnyAsync(e => e.Source == JournalSource.Disbursement));
        Assert.Equal(0m, await ApControlAsync(db));
    }

    [Fact]
    public async Task Disbursement_AgainstPartiallyReturnedPurchaseInvoice_PaysOnlyTheNetOpenAmount()
    {
        using var db = CreateContext();
        SeedChartOfAccounts(db);
        var (_, supplierId, _, _) = await SeedBaseAsync(db);
        var today = DateTime.Today;

        // Total 1200, discount 150, tax 100 => net 1150.
        var invoice = await SeedPurchaseAsync(db, supplierId, "P-PART", today.AddDays(-20), 1200m, 150m, 100m);
        Assert.Equal(1150m, invoice.NetAmount);
        Assert.Equal(575m, ReturnValuation.ReceivableBase(600m, invoice.TotalAmount, invoice.NetAmount));
        await PostPurchaseReturnAsync(db, invoice, 600m);
        Assert.Equal(575m, await ApControlAsync(db));

        var svc = new PaymentService(db, new AccountingService(db));

        var (tooBig, bigError, _) = await svc.CreatePaymentAsync(
            Disbursement(supplierId, 1150m, "PAY-PD-BIG", today), "test");
        Assert.False(tooBig);
        Assert.Contains("أكبر من إجمالي المستحق", bigError);

        var (ok, error, _) = await svc.CreatePaymentAsync(
            Disbursement(supplierId, 575m, "PAY-PD-OK", today), "test");
        Assert.True(ok, error);

        var allocation = await db.PurchasePaymentAllocations.AsNoTracking().SingleAsync();
        Assert.Equal(invoice.Id, allocation.PurchaseInvoiceId);
        Assert.Equal(575m, allocation.AllocatedAmount);
        Assert.Equal(0m, await ApControlAsync(db));

        var aging = await new ReportService(db, new FinancialReportService(db)).AgingAsync();
        Assert.Empty(aging.Payables);
        Assert.Equal(0m, aging.ApTotal);

        var (extra, _, _) = await svc.CreatePaymentAsync(
            Disbursement(supplierId, 0.01m, "PAY-PD-EXTRA", today), "test");
        Assert.False(extra);
        Assert.Equal(0m, await ApControlAsync(db));
    }
}
