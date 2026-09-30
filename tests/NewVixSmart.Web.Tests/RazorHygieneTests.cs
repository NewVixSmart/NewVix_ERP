using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// The mirror of <c>scripts/check-text-hygiene.ps1</c> and of the CI step that invokes it.
///
/// <para>
/// Why this class exists at all, when the script already runs in CI: a CI step only proves
/// itself on the runner, and a gate nobody has seen fail is a gate nobody knows works. Every
/// rule is therefore driven from here against a throwaway tree built byte by byte, so each
/// one has an observed red before the repository is allowed to be green - and so the
/// green in <c>TheRealRepositoryIsClean</c> means "the gate found nothing" rather than
/// "the gate found nothing because it looked at nothing".
/// </para>
///
/// <para>
/// The fixtures are written to a temp tree outside the repository, and the fixture directory
/// name is also on the script's exclusion list, so a deliberately broken file can never fail
/// the gate by either route.
/// </para>
///
/// <para>
/// One of the guarantees here is textual: <c>TheCiStepRunsTheGateBeforeTheBuild</c> reads
/// .github/workflows/ci.yml. That is recorded in SourceTextGuaranteeInventoryTests as
/// <c>Tooling</c>, the same way DependencyPinTests' read of .github/dependabot.yml is, and for
/// the same reason - a workflow file cannot be executed locally, so asserting on its text is
/// the strongest statement available. The behavioural half of the same guarantee is
/// <see cref="TheRealRepositoryIsClean"/>, which runs the exact command the step runs.
/// </para>
/// </summary>
public sealed class RazorHygieneTests
{
    private const string _scriptRelativePath = "scripts/check-text-hygiene.ps1";
    private const string _workflowRelativePath = ".github/workflows/ci.yml";
    private const int _timeoutMilliseconds = 180_000;

    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // ---------------------------------------------------------------- the real repository

