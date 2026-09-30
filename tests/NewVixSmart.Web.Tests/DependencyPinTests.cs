using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// A dependency guard, not a dependency bump.
///
/// <para>
/// <c>aspire/Vix.AppHost</c> carries a direct <c>MessagePack</c> reference that no source file
/// uses, because the real requirement is transitive: <c>Aspire.Hosting.* 13.0.0</c> ->
/// <c>StreamJsonRpc 2.22.23</c> declares <c>MessagePack &gt;= 2.5.192</c>, and 2.5.192 carries 11
/// advisories (9 moderate, 2 HIGH). Because <c>TreatWarningsAsErrors</c> is repo-wide, restoring
/// the un-pinned tree turns the NU1902/NU1903 audit warnings into hard build errors. A reviewer who
/// reads "no code references this package" and deletes the line therefore does not tidy the build,
/// they break it. This test is the counterweight to that reading.
/// </para>
///
/// <para>
/// It asserts two things that must stay true and that nothing else in the suite can observe:
/// the AppHost pins <c>MessagePack</c> to at least the first patched 2.x version, and every direct
/// <c>PackageReference</c> in every project is matched by some dependabot group so that no
/// reference is silently unmaintained.
/// </para>
///
/// <para>
/// The reachability caveat is worth recording because it bounds the real risk: <c>MessagePack</c>
/// is absent from <c>src/NewVixSmart.Web</c>'s dependency graph and from the published web output,
/// and <c>.dockerignore</c> excludes the AppHost, so the pinned serializer never ships. It serves
/// the local Aspire/DCP dev harness, and the two HIGH advisories live in paths that harness does not
/// exercise (LZ4 decompression, and unbounded reader recursion on attacker-shaped input). The pin
/// is still mandatory because of the build-time audit gate, not because this is a hot production
/// path.
/// </para>
/// </summary>
public sealed class DependencyPinTests
{
    /// <summary>The AppHost project that owns the security pin, relative to the repo root.</summary>
    private const string AppHostProject = "aspire/Vix.AppHost/Vix.AppHost.csproj";

    /// <summary>
    /// The first patched MessagePack 2.x version. Every advisory that reaches the 2.x line has
    /// "&lt; 2.5.301" as its vulnerable ceiling, so this is a floor, not a preference: any pin below
    /// it reintroduces at least one advisory, and any pin above it adds no security value.
    /// </summary>
    private const string MessagePackPatchedFloor = "2.5.301";

    /// <summary>
    /// Every project in the solution, paired with the dependabot directory that must cover it.
    /// Listing them here (rather than globbing the tree) means a brand-new project that someone
    /// forgets to register with dependabot is caught by the coverage test, not discovered months
    /// later when it has a CVE.
    /// </summary>
    private static readonly (string Project, string DependabotDirectory)[] Projects =
    [
        ("src/NewVixSmart.Web/NewVixSmart.Web.csproj", "/src/NewVixSmart.Web"),
        ("tests/NewVixSmart.Web.Tests/NewVixSmart.Web.Tests.csproj", "/tests/NewVixSmart.Web.Tests"),
        ("aspire/Vix.AppHost/Vix.AppHost.csproj", "/aspire/Vix.AppHost"),
        ("aspire/Vix.ServiceDefaults/Vix.ServiceDefaults.csproj", "/aspire/Vix.ServiceDefaults"),
    ];

    /// <summary>
    /// Walks up from the test binary until it finds the solution file, which is the only stable
    /// marker of the repository root. Falls back to the binary's own directory in a trimmed
    /// deployment, so a missing root produces a clear assertion failure instead of a null
    /// reference somewhere deeper.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NewVixSmart.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    /// <summary>
    /// The <c>PackageReference</c> ids a project declares directly (not transitively), which is
    /// the set dependabot actually offers to update. Private assets such as the EF Core design
    /// package and the xunit runner are still direct references, so they are included here; they
    /// remain the project's responsibility to keep current.
    /// </summary>
    private static IReadOnlyList<string> DirectPackageReferences(string projectRelativePath)
    {
        var path = Path.Combine(FindRepoRoot(), projectRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), "Project file not found at " + path);

