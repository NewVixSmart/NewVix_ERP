namespace NewVixSmart.Web.ViewModels.Import;

public sealed record ImportEntityItem(string Key, string NameAr, string Description, string Icon, int FieldCount, string MatchNote);

public class ImportCenterViewModel
{
    public IReadOnlyList<ImportEntityItem> Items { get; set; } = [];
}

public enum ImportValueType
{
    Text,
    Integer,
    Decimal,
    Bool,
    Date,
    Enum,
    Lookup
}

public sealed record ImportColumnDefinition(
    string Key,
    string HeaderAr,
    ImportValueType Type,
    bool IsRequired,
    string? LookupKey = null,
    bool LookupUseCode = false,
    string? EnumMap = null,
    decimal? MinInclusive = null,
    decimal? MaxInclusive = null,
    int? MaxLength = null,
    bool IsLineOnly = false,
    IReadOnlyList<string>? Aliases = null);

public sealed record ImportEntityDefinition(
    string Key,
    string NameAr,
    string Description,
    string Icon,
    string FilePrefix,
    string MatchNote,
    string MatchPrimaryColumn,
    string? MatchFallbackColumn,
    string? UniqueCodeColumn,
    IReadOnlyList<ImportColumnDefinition> Columns,
    string? GroupColumn = null,
    IReadOnlyList<string>? IgnoredHeaders = null);