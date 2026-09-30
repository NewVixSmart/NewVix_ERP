using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using NewVixSmart.Web.ViewModels.Dashboard;

namespace NewVixSmart.Web.Services;

public class DashboardService : IDashboardService
{
    private static readonly Expression<Func<Item, bool>> _lowStockPredicate =
        i => i.IsActive
            && ((i.CountUnitId.HasValue && i.MinCount > 0 && i.CurrentCount < i.MinCount)
             || (i.QuantityUnitId.HasValue && i.MinQuantity > 0 && i.CurrentQuantity < i.MinQuantity));

    private readonly AppDbContext _db;
    public DashboardService(AppDbContext db) => _db = db;

    public async Task<List<LowStockItemViewModel>> GetLowStockItemsAsync()
    {
        var items = await _db.Items
            .AsNoTracking()
            .Where(_lowStockPredicate)
            .Include(i => i.Category)
            .OrderBy(i => i.Name)
            .ToListAsync();

        return items.Select(MapLowStock).ToList();
    }

    private static LowStockItemViewModel MapLowStock(Item i) => new()
    {
        Id = i.Id,
        Name = i.Name,
        Code = i.Code,
        CategoryName = i.Category?.Name ?? "—",
        CurrentCount = i.CurrentCount,
        CurrentQuantity = i.CurrentQuantity,
        MinCount = i.MinCount,
        MinQuantity = i.MinQuantity
    };

    public async Task<DashboardViewModel> GetDashboardAsync()
    {
        var deliveredInvoiceIds = await _db.DeliveryOrders
            .AsNoTracking()
            .Where(d => d.Status == DeliveryOrderStatus.Delivered && d.SaleInvoiceId != null)
            .Select(d => d.SaleInvoiceId)
            .Distinct()
            .ToListAsync();

        var saleAmounts = deliveredInvoiceIds.Count == 0
            ? new List<decimal>()
            : await _db.SaleInvoices
                .AsNoTracking()
                .Where(s => deliveredInvoiceIds.Contains(s.Id))
                .Select(s => s.NetAmount)
                .ToListAsync();
        var totalSale = saleAmounts.Sum(v => Math.Round(v, 2));

        var purchaseAmounts = await _db.PurchaseInvoices
            .AsNoTracking()
            .Select(p => p.NetAmount)
            .ToListAsync();

        var vm = new DashboardViewModel
        {
            TotalItems = await _db.Items.CountAsync(i => i.IsActive),
            TotalCustomers = await _db.Customers.CountAsync(c => c.IsActive),
            TotalSuppliers = await _db.Suppliers.CountAsync(s => s.IsActive),
            TotalPurchaseAmount = purchaseAmounts.Sum(v => Math.Round(v, 2)),
            TotalSaleAmount = totalSale
        };

        vm.LowStockItems = await GetLowStockItemsAsync();

        var pending = await _db.SalesOrderItems
            .AsNoTracking()
            .Where(i => i.SalesOrder.Status != SalesOrderStatus.Cancelled
                && (i.Quantity - i.DeliveredQty > 0 || i.Count - i.DeliveredCount > 0))
            .Select(i => new
            {
                i.SalesOrderId,
                i.UnitPrice,
                PendingQty = i.Quantity - i.DeliveredQty,
                PendingCount = i.Count - i.DeliveredCount
            })
            .ToListAsync();

        vm.PendingDeliveryCount = pending
            .Select(p => p.SalesOrderId)
            .Distinct()
            .Count();
        vm.PendingDeliveryValue = decimal.Round(pending
            .Sum(p => (p.PendingQty > 0 ? p.PendingQty : p.PendingCount) * p.UnitPrice), 2);
        vm.ActiveReservationCount = await _db.StockReservations
            .AsNoTracking()
            .CountAsync(r => r.Status == StockReservationStatus.Active);

        var recentPurchases = await _db.PurchaseInvoices
            .AsNoTracking()
            .Include(p => p.Supplier)
            .OrderByDescending(p => p.InvoiceDate)
            .Take(5)
            .ToListAsync();

        vm.RecentPurchases = recentPurchases.Select(p => new RecentPurchaseViewModel
        {
            Id = p.Id,
            InvoiceNumber = p.InvoiceNumber,
            PartyName = p.Supplier?.Name ?? "—",
            InvoiceDate = p.InvoiceDate,
            NetAmount = p.NetAmount
        }).ToList();

        var recentSales = await _db.SaleInvoices
            .AsNoTracking()
            .Include(s => s.Customer)
            .OrderByDescending(s => s.InvoiceDate)
            .Take(5)
            .ToListAsync();

        vm.RecentSales = recentSales.Select(s => new RecentSaleViewModel
        {
            Id = s.Id,
            InvoiceNumber = s.InvoiceNumber,
            PartyName = s.Customer?.Name ?? "—",
            InvoiceDate = s.InvoiceDate,
            NetAmount = s.NetAmount
        }).ToList();

        await LoadDueAlertsAsync(vm);

        return vm;
    }

