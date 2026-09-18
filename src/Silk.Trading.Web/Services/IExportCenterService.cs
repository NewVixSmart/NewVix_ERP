using Silk.Trading.Web.ViewModels.Export;

namespace Silk.Trading.Web.Services;

public interface IExportCenterService
{
    IReadOnlyList<ExportOption> GetCatalog();
    Task<byte[]> ExportXlsxAsync(string key);
    Task<byte[]> ExportCsvAsync(string key);
}