using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.ViewModels.Dashboard;

namespace NewVixSmart.Web.Services;

public class DashboardService : IDashboardService
{
    private static readonly Expression<Func<Item, bool>> LowStockPredicate =
        i => i.IsActive
            && ((i.CountUnitId.HasValue && i.MinCount > 0 && i.CurrentCount < i.MinCount)
             || (i.QuantityUnitId.HasValue && i.MinQuantity > 0 && i.CurrentQuantity < i.MinQuantity));

    private readonly AppDbContext _db;
    public DashboardService(AppDbContext db) => _db = db;

    public async Task<List<LowStockItemViewModel>> GetLowStockItemsAsync()
    {
        var items = await _db.Items
            .AsNoTracking()
            .Where(LowStockPredicate)
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

        var totalSale = deliveredInvoiceIds.Count == 0
            ? 0m
            : await _db.SaleInvoices
                .AsNoTracking()
                .Where(s => deliveredInvoiceIds.Contains(s.Id))
                .SumAsync(s => (decimal?)Math.Round(s.NetAmount * (s.ExchangeRate ?? 1m), 2)) ?? 0m;

        var vm = new DashboardViewModel
        {
            TotalItems = await _db.Items.CountAsync(i => i.IsActive),
            TotalCustomers = await _db.Customers.CountAsync(c => c.IsActive),
            TotalSuppliers = await _db.Suppliers.CountAsync(s => s.IsActive),
            TotalPurchaseAmount = await _db.PurchaseInvoices.SumAsync(p => (decimal?)Math.Round(p.NetAmount * (p.ExchangeRate ?? 1m), 2)) ?? 0,
            TotalSaleAmount = totalSale
        };

        vm.LowStockItems = await GetLowStockItemsAsync();

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

        var sales = _db.SaleInvoices
            .AsNoTracking()
            .Select(s => new { s.NetAmount, s.PaidAmount, s.InvoiceDate, s.DueDate })
            .Where(s => s.NetAmount - s.PaidAmount > 0.005m);
        var purchases = _db.PurchaseInvoices
            .AsNoTracking()
            .Select(p => new { p.NetAmount, p.PaidAmount, p.InvoiceDate, p.DueDate })
            .Where(p => p.NetAmount - p.PaidAmount > 0.005m);

        var overdueSales = await sales.Where(s => (s.DueDate ?? s.InvoiceDate).Date < today).ToListAsync();
        var dueSoonSales = await sales
            .Where(s => (s.DueDate ?? s.InvoiceDate).Date >= today && (s.DueDate ?? s.InvoiceDate).Date <= today.AddDays(7))
            .ToListAsync();
        var overduePurchases = await purchases.Where(p => (p.DueDate ?? p.InvoiceDate).Date < today).ToListAsync();
        var dueSoonPurchases = await purchases
            .Where(p => (p.DueDate ?? p.InvoiceDate).Date >= today && (p.DueDate ?? p.InvoiceDate).Date <= today.AddDays(7))
            .ToListAsync();

        vm.OverdueReceivableCount = overdueSales.Count;
        vm.OverdueReceivableTotal = overdueSales.Sum(s => s.NetAmount - s.PaidAmount);
        vm.DueSoonReceivableCount = dueSoonSales.Count;
        vm.DueSoonReceivableTotal = dueSoonSales.Sum(s => s.NetAmount - s.PaidAmount);
        vm.OverduePayableCount = overduePurchases.Count;
        vm.OverduePayableTotal = overduePurchases.Sum(p => p.NetAmount - p.PaidAmount);
        vm.DueSoonPayableCount = dueSoonPurchases.Count;
        vm.DueSoonPayableTotal = dueSoonPurchases.Sum(p => p.NetAmount - p.PaidAmount);
    }
}