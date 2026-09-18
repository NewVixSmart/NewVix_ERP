using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Silk.Trading.Web.Services;

public sealed class BackupService : IBackupService
{
    private readonly string _connectionString;
    private readonly string _dbName;
    private readonly string _backupDir;
    private readonly ILogger<BackupService> _logger;
    private static readonly SemaphoreSlim _operationLock = new(1, 1);

    public string BackupDirectory => _backupDir;

    public BackupService(IConfiguration configuration, IWebHostEnvironment env, ILogger<BackupService> logger)
    {
        _logger = logger;
        var raw = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("connectionString \"DefaultConnection\" غير موجود في الإعدادات.");
        _connectionString = raw;
        var builder = new SqlConnectionStringBuilder(raw);
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
            throw new InvalidOperationException("اسم قاعدة البيانات (Initial Catalog) غير موجود في connectionString.");
        _dbName = builder.InitialCatalog;
        _backupDir = Path.Combine(env.ContentRootPath, "App_Data", "Backups");
        Directory.CreateDirectory(_backupDir);
    }

    public IReadOnlyList<BackupFileInfo> ListBackups()
    {
        var dir = new DirectoryInfo(_backupDir);
        if (!dir.Exists)
            return Array.Empty<BackupFileInfo>();

        return dir.GetFiles("*.bak")
            .OrderByDescending(f => f.Name)
            .Select(f => new BackupFileInfo(f.Name, f.Length, f.CreationTimeUtc))
            .ToList();
    }

