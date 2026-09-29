using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;

namespace NewVixSmart.Web.Services;

public sealed class InventoryService : IInventoryService
{
    private const int MaxAttempts = 3;
    private readonly AppDbContext _db;
    private readonly ILogger<InventoryService>? _logger;
    private readonly IAccountingService? _accounting;
    private readonly IStockReservationsService _reservations;

    public InventoryService(AppDbContext db) : this(db, null, null) { }

    public InventoryService(AppDbContext db, ILogger<InventoryService>? logger) : this(db, logger, null) { }

    public InventoryService(AppDbContext db, IAccountingService? accounting) : this(db, null, accounting) { }

    public InventoryService(AppDbContext db, IAccountingService? accounting, IStockReservationsService? reservations)
        : this(db, null, accounting, reservations) { }

    public InventoryService(AppDbContext db, ILogger<InventoryService>? logger, IAccountingService? accounting,
        IStockReservationsService? reservations = null)
    {
        _db = db;
        _logger = logger;
        _accounting = accounting;
        _reservations = reservations ?? new StockReservationsService(db);
    }

    public async Task<(bool Success, string? Error)> CreateSaleAsync(SaleInvoice invoice, List<SaleInvoiceItem> items, string? user, int? branchId = null, bool beginOwnTransaction = true)
    {
        var valid = items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل بالكمية أو العدد");
        var badPrice = items.FirstOrDefault(i => i.ItemId > 0 && i.UnitPrice < 0);
        if (badPrice != null)
            return (false, "سعر الوحدة يجب ألا يكون سالباً");
        var badQty = items.FirstOrDefault(i => i.ItemId > 0 && (i.Quantity < 0 || i.Count < 0));
        if (badQty != null)
            return (false, "الكمية أو العدد يجب ألا يكون سالباً");
        var duplicateSale = valid.GroupBy(i => i.ItemId).FirstOrDefault(g => g.Count() > 1);
        if (duplicateSale != null)
            return (false, $"الصنف رقم {duplicateSale.Key} مكرر أكثر من مرة في الفاتورة");

        if (!beginOwnTransaction)
            return await CreateSaleCoreAsync(invoice, valid, user, branchId);

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var result = await CreateSaleCoreAsync(invoice, valid, user, branchId);
                if (!result.Success) { await tx.RollbackAsync(); DetachAll(); ResetInvoiceKeys(invoice); return result; }
                await tx.CommitAsync();
                return (true, null);
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(); DetachAll(); ResetInvoiceKeys(invoice);
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync(); DetachAll(); ResetInvoiceKeys(invoice);
            }
        }
        return (false, "تعذر حفظ فاتورة البيع بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    private async Task<(bool Success, string? Error)> CreateSaleCoreAsync(SaleInvoice invoice, List<SaleInvoiceItem> valid, string? user, int? branchId)
    {
        if (await IsPeriodClosedAsync(invoice.InvoiceDate))
            return (false, $"السنة المالية {invoice.InvoiceDate.Year} مغلقة — لا يمكن إدراج فاتورة بيع فيها");

        invoice.InvoiceNumber = await NextInvoiceNumberAsync(
            _db.SaleInvoices.Select(s => s.InvoiceNumber), "SI");
        invoice.BranchId = branchId;

        if (valid.Any(i => i.Discount > i.Gross))
        {
            _db.ChangeTracker.Clear();
            return (false, "خصم الصنف لا يمكن أن يتجاوز قيمته الإجمالية");
        }

        invoice.TotalAmount = valid.Sum(i => i.Total);
        invoice.NetAmount = invoice.TotalAmount - invoice.Discount - (invoice.Discount2 ?? 0) - (invoice.Discount3 ?? 0) + invoice.Tax;
        if (invoice.NetAmount < 0)
        {
            _db.ChangeTracker.Clear();
            return (false, "الخصم أكبر من إجمالي الفاتورة؛ لا يمكن أن يكون الصافي سالباً");
        }
        if (invoice.PaymentTerms != InvoicePaymentTerms.OnReceipt
            && invoice.PaymentTerms != InvoicePaymentTerms.OpenTerm
            && invoice.DueDate == null)
            invoice.DueDate = invoice.InvoiceDate.AddDays(PaymentTermDays(invoice.PaymentTerms));

        invoice.PaidAmount = 0m;
        invoice.IsPaid = false;
        invoice.CreatedBy = user;
        invoice.CreatedAt = DateTime.UtcNow;
        invoice.Items = valid;

        _db.SaleInvoices.Add(invoice);
        await _db.SaveChangesAsync();

        _logger?.LogInformation("سُجّلت فاتورة بيع {Owner} رقم {Number} صافي {Net:C} بفاتورة {InvId} — غير مسددة، بانتظار أذن التسليم",
            user, invoice.InvoiceNumber, invoice.NetAmount, invoice.Id);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CreateDeliveryOrderAsync(DeliveryOrder delivery, List<DeliveryOrderItem> items, string? user)
    {
        var valid = items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل بالكمية أو العدد");
        var duplicate = valid.GroupBy(i => i.ItemId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            return (false, $"الصنف رقم {duplicate.Key} مكرر أكثر من مرة في أذن التسليم");
        if (!delivery.SaleInvoiceId.HasValue) return (false, "يجب ربط أذن التسليم بفاتورة بيع");

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var invoice = await _db.SaleInvoices.Include(i => i.Items).AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == delivery.SaleInvoiceId.Value);
                if (invoice == null) return (false, "فاتورة البيع غير موجودة");

                delivery.DeliveryNumber = await NextDeliveryNumberAsync();
                delivery.CustomerId = invoice.CustomerId;
                delivery.Status = DeliveryOrderStatus.Draft;
                delivery.CreatedBy = user;
                delivery.CreatedAt = DateTime.UtcNow;
                delivery.Items = valid;

                var delivered = await _db.DeliveryOrders
                    .Where(d => d.SaleInvoiceId == invoice.Id && d.Status != DeliveryOrderStatus.Cancelled)
                    .SelectMany(d => d.Items)
                    .AsNoTracking()
                    .ToListAsync();

                foreach (var line in valid)
                {
                    var invLine = invoice.Items.FirstOrDefault(i => i.ItemId == line.ItemId);
                    if (invLine == null) { await tx.RollbackAsync(); DetachAll(); return (false, $"الصنف رقم {line.ItemId} غير موجود في الفاتورة الأصلية"); }
                    decimal deliveredCount = delivered.Where(x => x.ItemId == line.ItemId).Sum(x => x.Count);
                    decimal deliveredQty = delivered.Where(x => x.ItemId == line.ItemId).Sum(x => x.Quantity);
                    if (line.Count + deliveredCount > invLine.Count || line.Quantity + deliveredQty > invLine.Quantity)
                    { await tx.RollbackAsync(); DetachAll(); return (false, $"الكمية المسلّمة أكبر من المتبقي في فاتورة البيع للصنف رقم {line.ItemId}"); }
                }

                _db.DeliveryOrders.Add(delivery);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                _logger?.LogInformation("أُنشئ أذن تسليم {Owner} رقم {Number} مرتبط بفاتورة {InvoiceId}",
                    user, delivery.DeliveryNumber, invoice.Id);
                return (true, null);
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(); DetachAll(); ResetDeliveryKeys(delivery);
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync(); DetachAll(); ResetDeliveryKeys(delivery);
            }
        }
        return (false, "تعذر حفظ أذن التسليم بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    public async Task<(bool Success, string? Error, DeliveryOrder? Delivery)> CreateSalesDeliveryNoteAsync(
        int? salesOrderId, int? saleInvoiceId, int? customerId, List<DeliveryOrderItem> items, string? user,
        DateTime? deliveryDate = null, string? notes = null)
    {
        if (salesOrderId.HasValue && saleInvoiceId.HasValue)
            return (false, "لا يمكن ربط أذن التسليم بأمر بيع وفاتورة في نفس الوقت", null);

        var valid = items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل بالكمية أو العدد", null);
        if (valid.Any(i => i.Quantity < 0 || i.Count < 0)) return (false, "الكمية أو العدد يجب ألا يكون سالباً", null);
        var duplicate = valid.GroupBy(i => i.ItemId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null) return (false, $"الصنف رقم {duplicate.Key} مكرر أكثر من مرة في أذن التسليم", null);

        var unknownItem = valid.Select(i => i.ItemId).FirstOrDefault(id => !_db.Items.Any(i => i.Id == id));
        if (unknownItem > 0) return (false, $"الصنف رقم {unknownItem} غير موجود", null);

        if (salesOrderId.HasValue)
        {
            var order = await _db.SalesOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == salesOrderId.Value);
            if (order == null) return (false, "أمر البيع غير موجود", null);
            if (order.Status == SalesOrderStatus.Draft) return (false, "لا يمكن إنشاء أذن تسليم من أمر بيع مسودة", null);
            if (order.Status == SalesOrderStatus.Cancelled) return (false, "لا يمكن إنشاء أذن تسليم من أمر بيع ملغي", null);
            customerId = order.CustomerId;
        }
        else if (saleInvoiceId.HasValue)
        {
            var invoice = await _db.SaleInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == saleInvoiceId.Value);
            if (invoice == null) return (false, "فاتورة البيع غير موجودة", null);
            customerId = invoice.CustomerId;
        }

        if (!customerId.HasValue || customerId.Value <= 0) return (false, "يرجى اختيار العميل", null);
        if (!await _db.Customers.AnyAsync(c => c.Id == customerId.Value)) return (false, "العميل غير موجود", null);

        if (salesOrderId.HasValue)
        {
            var open = await OpenQuantitiesAsync(salesOrderId.Value, null);
            var orderLines = await _db.SalesOrderItems.AsNoTracking()
                .Where(i => i.SalesOrderId == salesOrderId.Value).ToListAsync();
            foreach (var line in valid)
            {
                var orderLine = orderLines.FirstOrDefault(i => i.ItemId == line.ItemId);
                if (orderLine == null) return (false, $"الصنف رقم {line.ItemId} غير موجود في أمر البيع", null);
                var remainingQty = orderLine.Quantity - orderLine.DeliveredQty - (open.TryGetValue(line.ItemId, out var o) ? o.Qty : 0m);
                var remainingCount = orderLine.Count - orderLine.DeliveredCount - (open.TryGetValue(line.ItemId, out var o2) ? o2.Count : 0m);
                if (line.Quantity > remainingQty || line.Count > remainingCount)
                    return (false, $"الكمية في أذن التسليم أكبر من المتبقي في أمر البيع للصنف رقم {line.ItemId}", null);
            }
        }

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var delivery = new DeliveryOrder
                {
                    DeliveryNumber = await NextDeliveryNumberAsync(),
                    SalesOrderId = salesOrderId,
                    SaleInvoiceId = saleInvoiceId,
                    CustomerId = customerId.Value,
                    DeliveryDate = deliveryDate ?? DateTime.Today,
                    Status = DeliveryOrderStatus.Draft,
                    Notes = notes,
                    CreatedBy = user,
                    CreatedAt = DateTime.UtcNow,
                    Items = valid
                };

                _db.DeliveryOrders.Add(delivery);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                _logger?.LogInformation("أُنشئ أذن تسليم {Owner} رقم {Number} للأمر {OrderId}",
                    user, delivery.DeliveryNumber, salesOrderId);
                return (true, null, delivery);
            }
            catch (DbUpdateConcurrencyException) { await tx.RollbackAsync(); DetachAll(); }
            catch (DbUpdateException) { await tx.RollbackAsync(); DetachAll(); }
        }
        return (false, "تعذر حفظ أذن التسليم بسبب تعارض في البيانات، حاول مرة أخرى", null);
    }

    public async Task<(bool Success, string? Error, DeliveryIssue? Issue)> CreateDeliveryIssueAsync(
        int deliveryId, List<DeliveryIssueItem> items, string? user, DateTime? issueDate = null, string? notes = null,
        string? carrier = null, string? trackingNumber = null)
    {
        var valid = items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل بالكمية أو العدد", null);
        if (valid.Any(i => i.Quantity < 0 || i.Count < 0)) return (false, "الكمية أو العدد يجب ألا يكون سالباً", null);
        var duplicate = valid.GroupBy(i => i.ItemId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null) return (false, $"الصنف رقم {duplicate.Key} مكرر أكثر من مرة في أمر التسليم", null);

        var delivery = await _db.DeliveryOrders.AsNoTracking().FirstOrDefaultAsync(d => d.Id == deliveryId);
        if (delivery == null) return (false, "أذن التسليم غير موجود", null);
        if (delivery.Status == DeliveryOrderStatus.Cancelled) return (false, "لا يمكن إنشاء أمر تسليم من أذن ملغي", null);
        // An issue on an invoice-backed note posts the cost only (Dr 3000 / Cr 1300) and the
        // revenue path (DeliverDeliveryOrderAsync) is then permanently blocked for that note, so
        // the sale would leave the books with a cost and no revenue. Such a note is delivered by
        // posting the note itself.
        if (delivery.SaleInvoiceId.HasValue)
            return (false, "أذن التسليم مرتبط بفاتورة بيع — سلّمه بترحيل أذن التسليم نفسه ولا تنشئ له أمر تسليم", null);

        foreach (var line in valid)
        {
            if (!await _db.DeliveryOrderItems.AnyAsync(x => x.DeliveryOrderId == deliveryId && x.ItemId == line.ItemId))
                return (false, $"الصنف رقم {line.ItemId} غير موجود في أذن التسليم", null);
            if (line.DeliveryOrderItemId <= 0)
            {
                line.DeliveryOrderItemId = await _db.DeliveryOrderItems
                    .Where(x => x.DeliveryOrderId == deliveryId && x.ItemId == line.ItemId)
                    .Select(x => x.Id).FirstAsync();
            }
        }

        if (delivery.SalesOrderId.HasValue)
        {
            var open = await OpenQuantitiesAsync(delivery.SalesOrderId.Value, null, delivery.Id);
            var orderLines = await _db.SalesOrderItems.AsNoTracking()
                .Where(i => i.SalesOrderId == delivery.SalesOrderId.Value).ToListAsync();
            var noteLines = await _db.DeliveryOrderItems.AsNoTracking()
                .Where(i => i.DeliveryOrderId == deliveryId).ToListAsync();
            var issuedHere = await _db.DeliveryIssueItems.AsNoTracking()
                .Where(x => x.DeliveryIssue.DeliveryOrderId == deliveryId && x.DeliveryIssue.Status != DeliveryIssueStatus.Cancelled)
                .ToListAsync();

            foreach (var line in valid)
            {
                var orderLine = orderLines.FirstOrDefault(i => i.ItemId == line.ItemId);
                var noteLine = noteLines.FirstOrDefault(i => i.ItemId == line.ItemId);
                if (orderLine == null || noteLine == null)
                    return (false, $"الصنف رقم {line.ItemId} غير موجود في أمر البيع أو أذن التسليم", null);

                var openQty = open.TryGetValue(line.ItemId, out var o) ? o.Qty : 0m;
                var openCount = open.TryGetValue(line.ItemId, out var o2) ? o2.Count : 0m;
                if (noteLine.Quantity + orderLine.DeliveredQty + openQty > orderLine.Quantity ||
                    noteLine.Count + orderLine.DeliveredCount + openCount > orderLine.Count)
                    return (false, $"الكمية في أذن التسليم أكبر من المتبقي في أمر البيع للصنف رقم {line.ItemId}", null);

                var issuedQty = issuedHere.Where(x => x.ItemId == line.ItemId).Sum(x => x.Quantity);
                var issuedCount = issuedHere.Where(x => x.ItemId == line.ItemId).Sum(x => x.Count);
                if (line.Quantity + issuedQty > noteLine.Quantity || line.Count + issuedCount > noteLine.Count)
                    return (false, $"الكمية أكبر من المتبقي في أذن التسليم للصنف رقم {line.ItemId}", null);
            }
        }

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var issue = new DeliveryIssue
                {
                    IssueNumber = await NextIssueNumberAsync(),
                    DeliveryOrderId = deliveryId,
                    CustomerId = delivery.CustomerId,
                    SalesOrderId = delivery.SalesOrderId,
                    SaleInvoiceId = delivery.SaleInvoiceId,
                    IssueDate = issueDate ?? delivery.DeliveryDate,
                    Status = DeliveryIssueStatus.Draft,
                    Notes = notes,
                    Carrier = carrier,
                    TrackingNumber = trackingNumber,
                    CreatedBy = user,
                    CreatedAt = DateTime.UtcNow,
                    Items = valid
                };

                _db.DeliveryIssues.Add(issue);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                _logger?.LogInformation("أُنشئ أمر تسليم {Owner} رقم {Number} لأذن {DeliveryNumber}",
                    user, issue.IssueNumber, delivery.DeliveryNumber);
                return (true, null, issue);
            }
            catch (DbUpdateConcurrencyException) { await tx.RollbackAsync(); DetachAll(); }
            catch (DbUpdateException) { await tx.RollbackAsync(); DetachAll(); }
        }
        return (false, "تعذر حفظ أمر التسليم بسبب تعارض في البيانات، حاول مرة أخرى", null);
    }

    public async Task<(bool Success, string? Error)> IssueDeliveryAsync(int issueId, string? user, int? branchId = null)
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var issue = await _db.DeliveryIssues.Include(i => i.Items)
                    .FirstOrDefaultAsync(i => i.Id == issueId);
                if (issue == null) return (false, "أمر التسليم غير موجود");
                if (issue.Status != DeliveryIssueStatus.Draft) return (false, "أمر التسليم مرحّل أو ملغي بالفعل");
                if (await IsPeriodClosedAsync(issue.IssueDate))
                    return (false, $"السنة المالية {issue.IssueDate.Year} مغلقة — لا يمكن ترحيل قيود فيها");

                var delivery = await _db.DeliveryOrders.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == issue.DeliveryOrderId);
                if (delivery == null) return (false, "أذن التسليم غير موجود");
                if (delivery.Status == DeliveryOrderStatus.Cancelled) return (false, "أذن التسليم ملغي");

                if (delivery.SalesOrderId.HasValue)
                {
                    var order = await _db.SalesOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == delivery.SalesOrderId.Value);
                    if (order == null) return (false, "أمر البيع غير موجود");
                    if (order.Status == SalesOrderStatus.Cancelled) return (false, "أمر البيع ملغي — لا يمكن التسليم");
                }
                if (delivery.SaleInvoiceId.HasValue &&
                    !await _db.SaleInvoices.AnyAsync(i => i.Id == delivery.SaleInvoiceId.Value))
                    return (false, "فاتورة البيع المرتبطة غير موجودة");

                var valid = issue.Items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
                if (valid.Count == 0) return (false, "أمر التسليم لا يحتوي على أصناف صالحة للتسليم");

                if (delivery.SalesOrderId.HasValue)
                {
                    var open = await OpenQuantitiesAsync(delivery.SalesOrderId.Value, issue.Id, delivery.Id);
                    var orderLines = await _db.SalesOrderItems
                        .Where(i => i.SalesOrderId == delivery.SalesOrderId.Value).ToListAsync();
                    foreach (var line in valid)
                    {
                        var orderLine = orderLines.FirstOrDefault(i => i.ItemId == line.ItemId);
                        if (orderLine == null) return (false, $"الصنف رقم {line.ItemId} غير موجود في أمر البيع");
                        var remainingQty = orderLine.Quantity - orderLine.DeliveredQty - (open.TryGetValue(line.ItemId, out var o) ? o.Qty : 0m);
                        var remainingCount = orderLine.Count - orderLine.DeliveredCount - (open.TryGetValue(line.ItemId, out var o2) ? o2.Count : 0m);
                        if (line.Quantity > remainingQty || line.Count > remainingCount)
                        {
                            await tx.RollbackAsync(); DetachAll();
                            return (false, $"الكمية أكبر من المتبقي في أمر البيع للصنف رقم {line.ItemId}");
                        }
                    }
                }
                else if (delivery.SaleInvoiceId.HasValue)
                {
                    var invoice = await _db.SaleInvoices.AsNoTracking()
                        .Include(i => i.Items)
                        .FirstOrDefaultAsync(i => i.Id == delivery.SaleInvoiceId.Value);
                    if (invoice == null) return (false, "فاتورة البيع المرتبطة غير موجودة");
                    var delivered = await _db.DeliveryIssues.AsNoTracking()
                        .Where(i => i.SaleInvoiceId == invoice.Id && i.Id != issue.Id && i.Status != DeliveryIssueStatus.Cancelled)
                        .SelectMany(i => i.Items)
                        .ToListAsync();
                    foreach (var line in valid)
                    {
                        var invLine = invoice.Items.FirstOrDefault(i => i.ItemId == line.ItemId);
                        if (invLine == null)
                        {
                            await tx.RollbackAsync(); DetachAll();
                            return (false, $"الصنف رقم {line.ItemId} غير موجود في فاتورة البيع الأصلية");
                        }
                        var deliveredQty = delivered.Where(x => x.ItemId == line.ItemId).Sum(x => x.Quantity);
                        var deliveredCount = delivered.Where(x => x.ItemId == line.ItemId).Sum(x => x.Count);
                        if (line.Quantity + deliveredQty > invLine.Quantity || line.Count + deliveredCount > invLine.Count)
                        {
                            await tx.RollbackAsync(); DetachAll();
                            return (false, $"الكمية المسلّمة أكبر من المتبقي في فاتورة البيع للصنف رقم {line.ItemId}");
                        }
                    }
                }

                var itemIds = valid.Select(i => i.ItemId).Distinct().ToList();
                var stockItems = await _db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
                var ownReserved = await OwnReservationRemainingAsync(delivery.SalesOrderId);
                var shortages = new List<string>();
                foreach (var line in valid)
                {
                    if (!stockItems.TryGetValue(line.ItemId, out var item))
                    {
                        shortages.Add($"الصنف رقم {line.ItemId} غير موجود");
                        continue;
                    }
                    ownReserved.TryGetValue(line.ItemId, out var held);
                    var allowedQty = item.AvailableQuantity + held.Qty;
                    var allowedCount = item.AvailableCount + held.Count;
                    if (line.Quantity > allowedQty)
                        shortages.Add($"«{item.Name}»: المطلوب {line.Quantity:N2} كمية والمتاح {allowedQty:N2}");
                    if (line.Count > allowedCount)
                        shortages.Add($"«{item.Name}»: المطلوب {line.Count:N2} عدد والمتاح {allowedCount:N2}");
                }
                if (shortages.Count > 0)
                {
                    await tx.RollbackAsync(); DetachAll();
                    return (false, "الرصيد المتاح غير كافٍ للتسليم — " + string.Join(" — ", shortages));
                }

                var reservationLines = valid
                    .Select(line => new DeliveryIssueItemLine(
                        line.ItemId,
                        line.SalesOrderItemId,
                        line.Quantity,
                        line.Count))
                    .ToList();
                var consumedReservation = await _reservations.ConsumeForIssuesAsync(
                    reservationLines, issue.CustomerId, issue.IssueDate, beginOwnTransaction: false);
                if (!consumedReservation.Success)
                {
                    await tx.RollbackAsync(); DetachAll();
                    return (false, consumedReservation.Error);
                }

                var stockLines = ToIssueStockLines(valid);
                var stockError = await ApplyStockAsync(stockLines, sign: -1,
                    docNumber: issue.IssueNumber, docType: DocumentType.SalesDeliveryIssue, docId: issue.Id,
                    movementDate: issue.IssueDate, user);
                if (stockError != null) { await tx.RollbackAsync(); DetachAll(); return (false, stockError); }

                var consumed = await ConsumeFifoLayersAsync(stockLines, issue.IssueDate);
                var costTotal = consumed.DominantTotal;

                if (_accounting != null && costTotal > 0)
                    await _accounting.RecordSaleIssueCostAsync(issue.IssueDate, issue.Id, costTotal,
                        user, branchId);

                issue.Status = DeliveryIssueStatus.Issued;
                issue.IssuedBy = user;
                issue.IssuedAt = DateTime.UtcNow;

                foreach (var line in valid)
                {
                    if (line.SalesOrderItemId is int orderLineId)
                    {
                        var orderLine = await _db.SalesOrderItems.FirstOrDefaultAsync(i => i.Id == orderLineId);
                        if (orderLine == null)
                            orderLine = await _db.SalesOrderItems.FirstOrDefaultAsync(
                                i => i.SalesOrderId == delivery.SalesOrderId && i.ItemId == line.ItemId);
                        if (orderLine != null)
                        {
                            orderLine.DeliveredQty += line.Quantity;
                            orderLine.DeliveredCount += line.Count;
                        }
                    }
                }

                await _db.SaveChangesAsync();
                await RefreshDeliveryStatusAsync(delivery.Id);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                _logger?.LogInformation("رحّل أمر تسليم {Owner} رقم {Number} بكلفة {Cost:C} وخصم المخزون واستهلاك الحجز",
                    user, issue.IssueNumber, costTotal);
                return (true, null);
            }
            catch (DbUpdateConcurrencyException) { await tx.RollbackAsync(); DetachAll(); }
            catch (DbUpdateException) { await tx.RollbackAsync(); DetachAll(); }
        }
        return (false, "تعذر ترحيل أمر التسليم بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    public async Task<(bool Success, string? Error)> CancelDeliveryIssueAsync(int issueId, string? user)
    {
        var issue = await _db.DeliveryIssues.FirstOrDefaultAsync(i => i.Id == issueId);
        if (issue == null) return (false, "أمر التسليم غير موجود");
        if (issue.Status == DeliveryIssueStatus.Issued) return (false, "لا يمكن إلغاء أمر تسليم مرحّل — استخدم المرتجع");
        if (issue.Status == DeliveryIssueStatus.Cancelled) return (false, "أمر التسليم ملغي بالفعل");

        issue.Status = DeliveryIssueStatus.Cancelled;
        await _db.SaveChangesAsync();
        await RefreshDeliveryStatusAsync(issue.DeliveryOrderId);
        await _db.SaveChangesAsync();
        _logger?.LogInformation("أُلغي أمر تسليم {Number}", issue.IssueNumber);
        return (true, null);
    }

    private async Task RefreshDeliveryStatusAsync(int deliveryId)
    {
        var delivery = await _db.DeliveryOrders.FirstOrDefaultAsync(d => d.Id == deliveryId);
        if (delivery == null || delivery.Status == DeliveryOrderStatus.Cancelled) return;

        var issues = await _db.DeliveryIssues.AsNoTracking()
            .Include(i => i.Items)
            .Where(i => i.DeliveryOrderId == deliveryId && i.Status == DeliveryIssueStatus.Issued)
            .ToListAsync();
        if (issues.Count == 0)
        {
            delivery.Status = DeliveryOrderStatus.Draft;
            return;
        }

        var lines = await _db.DeliveryOrderItems.AsNoTracking()
            .Where(i => i.DeliveryOrderId == deliveryId).ToListAsync();
        // The "is this note fully issued?" boundary lives in DeliveryOpenLines so this status
        // refresh, the invoicing-status refresh and the note screen cannot drift apart on it.
        var issuedQty = issues.SelectMany(i => i.Items)
            .GroupBy(x => x.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var issuedCount = issues.SelectMany(i => i.Items)
            .GroupBy(x => x.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));
        var fullyIssued = lines.All(l => DeliveryOpenLines.IsLineSettled(
            l.Quantity, l.Count,
            issuedQty.GetValueOrDefault(l.ItemId),
            issuedCount.GetValueOrDefault(l.ItemId)));

        delivery.Status = fullyIssued ? DeliveryOrderStatus.Delivered : DeliveryOrderStatus.PartiallyIssued;
    }

    private async Task<Dictionary<int, (decimal Qty, decimal Count)>> OwnReservationRemainingAsync(int? salesOrderId)
    {
        if (!salesOrderId.HasValue)
            return new Dictionary<int, (decimal Qty, decimal Count)>();
        var reservation = await _reservations.GetForOrderAsync(salesOrderId.Value);
        if (reservation == null) return new Dictionary<int, (decimal Qty, decimal Count)>();
        return reservation.Items
            .GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => (Qty: g.Sum(l => l.RemainingQuantity), Count: g.Sum(l => l.RemainingCount)));
    }

    private async Task<Dictionary<int, (decimal Qty, decimal Count)>> OpenQuantitiesAsync(
        int salesOrderId, int? excludeIssueId, int? excludeDeliveryId = null)
    {
        var noted = await _db.DeliveryOrders.AsNoTracking()
            .Where(d => d.SalesOrderId == salesOrderId
                && d.Status != DeliveryOrderStatus.Cancelled
                && (!excludeDeliveryId.HasValue || d.Id != excludeDeliveryId.Value))
            .SelectMany(d => d.Items)
            .GroupBy(x => x.ItemId)
            .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.Quantity), Cnt = g.Sum(x => x.Count) })
            .ToDictionaryAsync(x => x.ItemId, x => (Qty: x.Qty, Cnt: x.Cnt));

        var issued = await _db.DeliveryIssues.AsNoTracking()
            .Where(i => i.SalesOrderId == salesOrderId
                && i.Status == DeliveryIssueStatus.Issued
                && (!excludeIssueId.HasValue || i.Id != excludeIssueId.Value)
                && (!excludeDeliveryId.HasValue || i.DeliveryOrderId != excludeDeliveryId.Value))
            .SelectMany(i => i.Items)
            .GroupBy(x => x.ItemId)
            .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.Quantity), Cnt = g.Sum(x => x.Count) })
            .ToDictionaryAsync(x => x.ItemId, x => (Qty: x.Qty, Cnt: x.Cnt));

        if (excludeIssueId.HasValue)
        {
            var excluded = await _db.DeliveryIssueItems.AsNoTracking()
                .Where(x => x.DeliveryIssueId == excludeIssueId.Value)
                .GroupBy(x => x.ItemId)
                .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.Quantity), Cnt = g.Sum(x => x.Count) })
                .ToDictionaryAsync(x => x.ItemId, x => (Qty: x.Qty, Cnt: x.Cnt));
            foreach (var (itemId, value) in excluded)
            {
                if (noted.TryGetValue(itemId, out var current))
                    noted[itemId] = (current.Qty - value.Qty, current.Cnt - value.Cnt);
            }
        }

        var result = new Dictionary<int, (decimal Qty, decimal Count)>();
        foreach (var (itemId, value) in noted)
        {
            var issuedValue = issued.TryGetValue(itemId, out var i) ? i : (Qty: 0m, Cnt: 0m);
            result[itemId] = (value.Qty - issuedValue.Qty, value.Cnt - issuedValue.Cnt);
        }
        return result;
    }

    public async Task<(bool Success, string? Error)> DeliverDeliveryOrderAsync(int deliveryId, string? user, int? branchId = null)
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var delivery = await _db.DeliveryOrders.Include(d => d.Items)
                    .FirstOrDefaultAsync(d => d.Id == deliveryId);
                if (delivery == null) return (false, "أذن التسليم غير موجود");
                if (delivery.Status != DeliveryOrderStatus.Draft) return (false, "أذن التسليم مرحّل أو ملغي بالفعل");
                if (delivery.IsOrderBacked)
                    return (false, "أذن التسليم مرتبط بأمر بيع — سلّمه عبر أمر التسليم");
                if (await _db.DeliveryIssues.AnyAsync(i => i.DeliveryOrderId == deliveryId && i.Status != DeliveryIssueStatus.Cancelled))
                    return (false, "يوجد أمر تسليم مرحّل لهذا الأذن — لا يمكن ترحيل الأذن مرة أخرى");
                if (await IsPeriodClosedAsync(delivery.DeliveryDate))
                    return (false, $"السنة المالية {delivery.DeliveryDate.Year} مغلقة — لا يمكن ترحيل قيود فيها");

                var invoice = await _db.SaleInvoices.Include(i => i.Items).Include(i => i.Customer).AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == delivery.SaleInvoiceId);
                if (invoice == null) return (false, "فاتورة البيع المرتبطة غير موجودة");

                var valid = delivery.Items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
                if (valid.Count == 0) return (false, "أذن التسليم لا يحتوي على أصناف صالحة للتسليم");

                var delivered = await _db.DeliveryOrders
                    .Where(d => d.SaleInvoiceId == invoice.Id && d.Id != deliveryId && d.Status == DeliveryOrderStatus.Delivered)
                    .SelectMany(d => d.Items)
                    .AsNoTracking()
                    .ToListAsync();

                foreach (var item in valid)
                {
                    var invLine = invoice.Items.FirstOrDefault(i => i.ItemId == item.ItemId);
                    if (invLine == null) continue;
                    decimal deliveredCount = delivered.Where(x => x.ItemId == item.ItemId).Sum(x => x.Count);
                    decimal deliveredQty = delivered.Where(x => x.ItemId == item.ItemId).Sum(x => x.Quantity);
                    if (item.Count + deliveredCount > invLine.Count || item.Quantity + deliveredQty > invLine.Quantity)
                    { await tx.RollbackAsync(); DetachAll(); return (false, $"الكمية المسلّمة أكبر من المتبقي في فاتورة البيع للصنف رقم {item.ItemId}"); }
                }

                var stockLines = ToStockLines(valid);
                var stockError = await ApplyStockAsync(stockLines, sign: -1,
                    docNumber: delivery.DeliveryNumber, docType: DocumentType.SaleDeliveryOrder, docId: delivery.Id,
                    movementDate: delivery.DeliveryDate, user);
                if (stockError != null) { await tx.RollbackAsync(); DetachAll(); return (false, stockError); }

                var consumed = await ConsumeFifoLayersAsync(stockLines, delivery.DeliveryDate);
                var costTotal = consumed.DominantTotal;

                decimal rawValue = 0m;
                foreach (var item in valid)
                {
                    var invLine = invoice.Items.FirstOrDefault(i => i.ItemId == item.ItemId);
                    if (invLine == null) continue;
                    decimal effective = item.Quantity > 0 ? item.Quantity : item.Count;
                    rawValue += effective * invLine.UnitPrice;
                }
                decimal value = 0m;
                decimal taxShare = 0m;
                if (invoice.TotalAmount > 0m && invoice.NetAmount >= 0m)
                {
                    decimal share = rawValue / invoice.TotalAmount;
                    value = invoice.NetAmount * share;
                    if (invoice.Tax > 0m)
                        taxShare = invoice.Tax * share;
                }
                if (_accounting != null && (value > 0 || costTotal > 0))
                    await _accounting.RecordSaleDeliveryAsync(delivery.DeliveryDate, invoice.CustomerId,
                        value, costTotal, user, branchId, delivery.Id, taxShare);

                delivery.Status = DeliveryOrderStatus.Delivered;
                delivery.DeliveredBy = user;
                delivery.DeliveredAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                _logger?.LogInformation("رحّل أذن تسليم {Owner} رقم {Number} بقيمة {Val:C} وكلفة {Cost:C} فخصم المخزون ورصيد العميل",
                    user, delivery.DeliveryNumber, value, costTotal);
                return (true, null);
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(); DetachAll();
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync(); DetachAll();
            }
        }
        return (false, "تعذر ترحيل أذن التسليم بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    public async Task<(bool Success, string? Error)> CancelDeliveryOrderAsync(int deliveryId, string? user)
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var delivery = await _db.DeliveryOrders.FirstOrDefaultAsync(d => d.Id == deliveryId);
                if (delivery == null) return (false, "أذن التسليم غير موجود");
                if (delivery.Status == DeliveryOrderStatus.Delivered) return (false, "لا يمكن إلغاء أذن تسليم تم ترحيله");
                if (delivery.Status == DeliveryOrderStatus.Cancelled) return (false, "أذن التسليم ملغي بالفعل");
                if (await _db.DeliveryIssues.AnyAsync(i => i.DeliveryOrderId == deliveryId && i.Status != DeliveryIssueStatus.Cancelled))
                    return (false, "يوجد أمر تسليم مرحّل لهذا الأذن — ألغِ أمر التسليم أولاً");

                delivery.Status = DeliveryOrderStatus.Cancelled;
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                _logger?.LogInformation("أُلغي أذن تسليم {Number}", delivery.DeliveryNumber);
                return (true, null);
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(); DetachAll();
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync(); DetachAll();
            }
        }
        return (false, "تعذر إلغاء أذن التسليم بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    public async Task<(bool Success, string? Error)> CreatePurchaseAsync(PurchaseInvoice invoice, List<PurchaseInvoiceItem> items, string? user, int? branchId = null, bool beginOwnTransaction = true)
    {
        if (await IsPeriodClosedAsync(invoice.InvoiceDate))
            return (false, $"السنة المالية {invoice.InvoiceDate.Year} مغلقة — لا يمكن إدراج قيود فيها");

        var valid = items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل بالكمية أو العدد");
        var badPrice = items.FirstOrDefault(i => i.ItemId > 0 && i.UnitPrice < 0);
        if (badPrice != null)
            return (false, "سعر الوحدة يجب ألا يكون سالباً");
        var badQty = items.FirstOrDefault(i => i.ItemId > 0 && (i.Quantity < 0 || i.Count < 0));
        if (badQty != null)
            return (false, "الكمية أو العدد يجب ألا يكون سالباً");
        var duplicatePurchase = valid.GroupBy(i => i.ItemId).FirstOrDefault(g => g.Count() > 1);
        if (duplicatePurchase != null)
            return (false, $"الصنف رقم {duplicatePurchase.Key} مكرر أكثر من مرة في الفاتورة");

        if (!beginOwnTransaction)
            return await CreatePurchaseCoreAsync(invoice, valid, user, branchId);

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var result = await CreatePurchaseCoreAsync(invoice, valid, user, branchId);
                if (!result.Success) { await tx.RollbackAsync(); DetachAll(); ResetInvoiceKeys(invoice); return result; }
                await tx.CommitAsync();
                return (true, null);
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(); DetachAll(); ResetInvoiceKeys(invoice);
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync(); DetachAll(); ResetInvoiceKeys(invoice);
            }
        }
        return (false, "تعذر حفظ فاتورة الشراء بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    private async Task<(bool Success, string? Error)> CreatePurchaseCoreAsync(PurchaseInvoice invoice, List<PurchaseInvoiceItem> valid, string? user, int? branchId)
    {
        invoice.InvoiceNumber = await NextInvoiceNumberAsync(
            _db.PurchaseInvoices.Select(p => p.InvoiceNumber), "PO");
        invoice.BranchId = branchId;

        foreach (var group in valid.GroupBy(i => i.ItemId))
        {
            var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == group.Key);
            if (item == null) continue;
            var priceLine = group.LastOrDefault(l => l.Quantity > 0);
            if (priceLine != null)
                item.PurchasePrice = priceLine.UnitPrice;
        }

        if (valid.Any(i => i.Discount > i.Gross))
        {
            _db.ChangeTracker.Clear();
            return (false, "خصم الصنف لا يمكن أن يتجاوز قيمته الإجمالية");
        }

        invoice.TotalAmount = valid.Sum(i => i.Total);
        invoice.NetAmount = invoice.TotalAmount - invoice.Discount - (invoice.Discount2 ?? 0) - (invoice.Discount3 ?? 0) + invoice.Tax;
        if (invoice.NetAmount < 0)
        {
            _db.ChangeTracker.Clear();
            return (false, "الخصم أكبر من إجمالي الفاتورة؛ لا يمكن أن يكون الصافي سالباً");
        }
        if (invoice.PaymentTerms != InvoicePaymentTerms.OnReceipt
            && invoice.PaymentTerms != InvoicePaymentTerms.OpenTerm
            && invoice.DueDate == null)
            invoice.DueDate = invoice.InvoiceDate.AddDays(PaymentTermDays(invoice.PaymentTerms));
        if (invoice.SupplierId > 0)
        {
            foreach (var line in valid)
            {
                await UpsertSupplierQuoteAsync(invoice.SupplierId, line.ItemId, line.UnitPrice);
            }
        }

        invoice.PaidAmount = invoice.PaymentTerms == InvoicePaymentTerms.OnReceipt ? invoice.NetAmount : 0;
        invoice.IsPaid = invoice.PaymentTerms == InvoicePaymentTerms.OnReceipt;
        invoice.CreatedBy = user;
        invoice.CreatedAt = DateTime.UtcNow;
        invoice.Items = valid;

        _db.PurchaseInvoices.Add(invoice);
        await _db.SaveChangesAsync();

        var stockLines = ToStockLines(valid);
        var stockError = await ApplyStockAsync(
            stockLines, sign: +1,
            docNumber: invoice.InvoiceNumber, docType: DocumentType.PurchaseInvoice, docId: invoice.Id,
            movementDate: invoice.InvoiceDate, user);
        if (stockError != null) { _db.ChangeTracker.Clear(); return (false, stockError); }

        await ReplenishFifoLayersAsync(valid, invoice.InvoiceDate);

        if (_accounting != null && invoice.NetAmount > 0)
            await _accounting.RecordPurchaseInvoiceAsync(invoice.InvoiceDate, invoice.SupplierId, invoice.NetAmount, user, branchId);

        if (_accounting != null && invoice.PaymentTerms == InvoicePaymentTerms.OnReceipt && invoice.NetAmount > 0)
            await _accounting.RecordDisbursementAsync(invoice.InvoiceDate,
                invoice.NetAmount,
                PaymentMethod.Cash, invoice.SupplierId, user, branchId);

        _logger?.LogInformation("فُتحت فاتورة شراء {Owner} رقم {Number} صافي {Net:C} بفاتورة {InvId}",
            user, invoice.InvoiceNumber, invoice.NetAmount, invoice.Id);
        return (true, null);
    }

    /// <summary>
    /// ينشئ مرتجع بيع مسودة ثم يرحّله. مع <c>beginOwnTransaction: false</c> تنضم المسودة
    /// والترحيل إلى معاملة المستدعي فيُحفظ المرتجع كاملًا أو لا يُحفظ منه شيء.
    /// </summary>
    public async Task<(bool Success, string? Error)> CreateSaleReturnAsync(SaleReturn saleReturn, List<SaleReturnItem> items, string? user, bool beginOwnTransaction = true)
    {
        if (!beginOwnTransaction) RequireAmbientTransaction(nameof(CreateSaleReturnAsync));
        var (draftOk, draftErr, returnId) = await CreateSaleReturnDraftAsync(saleReturn, items, user, beginOwnTransaction);
        if (!draftOk) return (false, draftErr);
        var (postOk, postErr) = await PostSaleReturnAsync(returnId, user, beginOwnTransaction);
        if (!postOk) return (false, postErr);
        return (true, null);
    }

    public async Task<(bool Success, string? Error, int ReturnId)> CreateSaleReturnDraftAsync(SaleReturn saleReturn, List<SaleReturnItem> items, string? user, bool beginOwnTransaction = true)
    {
        if (items.Any(i => i.ItemId > 0 && (i.Count < 0 || i.Quantity < 0)))
            return (false, "لا يمكن أن تكون الأعداد أو الكميات سالبة في المرتجع", 0);

        var valid = items.Where(i => i.ItemId > 0 && (i.Count != 0 || i.Quantity != 0)).ToList();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل بالعدد أو الكمية", 0);
        if (valid.GroupBy(i => i.ItemId).Any(g => g.Count() > 1))
            return (false, "لا يمكن تكرار نفس الصنف أكثر من مرة في مرتجع البيع", 0);
        if (!beginOwnTransaction) RequireAmbientTransaction(nameof(CreateSaleReturnDraftAsync));

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            // A null transaction means the caller's transaction owns this unit of work: EF
            // refuses a second transaction on the same connection, so the method enlists in
            // the ambient one instead of opening its own.
            await using var tx = beginOwnTransaction ? await _db.Database.BeginTransactionAsync() : null;
            try
            {
                saleReturn.ReturnNumber = await NextReturnNumberAsync(
                    _db.SaleReturns.Select(r => r.ReturnNumber), "SRTN");
                // The branch is server-owned (BindNever on the model): it is inherited from the
                // source invoice so a posted return can never book into a client-chosen branch.
                saleReturn.BranchId = saleReturn.SaleInvoiceId.HasValue
                    ? await _db.SaleInvoices.Where(i => i.Id == saleReturn.SaleInvoiceId.Value)
                        .Select(i => i.BranchId).FirstOrDefaultAsync()
                    : null;
                saleReturn.TotalAmount = decimal.Round(valid.Sum(i => i.Total), 2);
                saleReturn.CreatedBy = user;
                saleReturn.Status = ReturnStatus.Draft;
                saleReturn.PostedBy = null;
                saleReturn.PostedAt = null;
                saleReturn.Items = valid;

                _db.SaleReturns.Add(saleReturn);
                await _db.SaveChangesAsync();

                if (tx is not null) await tx.CommitAsync();
                _logger?.LogInformation("أُنشئت مسودة مرتجع بيع {Owner} رقم {Number} بمبلغ {Amt:C}", user, saleReturn.ReturnNumber, saleReturn.TotalAmount);
                return (true, null, saleReturn.Id);
            }
            catch (DbUpdateConcurrencyException)
            {
                await TryRollbackAsync(tx); DetachAll(); ResetReturnKeys(saleReturn);
                if (!beginOwnTransaction) throw;
            }
            catch (DbUpdateException)
            {
                await TryRollbackAsync(tx); DetachAll(); ResetReturnKeys(saleReturn);
                if (!beginOwnTransaction) throw;
            }
        }
        return (false, "تعذر حفظ مسودة مرتجع البيع بسبب تعارض في البيانات، حاول مرة أخرى", 0);
    }

    /// <summary>
    /// يرحّل مسودة مرتجع بيع. مع <c>beginOwnTransaction: false</c> ينضم الترحيل إلى معاملة
    /// المستدعي، فلا يبقى في القاعدة أثر لمرتجع غير مرحّل.
    /// </summary>
    public async Task<(bool Success, string? Error)> PostSaleReturnAsync(int saleReturnId, string? user, bool beginOwnTransaction = true)
    {
        if (!beginOwnTransaction) RequireAmbientTransaction(nameof(PostSaleReturnAsync));
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = beginOwnTransaction ? await _db.Database.BeginTransactionAsync() : null;
            try
            {
                var saleReturn = await _db.SaleReturns.Include(r => r.Items)
                    .FirstOrDefaultAsync(r => r.Id == saleReturnId);
                if (saleReturn == null) return (false, "مرتجع البيع غير موجود");
                if (saleReturn.Status == ReturnStatus.Posted) return (false, "مرتجع البيع مرحّل بالفعل");
                if (await IsPeriodClosedAsync(saleReturn.ReturnDate))
                    return (false, $"السنة المالية {saleReturn.ReturnDate.Year} مغلقة — لا يمكن ترحيل مرتجع فيها");

                if (saleReturn.Items.Any(i => i.ItemId > 0 && (i.Count < 0 || i.Quantity < 0)))
                    return (false, "لا يمكن أن تكون الأعداد أو الكميات سالبة في المرتجع");

                var valid = saleReturn.Items.Where(i => i.ItemId > 0 && (i.Count != 0 || i.Quantity != 0)).ToList();
                if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل بالعدد أو الكمية");
                saleReturn.TotalAmount = valid.Sum(i => i.Total);

                var returnError = await ValidateSaleReturnQuantitiesAsync(saleReturn, valid);
                if (returnError != null) { await TryRollbackAsync(tx); DetachAll(); return (false, returnError); }

                var invPrices = await _db.SaleInvoiceItems.AsNoTracking()
                    .Where(i => i.SaleInvoiceId == saleReturn.SaleInvoiceId)
                    .Select(i => new { i.ItemId, i.UnitPrice })
                    .ToListAsync();
                var itemIds = valid.Select(v => v.ItemId).Distinct().ToList();
                var masterPrices = await _db.Items.AsNoTracking()
                    .Where(i => itemIds.Contains(i.Id))
                    .Select(i => new { i.Id, i.SalePrice })
                    .ToListAsync();
                foreach (var line in valid)
                {
                    var match = invPrices.FirstOrDefault(p => p.ItemId == line.ItemId);
                    line.UnitPrice = match != null
                        ? match.UnitPrice
                        : masterPrices.FirstOrDefault(m => m.Id == line.ItemId)?.SalePrice ?? line.UnitPrice;
                }
                saleReturn.TotalAmount = decimal.Round(valid.Sum(i => i.Total), 2);

                var stockError = await ApplyStockAsync(
                    ToReturnStockLines(valid), sign: +1,
                    docNumber: saleReturn.ReturnNumber, docType: DocumentType.SaleReturn, docId: saleReturn.Id,
                    movementDate: saleReturn.ReturnDate, user);
                if (stockError != null) { await TryRollbackAsync(tx); DetachAll(); return (false, stockError); }

                var costTotal = await RestoreSaleReturnLayersAsync(valid, saleReturn.ReturnDate);

                // Reverse the source invoice proportionally: f = returned gross / invoice gross.
                // Without a source invoice there is nothing to prorate, so the return stands on
                // its own at the item-master price and carries no discount or tax reversal.
                var mirror = ReturnMirror.ForGross(saleReturn.TotalAmount);
                if (saleReturn.SaleInvoiceId.HasValue)
                {
                    var invTotals = await _db.SaleInvoices.AsNoTracking()
                        .Where(i => i.Id == saleReturn.SaleInvoiceId)
                        .Select(i => new { i.Tax, i.TotalAmount, i.NetAmount })
                        .FirstOrDefaultAsync();
                    if (invTotals != null)
                        mirror = ReturnMirror.ProratedAgainst(saleReturn.TotalAmount,
                            invTotals.TotalAmount, invTotals.NetAmount, invTotals.Tax);
                }

                await _db.SaveChangesAsync();

                if (_accounting != null && mirror.Receivable > 0m)
                    await _accounting.RecordSaleReturnWithCostAsync(
                        saleReturn.ReturnDate, saleReturn.Id, saleReturn.CustomerId,
                        mirror.ContraValue, costTotal,
                        user, saleReturn.BranchId, mirror.Tax);

                saleReturn.Status = ReturnStatus.Posted;
                saleReturn.PostedBy = user;
                saleReturn.PostedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                if (tx is not null) await tx.CommitAsync();
                _logger?.LogInformation("رحّل مرتجع بيع {Owner} رقم {Number} بمبلغ {Amt:C}", user, saleReturn.ReturnNumber, saleReturn.TotalAmount);
                return (true, null);
            }
            catch (DbUpdateConcurrencyException)
            {
                await TryRollbackAsync(tx); DetachAll();
                if (!beginOwnTransaction) throw;
            }
            catch (DbUpdateException)
            {
                await TryRollbackAsync(tx); DetachAll();
                if (!beginOwnTransaction) throw;
            }
        }
        return (false, "تعذر ترحيل مرتجع البيع بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    /// <summary>
    /// ينشئ مرتجع شراء مسودة ثم يرحّله. مع <c>beginOwnTransaction: false</c> تنضم المسودة
    /// والترحيل إلى معاملة المستدعي فيُحفظ المرتجع كاملًا أو لا يُحفظ منه شيء.
    /// </summary>
    public async Task<(bool Success, string? Error)> CreatePurchaseReturnAsync(PurchaseReturn purchaseReturn, List<PurchaseReturnItem> items, string? user, bool beginOwnTransaction = true)
    {
        if (!beginOwnTransaction) RequireAmbientTransaction(nameof(CreatePurchaseReturnAsync));
        var (draftOk, draftErr, returnId) = await CreatePurchaseReturnDraftAsync(purchaseReturn, items, user, beginOwnTransaction);
        if (!draftOk) return (false, draftErr);
        var (postOk, postErr) = await PostPurchaseReturnAsync(returnId, user, beginOwnTransaction);
        if (!postOk) return (false, postErr);
        return (true, null);
    }

    public async Task<(bool Success, string? Error, int ReturnId)> CreatePurchaseReturnDraftAsync(PurchaseReturn purchaseReturn, List<PurchaseReturnItem> items, string? user, bool beginOwnTransaction = true)
    {
        if (items.Any(i => i.ItemId > 0 && (i.Count < 0 || i.Quantity < 0)))
            return (false, "لا يمكن أن تكون الأعداد أو الكميات سالبة في المرتجع", 0);

        var valid = items.Where(i => i.ItemId > 0 && (i.Count != 0 || i.Quantity != 0)).ToList();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل بالعدد أو الكمية", 0);
        if (valid.GroupBy(i => i.ItemId).Any(g => g.Count() > 1))
            return (false, "لا يمكن تكرار نفس الصنف أكثر من مرة في مرتجع الشراء", 0);
        if (!beginOwnTransaction) RequireAmbientTransaction(nameof(CreatePurchaseReturnDraftAsync));

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = beginOwnTransaction ? await _db.Database.BeginTransactionAsync() : null;
            try
            {
                purchaseReturn.ReturnNumber = await NextReturnNumberAsync(
                    _db.PurchaseReturns.Select(r => r.ReturnNumber), "PRTN");
                // The branch is server-owned (BindNever on the model): it is inherited from the
                // source invoice so a posted return can never book into a client-chosen branch.
                purchaseReturn.BranchId = purchaseReturn.PurchaseInvoiceId.HasValue
                    ? await _db.PurchaseInvoices.Where(i => i.Id == purchaseReturn.PurchaseInvoiceId.Value)
                        .Select(i => i.BranchId).FirstOrDefaultAsync()
                    : null;
                purchaseReturn.TotalAmount = decimal.Round(valid.Sum(i => i.Total), 2);
                purchaseReturn.CreatedBy = user;
                purchaseReturn.Status = ReturnStatus.Draft;
                purchaseReturn.PostedBy = null;
                purchaseReturn.PostedAt = null;
                purchaseReturn.Items = valid;

                _db.PurchaseReturns.Add(purchaseReturn);
                await _db.SaveChangesAsync();

                if (tx is not null) await tx.CommitAsync();
                _logger?.LogInformation("أُنشئت مسودة مرتجع شراء {Owner} رقم {Number} بمبلغ {Amt:C}", user, purchaseReturn.ReturnNumber, purchaseReturn.TotalAmount);
                return (true, null, purchaseReturn.Id);
            }
            catch (DbUpdateConcurrencyException)
            {
                await TryRollbackAsync(tx); DetachAll(); ResetReturnKeys(purchaseReturn);
                if (!beginOwnTransaction) throw;
            }
            catch (DbUpdateException)
            {
                await TryRollbackAsync(tx); DetachAll(); ResetReturnKeys(purchaseReturn);
                if (!beginOwnTransaction) throw;
            }
        }
        return (false, "تعذر حفظ مسودة مرتجع الشراء بسبب تعارض في البيانات، حاول مرة أخرى", 0);
    }

    /// <summary>
    /// يرحّل مسودة مرتجع شراء. مع <c>beginOwnTransaction: false</c> ينضم الترحيل إلى معاملة
    /// المستدعي، فلا يبقى في القاعدة أثر لمرتجع غير مرحّل.
    /// </summary>
    public async Task<(bool Success, string? Error)> PostPurchaseReturnAsync(int purchaseReturnId, string? user, bool beginOwnTransaction = true)
    {
        if (!beginOwnTransaction) RequireAmbientTransaction(nameof(PostPurchaseReturnAsync));
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = beginOwnTransaction ? await _db.Database.BeginTransactionAsync() : null;
            try
            {
                var purchaseReturn = await _db.PurchaseReturns.Include(r => r.Items)
                    .FirstOrDefaultAsync(r => r.Id == purchaseReturnId);
                if (purchaseReturn == null) return (false, "مرتجع الشراء غير موجود");
                if (purchaseReturn.Status == ReturnStatus.Posted) return (false, "مرتجع الشراء مرحّل بالفعل");
                if (await IsPeriodClosedAsync(purchaseReturn.ReturnDate))
                    return (false, $"السنة المالية {purchaseReturn.ReturnDate.Year} مغلقة — لا يمكن ترحيل مرتجع فيها");

                if (purchaseReturn.Items.Any(i => i.ItemId > 0 && (i.Count < 0 || i.Quantity < 0)))
                    return (false, "لا يمكن أن تكون الأعداد أو الكميات سالبة في المرتجع");

                var valid = purchaseReturn.Items.Where(i => i.ItemId > 0 && (i.Count != 0 || i.Quantity != 0)).ToList();
                if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل بالعدد أو الكمية");
                purchaseReturn.TotalAmount = valid.Sum(i => i.Total);

                var returnError = await ValidatePurchaseReturnQuantitiesAsync(purchaseReturn, valid);
                if (returnError != null) { await TryRollbackAsync(tx); DetachAll(); return (false, returnError); }

                var invPrices = await _db.PurchaseInvoiceItems.AsNoTracking()
                    .Where(i => i.PurchaseInvoiceId == purchaseReturn.PurchaseInvoiceId)
                    .Select(i => new { i.ItemId, i.UnitPrice })
                    .ToListAsync();
                var itemIds = valid.Select(v => v.ItemId).Distinct().ToList();
                var masterPrices = await _db.Items.AsNoTracking()
                    .Where(i => itemIds.Contains(i.Id))
                    .Select(i => new { i.Id, i.PurchasePrice })
                    .ToListAsync();
                foreach (var line in valid)
                {
                    var match = invPrices.FirstOrDefault(p => p.ItemId == line.ItemId);
                    line.UnitPrice = match != null
                        ? match.UnitPrice
                        : masterPrices.FirstOrDefault(m => m.Id == line.ItemId)?.PurchasePrice ?? line.UnitPrice;
                }
                purchaseReturn.TotalAmount = decimal.Round(valid.Sum(i => i.Total), 2);

                var stockError = await ApplyStockAsync(
                    ToReturnStockLines(valid), sign: -1,
                    docNumber: purchaseReturn.ReturnNumber, docType: DocumentType.PurchaseReturn, docId: purchaseReturn.Id,
                    movementDate: purchaseReturn.ReturnDate, user);
                if (stockError != null) { await TryRollbackAsync(tx); DetachAll(); return (false, stockError); }

                var consumed = await ConsumeFifoLayersAsync(ToReturnStockLines(valid), purchaseReturn.ReturnDate);
                var costTotal = consumed.DominantTotal;

                // Reverse the source purchase invoice proportionally: f = returned gross / invoice
                // gross, applied to the invoice NET so the supplier credit mirrors the original
                // booking (which credited 2000 with the net, tax included). Without a source
                // invoice there is nothing to prorate and the return stands at the master price.
                var mirror = ReturnMirror.ForGross(purchaseReturn.TotalAmount);
                if (purchaseReturn.PurchaseInvoiceId.HasValue)
                {
                    var invTotals = await _db.PurchaseInvoices.AsNoTracking()
                        .Where(i => i.Id == purchaseReturn.PurchaseInvoiceId)
                        .Select(i => new { i.Tax, i.TotalAmount, i.NetAmount })
                        .FirstOrDefaultAsync();
                    if (invTotals != null)
                        mirror = ReturnMirror.ProratedAgainst(purchaseReturn.TotalAmount,
                            invTotals.TotalAmount, invTotals.NetAmount, invTotals.Tax);
                }

                await _db.SaveChangesAsync();

                if (_accounting != null && mirror.Receivable > 0m)
                    await _accounting.RecordPurchaseReturnWithCostAsync(
                        purchaseReturn.ReturnDate, purchaseReturn.Id, purchaseReturn.SupplierId,
                        mirror.Receivable, costTotal,
                        user, purchaseReturn.BranchId);

                purchaseReturn.Status = ReturnStatus.Posted;
                purchaseReturn.PostedBy = user;
                purchaseReturn.PostedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                if (tx is not null) await tx.CommitAsync();
                _logger?.LogInformation("رحّل مرتجع شراء {Owner} رقم {Number} بمبلغ {Amt:C}", user, purchaseReturn.ReturnNumber, purchaseReturn.TotalAmount);
                return (true, null);
            }
            catch (DbUpdateConcurrencyException)
            {
                await TryRollbackAsync(tx); DetachAll();
                if (!beginOwnTransaction) throw;
            }
            catch (DbUpdateException)
            {
                await TryRollbackAsync(tx); DetachAll();
                if (!beginOwnTransaction) throw;
            }
        }
        return (false, "تعذر ترحيل مرتجع الشراء بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    /// <summary>
    /// يسجّل جردًا ويعدّل رصيد الصنف وحركات المخزون وطبقات التقييم والقيود. مع
    /// <c>beginOwnTransaction: false</c> ينضم الجرد إلى معاملة المستدعي فلا يبقى جرد
    /// برصيد مخزون خاطئ إن فشل أي من هذه الخطوات.
    /// </summary>
    public async Task<(bool Success, string? Error)> CreateAdjustmentAsync(InventoryAdjustment adjustment, string? user, bool beginOwnTransaction = true)
    {
        if (await IsPeriodClosedAsync(adjustment.AdjustmentDate))
            return (false, $"السنة المالية {adjustment.AdjustmentDate.Year} مغلقة — لا يمكن إدراج قيود فيها");

        if (adjustment.NewCount < 0 || adjustment.NewQuantity < 0)
            return (false, "لا يمكن أن يكون الرصيد بعد الجرد سالباً");
        if (!beginOwnTransaction) RequireAmbientTransaction(nameof(CreateAdjustmentAsync));

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = beginOwnTransaction ? await _db.Database.BeginTransactionAsync() : null;
            try
            {
                var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == adjustment.ItemId);
                if (item == null) return (false, "الصنف غير موجود");

                adjustment.ReferenceNumber = await NextReturnNumberAsync(
                    _db.InventoryAdjustments.Select(a => a.ReferenceNumber), "ADJ");

                adjustment.CreatedBy = user;

                decimal oldCount = item.CurrentCount;
                decimal oldQty = item.CurrentQuantity;
                item.CurrentCount = adjustment.NewCount;
                item.CurrentQuantity = adjustment.NewQuantity;

                decimal countDelta = adjustment.NewCount - oldCount;
                decimal qtyDelta = adjustment.NewQuantity - oldQty;
                decimal inCount = Math.Max(countDelta, 0);
                decimal inQty = Math.Max(qtyDelta, 0);
                decimal outCount = Math.Min(countDelta, 0);
                decimal outQty = Math.Min(qtyDelta, 0);

                if (inCount > 0 || inQty > 0)
                    _db.StockMovements.Add(new StockMovement
                    {
                        ItemId = item.Id,
                        Type = MovementType.In,
                        Count = inCount,
                        Quantity = inQty,
                        CountBefore = oldCount,
                        CountAfter = oldCount + inCount,
                        BalanceBefore = oldQty,
                        BalanceAfter = oldQty + inQty,
                        DocumentNumber = adjustment.ReferenceNumber,
                        DocumentType = DocumentType.Adjustment,
                        MovementDate = adjustment.AdjustmentDate,
                        CreatedBy = user
                    });

                if (outCount < 0 || outQty < 0)
                    _db.StockMovements.Add(new StockMovement
                    {
                        ItemId = item.Id,
                        Type = MovementType.Out,
                        Count = outCount,
                        Quantity = outQty,
                        CountBefore = oldCount + inCount,
                        CountAfter = adjustment.NewCount,
                        BalanceBefore = oldQty + inQty,
                        BalanceAfter = adjustment.NewQuantity,
                        DocumentNumber = adjustment.ReferenceNumber,
                        DocumentType = DocumentType.Adjustment,
                        MovementDate = adjustment.AdjustmentDate,
                        CreatedBy = user
                    });

                _db.InventoryAdjustments.Add(adjustment);
                await _db.SaveChangesAsync();

                decimal addedQty = adjustment.NewQuantity - oldQty;
                decimal addedCount = adjustment.NewCount - oldCount;
                if (addedQty > 0 || addedCount > 0)
                {
                    CreateOrTopUpLayer(adjustment.ItemId, addedQty, addedCount,
                        item.PurchasePrice, item.PurchasePrice, adjustment.AdjustmentDate);
                    await _db.SaveChangesAsync();
                }
                else if (addedQty < 0 || addedCount < 0)
                {
                    var consumed = await ConsumeAdjustmentLayersAsync(adjustment.ItemId, -addedQty, -addedCount);
                    await _db.SaveChangesAsync();
                    if (_accounting != null)
                    {
                        var writtenQty = Math.Max(-addedQty, 0);
                        var writtenCount = Math.Max(-addedCount, 0);
                        if (consumed.QtyCost > 0 || consumed.CountCost > 0)
                            await _accounting.RecordStockWriteDownAsync(item.Id, consumed.QtyCost, consumed.CountCost, 1m, user, date: adjustment.AdjustmentDate);
                        else if (writtenQty > 0 || writtenCount > 0)
                            await _accounting.RecordStockWriteDownAsync(item.Id, writtenQty, writtenCount, item.PurchasePrice, user, date: adjustment.AdjustmentDate);
                    }
                }

                if (_accounting != null && (addedQty > 0 || addedCount > 0))
                    await _accounting.RecordOpeningStockAsync(item.Id, Math.Max(addedQty, 0), Math.Max(addedCount, 0), item.PurchasePrice, user, date: adjustment.AdjustmentDate);

                if (tx is not null) await tx.CommitAsync();
                return (true, null);
            }
            catch (DbUpdateConcurrencyException)
            {
                await TryRollbackAsync(tx); DetachAll(); adjustment.Id = 0;
                if (!beginOwnTransaction) throw;
            }
            catch (DbUpdateException)
            {
                await TryRollbackAsync(tx); DetachAll(); adjustment.Id = 0;
                if (!beginOwnTransaction) throw;
            }
        }
        return (false, "تعذر حفظ الجرد بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    public async Task<(bool Success, string? Error)> DeleteAdjustmentAsync(int adjustmentId, string? user)
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var adj = await _db.InventoryAdjustments.FirstOrDefaultAsync(a => a.Id == adjustmentId);
                if (adj == null) return (false, "سجل الجرد غير موجود");

                if (await _db.JournalEntries.AnyAsync(j => j.Source == JournalSource.OpeningStock && j.SourceId == adj.ItemId))
                    return (false, "لا يمكن حذف هذا الجرد لأن بياناته رُحّلت إلى قيود اليومية؛ اضبط المخزون بجرد جديد بدلاً من ذلك");

                var movements = await _db.StockMovements
                    .Where(s => s.DocumentType == DocumentType.Adjustment && s.DocumentNumber == adj.ReferenceNumber)
                    .OrderBy(s => s.Id)
                    .ToListAsync();
                if (movements.Count > 0)
                {
                    var last = movements[^1];
                    var hasLaterMovements = await _db.StockMovements.AnyAsync(m => m.ItemId == last.ItemId && m.Id > last.Id);
                    if (hasLaterMovements)
                        return (false, "لا يمكن حذف هذا الجرد لأن حركات مخزون لاحقة تمت على نفس الصنف؛ اضبط المخزون بجرد جديد بدلاً من ذلك");

                    var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == last.ItemId);
                    if (item == null) return (false, "الصنف المرتبط بالجرد غير موجود");
                    item.CurrentCount = movements[0].CountBefore;
                    item.CurrentQuantity = movements[0].BalanceBefore;
                    _db.StockMovements.RemoveRange(movements);
                }

                _db.InventoryAdjustments.Remove(adj);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                _logger?.LogInformation("حُذف سجل جرد {Owner} رقم {Number} وأُعيد المخزون إلى حالته السابقة",
                    user, adj.ReferenceNumber);
                return (true, null);
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(); DetachAll();
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync(); DetachAll();
            }
        }
        return (false, "تعذر حذف الجرد بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    public async Task<ConsumedCostResult?> GetConsumedCostAsync(int itemId, IReadOnlyCollection<StockLine> lines)
    {
        if (lines.Count == 0) return null;

        var totalQtyCost = 0m;
        var totalCountCost = 0m;

        var grouped = lines
            .Where(l => l.ItemId == itemId)
            .GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => new StockLine(g.Key, g.Sum(x => x.Count), g.Sum(x => x.Quantity)));
        if (grouped.Count == 0) return null;

        var neededQty = grouped.Values.Where(l => l.Quantity > 0).Sum(l => l.Quantity);
        var neededCount = grouped.Values.Where(l => l.Count > 0).Sum(l => l.Count);

        var layers = await _db.StockLayers
            .Where(sl => sl.ItemId == itemId && (sl.RemainingQty > 0 || sl.RemainingCount > 0))
            .OrderBy(sl => sl.DateReceived).ThenBy(sl => sl.Id)
            .ToListAsync();

        var tempQty = neededQty;
        var tempCount = neededCount;

        foreach (var layer in layers)
        {
            if (tempQty > 0 && layer.RemainingQty > 0)
            {
                var take = Math.Min(tempQty, layer.RemainingQty);
                totalQtyCost += take * layer.UnitCost;
                tempQty -= take;
            }
            if (tempCount > 0 && layer.RemainingCount > 0)
            {
                var take = Math.Min(tempCount, layer.RemainingCount);
                totalCountCost += take * layer.CountCost;
                tempCount -= take;
            }
            if (tempQty <= 0 && tempCount <= 0) break;
        }

        return new ConsumedCostResult(totalQtyCost, totalCountCost, neededQty > 0 ? totalQtyCost : totalCountCost);
    }

    private async Task<ConsumedCostResult> ConsumeFifoLayersAsync(IReadOnlyCollection<StockLine> lines, DateTime movementDate)
    {
        var qtyCost = 0m;
        var countCost = 0m;
        var dominantTotal = 0m;

        var grouped = lines
            .GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => new StockLine(g.Key, g.Sum(x => x.Count), g.Sum(x => x.Quantity)));

        var itemIds = grouped.Values.Select(l => l.ItemId).Distinct().ToList();
        var prices = await _db.Items.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.PurchasePrice);

        foreach (var line in grouped.Values)
        {
            var layers = await _db.StockLayers
                .Where(sl => sl.ItemId == line.ItemId && (sl.RemainingQty > 0 || sl.RemainingCount > 0))
                .OrderBy(sl => sl.DateReceived).ThenBy(sl => sl.Id)
                .ToListAsync();

            var lineQtyCost = 0m;
            var lineCountCost = 0m;

            if (line.Quantity > 0)
            {
                var remaining = line.Quantity;
                foreach (var layer in layers)
                {
                    if (remaining <= 0) break;
                    if (layer.RemainingQty <= 0) continue;
                    var take = Math.Min(remaining, layer.RemainingQty);
                    lineQtyCost += take * layer.UnitCost;
                    layer.RemainingQty -= take;
                    remaining -= take;
                }
                if (remaining > 0)
                    lineQtyCost += remaining * prices.GetValueOrDefault(line.ItemId, 0m);
            }

            if (line.Count > 0)
            {
                var remaining = line.Count;
                foreach (var layer in layers)
                {
                    if (remaining <= 0) break;
                    if (layer.RemainingCount <= 0) continue;
                    var take = Math.Min(remaining, layer.RemainingCount);
                    lineCountCost += take * layer.CountCost;
                    layer.RemainingCount -= take;
                    remaining -= take;
                }
                if (remaining > 0)
                    lineCountCost += remaining * prices.GetValueOrDefault(line.ItemId, 0m);
            }

            qtyCost += lineQtyCost;
            countCost += lineCountCost;
            dominantTotal += line.Quantity > 0 ? lineQtyCost : lineCountCost;
        }

        return new ConsumedCostResult(qtyCost, countCost, dominantTotal);
    }

    private async Task<ConsumedCostResult> ConsumeAdjustmentLayersAsync(int itemId, decimal qtyToRemove, decimal countToRemove)
    {
        var qtyCost = 0m;
        var countCost = 0m;
        if (qtyToRemove <= 0 && countToRemove <= 0) return new ConsumedCostResult(0, 0, 0);

        var layers = await _db.StockLayers
            .Where(sl => sl.ItemId == itemId && (sl.RemainingQty > 0 || sl.RemainingCount > 0))
            .OrderBy(sl => sl.DateReceived).ThenBy(sl => sl.Id)
            .ToListAsync();

        var q = qtyToRemove;
        var c = countToRemove;
        foreach (var layer in layers)
        {
            if (q > 0 && layer.RemainingQty > 0)
            {
                var takeQty = Math.Min(q, layer.RemainingQty);
                qtyCost += takeQty * layer.UnitCost;
                layer.RemainingQty -= takeQty;
                q -= takeQty;
            }
            if (c > 0 && layer.RemainingCount > 0)
            {
                var takeCount = Math.Min(c, layer.RemainingCount);
                countCost += takeCount * layer.CountCost;
                layer.RemainingCount -= takeCount;
                c -= takeCount;
            }
            if (q <= 0 && c <= 0) break;
        }

        return new ConsumedCostResult(qtyCost, countCost, qtyCost > 0 ? qtyCost : countCost);
    }

    private async Task ReplenishFifoLayersAsync(List<PurchaseInvoiceItem> lines, DateTime dateReceived)
    {
        foreach (var line in lines)
        {
            if (line.Quantity <= 0 && line.Count <= 0) continue;
            // StockLayer.UnitCost is decimal(18,6) - the six-decimal per-unit cost documented on
            // DecimalPrecision.CostScale - and this is where a purchase price becomes that cost. The
            // price itself is three decimals, so rounding it here used to discard the third decimal
            // of a price the invoice had already stored in full, and the layer is the record every
            // later valuation reads.
            var unitCost = decimal.Round(line.UnitPrice, DecimalPrecision.CostScale);
            CreateOrTopUpLayer(line.ItemId, line.Quantity, line.Count,
                unitCost, unitCost, dateReceived);
        }
        await _db.SaveChangesAsync();
    }

    private async Task<decimal> RestoreSaleReturnLayersAsync(List<SaleReturnItem> items, DateTime returnDate)
    {
        var totalCost = 0m;
        var returnItemIds = items.Select(i => i.ItemId).Distinct().ToList();
        var itemsById = (await _db.Items.AsNoTracking()
            .Where(i => returnItemIds.Contains(i.Id))
            .ToListAsync()).ToDictionary(i => i.Id);

        foreach (var item in items)
        {
            if (item.Quantity <= 0 && item.Count <= 0) continue;

            var layers = await _db.StockLayers
                .Where(sl => sl.ItemId == item.ItemId)
                .OrderBy(sl => sl.DateReceived).ThenBy(sl => sl.Id)
                .ToListAsync();

            decimal remainingQty = item.Quantity;
            decimal remainingCount = item.Count;
            bool quantityDriven = item.Quantity > 0;

            foreach (var layer in layers)
            {
                if (remainingQty > 0)
                {
                    var capacity = Math.Max(0m, layer.Qty - layer.RemainingQty);
                    if (capacity > 0)
                    {
                        var take = Math.Min(remainingQty, capacity);
                        layer.RemainingQty += take;
                        if (quantityDriven) totalCost += take * layer.UnitCost;
                        remainingQty -= take;
                    }
                }
                if (remainingCount > 0)
                {
                    var capacity = Math.Max(0m, layer.Count - layer.RemainingCount);
                    if (capacity > 0)
                    {
                        var take = Math.Min(remainingCount, capacity);
                        layer.RemainingCount += take;
                        if (!quantityDriven) totalCost += take * layer.CountCost;
                        remainingCount -= take;
                    }
                }
                if (remainingQty <= 0 && remainingCount <= 0) break;
            }

            if (remainingQty > 0 || remainingCount > 0)
            {
                // Same six-decimal cost width as the layer it tops up. A return price that is a third
                // decimal finer than two was previously written to StockLayer.UnitCost already
                // truncated, so the cost of the returned goods never matched the price they were
                // returned at.
                var price = itemsById.TryGetValue(item.ItemId, out var it)
                    ? it.PurchasePrice
                    : decimal.Round(item.UnitPrice, DecimalPrecision.CostScale);
                totalCost += (quantityDriven ? remainingQty : remainingCount) * price;
                CreateOrTopUpLayer(item.ItemId, remainingQty, remainingCount, price, price, returnDate);
            }
        }
        await _db.SaveChangesAsync();
        return totalCost;
    }

    private void CreateOrTopUpLayer(int itemId, decimal addQty, decimal addCount,
        decimal unitCost, decimal countCost, DateTime dateReceived)
    {
        if (addQty <= 0 && addCount <= 0) return;

        var existing = _db.StockLayers.Local
            .FirstOrDefault(sl => sl.ItemId == itemId
                && sl.UnitCost == unitCost
                && sl.DateReceived == dateReceived
                && sl.WarehouseId == null);

        if (existing != null)
        {
            existing.Qty += addQty;
            existing.Count += addCount;
            existing.RemainingQty += addQty;
            existing.RemainingCount += addCount;
        }
        else
        {
            _db.StockLayers.Add(new StockLayer
            {
                ItemId = itemId,
                Qty = addQty,
                Count = addCount,
                UnitCost = unitCost,
                CountCost = countCost,
                DateReceived = dateReceived,
                RemainingQty = addQty,
                RemainingCount = addCount
            });
        }
    }


    private async Task<string?> ValidateSaleReturnQuantitiesAsync(SaleReturn saleReturn, List<SaleReturnItem> valid)
    {
        if (valid.Any(l => l.Count < 0 || l.Quantity < 0))
            return "لا يمكن أن تكون الأعداد أو الكميات سالبة في المرتجع";

        if (valid.GroupBy(l => l.ItemId).Any(g => g.Count() > 1))
            return "لا يمكن تكرار نفس الصنف أكثر من مرة في مرتجع البيع";

        if (saleReturn.SaleInvoiceId == null) return null;

        var invoice = await _db.SaleInvoices.Include(i => i.Items).AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == saleReturn.SaleInvoiceId.Value);
        if (invoice == null) return "الفاتورة الأصلية غير موجودة";
        if (invoice.CustomerId != saleReturn.CustomerId) return "الفاتورة الأصلية لا تخص هذا العميل";

        var alreadyReturned = await _db.SaleReturnItems
            .Where(r => r.SaleReturn.SaleInvoiceId == invoice.Id && r.SaleReturnId != saleReturn.Id
                && r.SaleReturn.Status == ReturnStatus.Posted)
            .ToListAsync();

        // المسار القديم (AtDelivery) يربط أذون التسليم بالفاتورة عبر DeliveryOrder.SaleInvoiceId،
        // بينما المسار الجديد (AtInvoice) يربط أوامر التسليم بالفاتورة عبر DeliveryIssue.SaleInvoiceId.
        // نجمع الطرفين معاً كي لا يُحظر المرتجع في أي من المسارين.
        var legacyDelivered = await _db.DeliveryOrders
            .Where(d => d.SaleInvoiceId == invoice.Id && d.Status == DeliveryOrderStatus.Delivered)
            .SelectMany(d => d.Items)
            .AsNoTracking()
            .ToListAsync();

        var issueDelivered = await _db.DeliveryIssues
            .Where(i => i.SaleInvoiceId == invoice.Id && i.Status == DeliveryIssueStatus.Issued)
            .SelectMany(i => i.Items)
            .AsNoTracking()
            .ToListAsync();

        var deliveredItems = legacyDelivered
            .Select(d => new { d.ItemId, d.Quantity, d.Count })
            .Concat(issueDelivered.Select(i => new { i.ItemId, i.Quantity, i.Count }))
            .ToList();

        foreach (var line in valid)
        {
            var invLine = invoice.Items.FirstOrDefault(i => i.ItemId == line.ItemId);
            if (invLine == null) return $"الصنف رقم {line.ItemId} غير موجود في الفاتورة الأصلية";
            decimal returnedCount = alreadyReturned.Where(r => r.ItemId == line.ItemId).Sum(r => r.Count);
            decimal returnedQty = alreadyReturned.Where(r => r.ItemId == line.ItemId).Sum(r => r.Quantity);
            decimal deliveredCount = deliveredItems.Where(d => d.ItemId == line.ItemId).Sum(d => d.Count);
            decimal deliveredQty = deliveredItems.Where(d => d.ItemId == line.ItemId).Sum(d => d.Quantity);
            if (line.Count + returnedCount > deliveredCount || line.Quantity + returnedQty > deliveredQty)
                return $"الكمية المرتجعة أكبر من الكمية المسلّمة في أذونات التسليم للصنف رقم {line.ItemId}";
        }
        return null;
    }

    private async Task<string?> ValidatePurchaseReturnQuantitiesAsync(PurchaseReturn purchaseReturn, List<PurchaseReturnItem> valid)
    {
        if (valid.Any(l => l.Count < 0 || l.Quantity < 0))
            return "لا يمكن أن تكون الأعداد أو الكميات سالبة في المرتجع";

        if (valid.GroupBy(l => l.ItemId).Any(g => g.Count() > 1))
            return "لا يمكن تكرار نفس الصنف أكثر من مرة في مرتجع الشراء";

        if (purchaseReturn.PurchaseInvoiceId == null) return null;

        var invoice = await _db.PurchaseInvoices.Include(i => i.Items).AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == purchaseReturn.PurchaseInvoiceId.Value);
        if (invoice == null) return "الفاتورة الأصلية غير موجودة";
        if (invoice.SupplierId != purchaseReturn.SupplierId) return "الفاتورة الأصلية لا تخص هذا المورد";

        var alreadyReturned = await _db.PurchaseReturnItems
            .Where(r => r.PurchaseReturn.PurchaseInvoiceId == invoice.Id && r.PurchaseReturnId != purchaseReturn.Id
                && r.PurchaseReturn.Status == ReturnStatus.Posted)
            .ToListAsync();

        foreach (var line in valid)
        {
            var invLine = invoice.Items.FirstOrDefault(i => i.ItemId == line.ItemId);
            if (invLine == null) return $"الصنف رقم {line.ItemId} غير موجود في الفاتورة الأصلية";
            decimal returnedCount = alreadyReturned.Where(r => r.ItemId == line.ItemId).Sum(r => r.Count);
            decimal returnedQty = alreadyReturned.Where(r => r.ItemId == line.ItemId).Sum(r => r.Quantity);
            if (line.Count + returnedCount > invLine.Count || line.Quantity + returnedQty > invLine.Quantity)
                return $"الكمية المرتجعة أكبر من الكمية المشتراة في الفاتورة الأصلية للصنف رقم {line.ItemId}";
        }
        return null;
    }

    private async Task<string?> ApplyStockAsync(
        IReadOnlyCollection<StockLine> lines, int sign,
        string docNumber, DocumentType docType, int? docId, DateTime movementDate, string? user)
    {
        var grouped = lines
            .GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => new StockLine(g.Key, g.Sum(x => x.Count), g.Sum(x => x.Quantity)));

        var items = new Dictionary<int, Item>();
        foreach (var key in grouped.Keys)
        {
            var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == key);
            if (item == null) return $"الصنف رقم {key} غير موجود";
            items[key] = item;
        }

        var errors = new List<string>();
        foreach (var line in grouped.Values)
        {
            var item = items[line.ItemId];
            if (item.CurrentCount + sign * line.Count < 0 || item.CurrentQuantity + sign * line.Quantity < 0)
            {
                errors.Add($"الرصيد غير كافٍ للصنف «{item.Name}» — المتاح {item.CurrentCount} عدد / {item.CurrentQuantity} كمية");
            }
        }
        if (errors.Count > 0) return string.Join(" — ", errors);

        foreach (var line in grouped.Values)
        {
            var item = items[line.ItemId];
            decimal oldCount = item.CurrentCount;
            decimal oldQty = item.CurrentQuantity;

            item.CurrentCount = oldCount + sign * line.Count;
            item.CurrentQuantity = oldQty + sign * line.Quantity;

            _db.StockMovements.Add(new StockMovement
            {
                ItemId = item.Id,
                Type = sign > 0 ? MovementType.In : MovementType.Out,
                Count = line.Count,
                Quantity = line.Quantity,
                CountBefore = oldCount,
                CountAfter = item.CurrentCount,
                BalanceBefore = oldQty,
                BalanceAfter = item.CurrentQuantity,
                DocumentNumber = docNumber,
                DocumentType = docType,
                DocumentId = docId,
                MovementDate = movementDate,
                CreatedBy = user
            });
        }
        return null;
    }

    private static List<StockLine> ToStockLines(IEnumerable<SaleInvoiceItem> items) =>
        items.Select(i => new StockLine(i.ItemId, i.Count, i.Quantity)).ToList();

    private static List<StockLine> ToStockLines(IEnumerable<PurchaseInvoiceItem> items) =>
        items.Select(i => new StockLine(i.ItemId, i.Count, i.Quantity)).ToList();

    private static List<StockLine> ToStockLines(IEnumerable<DeliveryOrderItem> items) =>
        items.Select(i => new StockLine(i.ItemId, i.Count, i.Quantity)).ToList();

    private static List<StockLine> ToIssueStockLines(IEnumerable<DeliveryIssueItem> items) =>
        items.Select(i => new StockLine(i.ItemId, i.Count, i.Quantity)).ToList();

    private async Task<string> NextIssueNumberAsync()
    {
        var seriesPrefix = $"ISS-{DateTime.Now:yyyyMMdd}-";
        int next = await MaxSeriesValueAsync(
            _db.DeliveryIssues.Select(i => i.IssueNumber), seriesPrefix) + 1;
        string num = $"{seriesPrefix}{next:D3}";
        while (await _db.DeliveryIssues.AnyAsync(i => i.IssueNumber == num))
        {
            next++;
            num = $"{seriesPrefix}{next:D3}";
        }
        return num;
    }

    private static List<StockLine> ToReturnStockLines(IEnumerable<SaleReturnItem> items) =>
        items.Select(i => new StockLine(i.ItemId, i.Count, i.Quantity)).ToList();

    private static List<StockLine> ToReturnStockLines(IEnumerable<PurchaseReturnItem> items) =>
        items.Select(i => new StockLine(i.ItemId, i.Count, i.Quantity)).ToList();

    private static int PaymentTermDays(InvoicePaymentTerms t) => t switch
    {
        InvoicePaymentTerms.Net7 => 7,
        InvoicePaymentTerms.Net15 => 15,
        InvoicePaymentTerms.Net30 => 30,
        InvoicePaymentTerms.Net60 => 60,
        _ => 0
    };

    private async Task<bool> IsPeriodClosedAsync(DateTime date)
        => await _db.FiscalPeriods.AnyAsync(fp => fp.Year == date.Year && fp.IsClosed);

    private static async Task<int> MaxSeriesValueAsync(IQueryable<string> existing, string seriesPrefix)
    {
        var values = await existing.Where(n => n.StartsWith(seriesPrefix)).ToListAsync();
        int max = 0;
        foreach (var value in values)
        {
            if (value == null || value.Length <= seriesPrefix.Length) continue;
            if (int.TryParse(value.AsSpan(seriesPrefix.Length), out var parsed) && parsed > max)
                max = parsed;
        }
        return max;
    }

    private async Task<string> NextInvoiceNumberAsync(IQueryable<string> existing, string prefix)
    {
        var seriesPrefix = $"{prefix}-";
        int next = await MaxSeriesValueAsync(existing, seriesPrefix) + 1;
        string num = $"{seriesPrefix}{next:D5}";
        while (await existing.AnyAsync(n => n == num))
        {
            next++;
            num = $"{seriesPrefix}{next:D5}";
        }
        return num;
    }

    private async Task<string> NextReturnNumberAsync(IQueryable<string> existing, string prefix)
    {
        var seriesPrefix = $"{prefix}-{DateTime.Now:yyyyMMdd}-";
        int next = await MaxSeriesValueAsync(existing, seriesPrefix) + 1;
        string num = $"{seriesPrefix}{next:D3}";
        while (await existing.AnyAsync(n => n == num))
        {
            next++;
            num = $"{seriesPrefix}{next:D3}";
        }
        return num;
    }

    private async Task<string> NextDeliveryNumberAsync()
    {
        var seriesPrefix = $"DLV-{DateTime.Now:yyyyMMdd}-";
        int next = await MaxSeriesValueAsync(
            _db.DeliveryOrders.Select(d => d.DeliveryNumber), seriesPrefix) + 1;
        string num = $"{seriesPrefix}{next:D3}";
        while (await _db.DeliveryOrders.AnyAsync(d => d.DeliveryNumber == num))
        {
            next++;
            num = $"{seriesPrefix}{next:D3}";
        }
        return num;
    }

    private static void ResetDeliveryKeys(DeliveryOrder delivery)
    {
        delivery.Id = 0;
        foreach (var item in delivery.Items)
        {
            item.Id = 0;
            item.DeliveryOrderId = 0;
        }
    }

    private void DetachAll()
    {
        foreach (var entry in _db.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;
    }

    /// <summary>
    /// مسار استرجاع لا يبتلع الاستثناء الأصلي: فشل التراجع عن معاملة (اتصال مقطوع أو معاملة
    /// أُنهيت بالفعل) يجب ألا يحلّ محل استثناء التحديث الذي يعالجه حلقة إعادة المحاولة،
    /// وإلا ضاع التشخيص الحقيقي خلف خطأ آخر لا علاقة له بسبب الفشل.
    /// </summary>
    private static async Task TryRollbackAsync(IDbContextTransaction? tx)
    {
        if (tx == null) return;
        try { await tx.RollbackAsync(); }
        catch (Exception) { }
    }

    /// <summary>
    /// يضمن أن <c>beginOwnTransaction: false</c> لا تعني «بلا معاملة»: بدون معاملة قائمة
    /// تكون كل كتابة داخل Unidad الذرية الخاصّة بها، فيبقى مستند ناقص دون وسيلة للتراجع عنه.
    /// </summary>
    private void RequireAmbientTransaction(string operation)
    {
        if (_db.Database.CurrentTransaction == null)
            throw new InvalidOperationException(
                $"لا يمكن تنفيذ «{operation}» دون معاملة قائمة؛ ابدأ معاملة قبل الاستدعاء أو اترك القيمة الافتراضية لتفتح العملية معاملتها الخاصة");
    }

    private static void ResetInvoiceKeys(SaleInvoice invoice)
    {
        invoice.Id = 0;
        foreach (var item in invoice.Items)
        {
            item.Id = 0;
            item.SaleInvoiceId = 0;
        }
    }

    private static void ResetInvoiceKeys(PurchaseInvoice invoice)
    {
        invoice.Id = 0;
        foreach (var item in invoice.Items)
        {
            item.Id = 0;
            item.PurchaseInvoiceId = 0;
        }
    }

    private static void ResetReturnKeys(SaleReturn saleReturn)
    {
        saleReturn.Id = 0;
        foreach (var item in saleReturn.Items)
        {
            item.Id = 0;
            item.SaleReturnId = 0;
        }
    }

    private static void ResetReturnKeys(PurchaseReturn purchaseReturn)
    {
        purchaseReturn.Id = 0;
        foreach (var item in purchaseReturn.Items)
        {
            item.Id = 0;
            item.PurchaseReturnId = 0;
        }
    }

    public async Task<IReadOnlyList<StockTransfer>> GetTransfersAsync()
    {
        return await _db.StockTransfers
            .Include(t => t.SourceWarehouse)
            .Include(t => t.TargetWarehouse)
            .Include(t => t.Items).ThenInclude(i => i.Item)
            .AsNoTracking()
            .OrderByDescending(t => t.TransferDate)
            .ToListAsync();
    }

    /// <summary>
    /// ينقل كميات بين مستودعين. مع <c>beginOwnTransaction: false</c> ينضم التحويل إلى معاملة
    /// المستدعي، فلا يبقى نزيف من المستودع المصدر بلا إضافة في الهدف.
    /// </summary>
    public async Task<(bool Success, string? Error)> CreateTransferAsync(StockTransfer transfer, List<StockTransferItem> items, string? user, bool beginOwnTransaction = true)
    {
        var valid = items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل بالكمية أو العدد");
        var duplicateTransfer = valid.GroupBy(i => i.ItemId).FirstOrDefault(g => g.Count() > 1);
        if (duplicateTransfer != null)
            return (false, $"الصنف رقم {duplicateTransfer.Key} مكرر أكثر من مرة في التحويل");
        if (transfer.SourceWarehouseId == transfer.TargetWarehouseId)
            return (false, "لا يمكن التحويل من مستودع إلى نفسه");
        if (await IsPeriodClosedAsync(transfer.TransferDate))
            return (false, $"السنة المالية {transfer.TransferDate.Year} مغلقة — لا يمكن ترحيل قيود فيها");
        if (!beginOwnTransaction) RequireAmbientTransaction(nameof(CreateTransferAsync));

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = beginOwnTransaction ? await _db.Database.BeginTransactionAsync() : null;
            try
            {
                transfer.TransferNumber = await NextTransferNumberAsync(
                    _db.StockTransfers.Select(t => t.TransferNumber));

                transfer.CreatedBy = user;
                transfer.CreatedAt = DateTime.UtcNow;

                foreach (var item in valid)
                {
                    var product = await _db.Items.FirstOrDefaultAsync(i => i.Id == item.ItemId);
                    if (product == null) { await TryRollbackAsync(tx); DetachAll(); return (false, $"الصنف رقم {item.ItemId} غير موجود"); }

                    var sourceLayers = await _db.StockLayers
                        .Where(sl => sl.ItemId == item.ItemId
                            && (sl.WarehouseId == transfer.SourceWarehouseId || sl.WarehouseId == null)
                            && (sl.RemainingQty > 0 || sl.RemainingCount > 0))
                        .OrderBy(sl => sl.DateReceived).ThenBy(sl => sl.Id)
                        .ToListAsync();

                    decimal needQty = item.Quantity;
                    decimal needCount = item.Count;
                    decimal lineQtyCost = 0m;
                    decimal lineCountCost = 0m;

                    foreach (var layer in sourceLayers)
                    {
                        if (layer.WarehouseId == null)
                            layer.WarehouseId = transfer.SourceWarehouseId;
                        if (needQty > 0 && layer.RemainingQty > 0)
                        {
                            var take = Math.Min(needQty, layer.RemainingQty);
                            needQty -= take;
                            layer.RemainingQty -= take;
                            lineQtyCost += take * layer.UnitCost;
                            CreateTransferLayer(item.ItemId, transfer.TargetWarehouseId, take, 0, layer.UnitCost, layer.CountCost, layer.DateReceived);
                        }
                        if (needCount > 0 && layer.RemainingCount > 0)
                        {
                            var take = Math.Min(needCount, layer.RemainingCount);
                            needCount -= take;
                            layer.RemainingCount -= take;
                            lineCountCost += take * layer.CountCost;
                            CreateTransferLayer(item.ItemId, transfer.TargetWarehouseId, 0, take, layer.UnitCost, layer.CountCost, layer.DateReceived);
                        }
                        if (needQty <= 0 && needCount <= 0) break;
                    }

                    if (needQty > 0 || needCount > 0)
                    {
                        await TryRollbackAsync(tx); DetachAll();
                        return (false, $"الرصيد غير كافٍ للصنف «{product.Name}» في المستودع المصدر");
                    }

                    var transferredQty = item.Quantity - needQty;
                    var transferredCount = item.Count - needCount;

                    decimal sourceOldCount = product.CurrentCount;
                    decimal sourceOldQty = product.CurrentQuantity;
                    product.CurrentCount -= transferredCount;
                    product.CurrentQuantity -= transferredQty;
                    decimal destOldCount = product.CurrentCount;
                    decimal destOldQty = product.CurrentQuantity;

                    _db.StockMovements.Add(new StockMovement
                    {
                        ItemId = product.Id,
                        Type = MovementType.Out,
                        Count = transferredCount,
                        Quantity = transferredQty,
                        CountBefore = sourceOldCount,
                        CountAfter = product.CurrentCount,
                        BalanceBefore = sourceOldQty,
                        BalanceAfter = product.CurrentQuantity,
                        DocumentNumber = transfer.TransferNumber,
                        DocumentType = DocumentType.Transfer,
                        DocumentId = null,
                        MovementDate = transfer.TransferDate,
                        CreatedBy = user
                    });
                    product.CurrentCount += transferredCount;
                    product.CurrentQuantity += transferredQty;

                    _db.StockMovements.Add(new StockMovement
                    {
                        ItemId = product.Id,
                        Type = MovementType.In,
                        Count = transferredCount,
                        Quantity = transferredQty,
                        CountBefore = destOldCount,
                        CountAfter = product.CurrentCount,
                        BalanceBefore = destOldQty,
                        BalanceAfter = product.CurrentQuantity,
                        DocumentNumber = transfer.TransferNumber,
                        DocumentType = DocumentType.Transfer,
                        DocumentId = null,
                        MovementDate = transfer.TransferDate,
                        CreatedBy = user
                    });

                    transfer.Items.Add(new StockTransferItem
                    {
                        ItemId = item.ItemId,
                        Quantity = item.Quantity,
                        Count = item.Count,
                        UnitCost = DeriveTransferUnitCost(lineQtyCost, transferredQty, lineCountCost, transferredCount),
                        DateReceived = item.DateReceived
                    });
                }

                _db.StockTransfers.Add(transfer);
                await _db.SaveChangesAsync();
                if (tx is not null) await tx.CommitAsync();
                return (true, null);
            }
            catch (DbUpdateConcurrencyException)
            {
                await TryRollbackAsync(tx); DetachAll(); ResetTransferKeys(transfer); transfer.Items.Clear();
                if (!beginOwnTransaction) throw;
            }
            catch (DbUpdateException)
            {
                await TryRollbackAsync(tx); DetachAll(); ResetTransferKeys(transfer); transfer.Items.Clear();
                if (!beginOwnTransaction) throw;
            }
        }
        return (false, "تعذر حفظ التحويل بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    public async Task<IReadOnlyList<StockSnapshotItem>> GetStockSnapshotAsync(int? warehouseId)
    {
        var items = await _db.Items.Where(i => i.IsActive).AsNoTracking().ToListAsync();
        var layers = warehouseId.HasValue
            ? await _db.StockLayers.Where(sl => sl.WarehouseId == warehouseId.Value || sl.WarehouseId == null).AsNoTracking().ToListAsync()
            : await _db.StockLayers.AsNoTracking().ToListAsync();

        var result = items.Select(item =>
        {
            var itemLayers = layers.Where(l => l.ItemId == item.Id).ToList();
            decimal wCount = itemLayers.Sum(l => l.RemainingCount);
            decimal wQty = itemLayers.Sum(l => l.RemainingQty);

            decimal totalCount = item.CurrentCount;
            decimal totalQty = item.CurrentQuantity;
            if (warehouseId.HasValue)
            {
                totalCount = wCount;
                totalQty = wQty;
            }

            return new StockSnapshotItem(item.Id, item.Name, totalCount, totalQty, wCount, wQty);
        }).ToList();

        return result;
    }

    private void CreateTransferLayer(int itemId, int? warehouseId, decimal addQty, decimal addCount,
        decimal unitCost, decimal countCost, DateTime dateReceived)
    {
        if (addQty <= 0 && addCount <= 0) return;

        var existing = _db.StockLayers.Local
            .FirstOrDefault(sl => sl.ItemId == itemId
                && sl.WarehouseId == warehouseId
                && sl.UnitCost == unitCost
                && sl.DateReceived == dateReceived);

        if (existing != null)
        {
            existing.Qty += addQty;
            existing.Count += addCount;
            existing.RemainingQty += addQty;
            existing.RemainingCount += addCount;
        }
        else
        {
            _db.StockLayers.Add(new StockLayer
            {
                ItemId = itemId,
                WarehouseId = warehouseId,
                Qty = addQty,
                Count = addCount,
                UnitCost = unitCost,
                CountCost = countCost,
                DateReceived = dateReceived,
                RemainingQty = addQty,
                RemainingCount = addCount
            });
        }
    }

    /// <summary>
    /// The per-unit cost a transfer line carries: the cost that moved, divided by the amount that
    /// moved, at the six-decimal cost width of <c>StockTransferItem.UnitCost</c>.
    /// <para>
    /// Quantity leads, because a line is traded by quantity or by count or by both, and a
    /// quantity-traded line is the one whose cost a reader expects on the document. The division was
    /// previously rounded to two decimals into a six-decimal column, which threw away the only thing
    /// a per-unit derived cost is for: a transfer that splits a layer whose cost has more decimals
    /// than the quantity does has a cost per unit that is not itself on any grid, and forcing it onto
    /// a hundredth means the layers on both sides of the transfer no longer add back to the cost
    /// that left.
    /// </para>
    /// <para>
    /// The zero-denominator guard is preserved from the expression this replaces, and kept explicit
    /// because it is the only arithmetic in the transfer loop that has no precondition of its own:
    /// the caller checks that the request was fully satisfiable and then divides the cost that moved
    /// by the amount that moved. A zero denominator there throws <see cref="DivideByZeroException"/>,
    /// which is neither of the two EF exceptions the loop catches, so it escapes a transfer that has
    /// already taken stock out of the source warehouse. A line that moved nothing carries a cost of
    /// zero, which is the truth about it.
    /// </para>
    /// </summary>
    private static decimal DeriveTransferUnitCost(
        decimal quantityCost, decimal transferredQuantity, decimal countCost, decimal transferredCount)
    {
        if (transferredQuantity > 0)
            return decimal.Round(quantityCost / transferredQuantity, DecimalPrecision.CostScale);
        if (transferredCount > 0)
            return decimal.Round(countCost / transferredCount, DecimalPrecision.CostScale);
        return 0m;
    }

    private static void ResetTransferKeys(StockTransfer transfer)
    {
        transfer.Id = 0;
        foreach (var item in transfer.Items)
        {
            item.Id = 0;
            item.StockTransferId = 0;
        }
    }

    private async Task<string> NextTransferNumberAsync(IQueryable<string> existing)
    {
        var seriesPrefix = $"TRF-{DateTime.Now:yyyyMMdd}-";
        int next = await MaxSeriesValueAsync(existing, seriesPrefix) + 1;
        string num = $"{seriesPrefix}{next:D3}";
        while (await existing.AnyAsync(n => n == num))
        {
            next++;
            num = $"{seriesPrefix}{next:D3}";
        }
        return num;
    }

    private async Task UpsertSupplierQuoteAsync(int supplierId, int itemId, decimal unitPrice)
    {
        var existing = await _db.SupplierQuotes
            .FirstOrDefaultAsync(q => q.SupplierId == supplierId && q.ItemId == itemId);
        if (existing == null)
        {
            _db.SupplierQuotes.Add(new SupplierQuote
            {
                SupplierId = supplierId,
                ItemId = itemId,
                UnitPrice = unitPrice,
                EffectiveDate = DateTime.Today
            });
        }
        else if (existing.UnitPrice != unitPrice)
        {
            existing.UnitPrice = unitPrice;
            existing.EffectiveDate = DateTime.Today;
        }
    }
}
