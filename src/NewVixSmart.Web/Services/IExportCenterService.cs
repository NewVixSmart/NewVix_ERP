using NewVixSmart.Web.ViewModels.Export;

namespace NewVixSmart.Web.Services;

public interface IExportCenterService
{
    IReadOnlyList<ExportOption> GetCatalog();
    Task<byte[]> ExportXlsxAsync(string key);
    Task<byte[]> ExportCsvAsync(string key);
}
