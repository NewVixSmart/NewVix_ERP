using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// بديل <see cref="IPermissionService"/> للاختبارات. كان الباني بلا مفاتيح يمنح <b>كل</b> شيء،
/// فكانت أربعة استدعاءات في المجموعة تمرّ بمصادقة لا تفحص شيئًا: لو أُزيل فحص أذون من
/// <c>PurchaseReturnsController.Create</c> مثلًا لما لاحظ أحد، لأن البديل يمنح كل شيء.
/// الآن الباني بلا مفاتيح <b>يرفض كل شيء</b>، فيصير كل استدعاء يمرّ به دليلًا على أنه لا
/// يعتمد على الأذون - وهو ما كان المطلوب إثباته أصلًا.
/// </summary>
public sealed class TestPermissionService : IPermissionService
{
    private readonly HashSet<string> _allow;

    public TestPermissionService(params string[] allowedKeys)
    {
        _allow = new HashSet<string>(allowedKeys, StringComparer.Ordinal);
    }

    private TestPermissionService(bool administrator)
    {
        _allow = new HashSet<string>(StringComparer.Ordinal);
        IsAdmin = administrator;
    }

    /// <summary>
    /// محاكاة دور <c>Admin</c> كما يفعل <see cref="PermissionService"/>: كل مفتاح ممنوح.
    /// يُستخدم صراحةً في الاختبار الذي يحتاج «مستخدم له كل الأذون»، لا كقيمة افتراضية خفية.
    /// </summary>
    public static TestPermissionService Admin { get; } = new(administrator: true);

    public bool IsAdmin { get; }

    public Task<bool> HasAsync(string key) => Task.FromResult(IsAdmin || _allow.Contains(key));

    public Task<bool> HasAnyAsync(params string[] keys) => Task.FromResult(IsAdmin || keys.Any(_allow.Contains));

    public Task<List<string>> GetKeysAsync(string userId) =>
        Task.FromResult(_allow.OrderBy(k => k, StringComparer.Ordinal).ToList());
}
