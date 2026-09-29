using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// An <see cref="IPermissionService"/> whose granted keys are supplied by the test, standing in for
/// the scoped real service (which reads the permissions table for the signed-in user).
/// </summary>
internal sealed class AuthzTestPermissionService : IPermissionService
{
    private readonly HashSet<string> _keys;

    public AuthzTestPermissionService(params string[] keys) => _keys = keys.ToHashSet(StringComparer.Ordinal);

    public bool IsAdmin { get; set; }

    public Task<bool> HasAsync(string key) => Task.FromResult(_keys.Contains(key));

    public Task<bool> HasAnyAsync(params string[] keys) => Task.FromResult(keys.Any(_keys.Contains));

    public Task<List<string>> GetKeysAsync(string userId) => Task.FromResult(_keys.OrderBy(k => k, StringComparer.Ordinal).ToList());
}
