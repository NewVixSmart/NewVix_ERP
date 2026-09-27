using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Services;

public sealed class DeliveriesInvoicingService : IDeliveriesInvoicingService
{
    private const int MaxAttempts = 3;
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IAccountingService? _accounting;

    public DeliveriesInvoicingService(AppDbContext db, IInventoryService inventory, IAccountingService? accounting = null)
    {
        _db = db;
        _inventory = inventory;
        _accounting = accounting;
    }

    public async Task<IReadOnlyList<int>> GetOutstandingIssueIdsAsync(int salesOrderId)
        => await _db.DeliveryIssues.AsNoTracking()
            .Where(i => i.SalesOrderId == salesOrderId
                && i.Status == DeliveryIssueStatus.Issued
                && i.SaleInvoiceId == null)
            .OrderBy(i => i.IssueDate).ThenBy(i => i.Id)
            .Select(i => i.Id)
            .ToListAsync();

    public async Task<(bool Success, string? Error, SaleInvoice? Invoice)> CreateInvoiceFromIssuesAsync(
        IReadOnlyList<int> issueIds, SaleInvoice invoice, string? user, int? branchId = null)
    {
        var ids = issueIds.Where(i => i > 0).Distinct().ToList();
        if (ids.Count == 0) return (false, "يرجى اختيار أمر تسليم واحد على الأقل", null);

        var issues = await _db.DeliveryIssues.AsNoTracking()
            .Include(i => i.Items)
            .Include(i => i.SalesOrder)
            .Where(i => ids.Contains(i.Id))
            .ToListAsync();
        if (issues.Count != ids.Count) return (false, "يوجد أمر تسليم غير موجود", null);

        if (issues.Any(i => i.Status == DeliveryIssueStatus.Cancelled))
            return (false, "لا يمكن فاتورة أمر تسليم ملغي", null);
        if (issues.Any(i => i.Status != DeliveryIssueStatus.Issued))
            return (false, "يمكن فاتورة أوامر التسليم المرحّلة فقط", null);
        if (issues.Any(i => i.SaleInvoiceId.HasValue))
            return (false, "أمر التسليم مفوتر بالفعل", null);

        var customerIds = issues.Select(i => i.CustomerId).Distinct().ToList();
        if (customerIds.Count != 1) return (false, "يجب أن تكون أوامر التسليم لعميل واحد", null);
        var customerId = customerIds[0];
        if (invoice.CustomerId > 0 && invoice.CustomerId != customerId)
            return (false, "العميل المختار لا يطابق عميل أوامر التسليم", null);
        invoice.CustomerId = customerId;

        var lines = issues.SelectMany(i => i.Items)
            .Where(x => x.ItemId > 0 && (x.Quantity > 0 || x.Count > 0))
            .ToList();
        if (lines.Count == 0) return (false, "أوامر التسليم لا تحتوي على أصناف صالحة للفوترة", null);

        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await _db.Items.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id);
        var orderLineIds = lines.Where(l => l.SalesOrderItemId.HasValue)
            .Select(l => l.SalesOrderItemId!.Value).Distinct().ToList();
        var orderLines = orderLineIds.Count == 0
            ? []
            : await _db.SalesOrderItems.AsNoTracking()
                .Where(i => orderLineIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id);

        var invoiceItems = new List<SaleInvoiceItem>();
        foreach (var group in lines.GroupBy(l => l.ItemId))
        {
            if (!items.TryGetValue(group.Key, out var item))
                return (false, $"الصنف رقم {group.Key} غير موجود", null);

            var prices = new List<decimal>();
            foreach (var line in group.Where(l => l.SalesOrderItemId.HasValue))
            {
                if (orderLines.TryGetValue(line.SalesOrderItemId!.Value, out var orderLine))
                    prices.Add(decimal.Round(orderLine.UnitPrice, 2));
            }
            prices = prices.Distinct().ToList();
            if (prices.Count > 1)
                return (false, $"اختلف سعر الصنف «{item.Name}» بين أوامر التسليم — راجع الأسعار قبل الفوترة", null);

            invoiceItems.Add(new SaleInvoiceItem
            {
                ItemId = group.Key,
                Quantity = decimal.Round(group.Sum(l => l.Quantity), 2),
                Count = decimal.Round(group.Sum(l => l.Count), 2),
                UnitPrice = prices.Count > 0 ? prices[0] : item.SalePrice
            });
        }

