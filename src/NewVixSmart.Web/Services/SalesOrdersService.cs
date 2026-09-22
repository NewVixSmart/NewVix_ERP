using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Sales;

namespace NewVixSmart.Web.Services;

public sealed class SalesOrdersService : ISalesOrdersService
{
    private const int MaxAttempts = 3;
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;

    public SalesOrdersService(AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    public async Task<IReadOnlyList<SalesOrder>> GetOrdersAsync(SalesOrderStatus? status = null)
    {
        var query = _db.SalesOrders
            .Include(o => o.Customer)
            .Include(o => o.Currency)
            .Include(o => o.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
            .Include(o => o.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking()
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);

        return await query.OrderByDescending(o => o.OrderDate).ToListAsync();
    }

    public async Task<SalesOrder?> GetOrderAsync(int id)
    {
        return await _db.SalesOrders
            .Include(o => o.Customer)
            .Include(o => o.Currency)
            .Include(o => o.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
            .Include(o => o.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<(bool Success, string? Error)> CreateOrderAsync(SalesOrder order, List<SalesOrderItem> items, string? user)
    {
        var valid = items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل");

        if (await _db.Customers.FirstOrDefaultAsync(c => c.Id == order.CustomerId) == null)
            return (false, "العميل غير موجود");

        order.OrderNumber = await NextOrderNumberAsync();
        order.Status = SalesOrderStatus.Draft;
        order.CreatedBy = user;
        order.CreatedAt = DateTime.UtcNow;
        order.Items = valid;

        foreach (var item in valid)
            item.SalesOrderId = order.Id;

        _db.SalesOrders.Add(order);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> UpdateOrderAsync(SalesOrder order, List<SalesOrderItem> items, string? user)
    {
        var existing = await _db.SalesOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == order.Id);
        if (existing == null) return (false, "أمر البيع غير موجود");
        if (existing.Status != SalesOrderStatus.Draft) return (false, "لا يمكن تعديل أمر بيع غير مسودة");

        var valid = items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل");

        existing.CustomerId = order.CustomerId;
        existing.OrderDate = order.OrderDate;
        existing.ExpectedDate = order.ExpectedDate;
        existing.Notes = order.Notes;
        existing.CurrencyId = order.CurrencyId;
        existing.ExchangeRate = order.ExchangeRate;

        var oldItemIds = existing.Items.Select(i => i.Id).ToHashSet();
        var newItemIds = valid.Where(i => i.Id > 0).Select(i => i.Id).ToHashSet();
        var toRemove = existing.Items.Where(i => !newItemIds.Contains(i.Id)).ToList();
        foreach (var r in toRemove) existing.Items.Remove(r);

        foreach (var item in valid)
        {
            var existingItem = existing.Items.FirstOrDefault(i => i.Id == item.Id);
            if (existingItem != null)
            {
                existingItem.ItemId = item.ItemId;
                existingItem.Quantity = item.Quantity;
                existingItem.Count = item.Count;
                existingItem.UnitPrice = item.UnitPrice;
            }
            else
            {
                item.SalesOrderId = existing.Id;
                existing.Items.Add(item);
            }
        }

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ApproveOrderAsync(int orderId)
    {
        var order = await _db.SalesOrders.FindAsync(orderId);
        if (order == null) return (false, "أمر البيع غير موجود");
        if (order.Status != SalesOrderStatus.Draft) return (false, "يمكن اعتماد المسودات فقط");

        order.Status = SalesOrderStatus.Approved;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CancelOrderAsync(int orderId)
    {
        var order = await _db.SalesOrders.FindAsync(orderId);
        if (order == null) return (false, "أمر البيع غير موجود");
        if (order.Status == SalesOrderStatus.Invoiced) return (false, "لا يمكن إلغاء أمر تمت فوترته");
        if (order.Status == SalesOrderStatus.Cancelled) return (false, "الأمر ملغي بالفعل");

        order.Status = SalesOrderStatus.Cancelled;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CreateInvoiceFromOrderAsync(int orderId, string? user)
    {
        if (await _db.SaleInvoices.AnyAsync(s => s.SalesOrderId == orderId))
            return (false, "تم إنشاء فاتورة لهذا الأمر بالفعل");

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var order = await _db.SalesOrders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == orderId);
                if (order == null) return (false, "أمر البيع غير موجود");
                if (order.Status != SalesOrderStatus.Approved && order.Status != SalesOrderStatus.PartiallyInvoiced)
                    return (false, "يمكن إنشاء فاتورة لأمر معتمد فقط");
                if (await _db.SaleInvoices.AnyAsync(s => s.SalesOrderId == order.Id))
                {
                    await tx.RollbackAsync(); _db.ChangeTracker.Clear();
                    return (false, "تم إنشاء فاتورة لهذا الأمر بالفعل");
                }

                var remainingLines = order.Items
                    .Where(i => i.Quantity - i.InvoicedQty > 0 || i.Count - i.InvoicedCount > 0)
                    .ToList();
                if (remainingLines.Count == 0)
                    return (false, "لا توجد كمية متبقية للتحويل إلى فاتورة");

                var invoiceItems = remainingLines.Select(line => new SaleInvoiceItem
                {
                    ItemId = line.ItemId,
                    Quantity = line.Quantity - line.InvoicedQty,
                    Count = line.Count - line.InvoicedCount,
                    UnitPrice = line.UnitPrice
                }).ToList();

                var invoice = new SaleInvoice
                {
                    CustomerId = order.CustomerId,
                    InvoiceDate = DateTime.Today,
                    PaymentTerms = InvoicePaymentTerms.OpenTerm,
                    SalesOrderId = order.Id,
                    OrderReference = order.OrderNumber,
                    CurrencyId = order.CurrencyId,
                    ExchangeRate = order.ExchangeRate,
                    Notes = order.Notes
                };

                var (ok, error) = await _inventory.CreateSaleAsync(invoice, invoiceItems, user, branchId: null, beginOwnTransaction: false);
                if (!ok) { await tx.RollbackAsync(); _db.ChangeTracker.Clear(); return (false, error); }

                foreach (var line in order.Items)
                {
                    line.InvoicedQty = line.Quantity;
                    line.InvoicedCount = line.Count;
                }
                order.Status = SalesOrderStatus.Invoiced;
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return (true, null);
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(); _db.ChangeTracker.Clear();
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync(); _db.ChangeTracker.Clear();
                if (await _db.SaleInvoices.AnyAsync(s => s.SalesOrderId == orderId))
                    return (false, "تم إنشاء فاتورة لهذا الأمر بالفعل");
            }
        }
        return (false, "تعذر فوترة الأمر بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    private async Task<string> NextOrderNumberAsync()
    {
        int next = await _db.SalesOrders.CountAsync() + 1;
        string num = $"SO-{DateTime.Now:yyyyMMdd}-{next:D3}";
        while (await _db.SalesOrders.AnyAsync(o => o.OrderNumber == num))
        {
            next++;
            num = $"SO-{DateTime.Now:yyyyMMdd}-{next:D3}";
        }
        return num;
    }
}