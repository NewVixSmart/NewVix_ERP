using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;

namespace NewVixSmart.Web.Services;

public sealed class SalesOrdersService : ISalesOrdersService
{
    private const int _maxAttempts = 3;
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IStockReservationsService _reservations;
    private readonly IDeliveriesInvoicingService _deliveriesInvoicing;

    public SalesOrdersService(AppDbContext db, IInventoryService inventory,
        IStockReservationsService? reservations = null, IDeliveriesInvoicingService? deliveriesInvoicing = null)
    {
        _db = db;
        _inventory = inventory;
        _reservations = reservations ?? new StockReservationsService(db);
        _deliveriesInvoicing = deliveriesInvoicing ?? new DeliveriesInvoicingService(db, inventory);
    }

    public async Task<IReadOnlyList<SalesOrder>> GetOrdersAsync(SalesOrderStatus? status = null)
    {
        var query = _db.SalesOrders
            .Include(o => o.Customer)
            .Include(o => o.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
            .Include(o => o.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .AsNoTracking()
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }

        return await query.OrderByDescending(o => o.OrderDate).ToListAsync();
    }

    public async Task<SalesOrder?> GetOrderAsync(int id)
    {
        return await _db.SalesOrders
            .Include(o => o.Customer)
            .Include(o => o.SaleQuote)
            .Include(o => o.Items).ThenInclude(i => i.Item).ThenInclude(i => i.CountUnit)
            .Include(o => o.Items).ThenInclude(i => i.Item).ThenInclude(i => i.QuantityUnit)
            .Include(o => o.DeliveryOrders).ThenInclude(d => d.Issues).ThenInclude(i => i.Items)
            .Include(o => o.DeliveryOrders).ThenInclude(d => d.Issues).ThenInclude(i => i.SaleInvoice)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<(bool Success, string? Error)> CreateOrderAsync(SalesOrder order, List<SalesOrderItem> items, string? user)
    {
        var valid = items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
        if (valid.Count == 0)
        {
            return (false, "يرجى إضافة صنف واحد على الأقل");
        }

        if (valid.GroupBy(i => i.ItemId).Any(g => g.Count() > 1))
        {
            return (false, "لا يمكن إضافة الصنف نفسه في أكثر من سطر");
        }

        if (await _db.Customers.FirstOrDefaultAsync(c => c.Id == order.CustomerId) == null)
        {
            return (false, "العميل غير موجود");
        }

        order.Status = SalesOrderStatus.Draft;
        order.CreatedBy = user;
        order.CreatedAt = DateTime.UtcNow;
        order.Items = valid;

        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            order.OrderNumber = await NextOrderNumberAsync();

            foreach (var item in valid)
            {
                item.SalesOrderId = order.Id;
            }

            _db.SalesOrders.Add(order);
            try
            {
                await _db.SaveChangesAsync();
                return (true, null);
            }
            catch (DbUpdateException)
            {
                var colliding = order.OrderNumber;
                _db.ChangeTracker.Clear();
                if (!await _db.SalesOrders.AsNoTracking().AnyAsync(o => o.OrderNumber == colliding))
                {
                    return (false, "تعذر حفظ الأمر بسبب تعارض في البيانات، حاول مرة أخرى");
                }

                order.Id = 0;
                foreach (var item in valid) { item.Id = 0; item.SalesOrderId = 0; }
                order.Items = valid;
            }
        }
        return (false, "تعذر حفظ الأمر بسبب تعارض في الترقيم، حاول مرة أخرى");
    }

    public async Task<(bool Success, string? Error)> UpdateOrderAsync(SalesOrder order, List<SalesOrderItem> items, string? user)
    {
        var existing = await _db.SalesOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == order.Id);
        if (existing == null)
        {
            return (false, "أمر البيع غير موجود");
        }

        if (existing.Status != SalesOrderStatus.Draft)
        {
            return (false, "لا يمكن تعديل أمر بيع غير مسودة");
        }

        if (await _db.StockReservations.AnyAsync(r => r.SalesOrderId == order.Id &&
            (r.Status == StockReservationStatus.Active || r.Status == StockReservationStatus.PartiallyConsumed)))
        {
            return (false, "يوجد حجز قائم لهذا الأمر — ألغِ الحجز قبل التعديل");
        }

        var valid = items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
        if (valid.Count == 0)
        {
            return (false, "يرجى إضافة صنف واحد على الأقل");
        }

        if (valid.GroupBy(i => i.ItemId).Any(g => g.Count() > 1))
        {
            return (false, "لا يمكن إضافة الصنف نفسه في أكثر من سطر");
        }

        if (await _db.Customers.FirstOrDefaultAsync(c => c.Id == order.CustomerId) == null)
        {
            return (false, "العميل غير موجود");
        }

        existing.CustomerId = order.CustomerId;
        existing.OrderDate = order.OrderDate;
        existing.ExpectedDate = order.ExpectedDate;
        existing.Notes = order.Notes;

        var oldItemIds = existing.Items.Select(i => i.Id).ToHashSet();
        var newItemIds = valid.Where(i => i.Id > 0).Select(i => i.Id).ToHashSet();
        var toRemove = existing.Items.Where(i => !newItemIds.Contains(i.Id)).ToList();
        foreach (var r in toRemove)
        {
            existing.Items.Remove(r);
        }

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

    /// <summary>
    /// يعتمد أمر بيع ويحجز كمياته. مع <c>beginOwnTransaction: false</c> ينضم الاعتماد
    /// والحجز إلى معاملة المستدعي، فلا يبقى أمر معتمد بلا حجز لو فشل الحجز.
    /// </summary>
    public async Task<(bool Success, string? Error)> ApproveOrderAsync(int orderId, bool beginOwnTransaction = true)
    {
        if (!beginOwnTransaction)
        {
            RequireAmbientTransaction(nameof(ApproveOrderAsync));
        }

        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            await using var tx = beginOwnTransaction ? await _db.Database.BeginTransactionAsync() : null;
            try
            {
                var order = await _db.SalesOrders.FindAsync(orderId);
                if (order == null)
                {
                    return (false, "أمر البيع غير موجود");
                }

                if (order.Status != SalesOrderStatus.Draft)
                {
                    return (false, "يمكن اعتماد المسودات فقط");
                }

                order.Status = SalesOrderStatus.Approved;
                await _db.SaveChangesAsync();

                var (reserved, reserveError) = await _reservations.ReserveOrderAsync(orderId, order.CreatedBy, beginOwnTransaction: false);
                if (!reserved)
                {
                    await TryRollbackAsync(tx);
                    _db.ChangeTracker.Clear();
                    return (false, reserveError);
                }

                if (tx is not null)
                {
                    await tx.CommitAsync();
                }

                return (true, null);
            }
            catch (DbUpdateConcurrencyException)
            {
                await TryRollbackAsync(tx); _db.ChangeTracker.Clear(); if (!beginOwnTransaction)
                {
                    throw;
                }
            }
            catch (DbUpdateException)
            {
                await TryRollbackAsync(tx); _db.ChangeTracker.Clear(); if (!beginOwnTransaction)
                {
                    throw;
                }
            }
        }
        return (false, "تعذر اعتماد أمر البيع بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    public async Task<(bool Success, string? Error)> CancelOrderAsync(int orderId)
    {
        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var order = await _db.SalesOrders.FindAsync(orderId);
                if (order == null)
                {
                    return (false, "أمر البيع غير موجود");
                }

                if (order.Status == SalesOrderStatus.Invoiced)
                {
                    return (false, "لا يمكن إلغاء أمر تمت فوترته");
                }

                if (order.Status == SalesOrderStatus.Cancelled)
                {
                    return (false, "الأمر ملغي بالفعل");
                }

                if (await _db.DeliveryIssues.AnyAsync(i => i.SalesOrderId == orderId && i.Status == DeliveryIssueStatus.Issued))
                {
                    await tx.RollbackAsync();
                    _db.ChangeTracker.Clear();
                    return (false, "تم تسليم كميات من هذا الأمر — ألغِ أمر التسليم أولاً");
                }

                order.Status = SalesOrderStatus.Cancelled;
                await _db.SaveChangesAsync();

                await _reservations.ReleaseForOrderAsync(orderId, order.CreatedBy);
                await tx.CommitAsync();
                return (true, null);
            }
            catch (DbUpdateConcurrencyException) { await tx.RollbackAsync(); _db.ChangeTracker.Clear(); }
            catch (DbUpdateException) { await tx.RollbackAsync(); _db.ChangeTracker.Clear(); }
        }
        return (false, "تعذر إلغاء أمر البيع بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    public async Task<(bool Success, string? Error)> CreateInvoiceFromOrderAsync(int orderId, string? user)
    {
        if (await _db.SaleInvoices.AnyAsync(s => s.SalesOrderId == orderId))
        {
            return (false, "تم إنشاء فاتورة لهذا الأمر بالفعل");
        }

        if (await _db.DeliveryOrders.AnyAsync(d => d.SalesOrderId == orderId && d.Status != DeliveryOrderStatus.Cancelled))
        {
            return (false, "يوجد أذن تسليم لهذا الأمر — أنشئ الفاتورة من أوامر التسليم المرحّلة");
        }

        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var order = await _db.SalesOrders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == orderId);
                if (order == null)
                {
                    return (false, "أمر البيع غير موجود");
                }

                if (order.Status != SalesOrderStatus.Approved && order.Status != SalesOrderStatus.PartiallyInvoiced)
                {
                    return (false, "يمكن إنشاء فاتورة لأمر معتمد فقط");
                }

                if (await _db.SaleInvoices.AnyAsync(s => s.SalesOrderId == order.Id))
                {
                    await tx.RollbackAsync(); _db.ChangeTracker.Clear();
                    return (false, "تم إنشاء فاتورة لهذا الأمر بالفعل");
                }

                var remainingLines = order.Items
                    .Where(i => i.Quantity - i.InvoicedQty > 0 || i.Count - i.InvoicedCount > 0)
                    .ToList();
                if (remainingLines.Count == 0)
                {
                    return (false, "لا توجد كمية متبقية للتحويل إلى فاتورة");
                }

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
                {
                    return (false, "تم إنشاء فاتورة لهذا الأمر بالفعل");
                }
            }
        }
        return (false, "تعذر فوترة الأمر بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    public async Task<(bool Success, string? Error, SaleInvoice? Invoice)> InvoiceOutstandingDeliveriesAsync(
        int orderId, string? user, int? branchId = null)
    {
        var order = await _db.SalesOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null)
        {
            return (false, "أمر البيع غير موجود", null);
        }

