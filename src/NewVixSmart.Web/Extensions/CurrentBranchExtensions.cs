using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace NewVixSmart.Web.Extensions;

public static class CurrentBranchExtensions
{
    private const string Key = "CurrentBranchId";

    public static int? GetCurrentBranchId(this ISession session) =>
        int.TryParse(session.GetString(Key), out var id) ? id : null;

    public static void SetCurrentBranchId(this ISession session, int? branchId)
    {
        if (branchId.HasValue && branchId > 0)
            session.SetString(Key, branchId.Value.ToString());
        else
            session.Remove(Key);
    }

    public static int? GetCurrentBranchId(this IHttpContextAccessor http) =>
        http.HttpContext?.Features.Get<ISessionFeature>()?.Session.GetCurrentBranchId();

    public static void SetCurrentBranchId(this IHttpContextAccessor http, int? branchId)
    {
        var session = http.HttpContext?.Features.Get<ISessionFeature>()?.Session;
        if (session != null)
            session.SetCurrentBranchId(branchId);
    }
}
