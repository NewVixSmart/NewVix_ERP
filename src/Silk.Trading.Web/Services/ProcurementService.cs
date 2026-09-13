using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Purchases;

namespace Silk.Trading.Web.Services;

public sealed class ProcurementService : IProcurementService
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;

    public ProcurementService(AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    public async Task<IReadOnlyList<PurchaseOrder>> GetOrdersAsync(PurchaseOrderStatus? status = null)
    {
        var query = _db.PurchaseOrders
            .Include(o => o.Supplier)
            .Include(o => o.Items).ThenInclude(i => i.Item)
            .AsNoTracking()
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);

        return await query.OrderByDescending(o => o.OrderDate).ToListAsync();
    }

    public async Task<PurchaseOrder?> GetOrderAsync(int id)
    {
        return await _db.PurchaseOrders
            .Include(o => o.Supplier)
            .Include(o => o.Items).ThenInclude(i => i.Item)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<(bool Success, string? Error)> CreateOrderAsync(PurchaseOrder order, List<PurchaseOrderItem> items, string? user)
    {
        var valid = items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل");

        if (await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == order.SupplierId) == null)
            return (false, "المورد غير موجود");

        order.OrderNumber = await NextOrderNumberAsync();
        order.Status = PurchaseOrderStatus.Draft;
        order.CreatedBy = user;
        order.CreatedAt = DateTime.UtcNow;
        order.Items = valid;

        foreach (var item in valid)
            item.PurchaseOrderId = order.Id;

        _db.PurchaseOrders.Add(order);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> UpdateOrderAsync(PurchaseOrder order, List<PurchaseOrderItem> items, string? user)
    {
        var existing = await _db.PurchaseOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == order.Id);
        if (existing == null) return (false, "أمر الشراء غير موجود");
        if (existing.Status != PurchaseOrderStatus.Draft) return (false, "لا يمكن تعديل أمر شراء غير مسودة");

        var valid = items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل");

        existing.SupplierId = order.SupplierId;
        existing.OrderDate = order.OrderDate;
        existing.ExpectedDate = order.ExpectedDate;
        existing.Notes = order.Notes;

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
                item.PurchaseOrderId = existing.Id;
                existing.Items.Add(item);
            }
        }

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ApproveOrderAsync(int orderId)
    {
        var order = await _db.PurchaseOrders.FindAsync(orderId);
        if (order == null) return (false, "أمر الشراء غير موجود");
        if (order.Status != PurchaseOrderStatus.Draft) return (false, "يمكن اعتماد المسودات فقط");

        order.Status = PurchaseOrderStatus.Approved;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CancelOrderAsync(int orderId)
    {
        var order = await _db.PurchaseOrders.FindAsync(orderId);
        if (order == null) return (false, "أمر الشراء غير موجود");
        if (order.Status == PurchaseOrderStatus.Received) return (false, "لا يمكن إلغاء أمر مستلم");
        if (order.Status == PurchaseOrderStatus.Cancelled) return (false, "الأمر ملغي بالفعل");

        order.Status = PurchaseOrderStatus.Cancelled;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ReceiveOrderLineAsync(int orderId, int orderItemId, decimal receiveQty, decimal receiveCount)
    {
        var order = await _db.PurchaseOrders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) return (false, "أمر الشراء غير موجود");
        if (order.Status != PurchaseOrderStatus.Approved && order.Status != PurchaseOrderStatus.PartiallyReceived)
            return (false, "يمكن الاستلام على أوامر معتمدة فقط");

        var line = order.Items.FirstOrDefault(i => i.Id == orderItemId);
        if (line == null) return (false, "البند غير موجود");

        if (receiveQty < 0 || receiveCount < 0)
            return (false, "الكمية المستلمة لا يمكن أن تكون سالبة");

        if (line.ReceivedQty + receiveQty > line.Quantity || line.ReceivedCount + receiveCount > line.Count)
            return (false, "الكمية المستلمة أكبر من الكمية المطلوبة");

        line.ReceivedQty += receiveQty;
        line.ReceivedCount += receiveCount;

        var allFullyReceived = order.Items.All(i =>
            (i.Quantity <= 0 || i.ReceivedQty >= i.Quantity) &&
            (i.Count <= 0 || i.ReceivedCount >= i.Count));
        var anyReceived = order.Items.Any(i => i.ReceivedQty > 0 || i.ReceivedCount > 0);

        if (allFullyReceived)
            order.Status = PurchaseOrderStatus.Received;
        else if (anyReceived)
            order.Status = PurchaseOrderStatus.PartiallyReceived;

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CreateInvoiceFromOrderAsync(int orderId, string? user)
    {
        var order = await _db.PurchaseOrders
            .Include(o => o.Items).ThenInclude(i => i.Item)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) return (false, "أمر الشراء غير موجود");
        if (order.Status != PurchaseOrderStatus.Approved && order.Status != PurchaseOrderStatus.Received && order.Status != PurchaseOrderStatus.PartiallyReceived)
            return (false, "يمكن إنشاء فاتورة لأمر معتمد أو مستلم فقط");

        if (await _db.PurchaseInvoices.AnyAsync(p => p.PurchaseOrderId == order.Id))
            return (false, "لا يمكن فوترة أمر الشراء أكثر من مرة");

        var receivedLines = order.Items
            .Where(i => (i.ReceivedQty > 0 || i.ReceivedCount > 0))
            .ToList();
        if (receivedLines.Count == 0)
            return (false, "لا توجد أصناف مستلمة للتحويل إلى فاتورة");

        var invoiceItems = receivedLines.Select(line => new PurchaseInvoiceItem
        {
            ItemId = line.ItemId,
            Quantity = line.ReceivedQty,
            Count = line.ReceivedCount,
            UnitPrice = line.UnitPrice
        }).ToList();

        var invoice = new PurchaseInvoice
        {
            SupplierId = order.SupplierId,
            InvoiceDate = DateTime.Today,
            PurchaseOrderId = order.Id,
            OrderReference = order.OrderNumber,
            Notes = order.Notes
        };

        var (ok, error) = await _inventory.CreatePurchaseAsync(invoice, invoiceItems, user);
        return (ok, error);
    }

    public async Task<IReadOnlyList<SupplierQuote>> GetSupplierQuotesAsync()
    {
        return await _db.SupplierQuotes
            .Include(q => q.Supplier)
            .Include(q => q.Item)
            .AsNoTracking()
            .OrderByDescending(q => q.EffectiveDate)
            .ToListAsync();
    }

    public async Task<(bool Success, string? Error)> SaveSupplierQuoteAsync(SupplierQuote quote)
    {
        var existing = await _db.SupplierQuotes
            .FirstOrDefaultAsync(q => q.SupplierId == quote.SupplierId && q.ItemId == quote.ItemId);
        if (existing != null)
        {
            existing.UnitPrice = quote.UnitPrice;
            existing.EffectiveDate = quote.EffectiveDate;
            existing.Notes = quote.Notes;
        }
        else
        {
            quote.EffectiveDate = quote.EffectiveDate == default ? DateTime.Today : quote.EffectiveDate;
            _db.SupplierQuotes.Add(quote);
        }
        await _db.SaveChangesAsync();
        return (true, null);
    }

    private async Task<string> NextOrderNumberAsync()
    {
        int next = await _db.PurchaseOrders.CountAsync() + 1;
        string num = $"PRC-{DateTime.Now:yyyyMMdd}-{next:D3}";
        while (await _db.PurchaseOrders.AnyAsync(o => o.OrderNumber == num))
        {
            next++;
            num = $"PRC-{DateTime.Now:yyyyMMdd}-{next:D3}";
        }
        return num;
    }
}
