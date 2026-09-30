using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// An <see cref="IPermissionService"/> whose granted keys are supplied by the test, standing in for
/// the scoped real service (which reads the permissions table for the signed-in user).
/// It mirrors <see cref="PermissionService"/>: an administrator is allowed everything, and an
/// anonymous caller is allowed nothing even if flagged as an administrator.
/// </summary>
internal sealed class AuthzTestPermissionService : IPermissionService
{
    private readonly HashSet<string> _keys;

    public AuthzTestPermissionService(params string[] keys) => _keys = keys.ToHashSet(StringComparer.Ordinal);

    public bool IsAdmin { get; set; }

    /// <summary>
    /// Mirrors the real service's first guard. A double that ignores it would let a test
    /// "prove" that an unauthenticated request is refused when the refusal came from the
    /// double, not from the code under test.
    /// </summary>
    public bool IsAuthenticated { get; set; } = true;

    public Task<bool> HasAsync(string key) =>
        Task.FromResult(IsAuthenticated && (IsAdmin || _keys.Contains(key)));

    public Task<bool> HasAnyAsync(params string[] keys) =>
        Task.FromResult(IsAuthenticated && (IsAdmin || keys.Any(_keys.Contains)));

    public Task<List<string>> GetKeysAsync(string userId) =>
        Task.FromResult(_keys.OrderBy(k => k, StringComparer.Ordinal).ToList());
}
