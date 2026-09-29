using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewVixSmart.Web.Controllers;
using NewVixSmart.Web.Extensions;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// The menu showed screens the viewer was not allowed to open. Four gates were wrong: the home
/// dashboard had no gate at all, a user holding only <c>AuditLedger.View</c> or <c>Aging.View</c>
/// never saw the reports group that contains their link, <c>Stock/LowStock</c> was offered to anyone
/// with the broader <c>Stock.View</c>, and the stock group itself omitted <c>LowStock.View</c> so a
/// LowStock-only user saw an empty group.
///
/// A menu link must never be a wider door than the action it opens: whoever can see the link has to
/// hold every <c>[RequirePerm]</c> key the action demands. The gates around a link are ANDed, so a
/// link ends up exactly as wide as its own gate is narrow.
/// </summary>
public sealed class AuthzMenuLinkTests
{
    private static readonly Regex AnchorPattern = new(
        "<a\\b[^>]*asp-controller=\"(?<controller>[A-Za-z0-9_]+)\"[^>]*asp-action=\"(?<action>[A-Za-z0-9_]+)\"",
        RegexOptions.Compiled);

    private static readonly Regex GatePattern = new(
        "@if\\s*\\(\\s*await\\s+Perm\\.(?<call>HasAsync|HasAnyAsync)\\s*\\((?<args>[^)]*)\\)",
        RegexOptions.Compiled);

    private const string AdminMarker = "<admin>";

    /// <summary>
    /// A Razor <c>@if</c> block: the brace depth it was opened at, whether that block has been seen
    /// to start, and the OR-set of keys its condition admits.
    /// </summary>
    private sealed class Gate(int depth, string keys)
    {
        public int Depth { get; } = depth;

        public string Keys { get; } = keys;

        public bool Opened { get; set; }
    }

    /// <summary>
    /// One <c>asp-controller</c>/<c>asp-action</c> pair plus the permission gates that enclose it,
    /// innermost first. A Razor <c>@if</c> using <c>HasAnyAsync</c> is kept as one <c>a|b|c</c> entry
    /// because it is an OR, and a role check is kept as <c>&lt;admin&gt;</c>.
    /// </summary>
    private sealed record MenuLink(string Controller, string Action, IReadOnlyList<string> Gates)
    {
        /// <summary>The gate of the nav group that contains the link.</summary>
        public string OutermostGate => Gates.Count > 0 ? Gates[^1] : string.Empty;

        /// <summary>True when the link's own gate admits nobody but an administrator.</summary>
        public bool UnderAdminGate =>
            Gates.Count > 0
            && Gates[0].Split('|', StringSplitOptions.RemoveEmptyEntries).All(k => k == AdminMarker);
    }

    private static IReadOnlySet<string> Grants(params string[] keys) =>
        new HashSet<string>(keys, StringComparer.Ordinal);

    /// <summary>A subject reaches the link only when it satisfies every gate that encloses it.</summary>
    private static bool IsVisibleTo(MenuLink link, IReadOnlySet<string> subject) =>
        link.Gates.All(g => g.Split('|', StringSplitOptions.RemoveEmptyEntries).Any(subject.Contains));

    private static string ReadLayout()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NewVixSmart.slnx")))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, "Could not locate the repository root from " + AppContext.BaseDirectory);
        var path = Path.Combine(dir!.FullName, "src", "NewVixSmart.Web", "Views", "Shared", "_Layout.cshtml");
        Assert.True(File.Exists(path), "Layout not found at " + path);
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Walks the layout line by line, keeping the stack of open <c>@if</c> blocks so each link is
    /// checked against every gate that encloses it. The opening brace of a Razor <c>@if</c> sits on
    /// the line after the condition, so a gate remembers the depth it was opened at and is only
    /// popped once the depth has risen and come back down to that value.
    /// </summary>
    private static IReadOnlyList<MenuLink> ParseMenu()
    {
        var links = new List<MenuLink>();
        var gates = new Stack<Gate>();
        var depth = 0;

        foreach (var rawLine in ReadLayout().Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            var gate = GatePattern.Match(line);
            var admin = line.Contains("User.IsInRole(\"Admin\")", StringComparison.Ordinal);
            if (gate.Success || admin)
            {
                List<string> keys = gate.Success
                    ? Regex.Matches(gate.Groups["args"].Value, "\"(?<key>[A-Za-z0-9_.]+)\"")
                        .Select(m => m.Groups["key"].Value)
                        .ToList()
                    : [];

                // `HasAnyAsync(...) || User.IsInRole("Admin")` is one OR, so the role check widens the
                // same gate rather than becoming a second gate that also has to pass.
                if (admin)
                {
                    keys.Add(AdminMarker);
                }

                gates.Push(new Gate(depth, keys.Count == 0 ? "<<unparsed>>" : string.Join('|', keys)));
            }

            var anchor = AnchorPattern.Match(line);
            if (anchor.Success)
            {
                // Stack enumerates top-down, so this is innermost gate first.
                links.Add(new MenuLink(
                    anchor.Groups["controller"].Value,
                    anchor.Groups["action"].Value,
                    gates.Select(g => g.Keys).ToList()));
            }

            var opened = depth + Regex.Matches(line, "\\{").Count;
            var closed = opened - Regex.Matches(line, "\\}").Count;
            foreach (var open in gates.Where(g => !g.Opened && closed > g.Depth))
            {
                open.Opened = true;
            }

            depth = closed;
            while (gates.Count > 0 && gates.Peek().Opened && depth <= gates.Peek().Depth)
            {
                gates.Pop();
            }
        }

        return links;
    }

