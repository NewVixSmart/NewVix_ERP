using Silk.Trading.Web.ViewModels.Dashboard;

namespace Silk.Trading.Web.Services;

public interface IDashboardService
{
    Task<DashboardViewModel> GetDashboardAsync();
    Task<List<LowStockItemViewModel>> GetLowStockItemsAsync();
}