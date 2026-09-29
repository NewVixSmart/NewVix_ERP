using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;

namespace NewVixSmart.Web.Services;

public sealed class BatchService : IBatchService
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;

    public BatchService(AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    public async Task<BatchResults> RunSalesBatchAsync(BatchSalesBatchRequest request, string? user, int? branchId = null)
    {
        var blocks = request.Invoices
            .Where(b => b.Lines.Any(l => l.ItemId > 0 && (l.Quantity > 0 || l.Count > 0)))
            .ToList();

        if (blocks.Count == 0)
        {
            return new BatchResults("المبيعات الجماعية",
                [new BatchDocumentResult(0, "فاتورة بيع", null, false, "لا توجد فواتير صالحة تحتوي أصنافًا")]);
        }

        var results = new List<BatchDocumentResult>();
        int seq = 0;
        foreach (var block in blocks)
        {
            seq++;
            var lines = block.Lines.Where(l => l.ItemId > 0 && (l.Quantity > 0 || l.Count > 0)).ToList();
            var invoice = new SaleInvoice
            {
                CustomerId = request.CustomerId,
                InvoiceDate = request.InvoiceDate,
                PaymentTerms = request.PaymentTerms,
                Discount = request.Discount,
                Discount2 = request.Discount2,
                Discount3 = request.Discount3,
                Tax = request.Tax,
                Notes = request.Notes
            };
            var items = lines.Select(l => new SaleInvoiceItem
            {
                ItemId = l.ItemId,
                Quantity = l.Quantity,
                Count = l.Count,
                UnitPrice = l.UnitPrice,
                Discount = l.Discount
            }).ToList();

            var (ok, error) = await _inventory.CreateSaleAsync(invoice, items, user, branchId);
            results.Add(new BatchDocumentResult(seq, "فاتورة بيع", ok ? invoice.InvoiceNumber : null, ok, ok ? null : error));
        }

        return new BatchResults("المبيعات الجماعية", results);
    }

    public async Task<BatchResults> RunAdjustmentBatchAsync(BatchAdjustmentRequest request, string? user)
    {
        var lines = request.Lines.Where(l => l.ItemId > 0).ToList();

        if (lines.Count == 0)
        {
            return new BatchResults("الجرد الجماعي",
                [new BatchDocumentResult(0, "جرد", null, false, "لا توجد أصناف صالحة للجرد")]);
        }

        var results = new List<BatchDocumentResult>();
        int seq = 0;
        foreach (var line in lines)
        {
            seq++;
            var adjustment = new InventoryAdjustment
            {
                ItemId = line.ItemId,
                NewCount = line.NewCount,
                NewQuantity = line.NewQuantity,
                Reason = request.Reason,
                AdjustmentDate = request.AdjustDate
            };

            var (ok, error) = await _inventory.CreateAdjustmentAsync(adjustment, user);
            results.Add(new BatchDocumentResult(seq, "جرد", ok ? adjustment.ReferenceNumber : null, ok, ok ? null : error));
        }

        return new BatchResults("الجرد الجماعي", results);
    }

    public async Task<IReadOnlyList<SaleInvoice>> RecentSalesAsync(int take)
        => await _db.SaleInvoices.AsNoTracking().Include(s => s.Customer)
            .OrderByDescending(s => s.InvoiceDate).ThenByDescending(s => s.Id)
            .Take(take).ToListAsync();

    public async Task<IReadOnlyList<InventoryAdjustment>> RecentAdjustmentsAsync(int take)
        => await _db.InventoryAdjustments.AsNoTracking().Include(a => a.Item)
            .OrderByDescending(a => a.AdjustmentDate).ThenByDescending(a => a.Id)
            .Take(take).ToListAsync();
}
