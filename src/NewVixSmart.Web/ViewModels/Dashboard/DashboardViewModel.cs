namespace NewVixSmart.Web.ViewModels.Dashboard;

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

    public int OverdueReceivableCount { get; set; }
    public decimal OverdueReceivableTotal { get; set; }
    public int OverduePayableCount { get; set; }
    public decimal OverduePayableTotal { get; set; }
    public int DueSoonReceivableCount { get; set; }
    public decimal DueSoonReceivableTotal { get; set; }
    public int DueSoonPayableCount { get; set; }
    public decimal DueSoonPayableTotal { get; set; }
}