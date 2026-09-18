using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Models.Accounting;
using System.Text;

namespace Silk.Trading.Web.Services;

public class AccountEditResult
{
    public bool Ok { get; init; }
    public string Error { get; init; } = string.Empty;
}

public class AccountDeleteResult
{
    public bool CanDelete { get; init; }
    public bool IsSystem { get; init; }
    public bool IsPosted { get; init; }
}

public class AccountsService
{
    private readonly AppDbContext _db;

    public static readonly string[] SystemSeedCodes =
        ["1000", "1100", "1200", "1300", "2000", "3000", "4000", "4100", "5000", "5100", "3001", "4400", "8400", "5101", "5102"];

    public AccountsService(AppDbContext db)
    {
        _db = db;
    }

    public static string NormKey(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is '\u064B' or '\u064C' or '\u064D' or '\u064E' or '\u064F'
                or '\u0650' or '\u0651' or '\u0652' or '\u0653' or '\u0654' or '\u0655'
                or '\u200C' or '\u200D') continue;
            sb.Append(ch switch
            {
                '\u0622' or '\u0623' or '\u0625' => '\u0627',
                '\u0629' => '\u0647',
                '\u0649' => '\u064A',
                _ => ch
            });
        }
        return sb.ToString().Trim().ToUpperInvariant();
    }

    public bool IsSystemAccount(string code) => SystemSeedCodes.Contains(code.Trim());

    public bool IsPostedAccount(int accountId) => _db.JournalEntryLines.Any(l => l.AccountId == accountId);

    public async Task<bool> HasPostedLinesAsync(int accountId) => await _db.JournalEntryLines.AnyAsync(l => l.AccountId == accountId);

    public async Task<bool> CodeExistsAsync(string code, int? excludeId = null)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return await _db.GLAccounts.AnyAsync(a => a.Code == normalized && (excludeId == null || a.Id != excludeId));
    }

    public async Task<string?> ValidateCodeChangeAsync(int accountId, string? newCode, string currentCode)
    {
        var isSystem = IsSystemAccount(currentCode);
        var isPosted = await HasPostedLinesAsync(accountId);

        if (string.IsNullOrWhiteSpace(newCode)) return null;

        var normalizedNew = newCode.Trim();
        if (normalizedNew == currentCode) return null;

        if (isPosted)
            return "لا يمكن تغيير رمز حساب له قيود مرحلة";

        if (isSystem)
            return "لا يمكن تغيير رمز حساب النظام";

        if (await CodeExistsAsync(normalizedNew, accountId))
            return "حساب بهذا الرمز موجود بالفعل";

        return null;
    }

    public async Task<AccountEditResult> ApplyEditAsync(GLAccount account, GLAccount model)
    {
        if (!string.IsNullOrWhiteSpace(model.Name))
            account.Name = model.Name.Trim();

        account.ParentAccountId = model.ParentAccountId;
        account.NormalBalance = model.NormalBalance;
        account.Type = model.Type;

        if (!model.IsActive && account.IsActive)
        {
            if (IsSystemAccount(account.Code))
                return new AccountEditResult { Ok = false, Error = "لا يمكن تعطيل حساب نظامي مُعرَّف بالبذرة" };
            if (await HasPostedLinesAsync(account.Id))
                return new AccountEditResult { Ok = false, Error = "لا يمكن تعطيل حساب له قيود مرحلة" };
        }

        account.IsActive = model.IsActive;

        if (!string.IsNullOrWhiteSpace(model.Code))
        {
            var codeIssue = await ValidateCodeChangeAsync(account.Id, model.Code, account.Code);
            if (codeIssue != null)
                return new AccountEditResult { Ok = false, Error = codeIssue };
            if (model.Code.Trim() != account.Code)
                account.Code = model.Code.Trim();
        }

        return new AccountEditResult { Ok = true };
    }

    public async Task<AccountDeleteResult> GetDeleteInfoAsync(GLAccount account)
    {
        var isSystem = IsSystemAccount(account.Code);
        var isPosted = await HasPostedLinesAsync(account.Id);
        return new AccountDeleteResult
        {
            CanDelete = !isSystem && !isPosted,
            IsSystem = isSystem,
            IsPosted = isPosted
        };
    }

    public async Task<(decimal Debit, decimal Credit)> GetRunningBalanceAsync(int accountId)
    {
        var result = await _db.JournalEntryLines
            .AsNoTracking()
            .Where(l => l.AccountId == accountId && l.JournalEntry!.IsPosted)
            .GroupBy(l => 1)
            .Select(g => new { Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) })
            .FirstOrDefaultAsync();
        return (result?.Debit ?? 0m, result?.Credit ?? 0m);
    }
}