        if (order.Status == SalesOrderStatus.Draft)
        {
            return (false, "يمكن إنشاء فاتورة لأمر معتمد فقط", null);
        }

        if (order.Status == SalesOrderStatus.Cancelled)
        {
            return (false, "لا يمكن فاتورة أمر ملغي", null);
        }

        var issueIds = await _deliveriesInvoicing.GetOutstandingIssueIdsAsync(orderId);
        if (issueIds.Count == 0)
        {
            return (false, "لا توجد تسليمات غير مفوترة لهذا الأمر", null);
        }

        var invoice = new SaleInvoice
        {
            CustomerId = order.CustomerId,
            InvoiceDate = DateTime.Today,
            PaymentTerms = InvoicePaymentTerms.OpenTerm,
            Notes = order.Notes
        };
        return await _deliveriesInvoicing.CreateInvoiceFromIssuesAsync(issueIds, invoice, user, branchId);
    }

    /// <summary>
    /// مسار استرجاع لا يبتلع الاستثناء الأصلي: فشل التراجع عن معاملة يجب ألا يحلّ محل
    /// الاستثناء الذي تلتقطه الحلقة، وإلا ضاع التشخيص خلف خطأ آخر. وتمرير معاملة فارغة
    /// (معاملة المستدعي) يعني ببساطة أن التراجع من اختصاصه.
    /// </summary>
    private static async Task TryRollbackAsync(IDbContextTransaction? tx)
    {
        if (tx == null)
        {
            return;
        }

        try { await tx.RollbackAsync(); }
        catch (Exception) { }
    }

    /// <summary>
    /// يمنع معنى «بلا معاملة» الصامت: <c>beginOwnTransaction: false</c> بلا معاملة قائمة
    /// يعني اعتمادًا محفوظًا بلا حجز، وهي حالة لا يستطيع أحد التراجع عنها.
    /// </summary>
    private void RequireAmbientTransaction(string operation)
    {
        if (_db.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException(
                $"لا يمكن تنفيذ «{operation}» دون معاملة قائمة؛ ابدأ معاملة قبل الاستدعاء أو اترك القيمة الافتراضية لتفتح العملية معاملتها الخاصة");
        }
    }

    private async Task<string> NextOrderNumberAsync()
    {
        var seriesPrefix = $"SO-{DateTime.Now:yyyyMMdd}-";
        int next = await MaxSeriesValueAsync(seriesPrefix) + 1;
        string num = $"{seriesPrefix}{next:D3}";
        while (await _db.SalesOrders.AsNoTracking().AnyAsync(o => o.OrderNumber == num))
        {
            next++;
            num = $"{seriesPrefix}{next:D3}";
        }
        return num;
    }

    private async Task<int> MaxSeriesValueAsync(string seriesPrefix)
    {
        var values = await _db.SalesOrders.AsNoTracking()
            .Where(o => o.OrderNumber.StartsWith(seriesPrefix))
            .Select(o => o.OrderNumber)
            .ToListAsync();
        int max = 0;
        foreach (var value in values)
        {
            if (value.Length <= seriesPrefix.Length)
            {
                continue;
            }

            if (int.TryParse(value.AsSpan(seriesPrefix.Length), out var parsed) && parsed > max)
            {
                max = parsed;
            }
        }
        return max;
    }
}
