using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NewVixSmart.Web.Controllers;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// <c>RequirePermFilter</c> كان بلا أي اختبار ينفّذه: كل اختبارات الأذونات إما تفحص
/// <c>[RequirePerm]</c> بالانعكاس على <em>التعليق</em>، أو تستدعي <c>controller.Action()</c>
/// مباشرة فيتجاوز المرشّح أصلًا. هذا الملف يبني <see cref="AuthorizationFilterContext"/> حقيقيًا
/// وينفّذ المرشّح، فيختبر الفروع الثلاثة بدل أن يقرأ سطرًا في ملف مصدري.
/// </summary>
public sealed class RequirePermFilterTests
{
    private const string _key = "Sales.Create";

    private static AuthorizationFilterContext ContextFor(IAsyncAuthorizationFilter filter, bool authenticated)
    {
        var identity = authenticated
            ? new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester")], authenticationType: "Test")
            : new ClaimsIdentity();

        var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            [filter]);
    }

    private static async Task<RedirectToActionResult?> RunAsync(
        IPermissionService permissions,
        string key = _key,
        bool authenticated = true)
    {
        var filter = new RequirePermFilter(permissions, key);
        var context = ContextFor(filter, authenticated);
        await filter.OnAuthorizationAsync(context);
        return context.Result as RedirectToActionResult;
    }

    [Fact]
    public async Task GrantedPermission_LetsTheRequestThrough()
    {
        var redirect = await RunAsync(new AuthzTestPermissionService(_key));

        Assert.Null(redirect);
    }

    [Fact]
    public async Task MissingPermission_RedirectsToAccessDenied()
    {
        var redirect = await RunAsync(new AuthzTestPermissionService("Sales.View"));

        Assert.NotNull(redirect);
        Assert.Equal("AccessDenied", redirect!.ActionName);
        Assert.Equal("Account", redirect.ControllerName);
    }

    /// <summary>
    /// الفرع الذي يفرّقه الفحص كلّه: مستخدم غير مسجَّل الدخول يُحوَّل إلى صفحة الدخول
    /// <b>قبل</b> النظر في أذونِه. لو انقلب الترتيب لتحوّل إلى «ممنوع الوصول»، وسجّل
    /// التطبيقُ مستخدمًا غير معروف في سجل الوصول كأنه مرفوض صلاحياته.
    /// </summary>
    [Fact]
    public async Task UnauthenticatedCaller_RedirectsToLogin_AndThePermissionIsNeverConsulted()
    {
        // البديل يمنح المفتاح من أجله، فلو استُدعي قبل فحص المصادقة لَما وُجد المفتاح
        // ومنحَ الطلب. فالنتيجة تثبت أن فحص المصادقة يسبق فحص الأذون.
        var redirect = await RunAsync(new AuthzTestPermissionService(_key), authenticated: false);

        Assert.NotNull(redirect);
        Assert.Equal("Login", redirect!.ActionName);
        Assert.Equal("Account", redirect.ControllerName);
    }

    /// <summary>
    /// المفتاح المعطى للـ ctor هو ما يُفحص. لو فحص المرشّح مفتاحًا ثابتًا أو أول مفتاح في
    /// القائمة لكانت كل اختبارات «مرفوض» أعلاه تمرّ لسببٍ خاطئ.
    /// </summary>
    [Fact]
    public async Task TheFilterChecksTheKeyItWasConstructedWith_NotAFixedOrEmptyKey()
    {
        var withOtherKeysOnly = new AuthzTestPermissionService("Sales.View", "Sales.Edit", "Sales.Export");

        var redirect = await RunAsync(withOtherKeysOnly);

        Assert.NotNull(redirect);
        Assert.Equal("AccessDenied", redirect!.ActionName);
    }

    /// <summary>
    /// مفتاح فارغ لا يطابق أيّ مفتاح ممنوح، فيجب أن يُرفض. هذا يثبت أن المرشّح لا يملك
    /// مخرجًا تلقائيًا حين يكون الإعداد ناقصًا - وهو ما لا يظهر في أيّ فرع آخر.
    /// </summary>
    [Fact]
    public async Task AnEmptyKey_IsRefused_NotSilentlyAllowed()
    {
        var redirect = await RunAsync(new AuthzTestPermissionService(_key), key: string.Empty);

        Assert.NotNull(redirect);
        Assert.Equal("AccessDenied", redirect!.ActionName);
    }

    /// <summary>
    /// يوجد في المستودع إجراءٌ يعلن مفتاحين اثنين معًا (<c>ReportsController.ExportSalesCsv</c>:
    /// <c>Reports.Export</c> و<code>Sales.View</code>) فالمعنى «و» لا «أو». هذا الفحص يثبت
    /// تركيب المرشّحات: امتلاك أحدهما لا يكفي. لولاه لظنّ القارئ أن السمتان تتساويتان،
    /// وأن تكرار <c>[RequirePerm]</c> على نفس الإجراء يوسّع الصلاحية بدل أن يضيّقها.
    /// </summary>
    [Fact]
    public async Task StackedPermissions_RequireEveryOneOfThem()
    {
        var onlyOneOfTwo = new AuthzTestPermissionService("Reports.Export");
        var first = new RequirePermFilter(onlyOneOfTwo, "Reports.Export");
        var second = new RequirePermFilter(onlyOneOfTwo, "Sales.View");

        var context = ContextFor(first, authenticated: true);
        await first.OnAuthorizationAsync(context);
        Assert.Null(context.Result);

        await second.OnAuthorizationAsync(context);
        Assert.NotNull(context.Result);
        Assert.Equal("AccessDenied", (context.Result as RedirectToActionResult)!.ActionName);
    }

    /// <summary>
    /// المدير في <see cref="PermissionService"/> يمرّ من كل مفتاح، فالفحص الذي يقرأ
    /// <c>IsAdmin</c> وحده دون <c>HasAsync</c> كان سيمنعه من الوصول. الاختبار يثبت أن
    /// المرشّح يمرّره، وأن البديل يحاكي ذلك بدل أن يتجاهله.
    /// </summary>
    [Fact]
    public async Task AnAdministrator_IsLetThrough_WithoutAnExplicitlyListedKey()
    {
        var administrator = new AuthzTestPermissionService { IsAdmin = true };
        Assert.Empty(await administrator.GetKeysAsync("any-user"));

        var redirect = await RunAsync(administrator);

        Assert.Null(redirect);
    }
}

