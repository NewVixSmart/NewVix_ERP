using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewVixSmart.Web.Extensions;
using Xunit;

namespace NewVixSmart.Web.Tests;

public sealed class AuthorizationSweepTests
{
    private static readonly HashSet<(string Controller, string Action)> AnonymousAllowed =
    [
        (nameof(NewVixSmart.Web.Controllers.AccountController), nameof(NewVixSmart.Web.Controllers.AccountController.Login)),
        (nameof(NewVixSmart.Web.Controllers.AccountController), nameof(NewVixSmart.Web.Controllers.AccountController.AccessDenied)),
        (nameof(NewVixSmart.Web.Controllers.HomeController), nameof(NewVixSmart.Web.Controllers.HomeController.Error)),
        ("TokensController", "CreateToken")
    ];

    private static IEnumerable<Type> Controllers() =>
        typeof(NewVixSmart.Web.Controllers.HomeController).Assembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && !t.IsGenericTypeDefinition
                        && t.Name.EndsWith("Controller", StringComparison.Ordinal)
                        && typeof(ControllerBase).IsAssignableFrom(t));

    private static bool ReturnsActionResult(Type t)
    {
        if (t == typeof(void)) return false;
        if (typeof(IActionResult).IsAssignableFrom(t)) return true;
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(Task<>) || def == typeof(ValueTask<>))
                return typeof(IActionResult).IsAssignableFrom(t.GetGenericArguments()[0]);
        }
        return false;
    }

    private static IEnumerable<MethodInfo> ActionMethods(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>(inherit: true) is null)
            .Where(m => ReturnsActionResult(m.ReturnType));

    [Fact]
    public void EveryControllerAction_IsAuthorizedOrExplicitlyAnonymous()
    {
        var offenders = new List<string>();

        foreach (var controller in Controllers())
        {
            var classAuthorize = controller.GetCustomAttribute<AuthorizeAttribute>(inherit: true) is not null;
            var classAnonymous = controller.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null;

            foreach (var action in ActionMethods(controller))
            {
                var methodAuthorize = action.GetCustomAttribute<AuthorizeAttribute>(inherit: true) is not null;
                var methodAnonymous = action.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null;
                var anonymous = classAnonymous || methodAnonymous;

                if (anonymous)
                {
                    if (!AnonymousAllowed.Contains((controller.Name, action.Name)))
                        offenders.Add($"UNEXPECTED ANONYMOUS: {controller.Name}.{action.Name}");
                    continue;
                }

                if (!classAuthorize && !methodAuthorize && !AnonymousAllowed.Contains((controller.Name, action.Name)))
                    offenders.Add($"UNAUTHORIZED ACTION: {controller.Name}.{action.Name}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Actions without authorization:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void RequirePermActions_AreAlsoCoveredByAuthorize()
    {
        var offenders = new List<string>();

        foreach (var controller in Controllers())
        {
            var classAuthorize = controller.GetCustomAttribute<AuthorizeAttribute>(inherit: true) is not null;
            foreach (var action in ActionMethods(controller))
            {
                if (action.GetCustomAttribute<RequirePermAttribute>(inherit: true) is null) continue;
                var methodAuthorize = action.GetCustomAttribute<AuthorizeAttribute>(inherit: true) is not null;
                if (!classAuthorize && !methodAuthorize)
                    offenders.Add($"{controller.Name}.{action.Name}");
            }
        }

        Assert.True(offenders.Count == 0,
            "RequirePerm actions lacking [Authorize]:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void Controllers_ExistAndAreDiscoverable()
    {
        var all = Controllers().ToList();
        Assert.True(all.Count >= 30, $"Only found {all.Count} controllers");
        Assert.Contains(all, c => c.FullName == "NewVixSmart.Web.Controllers.HomeController");
        Assert.Contains(all, c => c.FullName == "NewVixSmart.Web.Api.TokensController");
    }

    private static string? PermKeyOf(MethodInfo action) =>
        action.GetCustomAttribute<RequirePermAttribute>(inherit: true)?.Arguments?.FirstOrDefault() as string;

    private static IReadOnlyList<string> RequiredPerms(Type controller) =>
        ActionMethods(controller)
            .Select(PermKeyOf)
            .Where(k => k is not null)
            .Select(k => k!)
            .ToList();

    [Fact]
    public void NewFlowControllers_GuardEveryActionWithTheExpectedPermission()
    {
        var expected = new Dictionary<Type, string[]>
        {
            [typeof(NewVixSmart.Web.Controllers.StockReservationsController)] =
                ["StockReservations.View", "StockReservations.Create", "StockReservations.Release"],
            [typeof(NewVixSmart.Web.Controllers.DeliveryIssuesController)] =
                ["DeliveryIssues.View", "DeliveryIssues.Create", "DeliveryIssues.Issue"]
        };

        foreach (var (controller, perms) in expected)
        {
            var guarded = RequiredPerms(controller).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
            var wanted = perms.OrderBy(k => k, StringComparer.Ordinal).ToList();

            Assert.Equal(wanted, guarded);
        }
    }

    [Fact]
    public void WarehouseRole_CanReserveAndDeliver_WithoutNeedingSalesCreate()
    {
        var warehouse = NewVixSmart.Web.Services.PermissionDefaults.DefaultsFor("Warehouse");

        foreach (var perm in new[]
                 {
                     "StockReservations.View", "StockReservations.Create", "StockReservations.Release",
                     "DeliveryIssues.View", "DeliveryIssues.Create", "DeliveryIssues.Issue",
                     "DeliveryOrders.View", "DeliveryOrders.Create", "DeliveryOrders.Deliver"
                 })
        {
            Assert.Contains(perm, warehouse);
        }

        var accountant = NewVixSmart.Web.Services.PermissionDefaults.DefaultsFor("Accountant");
        foreach (var perm in new[]
                 {
                     "StockReservations.View", "StockReservations.Create", "StockReservations.Release",
                     "DeliveryIssues.View", "DeliveryIssues.Create", "DeliveryIssues.Issue"
                 })
        {
            Assert.Contains(perm, accountant);
        }
    }

    [Fact]
    public void ReservationAndIssuePermissions_ExistInTheCatalog()
    {
        var stockReservationActions = NewVixSmart.Web.Services.PermissionCatalog.ActionsFor("StockReservations");
        var deliveryIssueActions = NewVixSmart.Web.Services.PermissionCatalog.ActionsFor("DeliveryIssues");

        Assert.Equal(["View", "Create", "Release"], stockReservationActions);
        Assert.Equal(["View", "Create", "Issue"], deliveryIssueActions);

        Assert.Contains(NewVixSmart.Web.Services.PermissionCatalog.Modules,
            m => m.Key == "StockReservations" && m.Controller == "StockReservations");
        Assert.Contains(NewVixSmart.Web.Services.PermissionCatalog.Modules,
            m => m.Key == "DeliveryIssues" && m.Controller == "DeliveryIssues");
    }
}