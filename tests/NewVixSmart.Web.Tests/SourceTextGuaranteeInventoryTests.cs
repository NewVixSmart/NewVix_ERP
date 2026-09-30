using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// An inventory, not a guarantee. Reading production source as text proves far less than
/// running it: a rename, a reworded comment, an equivalent-but-different formulation, or a
/// change three lines away all keep a grep green while the behaviour is broken or gone.
///
/// This file exists so the size of that gap is a number in the suite instead of an impression.
/// It fails when a test class starts reading a project file as text without saying what kind of
/// file it is, which forces the author to state whether the guarantee is behavioural or textual.
/// Adding an entry is allowed; adding a reader by accident is not.
/// </summary>
public sealed class SourceTextGuaranteeInventoryTests
{
    private const string CSharp = "C#";
    private const string Config = "config";
    private const string Razor = "Razor";
    private const string Tooling = "tooling";

    /// <summary>
    /// What every test class in this project reads as text, and what it reads. Two facts fall out
    /// of this table, and they are the honest answer to "how much of the suite proves anything by
    /// reading the file rather than running it":
    ///
    ///   9 classes rest at least one production guarantee on production source or configuration
    ///   being read as text - 8 on C#, 1 on configuration. Those guarantees are real but weak:
    ///   they break on a harmless refactor and survive a behaviour change. They are the honest
    ///   remainder after the behavioural work, not an oversight.
    ///
    ///   2 further classes read Razor markup (a view cannot be executed without a browser; the
    ///   browser-level proof is the Playwright + axe run in the docker-image CI job), and 1 reads
    ///   the a11y gate's own JavaScript manifest, which it cross-checks against live controller
    ///   reflection and is therefore not a production guarantee at all. A further class reads
    ///   the dependency graph, the same way.
    /// </summary>
    private static readonly Dictionary<string, string> TextReaders = new(StringComparer.Ordinal)
    {
        ["A11yGateManifestTests"] = Tooling,                 // e2e/a11y-gate.cjs, cross-checked by reflection
        ["AuthzExportCenterDatasetTests"] = CSharp,         // Services/ExportCenterService.cs (row cap)
        ["AuthzMenuLinkTests"] = Razor,                     // Views/Shared/_Layout.cshtml
        ["AuthzPublicIdAndItemBindingTests"] = CSharp,      // ten Controllers/*.cs
        ["DependencyPinTests"] = Tooling,                   // *.csproj + .github/dependabot.yml
        ["ProductionConfigGateTests"] = Config,             // appsettings*.json
        ["SecurityAccessTokenLifetimeTests"] = CSharp,      // Api/TokensController.cs
        ["SecurityImportUploadLimitTests"] = CSharp,        // ImportCenterController.cs + Service.cs
        ["SecurityLogoutAndRedirectTests"] = CSharp,        // Controllers/AccountController.cs
        ["SecurityResponseHeadersTests"] = CSharp,          // Program.cs
        ["SecurityViewCspNonceTests"] = CSharp,             // Program.cs, plus Razor views
        ["ViewLogicSweepTests"] = CSharp,                   // controllers and views
    };

    [Fact]
    public void EveryTestClassThatReadsAProjectFileAsText_IsInventoriedAbove_WithItsKind()
    {
        var found = FindTextReadingClasses();

        Assert.Equal(
            TextReaders.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList(),
            found.Select(entry => entry.ClassName).OrderBy(name => name, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void TheCSharpAndConfigCountStaysNine_SoARemovalCannotSilentlyNarrowTheKnownGap()
    {
        // The number reported in the summary of this work. Pinned so that deleting a textual
        // guarantee is a visible act: either the behaviour became testable - in which case the
        // class should stop reading source and this number drops on purpose - or the guarantee
        // was dropped, which should not pass unnoticed.
        var production = TextReaders
            .Where(entry => entry.Value is CSharp or Config)
            .Select(entry => entry.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(9, production.Count);
        Assert.Equal(9, FindTextReadingClasses().Count(entry => entry.Kind is CSharp or Config));
    }

    [Fact]
    public void TheInventoryIsNotEmpty_SoTheChecksAboveCannotPassVacuously()
    {
        Assert.NotEmpty(TextReaders);
        Assert.All(TextReaders, entry => Assert.EndsWith("Tests", entry.Key, StringComparison.Ordinal));
        Assert.All(TextReaders.Values, kind => Assert.Contains(kind, new[] { CSharp, Config, Razor, Tooling }));
    }

    private static List<(string ClassName, string Kind)> FindTextReadingClasses()
    {
        var markers = new[]
        {
            "TestPaths.WebProjectFile",
            "WebProjectFile(",
            "WebProjectDirectory(",
            "\"src\", \"NewVixSmart.Web\"",
            "Path.Combine(dir",
            "FindRepoRoot()",
        };

        return
        [
            .. Directory
                .EnumerateFiles(TestProjectDirectory(), "*.cs", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => name is not null)
                .Select(name => name!)
                .Where(name => !string.Equals(name, nameof(SourceTextGuaranteeInventoryTests), StringComparison.Ordinal))
                .Select(name => (ClassName: name, Source: File.ReadAllText(Path.Combine(TestProjectDirectory(), name + ".cs"))))
                .Where(entry => markers.Any(marker => entry.Source.Contains(marker, StringComparison.Ordinal)))
                .Select(entry => (entry.ClassName, TextReaders.TryGetValue(entry.ClassName, out var kind) ? kind : "UNDECLARED"))
                .ToList(),
        ];
    }

    private static string TestProjectDirectory()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "NewVixSmart.Web.Tests");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            $"تعذر العثور على tests\\NewVixSmart.Web.Tests بالبحث الصاعد من {AppContext.BaseDirectory}.");
    }
}
