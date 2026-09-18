using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using System.Security.Claims;

namespace NewVixSmart.Web.Services;

public interface IPermissionService
{
    bool IsAdmin { get; }
    Task<bool> HasAsync(string key);
    Task<bool> HasAnyAsync(params string[] keys);
    Task<List<string>> GetKeysAsync(string userId);
}

public class PermissionService : IPermissionService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;
    private HashSet<string>? _permissions;

    public PermissionService(AppDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public bool IsAdmin => _http.HttpContext?.User.IsInRole("Admin") == true;

    public async Task<bool> HasAsync(string key)
    {
        if (_http.HttpContext?.User.Identity?.IsAuthenticated != true) return false;
        if (IsAdmin) return true;
        var set = await GetSetAsync();
        return set.Contains(key);
    }

    public async Task<bool> HasAnyAsync(params string[] keys)
    {
        if (_http.HttpContext?.User.Identity?.IsAuthenticated != true) return false;
        if (IsAdmin) return true;
        var set = await GetSetAsync();
        return keys.Any(set.Contains);
    }

    public async Task<List<string>> GetKeysAsync(string userId) =>
        await _db.UserPermissions
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.PermissionKey)
            .Select(p => p.PermissionKey)
            .ToListAsync();

    private async Task<HashSet<string>> GetSetAsync()
    {
        if (_permissions != null) return _permissions;
        var userId = _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        _permissions = string.IsNullOrEmpty(userId)
            ? new HashSet<string>()
            : (await GetKeysAsync(userId)).ToHashSet();
        return _permissions;
    }
}