namespace Silk.Trading.Web.Services;

public sealed record BackupFileInfo(string FileName, long LengthBytes, DateTime CreatedAtUtc);

public sealed class BackupFileName
{
    public static bool IsValid(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;
        if (Path.GetFileName(fileName) != fileName)
            return false;
        if (!fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}

public interface IBackupService
{
    string BackupDirectory { get; }
    IReadOnlyList<BackupFileInfo> ListBackups();
    Task<string> CreateBackupAsync();
    Task DeleteAsync(string fileName);
    Task<byte[]> ReadBackupBytesAsync(string fileName);
    Task RestoreAsync(string fileName);
    Task ResetSystemAsync();
    string ResolveBackupPath(string fileName);
}