/// <summary>
/// <see cref="RequirePermAttribute"/> هو ما يربط المفتاح بالمرشّح عبر <c>TypeFilter</c>. هذا
/// الفحص يثبت الوصلتين: المفتاح يُمرَّر، والترتيب يسبق بقية المرشّحات. الفحص على المرشّح
/// وحده لا يكفي: لو انقطع الربط لأصبحت <c>[RequirePerm]</c> زينةً بلا أثر، وتمرّ كل
/// اختبارات المرشّح لأنه يُبنى يدويًا.
/// </summary>
public sealed class RequirePermAttributeTests
{
    [Fact]
    public void TheAttribute_PassesTheKeyToTheFilter()
    {
        var attribute = new RequirePermAttribute("Sales.Create");

        Assert.Equal(typeof(RequirePermFilter), attribute.ImplementationType);
        Assert.Equal(new object[] { "Sales.Create" }, attribute.Arguments);
    }

    [Fact]
    public void TheAttribute_RunsBeforeOtherFilters()
    {
        // الترتيب ‎-10 يجعل فحص الأذون سابقًا لبقية المرشّحات: التحقّق من صحّة النموذج
        // لا معنى له لمستخدم لا يملك الإذن أصلًا.
        Assert.Equal(-10, new RequirePermAttribute("Sales.Create").Order);
    }

    [Fact]
    public void EveryDeclaredPermissionKey_ExistsInTheCatalog()
    {
        // الانعكاس على كل إجراء يعلن [RequirePerm]، ومقارنة كل مفتاح بالكتالوج. هذا لا
        // يكشف <b>حذف</b> السمة - فذلك شأن AuthorizationSweepTests - بل يمنع أن يُطلب مفتاح
        // غير موجود في الكتالوج، فيصير الإذن غير قابل للمنح لأي دور.
        var catalog = new HashSet<string>(
            PermissionCatalog.Modules.SelectMany(m =>
                PermissionCatalog.ActionsFor(m.Key).Select(a => PermissionCatalog.Key(m.Key, a))),
            StringComparer.Ordinal);

        Assert.NotEmpty(catalog);

        var declarations = new List<string>();
        foreach (var controller in typeof(Program).Assembly.GetTypes()
                     .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract))
        {
            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var keys = method.GetCustomAttributes<RequirePermAttribute>()
                    .Select(a => (string)a.Arguments![0]!)
                    .ToList();
                if (keys.Count == 0)
                {
                    continue;
                }

                // التكرار داخل الإجراء الواحد خطأ نسخ ولصق: لا يضيف شرطًا جديدًا، ويوهم
                // القارئ بأن هناك شرطين. أما أكثر من مفتاح <b>مختلف</b> فهو مقصود.
                Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());

                foreach (var key in keys)
                {
                    Assert.True(
                        catalog.Contains(key),
                        $"{controller.Name}.{method.Name} يطلب صلاحية غير موجودة في الكتالوج: {key}");
                    declarations.Add($"{controller.Name}.{method.Name}:{key}");
                }
            }
        }

        Assert.NotEmpty(declarations);
    }

    [Fact]
    public void AnActionReallyDoesStackTwoKeys_SoTheAndSemanticsIsNotHypothetical()
    {
        var method = typeof(ReportsController).GetMethod(nameof(ReportsController.ExportSalesCsv))!;
        var keys = method.GetCustomAttributes<RequirePermAttribute>()
            .Select(a => (string)a.Arguments![0]!)
            .ToList();

        Assert.Equal(["Reports.Export", "Sales.View"], keys);
    }
}
