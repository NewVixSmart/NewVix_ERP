using Microsoft.AspNetCore.Http;

namespace Silk.Trading.Web.Extensions;

public static class CurrentBranchExtensions
{
    private const string Key = "CurrentBranchId";

    public static int? GetCurrentBranchId(this ISession session) =>
        int.TryParse(session.GetString(Key), out var id) ? id : null;

    public static void SetCurrentBranchId(this ISession session, int? branchId)
    {
        if (branchId.HasValue)
            session.SetString(Key, branchId.Value.ToString());
        else
            session.Remove(Key);
    }

    public static int? GetCurrentBranchId(this IHttpContextAccessor http) =>
        http.HttpContext?.Session.GetCurrentBranchId();

    public static void SetCurrentBranchId(this IHttpContextAccessor http, int? branchId)
    {
        if (http.HttpContext?.Session != null)
            http.HttpContext.Session.SetCurrentBranchId(branchId);
    }
}
