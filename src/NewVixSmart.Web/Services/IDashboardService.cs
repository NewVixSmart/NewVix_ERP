using NewVixSmart.Web.ViewModels.Dashboard;

namespace NewVixSmart.Web.Services;

public interface IDashboardService
{
    Task<DashboardViewModel> GetDashboardAsync();
    Task<List<LowStockItemViewModel>> GetLowStockItemsAsync();
}