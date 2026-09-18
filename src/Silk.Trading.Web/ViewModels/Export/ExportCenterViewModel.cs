namespace Silk.Trading.Web.ViewModels.Export;

public sealed record ExportOption(string Key, string NameAr, string Description, string Icon, bool HasCsv);

public class ExportCenterViewModel
{
    public IReadOnlyList<ExportOption> Items { get; set; } = [];
}