    private static IEnumerable<Type> Controllers() =>
        typeof(HomeController).Assembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
            .ToList();

    private static Type ControllerType(string controllerName) =>
        Controllers().First(c => c.Name == controllerName + "Controller");

    private static MethodInfo? ResolveAction(string controllerName, string actionName) =>
        Controllers()
            .Where(c => string.Equals(c.Name, controllerName + "Controller", StringComparison.Ordinal))
            .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .FirstOrDefault(m => m.Name == actionName);

    private static IReadOnlyList<string> RequiredKeys(MethodInfo action, Type controller) =>
        controller.GetCustomAttributes<RequirePermAttribute>(inherit: true)
            .Concat(action.GetCustomAttributes<RequirePermAttribute>(inherit: true))
            .Select(a => a.Arguments?.FirstOrDefault() as string)
            .Where(k => k is not null)
            .Select(k => k!)
            .Distinct()
            .ToList();

    /// <summary>True when the action is restricted by role rather than by a permission key.</summary>
    private static bool IsRoleOnly(MethodInfo action, Type controller) =>
        action.GetCustomAttribute<AuthorizeAttribute>(inherit: true) is { Roles: not null and not "" }
        || controller.GetCustomAttribute<AuthorizeAttribute>(inherit: true) is { Roles: not null and not "" };

    /// <summary>
    /// The smallest grant sets that reveal a link: a subject has to satisfy every enclosing gate, so
    /// one key taken from each gate is enough, and any such combination is a real holder. Enumerating
    /// them is enough to prove the gate is not wider than the action, because a subject holding more
    /// than one of the keys of a gate is a superset of one of these minimal holders.
    /// </summary>
    private static IEnumerable<IReadOnlySet<string>> MinimalSubjects(MenuLink link)
    {
        IEnumerable<IReadOnlySet<string>> subjects = [Grants()];

        foreach (var gate in link.Gates)
        {
            subjects = subjects
                .SelectMany(subject => gate
                    .Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(key => (IReadOnlySet<string>)new HashSet<string>(subject) { key }))
                .ToList();
        }

        return subjects;
    }

    [Fact]
    public void TheLayoutMenu_ParsesLinksWithGates()
    {
        var links = ParseMenu();
        Assert.True(links.Count >= 30, $"Only found {links.Count} menu links.");

        // A gate we failed to parse would silently disable the check, so fail loudly on one.
        Assert.DoesNotContain(links, l => l.Gates.Contains("<<unparsed>>"));
        Assert.Contains(links, l => l.Gates.Count >= 2); // group gate plus a link's own gate
    }

