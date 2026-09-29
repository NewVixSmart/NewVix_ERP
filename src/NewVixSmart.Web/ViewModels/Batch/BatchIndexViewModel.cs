using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;

namespace NewVixSmart.Web.ViewModels.Batch;

public class BatchIndexViewModel
{
    public List<SaleInvoice> RecentSales { get; set; } = new();
    public List<InventoryAdjustment> RecentAdjustments { get; set; } = new();
}
