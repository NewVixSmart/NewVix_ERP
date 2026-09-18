using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Core;
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
        var vm = new DashboardViewModel
        {
            TotalItems = await _db.Items.CountAsync(i => i.IsActive),
            TotalCustomers = await _db.Customers.CountAsync(c => c.IsActive),
            TotalSuppliers = await _db.Suppliers.CountAsync(s => s.IsActive),
            TotalPurchaseAmount = await _db.PurchaseInvoices.SumAsync(p => (decimal?)Math.Round(p.NetAmount * (p.ExchangeRate ?? 1m), 2)) ?? 0,
            TotalSaleAmount = await _db.SaleInvoices.SumAsync(s => (decimal?)Math.Round(s.NetAmount * (s.ExchangeRate ?? 1m), 2)) ?? 0
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

        var sales = await _db.SaleInvoices
            .AsNoTracking()
            .Select(s => new { s.NetAmount, s.PaidAmount, s.InvoiceDate, s.DueDate })
            .ToListAsync();
        var purchases = await _db.PurchaseInvoices
            .AsNoTracking()
            .Select(p => new { p.NetAmount, p.PaidAmount, p.InvoiceDate, p.DueDate })
            .ToListAsync();

        (vm.OverdueReceivableCount, vm.OverdueReceivableTotal) =
            DueAggregates(sales.Select(s => (s.NetAmount, s.PaidAmount, s.InvoiceDate, s.DueDate)), today, upcoming: false);
        (vm.DueSoonReceivableCount, vm.DueSoonReceivableTotal) =
            DueAggregates(sales.Select(s => (s.NetAmount, s.PaidAmount, s.InvoiceDate, s.DueDate)), today, upcoming: true);
        (vm.OverduePayableCount, vm.OverduePayableTotal) =
            DueAggregates(purchases.Select(p => (p.NetAmount, p.PaidAmount, p.InvoiceDate, p.DueDate)), today, upcoming: false);
        (vm.DueSoonPayableCount, vm.DueSoonPayableTotal) =
            DueAggregates(purchases.Select(p => (p.NetAmount, p.PaidAmount, p.InvoiceDate, p.DueDate)), today, upcoming: true);
    }

    private static (int Count, decimal Total) DueAggregates(
        IEnumerable<(decimal Net, decimal Paid, DateTime Invoice, DateTime? Due)> items,
        DateTime today,
        bool upcoming)
    {
        int count = 0;
        decimal total = 0;
        foreach (var (net, paid, invoice, due) in items)
        {
            var outstanding = net - paid;
            if (outstanding <= 0.005m) continue;
            var dueDate = (due ?? invoice).Date;
            bool hit = upcoming
                ? dueDate >= today && dueDate <= today.AddDays(7)
                : dueDate < today;
            if (!hit) continue;
            count++;
            total += outstanding;
        }
        return (count, total);
    }
}