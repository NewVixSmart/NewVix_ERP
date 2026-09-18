using NewVixSmart.Web.ViewModels.Import;

namespace NewVixSmart.Web.Services;

public interface IImportCenterService
{
    IReadOnlyList<ImportEntityDefinition> GetEntities();
    ImportEntityDefinition? FindEntity(string key);
    Task<byte[]> DownloadTemplateAsync(string entityKey);
    Task<ImportPreviewViewModel> ParseAsync(string entityKey, string fileName, byte[] data);
    Task<ImportResult> ImportAsync(string entityKey, string payload, string applyToken);
}