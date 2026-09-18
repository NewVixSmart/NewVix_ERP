namespace Silk.Trading.Web.ViewModels.Import;

public sealed record ImportPreviewRow(int RowNumber, IReadOnlyList<string> Values, IReadOnlyList<string> Errors, string? Notice, bool IsDuplicate);

public sealed record ImportRowPayload(int RowNumber, Dictionary<string, string> Fields);

public sealed record ImportPayloadEnvelope(string EntityKey, List<ImportRowPayload> Rows);

public sealed record ImportResult(bool Success, string Message, int Created, int Updated, int Skipped, int Failed);

public class ImportPreviewViewModel
{
    public string EntityKey { get; set; } = string.Empty;
    public string EntityNameAr { get; set; } = string.Empty;
    public string? FatalError { get; set; }
    public string? Warning { get; set; }
    public IReadOnlyList<string> Headers { get; set; } = [];
    public IReadOnlyList<ImportPreviewRow> Rows { get; set; } = [];
    public string Payload { get; set; } = string.Empty;
    public string? ApplyToken { get; set; }
    public int TotalCount { get; set; }
    public int ValidCount { get; set; }
    public int ErrorCount { get; set; }
    public int DuplicateCount { get; set; }
    public bool HasValidRows => ValidCount > 0;
}