    [Fact]
    public void EveryMenuLink_IsNoWiderThanTheActionItOpens()
    {
        var offenders = new List<string>();

        foreach (var link in ParseMenu())
        {
            var action = ResolveAction(link.Controller, link.Action);
            if (action is null)
            {
                continue; // external link or a controller outside this assembly
            }

            var controllerType = ControllerType(link.Controller);
            var required = RequiredKeys(action, controllerType);
            if (required.Count == 0)
            {
                // Role-restricted actions (Users, Backup) are covered by an admin-only gate.
                if (IsRoleOnly(action, controllerType) && !link.UnderAdminGate)
                {
                    offenders.Add($"{link.Controller}/{link.Action} is role-restricted but not behind an admin gate");
                }
                continue;
            }

            if (IsRoleOnly(action, controllerType))
            {
                continue; // role gate already covers the keys
            }

            foreach (var subject in MinimalSubjects(link))
            {
                var missing = required.Where(k => !subject.Contains(k)).OrderBy(k => k).ToList();
                if (missing.Count == 0)
                {
                    continue;
                }

                offenders.Add(
                    $"{link.Controller}/{link.Action} is reachable holding {{{string.Join(", ", subject)}}} " +
                    $"but the action requires {string.Join(" + ", missing)}; gates: [{string.Join("] [", link.Gates)}]");
                break;
            }
        }

        Assert.True(offenders.Count == 0,
            "Menu links that are wider than the action they open:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void HomeDashboard_IsGatedOnReportsDashboard()
    {
        var home = ParseMenu().Single(l => l.Controller == "Home" && l.Action == "Index");

        Assert.Equal(["Reports.Dashboard"], home.Gates);

        var action = ResolveAction("Home", "Index");
        Assert.NotNull(action);
        Assert.Equal(["Reports.Dashboard"], RequiredKeys(action!, ControllerType("Home")));
    }

    [Fact]
    public void LowStockLink_IsNotReachableByTheBroaderStockViewGrant()
    {
        var link = ParseMenu().Single(l => l.Controller == "Stock" && l.Action == "LowStock");

        // Stock.View opens the stock group, so only the link's own gate can keep the LowStock page
        // away from a holder of that broader key.
        Assert.True(IsVisibleTo(link, Grants("LowStock.View")));
        Assert.False(IsVisibleTo(link, Grants("Stock.View")));

        var action = ResolveAction("Stock", "LowStock");
        Assert.NotNull(action);
        Assert.Equal(["LowStock.View"], RequiredKeys(action!, ControllerType("Stock")));
    }

    [Fact]
    public void ReportsGroup_OpensForStandaloneAuditLedgerAndAgingHolders()
    {
        var audit = ParseMenu().Single(l => l.Controller == "Reports" && l.Action == "AuditLedger");

        // A holder of nothing but AuditLedger.View or Aging.View must still get the group that holds
        // their link, otherwise the only way into those pages is to type the URL.
        var group = audit.OutermostGate;
        Assert.Contains("AuditLedger.View", group);
        Assert.Contains("Aging.View", group);
        Assert.Contains("Reports.View", group);
        Assert.Contains("Reports.Dashboard", group);

        Assert.True(IsVisibleTo(audit, Grants("AuditLedger.View")));
        Assert.True(IsVisibleTo(ParseMenu().Single(l => l.Controller == "Reports" && l.Action == "Aging"),
            Grants("Aging.View")));
    }

    [Fact]
    public void ReportsListLinks_AreNotOfferedToAuditAgingOrDashboardOnlyHolders()
    {
        var links = ParseMenu()
            .Where(l => l.Controller == "Reports" && l.Action is "Index" or "Sales" or "Purchases" or "Payments" or "CashFlow")
            .ToList();
        Assert.Equal(5, links.Count);

        foreach (var link in links)
        {
            Assert.True(IsVisibleTo(link, Grants("Reports.View")));
            Assert.False(IsVisibleTo(link, Grants("AuditLedger.View")),
                $"{link.Action} is offered to an audit-ledger-only holder but needs Reports.View.");
            Assert.False(IsVisibleTo(link, Grants("Aging.View")),
                $"{link.Action} is offered to an aging-only holder but needs Reports.View.");
            Assert.False(IsVisibleTo(link, Grants("Reports.Dashboard")),
                $"{link.Action} is offered to a dashboard-only holder but needs Reports.View.");
        }
    }

    [Fact]
    public void AgingLink_IsNotOfferedToASubjectWithoutAgingView()
    {
        var link = ParseMenu().Single(l => l.Controller == "Reports" && l.Action == "Aging");

        Assert.Equal("Aging.View", link.Gates[0]);
        Assert.True(IsVisibleTo(link, Grants("Aging.View")));
        Assert.False(IsVisibleTo(link, Grants("Reports.View")));
        Assert.False(IsVisibleTo(link, Grants("Reports.Dashboard")));
        Assert.False(IsVisibleTo(link, Grants("AuditLedger.View")));

        var action = ResolveAction("Reports", "Aging");
        Assert.NotNull(action);
        Assert.Equal(["Aging.View"], RequiredKeys(action!, ControllerType("Reports")));
    }

    [Fact]
    public void StockGroup_OpensForALowStockOnlyHolder()
    {
        // A viewer with nothing but LowStock.View must still see the group that contains their link.
        var lowStock = ParseMenu().Single(l => l.Controller == "Stock" && l.Action == "LowStock");

        Assert.Contains("LowStock.View", lowStock.OutermostGate);
        Assert.True(IsVisibleTo(lowStock, Grants("LowStock.View")));
    }
}