    [Fact]
    public void TheRealRepositoryIsClean()
    {
        var run = RunGate(FindRepoRoot());

        Assert.True(
            run.ExitCode == 0,
            $"check-text-hygiene.ps1 exited {run.ExitCode} on the repository itself:{Environment.NewLine}{run.Output}");

        Assert.Matches(@"text-hygiene: result\s+blocking=0", run.Output);
        Assert.Contains("text-hygiene: PASS", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGateActuallyWalksTheRepository()
    {
        // Non-vacuity for the test above. If the walk silently stopped descending, or an
        // exclusion list grew by accident, the repository would pass for the wrong reason.
        var scanned = ScannedCount(RunGate(FindRepoRoot()).Output);

        Assert.True(
            scanned >= 100,
            $"Expected the gate to scan at least 100 .cshtml/.md files, it scanned {scanned}. " +
            "Either the walk broke or an exclusion is pruning the real tree.");
    }

    [Fact]
    public void TheViewsDirectoryIsNotAccidentallyExcluded()
    {
        // The exclusions are name-based, so a future entry like "Views" would take the entire
        // user-facing interface out of the gate while the gate still reported a clean tree.
        using var tree = new Tree();
        tree.Write("src/NewVixSmart.Web/Views/Home/Index.cshtml", "<p>x</p>");

        var run = RunGate(tree.Root);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("[R5]", run.Output, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- R1: undecodable

    [Fact]
    public void R1_BlocksAFileThatIsNotStrictUtf8()
    {
        // 0xC3 announces a two-byte sequence; 0x28 is '(' and cannot continue one.
        using var tree = new Tree();
        tree.Write("broken.cshtml", [0x3C, 0x70, 0x3E, 0xC3, 0x28, 0x3C, 0x2F, 0x70, 0x3E, 0x0A]);

        AssertBlocks(tree, "R1", "broken.cshtml", expectedLine: 1);
    }

    [Fact]
    public void R1_DoesNotFireOnAFileWhoseMultibyteCharacterIsLegitimate()
    {
        // The regression this guards: a validator that binary-searches for the shortest
        // undecodable PREFIX reports a healthy file as corrupt, because a prefix can end in
        // the middle of a multi-byte character. Arabic here is a real three-byte sequence.
        using var tree = new Tree();
        tree.Write("arabic.cshtml", "<p>\u0645\u0631\u062D\u0628\u0627 \u0628\u0627\u0644\u0639\u0627\u0644\u0645</p>\n");

        var run = RunGate(tree.Root);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("[R1]", run.Output, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- R2: U+FFFD

    [Fact]
    public void R2_BlocksTheReplacementCharacter()
    {
        using var tree = new Tree();
        tree.Write("mojibake.cshtml", "<p>" + (char)0xFFFD + "</p>\n");

        AssertBlocks(tree, "R2", "mojibake.cshtml", expectedLine: 1);
    }

    // ---------------------------------------------------------------- R3: BOM

    [Fact]
    public void R3_BlocksAUtf8Bom()
    {
        using var tree = new Tree();
        tree.Write("bom.cshtml", [0xEF, 0xBB, 0xBF, .. _utf8NoBom.GetBytes("<p>x</p>\n")]);

        AssertBlocks(tree, "R3", "bom.cshtml", expectedLine: 1);
    }

    // ---------------------------------------------------------------- R4: CR

    [Fact]
    public void R4_BlocksCrlfLineEndings()
    {
        using var tree = new Tree();
        tree.Write("crlf.cshtml", "<p>a</p>\r\n<p>b</p>\n");

        AssertBlocks(tree, "R4", "crlf.cshtml", expectedLine: 1);
    }

    [Fact]
    public void R4_BlocksALoneCarriageReturn()
    {
        using var tree = new Tree();
        tree.Write("lone-cr.md", "# \u0627\u0644\u0627\u062E\u062A\u0628\u0627\u0631\r\n\u0646\u0635\n");

        AssertBlocks(tree, "R4", "lone-cr.md", expectedLine: 1);
    }

    // ---------------------------------------------------------------- R5: final newline

    [Fact]
    public void R5_BlocksAMissingFinalNewlineAndNamesTheLastLine()
    {
        using var tree = new Tree();
        tree.Write("no-newline.cshtml", "<p>a</p>\n<p>b</p>");

        // Line 2, not line 0 and not line 1: the finding has to point at the line that is
        // actually missing its terminator.
        AssertBlocks(tree, "R5", "no-newline.cshtml", expectedLine: 2);
    }

    [Fact]
    public void R5_BlocksTrailingBlankLines()
    {
        using var tree = new Tree();
        tree.Write("blank-tail.md", "\u0627\u0644\u0633\u0637\u0631\n\n\n");

        AssertBlocks(tree, "R5", "blank-tail.md", expectedLine: 2);
    }

    [Fact]
    public void R5_AcceptsExactlyOneFinalNewline()
    {
        using var tree = new Tree();
        tree.Write("ok.cshtml", "<p>a</p>\n");

        var run = RunGate(tree.Root);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("[R5]", run.Output, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- R6: trailing whitespace

    [Fact]
    public void R6_BlocksTrailingSpaces()
    {
        using var tree = new Tree();
        tree.Write("trailing.cshtml", "<p>a</p>   \n");

        AssertBlocks(tree, "R6", "trailing.cshtml", expectedLine: 1);
    }

    [Fact]
    public void R6_BlocksASingleTrailingSpaceInMarkdown()
    {
        using var tree = new Tree();
        tree.Write("one-space.md", "\u0627\u0644\u0633\u0637\u0631 \n");

        AssertBlocks(tree, "R6", "one-space.md", expectedLine: 1);
    }

    [Fact]
    public void R6_AcceptsTheMarkdownTwoSpaceHardBreak()
    {
        // The one documented exemption, and .editorconfig sets trim_trailing_whitespace = false
        // for *.md for this idiom. If this ever starts failing, the idiom is the casualty.
        using var tree = new Tree();
        tree.Write("hard-break.md", "\u0633\u0637\u0631 \u0623\u0648\u0644  \n\u0633\u0637\u0631 \u062B\u0627\u0646\u064A\n");

        var run = RunGate(tree.Root);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("[R6]", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void R6_StillBlocksALineThatIsNothingButSpaces()
    {
        // A blank line of two spaces is trailing whitespace, not a hard break: there is no
        // line before it to break away from.
        using var tree = new Tree();
        tree.Write("spaces-only.md", "\u0627\u0644\u0633\u0637\u0631\n  \n");

        AssertBlocks(tree, "R6", "spaces-only.md", expectedLine: 2);
    }

    // ---------------------------------------------------------------- R7 / R9: tab indentation

    [Fact]
    public void R7_BlocksTabIndentationInACshtmlFile()
    {
        using var tree = new Tree();
        tree.Write("tab.cshtml", "\t<p>\u0627\u0644\u0639\u0631\u0636</p>\n");

        AssertBlocks(tree, "R7", "tab.cshtml", expectedLine: 1);
        Assert.DoesNotContain("[R9]", RunGate(tree.Root).Output, StringComparison.Ordinal);
    }

    [Fact]
    public void R9_BlocksTabIndentationInAMarkdownFile()
    {
        using var tree = new Tree();
        tree.Write("tab.md", "\t- \u0639\u0646\u0635\u0631\n");

        var run = RunGate(tree.Root);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("[R9]", run.Output, StringComparison.Ordinal);
        Assert.Contains("tab.md:1:", run.Output, StringComparison.Ordinal);
        // R7 is the .cshtml half of the same predicate; it must not double-report here.
        Assert.DoesNotContain("[R7]", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void SpaceIndentationIsFine()
    {
        using var tree = new Tree();
        tree.Write("spaces.cshtml", "    <p>\u0627\u0644\u0639\u0631\u0636</p>\n");
        tree.Write("spaces.md", "  - \u0639\u0646\u0635\u0631\n");

        var run = RunGate(tree.Root);

        Assert.Equal(0, run.ExitCode);
    }

    // ---------------------------------------------------------------- R8: foreign script

    [Fact]
    public void R8_WarnsAboutAForeignWordInsideAnArabicSentence()
    {
        using var tree = new Tree();
        tree.Write("mixed.md", "# \u0627\u0644\u0627\u062E\u062A\u0628\u0627\u0631\n\n\u062C\u0645\u0644\u0629 \u0639\u0631\u0628\u064A\u0629 \u062F\u0627\u062E\u0644\u0629 \u0628\u0643\u0644\u0645\u0629 \u4E2D\u6587.\n");

        var run = RunGate(tree.Root);

        // A warning, not a block: a document may legitimately quote a foreign string, and a
        // gate that can be blocked by correct text is a gate people learn to bypass.
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("[R8]", run.Output, StringComparison.Ordinal);
        Assert.Contains("mixed.md:3:", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void R8_IgnoresForeignTextInsideAFencedCodeBlock()
    {
        using var tree = new Tree();
        tree.Write("fenced.md",
            "# \u0627\u0644\u0627\u062E\u062A\u0628\u0627\u0631\n\n" +
            "\u0646\u0635 \u0639\u0631\u0628\u064A \u0628\u0627\u0644\u0645\u062B\u0627\u0644.\n\n" +
            "```\n\u4E2D\u6587\n```\n");

        var run = RunGate(tree.Root);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("[R8]", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void R8_IgnoresForeignTextInsideAnInlineCodeSpan()
    {
        using var tree = new Tree();
        tree.Write("inline.md", "# \u0627\u0644\u0627\u062E\u062A\u0628\u0627\u0631\n\n\u0646\u0635 \u0639\u0631\u0628\u064A \u0628\u0627\u0644\u0645\u062B\u0627\u0644 `\u4E2D\u6587` \u0648\u0627\u0644\u0627\u062E\u0631.\n");

        var run = RunGate(tree.Root);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("[R8]", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void R8_IgnoresForeignTextInsidePreAndCodeInACshtmlFile()
    {
        using var tree = new Tree();
        tree.Write("pre.cshtml",
            "<p>\u0646\u0635 \u0639\u0631\u0628\u064A \u0628\u0627\u0644\u0645\u062B\u0627\u0644.</p>\n<pre>\n\u4E2D\u6587\n</pre>\n<code>\u4E2D\u6587</code>\n");

        var run = RunGate(tree.Root);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("[R8]", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void R8_DoesNotFireInALatinDominantFile()
    {
        // The rule is deliberately scoped to Arabic-dominant files, because that is where an
        // unrelated script is a defect rather than the document's actual subject matter.
        using var tree = new Tree();
        tree.Write("english.md", "# Release notes\n\nThe word \u4E2D\u6587 appears in this English paragraph.\n");

        var run = RunGate(tree.Root);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("[R8]", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void R8_ReportsTheLineAfterAFencedBlockNotTheLineInsideIt()
    {
        // The masking blanks code-block content but keeps the line breaks, so a finding after a
        // fence must be numbered by real lines. A mask that also collapsed newlines would point
        // at a line number from the middle of the block.
        using var tree = new Tree();
        tree.Write("after-fence.md",
            "# \u0627\u0644\u0627\u062E\u062A\u0628\u0627\u0631\n\n" +
            "\u0646\u0635 \u0639\u0631\u0628\u064A \u0628\u0627\u0644\u0645\u062B\u0627\u0644.\n\n" +
            "```\n\u0644\u064A\u0646\u0629 \u0623\u062E\u0631\u0649\n\u0644\u064A\u0646\u0629 \u0623\u062E\u0631\u0649\n\u0644\u064A\u0646\u0629 \u0623\u062E\u0631\u0649\n```\n\n" +
            "\u062C\u0645\u0644\u0629 \u0639\u0631\u0628\u064A\u0629 \u062F\u0627\u062E\u0644\u0629 \u0628\u0643\u0644\u0645\u0629 \u4E2D\u6587.\n");

        var run = RunGate(tree.Root);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("[R8]", run.Output, StringComparison.Ordinal);
        Assert.Contains("after-fence.md:11:", run.Output, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- R10: stray tab

    [Fact]
    public void R10_WarnsAboutATabUsedAsAColumnSeparator()
    {
        using var tree = new Tree();
        tree.Write("columns.md", "\u0627\u0644\u0627\u0633\u0645\t\u0627\u0644\u0642\u064A\u0645\u0629\n");

        var run = RunGate(tree.Root);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("[R10]", run.Output, StringComparison.Ordinal);
        Assert.Contains("columns.md:1:", run.Output, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- exclusions

    [Fact]
    public void ExcludedDirectoriesAreNotScanned()
    {
        using var tree = new Tree();

        // Every one of these is a file that WOULD fail R5 if it were scanned. Build output and
        // tool caches hold generated text; wwwroot/lib is vendored; RazorHygieneFixtures is this
        // gate's own deliberately-broken test data.
        foreach (var excluded in new[]
                 {
                     "bin/x.md",
                     "obj/x.md",
                     "node_modules/pkg/x.md",
                     ".git/x.md",
                     ".vs/x.md",
                     "TestResults/x.md",
                     "test-results/x.md",
                     "artifacts/x.md",
                     "screenshots/x.md",
                     "backups/x.md",
                     ".playwright-cli/x.md",
                     "RazorHygieneFixtures/broken.cshtml",
                     "src/NewVixSmart.Web/wwwroot/lib/jquery/LICENSE.md",
                 })
        {
            tree.Write(excluded, "no final newline here");
        }

        // One file that is in scope, and clean. If the walk stopped entirely this would still
        // pass, which is why the count is asserted too.
        tree.Write("docs/in-scope.md", "in scope\n");

        var run = RunGate(tree.Root);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(1, ScannedCount(run.Output));
    }

    [Fact]
    public void ListExclusionsExplainsItselfAndSucceeds()
    {
        var run = RunGate(FindRepoRoot(), "-ListExclusions");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Designer.cs", run.Output, StringComparison.Ordinal);
        Assert.Contains("dotnet ef migrations add", run.Output, StringComparison.Ordinal);
        Assert.Contains("wwwroot/lib", run.Output, StringComparison.Ordinal);
        Assert.Contains("RazorHygieneFixtures", run.Output, StringComparison.Ordinal);
        // Every rule id has to be documented, or the table and the implementation drift apart.
        foreach (var rule in new[] { "R1", "R2", "R3", "R4", "R5", "R6", "R7", "R8", "R9", "R10" })
        {
            Assert.Contains(rule, run.Output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AMissingPathIsAUsageErrorNotAViolation()
    {
        var run = RunGate(Path.Combine(Path.GetTempPath(), "razor-hygiene-does-not-exist-" + Guid.NewGuid().ToString("n")));

        Assert.Equal(1, run.ExitCode);
    }

    // ---------------------------------------------------------------- the gate is read-only

    [Fact]
    public void TheGateDoesNotWriteToTheTreeItScans()
    {
        using var tree = new Tree();
        tree.Write("clean.cshtml", "<p>\u0627\u0644\u0639\u0631\u0636</p>\n");
        tree.Write("broken.cshtml", "<p>a</p>   \n");
        tree.Write("docs/notes.md", "\u0646\u0635 \u0639\u0631\u0628\u064A.\n");

        var before = Snapshot(tree.Root);
        var run = RunGate(tree.Root);
        var after = Snapshot(tree.Root);

        Assert.Equal(2, run.ExitCode);
        // Non-vacuity: two empty dictionaries are equal too.
        Assert.Equal(3, before.Count);
        Assert.Equal(before, after);
    }

    [Fact]
    public void TheScriptContainsNoWriteApi()
    {
        // The behavioural proof is the snapshot test above; this one survives a reviewer who
        // never runs the suite. A gate that rewrote files from inside CI would be a far worse
        // thing to trust than one that names them.
        var source = File.ReadAllText(ScriptPath());

        foreach (var forbidden in new[]
                 {
                     "Set-Content", "Add-Content", "Out-File", "New-Item", "Remove-Item",
                     "Copy-Item", "Move-Item", "Rename-Item", "WriteAllBytes", "WriteAllText",
                     "::Delete", "::Move", "::Copy",
                 })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheScriptItselfIsAsciiAndLfOnlyWithoutABom()
    {
        // Windows PowerShell 5.1 decodes a BOM-less script using the console codepage, so a
        // non-ASCII character in this file would parse as mojibake on exactly the platform that
        // runs the gate for a developer's local `.\scripts\` invocation. ASCII-only is the
        // constraint that makes the script portable across both hosts.
        var bytes = File.ReadAllBytes(ScriptPath());

        Assert.NotEmpty(bytes);
        Assert.All(bytes, b => Assert.InRange(b, 0, 0x7F));
        Assert.DoesNotContain((byte)0x0D, bytes);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal((byte)0x0A, bytes[^1]);
    }

    // ---------------------------------------------------------------- CI wiring

    [Fact]
    public void TheCiStepRunsTheGateBeforeTheBuild()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRepoRoot(), _workflowRelativePath.Replace('/', Path.DirectorySeparatorChar)));

        const string stepName = "- name: Verify Razor and Markdown text hygiene";
        const string runLine = "run: pwsh -NoProfile -File scripts/check-text-hygiene.ps1";

        var step = workflow.IndexOf(stepName, StringComparison.Ordinal);
        Assert.True(step >= 0, $"{_workflowRelativePath} has no '{stepName}' step.");

        var runAt = workflow.IndexOf(runLine, step, StringComparison.Ordinal);
        Assert.True(runAt > step, $"'{stepName}' does not run '{runLine}'.");

        // No GitHub expression in the command: `${{ }}` in a run block is an injection
        // surface, and the existing format gate in this same file already sets the precedent.
        Assert.DoesNotContain("${{", runLine, StringComparison.Ordinal);

        // The point of the step is to fail before the expensive work, so its position is part
        // of the guarantee: before the SDK install, the restore, the build and the tests.
        foreach (var later in new[]
                 {
                     "uses: actions/setup-dotnet@",
                     "- name: Restore",
                     "- name: Build (warnings are errors)",
                     "- name: Test\n",
                 })
        {
            var at = workflow.IndexOf(later, StringComparison.Ordinal);
            Assert.True(at >= 0, $"{_workflowRelativePath} no longer contains '{later.Trim()}'.");
            Assert.True(runAt < at, $"The hygiene step must come before '{later.Trim()}'.");
        }
    }

    [Fact]
    public void TheWorkflowIsStillValidYaml()
    {
        // A gate step pasted at the wrong indentation would make the whole workflow invalid,
        // and an invalid workflow fails silently - no job runs at all, so the gate and the
        // build and the tests all stop happening without a single red mark.
        var lines = File.ReadAllLines(Path.Combine(FindRepoRoot(), _workflowRelativePath.Replace('/', Path.DirectorySeparatorChar)));

        int IndentOfLineWith(string marker, int from = 0)
        {
            for (var i = from; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith(marker, StringComparison.Ordinal))
                {
                    return lines[i].Length - lines[i].TrimStart().Length;
                }
            }

            throw new Xunit.Sdk.XunitException($"No line starting with '{marker}' after line {from}.");
        }

        var checkout = IndentOfLineWith("- name: Checkout");
        var hygiene = IndentOfLineWith("- name: Verify Razor and Markdown text hygiene");

        Assert.Equal(checkout, hygiene);
        Assert.True(checkout % 2 == 0, $"Step indentation must be a whole number of two-space levels, got {checkout}.");
    }

    // ---------------------------------------------------------------- helpers

    private static void AssertBlocks(Tree tree, string ruleId, string fileName, int expectedLine)
    {
        var run = RunGate(tree.Root);

        Assert.True(
            run.ExitCode == 2,
            $"Expected exit code 2 for [{ruleId}] in {fileName}, got {run.ExitCode}:{Environment.NewLine}{run.Output}");
        Assert.Contains($"[{ruleId}]", run.Output, StringComparison.Ordinal);
        Assert.Contains($"{fileName}:{expectedLine}:", run.Output, StringComparison.Ordinal);
        Assert.Contains($"::error file={fileName},line={expectedLine},title={ruleId}::", run.Output, StringComparison.Ordinal);
    }

    private static int ScannedCount(string output)
    {
        var match = Regex.Match(output, @"scanned\s+(\d+)\s+file", RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"The gate did not report a scanned count:{Environment.NewLine}{output}");
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static GateRun RunGate(string root, params string[] extraArguments)
    {
        var host = PowerShellHost();
        var startInfo = new ProcessStartInfo
        {
            FileName = host,
            WorkingDirectory = Directory.Exists(root) ? root : FindRepoRoot(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        startInfo.ArgumentList.Add("-NoProfile");
        if (!host.EndsWith("pwsh", StringComparison.OrdinalIgnoreCase))
        {
            // Windows PowerShell only; pwsh on Linux does not accept the switch.
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
        }

        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(ScriptPath());
        startInfo.ArgumentList.Add("-Path");
        startInfo.ArgumentList.Add(root);
        foreach (var extra in extraArguments)
        {
            startInfo.ArgumentList.Add(extra);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{host}'.");

        // Both streams must be drained while the process runs, or a full pipe buffer deadlocks
        // the child and the wait below times out on a script that already finished.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(_timeoutMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"{host} did not finish in {_timeoutMilliseconds} ms on '{root}'.");
        }

        return new GateRun(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private static string PowerShellHost()
    {
        // pwsh on Linux and macOS, Windows PowerShell on a developer machine. Either is
        // acceptable; neither being present is a failure, never a skip.
        foreach (var candidate in new[] { "pwsh", "powershell", "powershell.exe" })
        {
            if (IsOnPath(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "\u0644\u0645 \u064A\u062C\u062F \u0645\u062D \u062A\u062B\u0628\u064A\u062A PowerShell \u0639\u0644\u0649 PATH. " +
            "\u064A\u062D\u062A\u0627\u062C \u0627\u0644\u0628\u0648\u0627\u0628\u0629 \u062F\u0648\u0646 \u062A\u0642\u0648\u064A\u0645 \u062A\u062D\u062A\u0627\u062C pwsh.");
    }

    private static bool IsOnPath(string executable)
    {
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [string.Empty];

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                if (File.Exists(Path.Combine(directory.Trim('"'), executable + extension)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "scripts", "check-text-hygiene.ps1")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"\u062A\u0639\u0630\u0631 \u0627\u0644\u0639\u062B\u0648\u062F \u0639\u0644\u0649 {_scriptRelativePath} \u0628\u0627\u0644\u0628\u062D\u062B \u0627\u0644\u0635\u0627\u0639\u062F \u0645\u0646 {AppContext.BaseDirectory}.");
    }

    private static string ScriptPath() =>
        Path.Combine(FindRepoRoot(), "scripts", "check-text-hygiene.ps1");

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexString(File.ReadAllBytes(path)), StringComparer.Ordinal);

    private sealed record GateRun(int ExitCode, string Output, string Error);

    /// <summary>A throwaway tree, deliberately full of files no committed file should be.</summary>
    private sealed class Tree : IDisposable
    {
        public Tree()
        {
            Root = Path.Combine(Path.GetTempPath(), "razor-hygiene-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Write(string relativePath, string content) =>
            Write(relativePath, _utf8NoBom.GetBytes(content));

        public string Write(string relativePath, byte[] bytes)
        {
            var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(full, bytes);
            return full;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp tree is not worth failing a test over.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