    public async Task<string> CreateBackupAsync()
    {
        if (!await _operationLock.WaitAsync(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("عملية أخرى قيد التنفيذ. يرجى الانتظار.");

        try
        {
            Directory.CreateDirectory(_backupDir);

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var baseName = $"SilkTrading_{stamp}.bak";
            var filePath = Path.Combine(_backupDir, baseName);

            if (File.Exists(filePath))
            {
                int seq = 2;
                while (File.Exists(Path.Combine(_backupDir, $"SilkTrading_{stamp}_{seq}.bak")))
                    seq++;
                baseName = $"SilkTrading_{stamp}_{seq}.bak";
                filePath = Path.Combine(_backupDir, baseName);
            }

            var dbQuoted = "[" + _dbName + "]";
            var sql = $"BACKUP DATABASE {dbQuoted} TO DISK = N'{EscapeSqlString(filePath)}' WITH INIT";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 180 };
            await cmd.ExecuteNonQueryAsync();

            _logger.LogInformation("Backup created: {FileName}", baseName);
            return baseName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup creation failed");
            throw new InvalidOperationException("حدث خطأ أثناء إنشاء النسخة الاحتياطية.", ex);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task DeleteAsync(string fileName)
    {
        if (!await _operationLock.WaitAsync(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("عملية أخرى قيد التنفيذ. يرجى الانتظار.");

        try
        {
            var fullPath = ResolveBackupPath(fileName);
            File.Delete(fullPath);
            _logger.LogInformation("Backup deleted: {FileName}", fileName);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<byte[]> ReadBackupBytesAsync(string fileName)
    {
        if (!await _operationLock.WaitAsync(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("عملية أخرى قيد التنفيذ. يرجى الانتظار.");

        try
        {
            var fullPath = ResolveBackupPath(fileName);
            return await File.ReadAllBytesAsync(fullPath);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task RestoreAsync(string fileName)
    {
        if (!await _operationLock.WaitAsync(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("عملية أخرى قيد التنفيذ. يرجى الانتظار.");

        try
        {
            var fullPath = ResolveBackupPath(fileName);

            var masterBuilder = new SqlConnectionStringBuilder(_connectionString)
            {
                InitialCatalog = "master"
            };

            var dbQuoted = "[" + _dbName + "]";
            await using var conn = new SqlConnection(masterBuilder.ConnectionString);
            await conn.OpenAsync();

            await using (var cmd1 = new SqlCommand($"ALTER DATABASE {dbQuoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE", conn) { CommandTimeout = 300 })
                await cmd1.ExecuteNonQueryAsync();

            try
            {
                await using var cmd2 = new SqlCommand($"RESTORE DATABASE {dbQuoted} FROM DISK = N'{EscapeSqlString(fullPath)}' WITH REPLACE", conn) { CommandTimeout = 300 };
                await cmd2.ExecuteNonQueryAsync();
            }
            finally
            {
                try
                {
                    await using var cmd3 = new SqlCommand($"ALTER DATABASE {dbQuoted} SET MULTI_USER", conn) { CommandTimeout = 300 };
                    await cmd3.ExecuteNonQueryAsync();
                }
                catch
                {
                    try
                    {
                        await using var cmdRecover = new SqlCommand($"RESTORE DATABASE {dbQuoted} WITH RECOVERY", conn) { CommandTimeout = 300 };
                        await cmdRecover.ExecuteNonQueryAsync();
                    }
                    catch (Exception recoveryEx)
                    {
                        _logger.LogCritical(recoveryEx, "Database stuck in SINGLE_USER after restore — manual intervention may be required");
                    }
                }
            }

            SqlConnection.ClearAllPools();
            _logger.LogInformation("Database restored from {FileName}", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore failed from {FileName}", fileName);
            throw new InvalidOperationException("حدث خطأ أثناء استعادة النسخة الاحتياطية.", ex);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task ResetSystemAsync()
    {
        if (!await _operationLock.WaitAsync(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("عملية أخرى قيد التنفيذ. يرجى الانتظار.");

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var transaction = conn.BeginTransaction();
            try
            {
                var adminId = await GetAdminIdAsync(conn, transaction);

                var tables = await GetNonIdentityTablesAsync(conn, transaction);

                foreach (var table in tables)
                {
                    var tquoted = "[" + table + "]";
                    await ExecuteNonQueryAsync(conn, transaction, $"ALTER TABLE {tquoted} NOCHECK CONSTRAINT ALL");
                }

                foreach (var table in tables)
                {
                    var tquoted = "[" + table + "]";
                    await ExecuteNonQueryAsync(conn, transaction, $"DELETE FROM {tquoted}");

                    if (await HasIdentityColumnAsync(conn, transaction, table))
                        await ExecuteNonQueryAsync(conn, transaction, $"DBCC CHECKIDENT (N'{EscapeSqlString(table)}', RESEED, 0)");
                }

                foreach (var table in tables)
                {
                    var tquoted = "[" + table + "]";
                    await ExecuteNonQueryAsync(conn, transaction, $"ALTER TABLE {tquoted} WITH CHECK CHECK CONSTRAINT ALL");
                }

                string[] childTables = ["AspNetUserTokens", "AspNetUserLogins", "AspNetUserClaims", "AspNetUserRoles"];
                foreach (var child in childTables)
                    await ExecuteNonQueryAsync(conn, transaction, $"DELETE FROM [{child}] WHERE UserId <> @adminId", adminId);

                await ExecuteNonQueryAsync(conn, transaction, "DELETE FROM [dbo].[AspNetUsers] WHERE Id <> @adminId", adminId);

                transaction.Commit();
                _logger.LogWarning("System reset completed — all data except admin deleted");
            }
            catch (Exception ex)
            {
                try { transaction.Rollback(); }
                catch (Exception rbEx)
                {
                    _logger.LogCritical(rbEx, "Transaction rollback failed during system reset — database may be in inconsistent state");
                }

                if (ex is InvalidOperationException)
                    throw;
                throw new InvalidOperationException("حدث خطأ أثناء إعادة ضبط النظام.", ex);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public string ResolveBackupPath(string fileName)
    {
        if (!BackupFileName.IsValid(fileName))
            throw new ArgumentException("اسم الملف غير صالح.", nameof(fileName));

        var dirFull = Path.GetFullPath(_backupDir);
        var fullPath = Path.GetFullPath(Path.Combine(dirFull, fileName));
        if (!fullPath.StartsWith(dirFull, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("المسار غير مسموح.", nameof(fileName));

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("الملف غير موجود.", fileName);

        return fullPath;
    }

    private static string EscapeSqlString(string value) => value.Replace("'", "''");

    private static async Task ExecuteNonQueryAsync(SqlConnection conn, SqlTransaction transaction, string sql, string? adminId = null)
    {
        await using var cmd = new SqlCommand(sql, conn, transaction) { CommandTimeout = 300 };
        if (adminId != null)
            cmd.Parameters.AddWithValue("@adminId", adminId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string> GetAdminIdAsync(SqlConnection conn, SqlTransaction transaction)
    {
        await using var cmd = new SqlCommand("SELECT Id FROM AspNetUsers WHERE UserName = 'admin'", conn, transaction);
        var result = await cmd.ExecuteScalarAsync();
        if (result == null || result == DBNull.Value)
            throw new InvalidOperationException("لا يمكن إعادة الضبط: لم يتم العثور على مستخدم admin");
        return result.ToString()!;
    }

    private static async Task<List<string>> GetNonIdentityTablesAsync(SqlConnection conn, SqlTransaction transaction)
    {
        await using var cmd = new SqlCommand("SELECT t.name FROM sys.tables t WHERE t.name NOT LIKE 'AspNet%' AND t.name <> '__EFMigrationsHistory'", conn, transaction);
        var tables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));
        return tables;
    }

    private static async Task<bool> HasIdentityColumnAsync(SqlConnection conn, SqlTransaction transaction, string tableName)
    {
        await using var cmd = new SqlCommand("SELECT COUNT(1) FROM sys.columns WHERE object_id = OBJECT_ID(@tbl) AND is_identity = 1", conn, transaction);
        cmd.Parameters.AddWithValue("@tbl", tableName);
        var result = await cmd.ExecuteScalarAsync();
        return result != null && result != DBNull.Value && (int)result > 0;
    }
}