    private async Task LoadDueAlertsAsync(DashboardViewModel vm)
    {
        var today = DateTime.Today;

        var saleOpen = await OpenAmountRule.SaleOpenByInvoiceAsync(_db);
        var saleDueDates = await _db.SaleInvoices
            .AsNoTracking()
            .Select(s => new { s.Id, s.DueDate, s.InvoiceDate })
            .ToListAsync();
        var sales = DueAlerts(
            saleDueDates
                .Where(s => saleOpen.GetValueOrDefault(s.Id) > OpenAmountRule.OpenTolerance)
                .Select(s => (Due: (s.DueDate ?? s.InvoiceDate).Date, Open: saleOpen[s.Id])),
            today);

        var purchaseOpen = await OpenAmountRule.PurchaseOpenByInvoiceAsync(_db);
        var purchaseDueDates = await _db.PurchaseInvoices
            .AsNoTracking()
            .Select(p => new { p.Id, p.DueDate, p.InvoiceDate })
            .ToListAsync();
        var purchases = DueAlerts(
            purchaseDueDates
                .Where(p => purchaseOpen.GetValueOrDefault(p.Id) > OpenAmountRule.OpenTolerance)
                .Select(p => (Due: (p.DueDate ?? p.InvoiceDate).Date, Open: purchaseOpen[p.Id])),
            today);

        vm.OverdueReceivableCount = sales.Overdue.Count;
        vm.OverdueReceivableTotal = sales.Overdue.Total;
        vm.DueSoonReceivableCount = sales.DueSoon.Count;
        vm.DueSoonReceivableTotal = sales.DueSoon.Total;
        vm.OverduePayableCount = purchases.Overdue.Count;
        vm.OverduePayableTotal = purchases.Overdue.Total;
        vm.DueSoonPayableCount = purchases.DueSoon.Count;
        vm.DueSoonPayableTotal = purchases.DueSoon.Total;
    }

    /// <summary>
    /// The single bucketing all four due tiles are built from, so the receivable and the payable
    /// side cannot drift apart. Every document reaches it priced through <see cref="OpenAmountRule"/>
    /// — the same open amount the aging report and the payment allocation use — because
    /// <c>NetAmount - PaidAmount</c> ignores a posted return and made this board disagree with
    /// the ledger it was supposed to summarise.
    /// </summary>
    private static DueAlertTiles DueAlerts(IEnumerable<(DateTime Due, decimal Open)> rows, DateTime today)
    {
        int overdueCount = 0, dueSoonCount = 0;
        decimal overdueTotal = 0m, dueSoonTotal = 0m;
        var horizon = today.AddDays(7);

        foreach (var (due, open) in rows)
        {
            if (due.Date < today) { overdueCount++; overdueTotal += open; }
            else if (due.Date <= horizon) { dueSoonCount++; dueSoonTotal += open; }
        }

        return new DueAlertTiles(
            new DueAlert(overdueCount, decimal.Round(overdueTotal, 2)),
            new DueAlert(dueSoonCount, decimal.Round(dueSoonTotal, 2)));
    }

    private readonly record struct DueAlert(int Count, decimal Total);

    private readonly record struct DueAlertTiles(DueAlert Overdue, DueAlert DueSoon);
}
