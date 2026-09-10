using Silk.Trading.Web.Models.Sales;
using Silk.Trading.Web.Models.Stock;

namespace Silk.Trading.Web.ViewModels.Batch;

public class BatchIndexViewModel
{
    public List<SaleInvoice> RecentSales { get; set; } = new();
    public List<InventoryAdjustment> RecentAdjustments { get; set; } = new();
}