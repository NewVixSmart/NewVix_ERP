using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.Tests;

public sealed class TestPermissionService : IPermissionService
{
    private readonly HashSet<string> _allow;
    private readonly bool _allowAll;

    public TestPermissionService(params string[] allowedKeys)
    {
        _allow = new HashSet<string>(allowedKeys);
        _allowAll = allowedKeys.Length == 0;
    }

    public bool IsAdmin => false;

    public Task<bool> HasAsync(string key) => Task.FromResult(_allowAll || _allow.Contains(key));

    public Task<bool> HasAnyAsync(params string[] keys) => Task.FromResult(_allowAll || keys.Any(_allow.Contains));

    public Task<List<string>> GetKeysAsync(string userId) => Task.FromResult(new List<string>());
}