        var orderIds = issues.Select(i => i.SalesOrderId).Distinct().ToList();
        if (orderIds.Count == 1 && orderIds[0].HasValue)
        {
            invoice.SalesOrderId = orderIds[0]!.Value;
            var orderNumber = issues.First(i => i.SalesOrderId == orderIds[0])?.SalesOrder?.OrderNumber;
            invoice.OrderReference = orderNumber ?? invoice.OrderReference;
            if (!invoice.CurrencyId.HasValue)
            {
                var order = await _db.SalesOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderIds[0]!.Value);
                if (order != null)
                {
                    invoice.CurrencyId = order.CurrencyId;
                    invoice.ExchangeRate = order.ExchangeRate;
                }
            }
        }
        else
        {
            var references = issues.Where(i => i.SalesOrder != null).Select(i => i.SalesOrder!.OrderNumber).ToList();
            if (references.Count > 0)
                invoice.OrderReference = string.Join("، ", references.Distinct());
        }

        invoice.PostingMode = SalesPostingMode.AtInvoice;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var (created, createError) = await _inventory.CreateSaleAsync(
                    invoice, invoiceItems, user, branchId, beginOwnTransaction: false);
                if (!created)
                {
                    await tx.RollbackAsync();
                    _db.ChangeTracker.Clear();
                    return (false, createError, null);
                }

                var tracked = await _db.DeliveryIssues.Where(i => ids.Contains(i.Id)).ToListAsync();
                foreach (var issue in tracked)
                    issue.SaleInvoiceId = invoice.Id;

                var billedOrderLineIds = lines.Where(l => l.SalesOrderItemId.HasValue)
                    .Select(l => l.SalesOrderItemId!.Value).Distinct().ToList();
                var trackedOrderLines = await _db.SalesOrderItems
                    .Where(i => billedOrderLineIds.Contains(i.Id)).ToListAsync();
                foreach (var orderLine in trackedOrderLines)
                {
                    orderLine.InvoicedQty += lines.Where(l => l.SalesOrderItemId == orderLine.Id).Sum(l => l.Quantity);
                    orderLine.InvoicedCount += lines.Where(l => l.SalesOrderItemId == orderLine.Id).Sum(l => l.Count);
                }

                foreach (var orderId in orderIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct())
                    await RefreshOrderInvoicingStatusAsync(orderId);

                await _db.SaveChangesAsync();

                // A fully discounted invoice nets to zero: there is no receivable and no revenue
                // to recognise, so nothing is posted here. The delivery cost entry still stands,
                // because the goods did leave stock. Mirrors the purchase invoice path, which
                // guards the same way - without it RecordSaleInvoiceRevenueAsync throws on a
                // zero value and rolls the whole delivery back.
                if (_accounting != null && invoice.NetAmount > 0m)
                    await _accounting.RecordSaleInvoiceRevenueAsync(invoice.InvoiceDate, invoice.CustomerId,
                        invoice.NetAmount, invoice.Tax, invoice.CurrencyId, invoice.ExchangeRate,
                        user, branchId, invoice.Id);

                await tx.CommitAsync();
                return (true, null, invoice);
            }
            catch (DbUpdateConcurrencyException) { await tx.RollbackAsync(); _db.ChangeTracker.Clear(); }
            catch (DbUpdateException) { await tx.RollbackAsync(); _db.ChangeTracker.Clear(); }
        }
        return (false, "تعذر إنشاء فاتورة التسليمات بسبب تعارض في البيانات، حاول مرة أخرى", null);
    }

    private async Task RefreshOrderInvoicingStatusAsync(int orderId)
    {
        var order = await _db.SalesOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null || order.Status == SalesOrderStatus.Cancelled || order.Status == SalesOrderStatus.Draft)
            return;

        var fullyInvoiced = order.Items.Count > 0 && order.Items.All(i =>
            i.InvoicedQty >= i.Quantity - 0.005m && i.InvoicedCount >= i.Count - 0.005m);
        var anyInvoiced = order.Items.Any(i => i.InvoicedQty > 0 || i.InvoicedCount > 0);

        order.Status = fullyInvoiced
            ? SalesOrderStatus.Invoiced
            : anyInvoiced ? SalesOrderStatus.PartiallyInvoiced : SalesOrderStatus.Approved;
    }
}
