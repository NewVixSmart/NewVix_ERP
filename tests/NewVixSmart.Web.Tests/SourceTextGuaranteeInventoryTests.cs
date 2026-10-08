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
    private const string _cSharp = "C#";
    private const string _config = "config";
    private const string _razor = "Razor";
    private const string _tooling = "tooling";

    /// <summary>
    /// What every test class in this project reads as text, and what it reads. Two facts fall out
    /// of this table, and they are the honest answer to "how much of the suite proves anything by
    /// reading the file rather than running it":
    ///
    ///   6 classes rest at least one production guarantee on production source or configuration
    ///   being read as text - 5 on C#, 1 on configuration. Those guarantees are real but weak:
    ///   they break on a harmless refactor and survive a behaviour change. They are the honest
    ///   remainder after the behavioural work, not an oversight. The three security classes that
    ///   used to be counted here - SecurityAccessTokenLifetimeTests, SecurityImportUploadLimitTests
    ///   and SecurityLogoutAndRedirectTests - stopped reading source when their greps were replaced
    ///   by requests to a real host, which is why the number fell from 9 to 6.
    ///
    ///   2 further classes read Razor markup (a view cannot be executed without a browser; the
    ///   browser-level proof is the Playwright + axe run in the docker-image CI job), and 4 read
    ///   tooling rather than production code: the a11y gate's own JavaScript manifest, which it
    ///   cross-checks against live controller reflection; the dependency graph; and
    ///   RazorHygieneTests, which reads the text-hygiene script and the CI workflow that invokes
    ///   it, and whose behavioural half - actually running that gate over throwaway fixtures -
    ///   is the point of the class. A workflow file cannot be executed locally, so asserting on
    ///   its text is the strongest statement available about it, exactly as for dependabot.yml.
    ///   The fourth is DocClaimTests, which reads README.md, AGENTS.md and the CI workflow to
    ///   keep the documented commands and job names resolvable. Its guarantees are textual on
    ///   purpose: it asserts that a command or a job named in a document still exists, never
    ///   what the command would print, so it cannot disagree with the suite about a count.
    /// </summary>
    private static readonly Dictionary<string, string> _textReaders = new(StringComparer.Ordinal)
    {
        ["A11yGateManifestTests"] = _tooling,                 // e2e/a11y-gate.cjs, cross-checked by reflection
        ["AuthzExportCenterDatasetTests"] = _cSharp,         // Services/ExportCenterService.cs (row cap)
        ["AuthzMenuLinkTests"] = _razor,                     // Views/Shared/_Layout.cshtml
        ["AuthzPublicIdAndItemBindingTests"] = _cSharp,      // ten Controllers/*.cs
        ["ConcurrencyPropagationTests"] = _razor,             // two order views + their controllers
        ["DependencyPinTests"] = _tooling,                   // *.csproj + .github/dependabot.yml
        ["DocClaimTests"] = _tooling,                        // README.md + AGENTS.md + .github/workflows/ci.yml
        ["ProductionConfigGateTests"] = _config,             // appsettings*.json
        ["RazorHygieneTests"] = _tooling,                    // scripts/check-text-hygiene.ps1 + .github/workflows/ci.yml
        ["SecurityResponseHeadersTests"] = _cSharp,          // Program.cs
        ["SecurityViewCspNonceTests"] = _cSharp,             // Program.cs, plus _razor views
        ["ViewLogicSweepTests"] = _cSharp,                   // controllers and views
    };

    [Fact]
    public void EveryTestClassThatReadsAProjectFileAsText_IsInventoriedAbove_WithItsKind()
    {
        var found = FindTextReadingClasses();

        Assert.Equal(
            _textReaders.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList(),
            found.Select(entry => entry.ClassName).OrderBy(name => name, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void TheCSharpAndConfigCountStaysSix_SoARemovalCannotSilentlyNarrowTheKnownGap()
    {
        // The number reported in the summary of this work. Pinned so that deleting a textual
        // guarantee is a visible act: either the behaviour became testable - in which case the
        // class should stop reading source and this number drops on purpose - or the guarantee
        // was dropped, which should not pass unnoticed. It fell from 9 to 6 when the three
        // security classes stopped reading source.
        var production = _textReaders
            .Where(entry => entry.Value is _cSharp or _config)
            .Select(entry => entry.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(6, production.Count);
        Assert.Equal(6, FindTextReadingClasses().Count(entry => entry.Kind is _cSharp or _config));
    }

    [Fact]
    public void TheInventoryIsNotEmpty_SoTheChecksAboveCannotPassVacuously()
    {
        Assert.NotEmpty(_textReaders);
        Assert.All(_textReaders, entry => Assert.EndsWith("Tests", entry.Key, StringComparison.Ordinal));
        Assert.All(_textReaders.Values, kind => Assert.Contains(kind, new[] { _cSharp, _config, _razor, _tooling }));
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
                .Select(entry => (entry.ClassName, _textReaders.TryGetValue(entry.ClassName, out var kind) ? kind : "UNDECLARED"))
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