        return XDocument.Load(path)
            .Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();
    }

    /// <summary>
    /// A dependabot pattern is a glob, not a regex: <c>*</c> stands for "any characters" and every
    /// other character is literal. Anchoring the whole name is what keeps
    /// <c>Microsoft.AspNetCore.*</c> from quietly swallowing an unrelated
    /// <c>Microsoft.AspNetCoreX</c> package.
    /// </summary>
    private static bool GlobMatches(string pattern, string name)
    {
        var segments = pattern.Split('*').Select(Regex.Escape);
        return Regex.IsMatch(
            name,
            "^" + string.Join(".*", segments) + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Maps each nuget dependabot directory to the union of the glob patterns declared under its
    /// <c>groups:</c> block. This is a deliberately small line scanner rather than a full YAML
    /// parse: it reads one file we own, in one fixed shape, and pulling a YAML dependency into the
    /// test project to audit a dependency file would be the tail wagging the dog. Anything outside a
    /// <c>groups:</c> block is ignored, so <c>commit-message</c> and <c>schedule</c> keys can never
    /// be mistaken for a pattern.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> NuGetGroupPatterns()
    {
        var path = Path.Combine(FindRepoRoot(), ".github", "dependabot.yml");
        Assert.True(File.Exists(path), "dependabot.yml not found at " + path);

        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var isNuGet = false;
        var inGroups = false;
        var groupsIndent = 0;
        string? directory = null;

        foreach (var raw in File.ReadAllLines(path))
        {
            var indent = raw.Length - raw.TrimStart().Length;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("- package-ecosystem:", StringComparison.Ordinal))
            {
                isNuGet = line.EndsWith("nuget", StringComparison.Ordinal);
                inGroups = false;
                directory = null;
                continue;
            }

            if (!isNuGet)
            {
                continue;
            }

            if (line.StartsWith("directory:", StringComparison.Ordinal))
            {
                directory = line["directory:".Length..].Trim().Trim('"');
                continue;
            }

            if (line.Equals("groups:", StringComparison.Ordinal))
            {
                inGroups = true;
                groupsIndent = indent;
                continue;
            }

            if (inGroups && line.StartsWith("- \"", StringComparison.Ordinal))
            {
                var pattern = line[2..].Trim().Trim('"');
                if (directory is not null)
                {
                    if (!map.TryGetValue(directory, out var patterns))
                    {
                        patterns = [];
                        map[directory] = patterns;
                    }

                    patterns.Add(pattern);
                }

                continue;
            }

            // A group name or a "patterns:" key sits deeper than "groups:" itself and keeps the
            // section open; only a key at the same level or shallower (the closing
            // "commit-message:" block) ends it. Indentation, not the trailing colon, is what
            // tells the two apart.
            if (inGroups && indent <= groupsIndent && line.EndsWith(":", StringComparison.Ordinal) && !line.StartsWith('-'))
            {
                inGroups = false;
            }
        }

        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The AppHost must keep an explicit, directly-referenced <c>MessagePack</c> override. Without
    /// it the restore resolves 2.5.192 and the audit gate fails the build; this is the assertion
    /// that makes "it's unused, delete it" a test failure instead of an outage.
    /// </summary>
    [Fact]
    public void AppHost_PinsMessagePack_Explicitly()
    {
        var id = DirectPackageReferences(AppHostProject);
        Assert.Contains("MessagePack", id);
    }

    /// <summary>
    /// The pin must sit at or above 2.5.301. A downgrade to the transitive floor reintroduces the
    /// 11 advisories; a version below the floor is the exact regression this suite exists to catch.
    /// </summary>
    [Fact]
    public void AppHost_PinsMessagePack_AtOrAboveFirstPatchedVersion()
    {
        var path = Path.Combine(FindRepoRoot(), AppHostProject.Replace('/', Path.DirectorySeparatorChar));
        var version = XDocument.Load(path)
            .Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Where(e => string.Equals((string?)e.Attribute("Include"), "MessagePack", StringComparison.OrdinalIgnoreCase))
            .Select(e => (string?)e.Attribute("Version"))
            .SingleOrDefault();

        Assert.False(string.IsNullOrWhiteSpace(version), "AppHost must pin MessagePack to an explicit version.");
        Assert.True(
            Version.TryParse(version, out var parsed) && parsed >= Version.Parse(MessagePackPatchedFloor),
            $"AppHost pins MessagePack {version}, which is below the first patched 2.x version "
            + $"({MessagePackPatchedFloor}). See docs/audit-round18-findings.md (R18-8).");
    }

    /// <summary>
    /// No project may hold a direct reference that dependabot does not track. A reference outside
    /// every group would otherwise be proposed as its own one-off PR, or - worse - go stale
    /// unnoticed because nothing reminds anyone it exists. The security pin in particular must stay
    /// inside a group, since that group is what keeps it current.
    /// </summary>
    [Fact]
    public void EveryDirectDependency_IsCoveredByADependabotGroup()
    {
        var patterns = NuGetGroupPatterns();
        var uncovered = new List<string>();

        foreach (var (project, directory) in Projects)
        {
            if (!patterns.TryGetValue(directory, out var groups) || groups.Count == 0)
            {
                uncovered.Add($"{project}: no dependabot group registered for {directory}");
                continue;
            }

            foreach (var reference in DirectPackageReferences(project))
            {
                if (!groups.Any(g => GlobMatches(g, reference)))
                {
                    uncovered.Add($"{project}: {reference} is not covered by any group in {directory}");
                }
            }
        }

        Assert.True(
            uncovered.Count == 0,
            "Direct dependencies with no dependabot coverage:" + Environment.NewLine + string.Join(Environment.NewLine, uncovered));
    }
}
