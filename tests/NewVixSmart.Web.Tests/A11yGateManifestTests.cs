using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// Audit H-7: the axe gate enumerates routes by hand, so any controller added later is
/// silently never scanned and the gate keeps reporting PASS. This test compares every
/// view-rendering GET action against the manifest in e2e/a11y-gate.cjs and fails when
/// one is missing, so coverage cannot silently drift again.
/// </summary>
public sealed class A11yGateManifestTests
{
    private const string GatePath = "e2e/a11y-gate.cjs";

    /// <summary>
    /// Actions that must stay out of the manifest: they return a file, a redirect to the
    /// dashboard, or require interactive state that axe cannot judge. Keep this list short
    /// and justified - every entry is an acknowledged coverage gap.
    /// </summary>
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "Account.Login", "Account.Logout", "Account.AccessDenied",
        "Home.Error",
        // Reached only through UseStatusCodePagesWithReExecute, never by direct navigation,
        // and the gate already scans the resulting error body on every 404 route.
        "Home.StatusCode",
        "Tokens.CreateToken",
        // JSON endpoints used by the item/customer pickers - there is no DOM to scan.
        "Customers.GetCustomers", "Items.GetItems", "Items.GetItem", "Items.GetStock",
        "JournalEntries.GetJournalEntries", "Payments.GetPayments", "Suppliers.GetSuppliers"
    };

    /// <summary>Action names that return a file or a raw stream, so there is no DOM to scan.</summary>
    private static bool IsFileReturning(string action) =>
        action.EndsWith("Xlsx", StringComparison.OrdinalIgnoreCase)
        || action.EndsWith("Pdf", StringComparison.OrdinalIgnoreCase)
        || action.EndsWith("Csv", StringComparison.OrdinalIgnoreCase)
        || action.StartsWith("Print", StringComparison.OrdinalIgnoreCase)
        || action.StartsWith("Export", StringComparison.OrdinalIgnoreCase)
        || action is "Download" or "Template" or "Preview" or "PrintPreview";

    /// <summary>Route aliases: the gate uses the shorter path the site actually serves.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/Home"] = "/"
    };

    /// <summary>
    /// A manifest entry covers an action when it is exactly the route, or the route plus a
    /// parameter segment (<c>/Customers/Ledger/1</c>), or the route plus a query string.
    /// </summary>
    private static bool IsCovered(string route, IReadOnlySet<string> manifest) =>
        manifest.Contains(route)
        || manifest.Contains(route + "/")
        || manifest.Any(m => m.StartsWith(route + "/", StringComparison.OrdinalIgnoreCase)
                            || m.StartsWith(route + "?", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Controllers here declare <c>IActionResult</c> almost everywhere rather than
    /// <c>ViewResult</c>, so a bare IActionResult has to count as view-rendering.
    /// File/redirect results are handled by <see cref="Excluded"/>.
    /// </summary>
    private static bool ReturnsView(Type returnType)
    {
        if (returnType == typeof(void)) return false;
        if (typeof(ViewResult).IsAssignableFrom(returnType)) return true;
        if (typeof(IActionResult).IsAssignableFrom(returnType)) return true;
        if (returnType.IsGenericType)
        {
            var arg = returnType.GetGenericArguments()[0];
            if (arg == typeof(void)) return false;
            if (typeof(ViewResult).IsAssignableFrom(arg) || typeof(IActionResult).IsAssignableFrom(arg)) return true;
        }
        return false;
    }

    private static bool IsGetAction(MethodInfo method)
    {
        if (method.GetCustomAttribute<NonActionAttribute>(inherit: true) is not null) return false;
        if (method.GetCustomAttributes<HttpPostAttribute>(inherit: true).Any()) return false;

        var verb = method.GetCustomAttributes()
            .FirstOrDefault(a => a.GetType().Name.StartsWith("Http", StringComparison.Ordinal)
                                 && a.GetType().Name != "HttpPostAttribute");

        // No verb attribute at all means GET is the default convention.
        return verb is not null
            ? verb.GetType().Name.StartsWith("HttpGet", StringComparison.Ordinal)
            : method.GetCustomAttribute<HttpPostAttribute>(inherit: true) is null;
    }

    private static bool AnonymousOnly(Type controller) =>
        controller.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null;

    private static IEnumerable<string> ManifestRoutes()
    {
        var root = FindRepoRoot();
        var full = Path.Combine(root, GatePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"a11y gate not found at {full}");

        var text = File.ReadAllText(full);
        var block = System.Text.RegularExpressions.Regex.Match(
            text, "ROUTE_MANIFEST\\s*=\\s*\\[(?<body>[\\s\\S]*?)\\];");

        if (!block.Success)
            throw new InvalidOperationException("ROUTE_MANIFEST array not found in e2e/a11y-gate.cjs");

        return System.Text.RegularExpressions.Regex.Matches(block.Groups["body"].Value, "'(?<r>/[^']*)'")
            .Select(m => m.Groups["r"].Value)
            .ToList();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NewVixSmart.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void EveryViewRenderingGetAction_IsCoveredByTheAxeGate()
    {
        var manifest = ManifestRoutes().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var controllers = typeof(Controllers.HomeController).Assembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && t.Name.EndsWith("Controller", StringComparison.Ordinal))
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .Where(t => !AnonymousOnly(t));

        var missing = new List<string>();

        foreach (var controller in controllers)
        {
            var name = controller.Name[..^"Controller".Length];

            foreach (var action in controller
                         .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => IsGetAction(m) && ReturnsView(m.ReturnType)))
            {
                if (Excluded.Contains($"{name}.{action.Name}") || IsFileReturning(action.Name)) continue;

                var route = action.Name == "Index" ? $"/{name}" : $"/{name}/{action.Name}";
                if (Aliases.TryGetValue(route, out var alias)) route = alias;
                if (IsCovered(route, manifest)) { covered.Add(route); continue; }
                missing.Add(route);
            }
        }

        Assert.True(missing.Count == 0,
            "GET actions that render a view but are absent from " + GatePath +
            " (they are never axe-scanned):" + Environment.NewLine +
            string.Join(Environment.NewLine, missing.Select(m => "  " + m)));
    }

    [Fact]
    public void ManifestHasNoDeadEntries()
    {
        var manifest = ManifestRoutes().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(Controllers.HomeController).Assembly;

        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var controller in assembly.GetTypes()
                     .Where(t => t.IsPublic && t.Name.EndsWith("Controller", StringComparison.Ordinal)))
        {
            var name = controller.Name[..^"Controller".Length];
            var index = $"/{name}";
            live.Add(Aliases.TryGetValue(index, out var alias) ? alias : index);
            foreach (var action in controller
                         .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => !m.IsSpecialName))
            {
                live.Add($"/{name}/{action.Name}");
            }
        }

        // Routes with a query string or a trailing segment are validated by the gate at runtime.
        var dead = manifest
            .Where(r => !r.Contains('?') && !r.Split('/').Skip(2).Any())
            .Where(r => !live.Contains(r))
            .ToList();

        Assert.True(dead.Count == 0,
            "Manifest routes with no matching controller action (stale entries):" + Environment.NewLine +
            string.Join(Environment.NewLine, dead.Select(d => "  " + d)));
    }
}
