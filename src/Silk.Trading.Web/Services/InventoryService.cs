using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Accounting;
using Silk.Trading.Web.Models.Core;
using Silk.Trading.Web.Models.Purchases;
using Silk.Trading.Web.Models.Sales;
using Silk.Trading.Web.Models.Stock;

namespace Silk.Trading.Web.Services;

public sealed class InventoryService : IInventoryService
{
    private const int MaxAttempts = 3;
    private readonly AppDbContext _db;
    private readonly ILogger<InventoryService>? _logger;
    private readonly IAccountingService? _accounting;

    public InventoryService(AppDbContext db) : this(db, null, null) { }

    public InventoryService(AppDbContext db, ILogger<InventoryService>? logger) : this(db, logger, null) { }

    public InventoryService(AppDbContext db, IAccountingService? accounting) : this(db, null, accounting) { }

    public InventoryService(AppDbContext db, ILogger<InventoryService>? logger, IAccountingService? accounting)
    {
        _db = db;
        _logger = logger;
        _accounting = accounting;
    }

    public async Task<(bool Success, string? Error)> CreateSaleAsync(SaleInvoice invoice, List<SaleInvoiceItem> items, string? user, int? branchId = null)
    {
        if (await IsPeriodClosedAsync(invoice.InvoiceDate))
            return (false, $"السنة المالية {invoice.InvoiceDate.Year} مغلقة — لا يمكن إدراج قيود فيها");

        var valid = items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل بالكمية أو العدد");
        var duplicateSale = valid.GroupBy(i => i.ItemId).FirstOrDefault(g => g.Count() > 1);
        if (duplicateSale != null)
            return (false, $"الصنف رقم {duplicateSale.Key} مكرر أكثر من مرة في الفاتورة");

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                invoice.InvoiceNumber = await NextInvoiceNumberAsync(
                    _db.SaleInvoices.Select(s => s.InvoiceNumber), "SI");
                invoice.BranchId = branchId;

                var stockLines = ToStockLines(valid);
                var stockError = await ApplyStockAsync(
                    stockLines, sign: -1,
                    docNumber: invoice.InvoiceNumber, docType: DocumentType.SaleInvoice, docId: null,
                    movementDate: DateTime.UtcNow, user);
                if (stockError != null) { await tx.RollbackAsync(); DetachAll(); return (false, stockError); }

                var (consumedQtyCost, consumedCountCost) = await ConsumeFifoLayersAsync(stockLines, DateTime.UtcNow);

                invoice.TotalAmount = valid.Sum(i => i.Total);
                invoice.NetAmount = invoice.TotalAmount - invoice.Discount - (invoice.Discount2 ?? 0) - (invoice.Discount3 ?? 0) + invoice.Tax;
                if (invoice.NetAmount < 0)
                {
                    await tx.RollbackAsync(); DetachAll();
                    return (false, "الخصم أكبر من إجمالي الفاتورة؛ لا يمكن أن يكون الصافي سالباً");
                }
                if (invoice.PaymentTerms != InvoicePaymentTerms.OnReceipt && invoice.DueDate == null)
                    invoice.DueDate = invoice.InvoiceDate.AddDays(PaymentTermDays(invoice.PaymentTerms));
                invoice.PaidAmount = invoice.PaymentTerms == InvoicePaymentTerms.OnReceipt ? invoice.NetAmount : 0;
                invoice.IsPaid = invoice.PaymentTerms == InvoicePaymentTerms.OnReceipt;
                invoice.CreatedBy = user;
                invoice.CreatedAt = DateTime.UtcNow;
                invoice.Items = valid;

                _db.SaleInvoices.Add(invoice);
                await _db.SaveChangesAsync();

                if (_accounting != null && (invoice.NetAmount > 0 || consumedQtyCost + consumedCountCost > 0))
                    await _accounting.RecordSaleInvoiceAsync(invoice.InvoiceDate, invoice.CustomerId, invoice.NetAmount,
                        consumedQtyCost + consumedCountCost, invoice.CurrencyId, invoice.ExchangeRate, user, branchId);

                if (_accounting != null && invoice.PaymentTerms == InvoicePaymentTerms.OnReceipt && invoice.NetAmount > 0)
                    await _accounting.RecordReceiptAsync(invoice.InvoiceDate,
                        decimal.Round(invoice.NetAmount * (invoice.ExchangeRate ?? 1m), 2),
                        PaymentMethod.Cash, invoice.CustomerId, user, branchId);

                await tx.CommitAsync();
                _logger?.LogInformation("فُتحت فاتورة بيع {Owner} رقم {Number} صافي {Net:C} بفاتورة {InvId}",
                    user, invoice.InvoiceNumber, invoice.NetAmount, invoice.Id);
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

    public async Task<(bool Success, string? Error)> CreatePurchaseAsync(PurchaseInvoice invoice, List<PurchaseInvoiceItem> items, string? user, int? branchId = null)
    {
        if (await IsPeriodClosedAsync(invoice.InvoiceDate))
            return (false, $"السنة المالية {invoice.InvoiceDate.Year} مغلقة — لا يمكن إدراج قيود فيها");

        var valid = items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل بالكمية أو العدد");
        var duplicatePurchase = valid.GroupBy(i => i.ItemId).FirstOrDefault(g => g.Count() > 1);
        if (duplicatePurchase != null)
            return (false, $"الصنف رقم {duplicatePurchase.Key} مكرر أكثر من مرة في الفاتورة");

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
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
                        item.PurchasePrice = decimal.Round(priceLine.UnitPrice * (invoice.ExchangeRate ?? 1m), 2);
                }

                var stockLines = ToStockLines(valid);
                var stockError = await ApplyStockAsync(
                    stockLines, sign: +1,
                    docNumber: invoice.InvoiceNumber, docType: DocumentType.PurchaseInvoice, docId: null,
                    movementDate: DateTime.UtcNow, user);
                if (stockError != null) { await tx.RollbackAsync(); DetachAll(); return (false, stockError); }

                invoice.TotalAmount = valid.Sum(i => i.Total);
                invoice.NetAmount = invoice.TotalAmount - invoice.Discount - (invoice.Discount2 ?? 0) - (invoice.Discount3 ?? 0) + invoice.Tax;
                if (invoice.NetAmount < 0)
                {
                    await tx.RollbackAsync(); DetachAll();
                    return (false, "الخصم أكبر من إجمالي الفاتورة؛ لا يمكن أن يكون الصافي سالباً");
                }
                if (invoice.PaymentTerms != InvoicePaymentTerms.OnReceipt && invoice.DueDate == null)
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

                await ReplenishFifoLayersAsync(valid, invoice.InvoiceDate, invoice.ExchangeRate);

                if (_accounting != null && invoice.NetAmount > 0)
                    await _accounting.RecordPurchaseInvoiceAsync(invoice.InvoiceDate, invoice.SupplierId, invoice.NetAmount, invoice.CurrencyId, invoice.ExchangeRate, user, branchId);

                if (_accounting != null && invoice.PaymentTerms == InvoicePaymentTerms.OnReceipt && invoice.NetAmount > 0)
                    await _accounting.RecordDisbursementAsync(invoice.InvoiceDate,
                        decimal.Round(invoice.NetAmount * (invoice.ExchangeRate ?? 1m), 2),
                        PaymentMethod.Cash, invoice.SupplierId, user, branchId);

                await tx.CommitAsync();
                _logger?.LogInformation("فُتحت فاتورة شراء {Owner} رقم {Number} صافي {Net:C} بفاتورة {InvId}",
                    user, invoice.InvoiceNumber, invoice.NetAmount, invoice.Id);
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

    public async Task<(bool Success, string? Error)> CreateSaleReturnAsync(SaleReturn saleReturn, List<SaleReturnItem> items, string? user)
    {
        var (draftOk, draftErr, returnId) = await CreateSaleReturnDraftAsync(saleReturn, items, user);
        if (!draftOk) return (false, draftErr);
        var (postOk, postErr) = await PostSaleReturnAsync(returnId, user);
        if (!postOk) return (false, postErr);
        return (true, null);
    }

    public async Task<(bool Success, string? Error, int ReturnId)> CreateSaleReturnDraftAsync(SaleReturn saleReturn, List<SaleReturnItem> items, string? user)
    {
        if (items.Any(i => i.ItemId > 0 && (i.Count < 0 || i.Quantity < 0)))
            return (false, "لا يمكن أن تكون الأعداد أو الكميات سالبة في المرتجع", 0);

        var valid = items.Where(i => i.ItemId > 0 && (i.Count != 0 || i.Quantity != 0)).ToList();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل بالعدد أو الكمية", 0);

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                saleReturn.ReturnNumber = await NextReturnNumberAsync(
                    _db.SaleReturns.Select(r => r.ReturnNumber), "SRTN");
                saleReturn.TotalAmount = valid.Sum(i => i.Total);
                saleReturn.CreatedBy = user;
                saleReturn.Status = ReturnStatus.Draft;
                saleReturn.PostedBy = null;
                saleReturn.PostedAt = null;
                saleReturn.Items = valid;

                _db.SaleReturns.Add(saleReturn);
                await _db.SaveChangesAsync();

                await tx.CommitAsync();
                _logger?.LogInformation("أُنشئت مسودة مرتجع بيع {Owner} رقم {Number} بمبلغ {Amt:C}", user, saleReturn.ReturnNumber, saleReturn.TotalAmount);
                return (true, null, saleReturn.Id);
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(); DetachAll(); ResetReturnKeys(saleReturn);
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync(); DetachAll(); ResetReturnKeys(saleReturn);
            }
        }
        return (false, "تعذر حفظ مسودة مرتجع البيع بسبب تعارض في البيانات، حاول مرة أخرى", 0);
    }

    public async Task<(bool Success, string? Error)> PostSaleReturnAsync(int saleReturnId, string? user)
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

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var returnError = await ValidateSaleReturnQuantitiesAsync(saleReturn, valid);
                if (returnError != null) { await tx.RollbackAsync(); DetachAll(); return (false, returnError); }

                var stockError = await ApplyStockAsync(
                    ToReturnStockLines(valid), sign: +1,
                    docNumber: saleReturn.ReturnNumber, docType: DocumentType.SaleReturn, docId: saleReturn.Id,
                    movementDate: saleReturn.ReturnDate, user);
                if (stockError != null) { await tx.RollbackAsync(); DetachAll(); return (false, stockError); }

                var costTotal = await RestoreSaleReturnLayersAsync(valid, saleReturn.ReturnDate, saleReturn.ExchangeRate);

                await _db.SaveChangesAsync();

                if (_accounting != null && saleReturn.TotalAmount > 0)
                    await _accounting.RecordSaleReturnWithCostAsync(
                        saleReturn.ReturnDate, saleReturn.Id, saleReturn.CustomerId,
                        saleReturn.TotalAmount, costTotal,
                        saleReturn.CurrencyId, saleReturn.ExchangeRate, user, saleReturn.BranchId);

                saleReturn.Status = ReturnStatus.Posted;
                saleReturn.PostedBy = user;
                saleReturn.PostedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                await tx.CommitAsync();
                _logger?.LogInformation("رحّل مرتجع بيع {Owner} رقم {Number} بمبلغ {Amt:C}", user, saleReturn.ReturnNumber, saleReturn.TotalAmount);
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
        return (false, "تعذر ترحيل مرتجع البيع بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    public async Task<(bool Success, string? Error)> CreatePurchaseReturnAsync(PurchaseReturn purchaseReturn, List<PurchaseReturnItem> items, string? user)
    {
        var (draftOk, draftErr, returnId) = await CreatePurchaseReturnDraftAsync(purchaseReturn, items, user);
        if (!draftOk) return (false, draftErr);
        var (postOk, postErr) = await PostPurchaseReturnAsync(returnId, user);
        if (!postOk) return (false, postErr);
        return (true, null);
    }

    public async Task<(bool Success, string? Error, int ReturnId)> CreatePurchaseReturnDraftAsync(PurchaseReturn purchaseReturn, List<PurchaseReturnItem> items, string? user)
    {
        if (items.Any(i => i.ItemId > 0 && (i.Count < 0 || i.Quantity < 0)))
            return (false, "لا يمكن أن تكون الأعداد أو الكميات سالبة في المرتجع", 0);

        var valid = items.Where(i => i.ItemId > 0 && (i.Count != 0 || i.Quantity != 0)).ToList();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل بالعدد أو الكمية", 0);

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                purchaseReturn.ReturnNumber = await NextReturnNumberAsync(
                    _db.PurchaseReturns.Select(r => r.ReturnNumber), "PRTN");
                purchaseReturn.TotalAmount = valid.Sum(i => i.Total);
                purchaseReturn.CreatedBy = user;
                purchaseReturn.Status = ReturnStatus.Draft;
                purchaseReturn.PostedBy = null;
                purchaseReturn.PostedAt = null;
                purchaseReturn.Items = valid;

                _db.PurchaseReturns.Add(purchaseReturn);
                await _db.SaveChangesAsync();

                await tx.CommitAsync();
                _logger?.LogInformation("أُنشئت مسودة مرتجع شراء {Owner} رقم {Number} بمبلغ {Amt:C}", user, purchaseReturn.ReturnNumber, purchaseReturn.TotalAmount);
                return (true, null, purchaseReturn.Id);
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(); DetachAll(); ResetReturnKeys(purchaseReturn);
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync(); DetachAll(); ResetReturnKeys(purchaseReturn);
            }
        }
        return (false, "تعذر حفظ مسودة مرتجع الشراء بسبب تعارض في البيانات، حاول مرة أخرى", 0);
    }

    public async Task<(bool Success, string? Error)> PostPurchaseReturnAsync(int purchaseReturnId, string? user)
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

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var returnError = await ValidatePurchaseReturnQuantitiesAsync(purchaseReturn, valid);
                if (returnError != null) { await tx.RollbackAsync(); DetachAll(); return (false, returnError); }

                var stockError = await ApplyStockAsync(
                    ToReturnStockLines(valid), sign: -1,
                    docNumber: purchaseReturn.ReturnNumber, docType: DocumentType.PurchaseReturn, docId: purchaseReturn.Id,
                    movementDate: purchaseReturn.ReturnDate, user);
                if (stockError != null) { await tx.RollbackAsync(); DetachAll(); return (false, stockError); }

                var consumed = await ConsumeFifoLayersAsync(ToReturnStockLines(valid), purchaseReturn.ReturnDate);
                var costTotal = consumed.CountCost + consumed.QtyCost;

                await _db.SaveChangesAsync();

                if (_accounting != null && purchaseReturn.TotalAmount > 0)
                    await _accounting.RecordPurchaseReturnWithCostAsync(
                        purchaseReturn.ReturnDate, purchaseReturn.Id, purchaseReturn.SupplierId,
                        purchaseReturn.TotalAmount, costTotal,
                        purchaseReturn.CurrencyId, purchaseReturn.ExchangeRate, user, purchaseReturn.BranchId);

                purchaseReturn.Status = ReturnStatus.Posted;
                purchaseReturn.PostedBy = user;
                purchaseReturn.PostedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                await tx.CommitAsync();
                _logger?.LogInformation("رحّل مرتجع شراء {Owner} رقم {Number} بمبلغ {Amt:C}", user, purchaseReturn.ReturnNumber, purchaseReturn.TotalAmount);
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
        return (false, "تعذر ترحيل مرتجع الشراء بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    public async Task<(bool Success, string? Error)> CreateAdjustmentAsync(InventoryAdjustment adjustment, string? user)
    {
        if (await IsPeriodClosedAsync(adjustment.AdjustmentDate))
            return (false, $"السنة المالية {adjustment.AdjustmentDate.Year} مغلقة — لا يمكن إدراج قيود فيها");

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
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

                _db.StockMovements.Add(new StockMovement
                {
                    ItemId = item.Id,
                    Type = adjustment.NewCount >= oldCount && adjustment.NewQuantity >= oldQty
                        ? MovementType.In : MovementType.Out,
                    Count = adjustment.NewCount - oldCount,
                    Quantity = adjustment.NewQuantity - oldQty,
                    CountBefore = oldCount,
                    CountAfter = adjustment.NewCount,
                    BalanceBefore = oldQty,
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
                            await _accounting.RecordStockWriteDownAsync(item.Id, consumed.QtyCost, consumed.CountCost, 1m, user);
                        else if (writtenQty > 0 || writtenCount > 0)
                            await _accounting.RecordStockWriteDownAsync(item.Id, writtenQty, writtenCount, item.PurchasePrice, user);
                    }
                }

                if (_accounting != null && (addedQty > 0 || addedCount > 0))
                    await _accounting.RecordOpeningStockAsync(item.Id, Math.Max(addedQty, 0), Math.Max(addedCount, 0), item.PurchasePrice, user);

                await tx.CommitAsync();
                return (true, null);
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(); DetachAll(); adjustment.Id = 0;
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync(); DetachAll(); adjustment.Id = 0;
            }
        }
        return (false, "تعذر حفظ الجرد بسبب تعارض في البيانات، حاول مرة أخرى");
    }

    public async Task<ConsumedCostResult?> GetConsumedCostAsync(int itemId, IReadOnlyCollection<StockLine> lines)
    {
        if (lines.Count == 0) return null;

        var totalQtyCost = 0m;
        var totalCountCost = 0m;

        var grouped = lines
            .GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => new StockLine(g.Key, g.Sum(x => x.Count), g.Sum(x => x.Quantity)));

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

        return new ConsumedCostResult(totalQtyCost, totalCountCost);
    }

    private async Task<ConsumedCostResult> ConsumeFifoLayersAsync(IReadOnlyCollection<StockLine> lines, DateTime movementDate)
    {
        var qtyCost = 0m;
        var countCost = 0m;

        var grouped = lines
            .GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => new StockLine(g.Key, g.Sum(x => x.Count), g.Sum(x => x.Quantity)));

        foreach (var line in grouped.Values)
        {
            var layers = await _db.StockLayers
                .Where(sl => sl.ItemId == line.ItemId && (sl.RemainingQty > 0 || sl.RemainingCount > 0))
                .OrderBy(sl => sl.DateReceived).ThenBy(sl => sl.Id)
                .ToListAsync();

            if (line.Quantity > 0)
            {
                var remaining = line.Quantity;
                foreach (var layer in layers)
                {
                    if (remaining <= 0) break;
                    if (layer.RemainingQty <= 0) continue;
                    var take = Math.Min(remaining, layer.RemainingQty);
                    qtyCost += take * layer.UnitCost;
                    layer.RemainingQty -= take;
                    remaining -= take;
                }
            }

            if (line.Count > 0)
            {
                var remaining = line.Count;
                foreach (var layer in layers)
                {
                    if (remaining <= 0) break;
                    if (layer.RemainingCount <= 0) continue;
                    var take = Math.Min(remaining, layer.RemainingCount);
                    countCost += take * layer.CountCost;
                    layer.RemainingCount -= take;
                    remaining -= take;
                }
            }
        }

        return new ConsumedCostResult(qtyCost, countCost);
    }

    private async Task<ConsumedCostResult> ConsumeAdjustmentLayersAsync(int itemId, decimal qtyToRemove, decimal countToRemove)
    {
        var qtyCost = 0m;
        var countCost = 0m;
        if (qtyToRemove <= 0 && countToRemove <= 0) return new ConsumedCostResult(0, 0);

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

        return new ConsumedCostResult(qtyCost, countCost);
    }

    private async Task ReplenishFifoLayersAsync(List<PurchaseInvoiceItem> lines, DateTime dateReceived, decimal? exchangeRate)
    {
        var rate = exchangeRate ?? 1m;
        foreach (var line in lines)
        {
            if (line.Quantity <= 0 && line.Count <= 0) continue;
            var baseUnitCost = decimal.Round(line.UnitPrice * rate, 2);
            CreateOrTopUpLayer(line.ItemId, line.Quantity, line.Count,
                baseUnitCost, baseUnitCost, dateReceived);
        }
        await _db.SaveChangesAsync();
    }

    private async Task<decimal> RestoreSaleReturnLayersAsync(List<SaleReturnItem> items, DateTime returnDate, decimal? exchangeRate)
    {
        var totalCost = 0m;
        var itemsById = (await _db.Items.AsNoTracking().ToListAsync()).ToDictionary(i => i.Id);

        foreach (var item in items)
        {
            if (item.Quantity <= 0 && item.Count <= 0) continue;

            var layers = await _db.StockLayers
                .Where(sl => sl.ItemId == item.ItemId)
                .OrderBy(sl => sl.DateReceived).ThenBy(sl => sl.Id)
                .ToListAsync();

            decimal remainingQty = item.Quantity;
            decimal remainingCount = item.Count;

            foreach (var layer in layers)
            {
                if (remainingQty > 0)
                {
                    var capacity = Math.Max(0m, layer.Qty - layer.RemainingQty);
                    if (capacity > 0)
                    {
                        var take = Math.Min(remainingQty, capacity);
                        layer.RemainingQty += take;
                        totalCost += take * layer.UnitCost;
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
                        totalCost += take * layer.CountCost;
                        remainingCount -= take;
                    }
                }
                if (remainingQty <= 0 && remainingCount <= 0) break;
            }

            if (remainingQty > 0 || remainingCount > 0)
            {
                var price = itemsById.TryGetValue(item.ItemId, out var it)
                    ? it.PurchasePrice
                    : decimal.Round(item.UnitPrice * (exchangeRate ?? 1m), 2);
                totalCost += remainingQty * price + remainingCount * price;
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

        if (saleReturn.SaleInvoiceId == null) return null;

        var invoice = await _db.SaleInvoices.Include(i => i.Items).AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == saleReturn.SaleInvoiceId.Value);
        if (invoice == null) return "الفاتورة الأصلية غير موجودة";
        if (invoice.CustomerId != saleReturn.CustomerId) return "الفاتورة الأصلية لا تخص هذا العميل";

        var alreadyReturned = await _db.SaleReturnItems
            .Where(r => r.SaleReturn.SaleInvoiceId == invoice.Id && r.SaleReturnId != saleReturn.Id
                && r.SaleReturn.Status == ReturnStatus.Posted)
            .ToListAsync();

        foreach (var line in valid)
        {
            var invLine = invoice.Items.FirstOrDefault(i => i.ItemId == line.ItemId);
            if (invLine == null) return $"الصنف رقم {line.ItemId} غير موجود في الفاتورة الأصلية";
            decimal returnedCount = alreadyReturned.Where(r => r.ItemId == line.ItemId).Sum(r => r.Count);
            decimal returnedQty = alreadyReturned.Where(r => r.ItemId == line.ItemId).Sum(r => r.Quantity);
            if (line.Count + returnedCount > invLine.Count || line.Quantity + returnedQty > invLine.Quantity)
                return $"الكمية المرتجعة أكبر من الكمية المباعة في الفاتورة الأصلية للصنف رقم {line.ItemId}";
        }
        return null;
    }

    private async Task<string?> ValidatePurchaseReturnQuantitiesAsync(PurchaseReturn purchaseReturn, List<PurchaseReturnItem> valid)
    {
        if (valid.Any(l => l.Count < 0 || l.Quantity < 0))
            return "لا يمكن أن تكون الأعداد أو الكميات سالبة في المرتجع";

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

    private async Task<string> NextInvoiceNumberAsync(IQueryable<string> existing, string prefix)
    {
        int next = await existing.CountAsync() + 1;
        string num = $"{prefix}-{next:D5}";
        while (await existing.AnyAsync(n => n == num))
        {
            next++;
            num = $"{prefix}-{next:D5}";
        }
        return num;
    }

    private async Task<string> NextReturnNumberAsync(IQueryable<string> existing, string prefix)
    {
        int next = await existing.CountAsync() + 1;
        string num = $"{prefix}-{DateTime.Now:yyyyMMdd}-{next:D3}";
        while (await existing.AnyAsync(n => n == num))
        {
            next++;
            num = $"{prefix}-{DateTime.Now:yyyyMMdd}-{next:D3}";
        }
        return num;
    }

    private void DetachAll()
    {
        foreach (var entry in _db.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;
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

    public async Task<(bool Success, string? Error)> CreateTransferAsync(StockTransfer transfer, List<StockTransferItem> items, string? user)
    {
        var valid = items.Where(i => i.ItemId > 0 && (i.Quantity > 0 || i.Count > 0)).ToList();
        if (valid.Count == 0) return (false, "يرجى إضافة صنف واحد على الأقل بالكمية أو العدد");
        var duplicateTransfer = valid.GroupBy(i => i.ItemId).FirstOrDefault(g => g.Count() > 1);
        if (duplicateTransfer != null)
            return (false, $"الصنف رقم {duplicateTransfer.Key} مكرر أكثر من مرة في التحويل");
        if (transfer.SourceWarehouseId == transfer.TargetWarehouseId)
            return (false, "لا يمكن التحويل من مستودع إلى نفسه");

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                transfer.TransferNumber = await NextTransferNumberAsync(
                    _db.StockTransfers.Select(t => t.TransferNumber));

                transfer.CreatedBy = user;
                transfer.CreatedAt = DateTime.UtcNow;

                foreach (var item in valid)
                {
                    var product = await _db.Items.FirstOrDefaultAsync(i => i.Id == item.ItemId);
                    if (product == null) { await tx.RollbackAsync(); DetachAll(); return (false, $"الصنف رقم {item.ItemId} غير موجود"); }

                    var sourceLayers = await _db.StockLayers
                        .Where(sl => sl.ItemId == item.ItemId
                            && sl.WarehouseId == transfer.SourceWarehouseId
                            && (sl.RemainingQty > 0 || sl.RemainingCount > 0))
                        .OrderBy(sl => sl.DateReceived).ThenBy(sl => sl.Id)
                        .ToListAsync();

                    decimal needQty = item.Quantity;
                    decimal needCount = item.Count;

                    foreach (var layer in sourceLayers)
                    {
                        if (needQty > 0 && layer.RemainingQty > 0)
                        {
                            var take = Math.Min(needQty, layer.RemainingQty);
                            needQty -= take;
                            layer.RemainingQty -= take;
                            CreateTransferLayer(item.ItemId, transfer.TargetWarehouseId, take, 0, layer.UnitCost, layer.CountCost, layer.DateReceived);
                        }
                        if (needCount > 0 && layer.RemainingCount > 0)
                        {
                            var take = Math.Min(needCount, layer.RemainingCount);
                            needCount -= take;
                            layer.RemainingCount -= take;
                            CreateTransferLayer(item.ItemId, transfer.TargetWarehouseId, 0, take, layer.UnitCost, layer.CountCost, layer.DateReceived);
                        }
                        if (needQty <= 0 && needCount <= 0) break;
                    }

                    if (needQty > 0 || needCount > 0)
                    {
                        await tx.RollbackAsync(); DetachAll();
                        return (false, $"الرصيد غير كافٍ للصنف «{product.Name}» في المستودع المصدر");
                    }

                    var transferredQty = item.Quantity - needQty;
                    var transferredCount = item.Count - needCount;

                    decimal sourceOldCount = product.CurrentCount;
                    decimal sourceOldQty = product.CurrentQuantity;
                    decimal destOldCount = product.CurrentCount;
                    decimal destOldQty = product.CurrentQuantity;
                    product.CurrentCount -= transferredCount;
                    product.CurrentQuantity -= transferredQty;

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
                        UnitCost = item.UnitCost,
                        DateReceived = item.DateReceived
                    });
                }

                _db.StockTransfers.Add(transfer);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return (true, null);
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(); DetachAll(); ResetTransferKeys(transfer);
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync(); DetachAll(); ResetTransferKeys(transfer);
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
        int next = await existing.CountAsync() + 1;
        string num = $"TRF-{DateTime.Now:yyyyMMdd}-{next:D3}";
        while (await existing.AnyAsync(n => n == num))
        {
            next++;
            num = $"TRF-{DateTime.Now:yyyyMMdd}-{next:D3}";
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