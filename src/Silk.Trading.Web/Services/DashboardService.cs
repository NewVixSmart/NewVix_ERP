using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Core;
using Silk.Trading.Web.ViewModels.Dashboard;

namespace Silk.Trading.Web.Services;

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
            TotalPurchaseAmount = await _db.PurchaseInvoices.SumAsync(p => (decimal?)p.NetAmount) ?? 0,
            TotalSaleAmount = await _db.SaleInvoices.SumAsync(s => (decimal?)s.NetAmount) ?? 0
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

        return vm;
    }
}