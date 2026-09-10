namespace Silk.Trading.Web.ViewModels.Dashboard;

public class DashboardViewModel
{
    public int TotalItems { get; set; }
    public int TotalCustomers { get; set; }
    public int TotalSuppliers { get; set; }
    public decimal TotalPurchaseAmount { get; set; }
    public decimal TotalSaleAmount { get; set; }

    public int LowStockCount => LowStockItems.Count;
    public List<LowStockItemViewModel> LowStockItems { get; set; } = new();
    public List<RecentPurchaseViewModel> RecentPurchases { get; set; } = new();
    public List<RecentSaleViewModel> RecentSales { get; set; } = new();
}