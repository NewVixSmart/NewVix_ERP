using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// Documentation is only part of the build if something checks it. This class covers the
/// claims that are cheap to verify and expensive to get wrong: a command an operator is told
/// to run, a CI job the README says exists, and a test count that is false the moment anyone
/// adds a test.
///
/// <para>
/// What this class deliberately does <em>not</em> assert, because a test that re-implements
/// what it documents is a second source of truth that can disagree with the first:
/// </para>
/// <list type="bullet">
///   <item><description>
///     It never derives the real number of tests. Computing it here would give the count two
///     sources, and this file's own test methods would move that number - so the assertion
///     would be self-referential and would rot on the next method added. The claim being
///     protected is the weaker and sturdier one: <em>the count is printed by a command the
///     reader can run</em>, not the count itself.
///   </description></item>
///   <item><description>
///     It never runs the commands it documents. <c>dotnet format</c>,
///     <c>check-text-hygiene.ps1</c> and <c>has-pending-model-changes</c> already run as
///     behavioural gates in CI. Asserting on their text proves they are named and current, not
///     that they pass; claiming the latter here would overstate the guarantee.
///   </description></item>
///   <item><description>
///     It reads no test source. Nothing below counts <c>[Fact]</c> methods or inspects the test
///     project, so no assertion in this file can be moved by the existence of another test -
///     including this one.
///   </description></item>
/// </list>
///
/// <para>
/// "No hardcoded test count" is asserted over README.md and AGENTS.md only, not over
/// <c>docs/</c>. The dated records there carry a per-milestone count on purpose
/// (<c>docs/BUILD-PLAN-README.md</c> alone has eighteen of them, from 18 green tests through
/// to 128), and rewriting those to match today would destroy the history they exist to keep.
/// Scoping the rule to the two live entry points is a decision, not an oversight.
/// </para>
/// </summary>
public sealed class DocClaimTests
{
    private const string _readme = "README.md";
    private const string _agents = "AGENTS.md";
    private const string _workflow = ".github/workflows/ci.yml";
    private const string _toolManifest = ".config/dotnet-tools.json";

    /// <summary>
    /// A bare run of digits glued to a test noun - "616 اختبارًا", "838 tests", "848 passed".
    /// In a live document that is a number with no way to be revalidated, so it is a defect the
    /// moment the suite changes size. Two digits minimum so that incidental numbers like a
    /// single "0" in a passing comment are not mistaken for a count.
    /// </summary>
    private static readonly Regex _hardcodedCount =
        new(@"\d{2,5}\s*(?:اختبار|اختبارات|tests?\b|passed\b|passing\b)", RegexOptions.Compiled);

    /// <summary>
    /// The command a reader runs instead of trusting a number. Compared against whitespace-
    /// collapsed text, because the meaningful content is the verb, the solution and the
    /// configuration - not how many spaces the author typed after "dotnet test".
    /// </summary>
    private const string _testCommand = "dotnet test NewVixSmart.slnx -c Release";

    // ------------------------------------------------------------------ the test count

    [Fact]
    public void LiveDocs_DoNotHardcodeTheTestCount_AndCarryTheCommandThatPrintsIt()
    {
        var offenders = new List<string>();

        foreach (var relative in new[] { _readme, _agents })
        {
            var text = ReadRepoText(relative);

            foreach (Match match in _hardcodedCount.Matches(text))
            {
                offenders.Add($"  {relative}: \"{match.Value.Trim()}\"");
            }

            // The durable replacement for a number: the command that produces it.
            Assert.True(
                CollapseWhitespace(text).Contains(_testCommand, StringComparison.Ordinal),
                $"{relative} no longer tells the reader how to reproduce the test count. Expected "
                + $"the literal `{_testCommand}` somewhere in the file (compared with runs of "
                + "whitespace collapsed to one space).");
        }

        Assert.True(
            offenders.Count == 0,
            "A test count is hardcoded in a live document, so it is already stale: adding one test "
            + "silently falsifies it and nothing fails. Print it from the command instead - see "
            + "the \"لماذا لا رقم للاختبارات هنا؟\" note in README.md." + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    // ------------------------------------------------------- commands in AGENTS.md

    [Fact]
    public void EveryCommandNamedInAgents_ResolvesToAFileOrToolThatExists()
    {
        var commands = ParseFencedCommands(ReadRepoText(_agents));

        Assert.True(
            commands.Count > 0,
            "No fenced command block was found in AGENTS.md, so this test would pass for the "
            + "wrong reason: the manual it guards may have lost its instructions entirely.");

        var missing = new List<string>();
        foreach (var command in commands)
        {
            foreach (var token in command.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith('-') || !LooksLikeRepoPath(token))
                {
                    continue;
                }

                var full = Path.Combine(FindRepoRoot(), token.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full) && !Directory.Exists(full))
                {
                    missing.Add($"  {token}   (in: {command})");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "AGENTS.md names a path that does not exist. An agent follows these commands "
            + "literally, so a renamed or deleted file produces a confident, wrong action "
            + "rather than an obvious failure:" + Environment.NewLine +
            string.Join(Environment.NewLine, missing));

        // `dotnet` is the one executable whose availability may be assumed here, because it is
        // the process running this test. `pwsh` is deliberately NOT asserted against the local
        // PATH: whether PowerShell 7 is installed on the developer's machine is an environment
        // fact, not a documentation claim, and asserting it would fail on any box that only has
        // Windows PowerShell while leaving both documents perfectly correct. What must hold is
        // that the manual and the workflow name the same host, so the command a reader is told
        // to run is the one the pipeline actually runs.
        Assert.True(
            OnPath("dotnet"),
            "`dotnet` is not on PATH, so none of the mandatory commands in AGENTS.md can be run.");

        Assert.True(
            ReadRepoText(_workflow).Contains("pwsh -NoProfile -File scripts/check-text-hygiene.ps1", StringComparison.Ordinal),
            "AGENTS.md documents the text-hygiene gate as `pwsh ...`, but .github/workflows/ci.yml "
            + "does not run it that way. The manual and the pipeline disagree about which "
            + "PowerShell host the gate runs under, so a reader following the manual is not "
            + "reproducing CI.");

        // `dotnet ef` is a local tool, not a PATH executable, so it resolves through the manifest.
        Assert.True(
            File.Exists(Path.Combine(FindRepoRoot(), _toolManifest.Replace('/', Path.DirectorySeparatorChar))),
            $"{_toolManifest} is missing, so `dotnet ef` - the command AGENTS.md makes mandatory "
            + "after any model change - cannot resolve.");
        Assert.Contains(
            "dotnet-ef",
            ReadRepoText(_toolManifest),
            StringComparison.Ordinal);

        // AGENTS.md's own hard rules, restated where they are cheap to check.
        AssertFlagged(commands, "dotnet test", "-c Release");
        AssertFlagged(commands, "dotnet build", "-c Release");
        AssertFlagged(commands, "dotnet format", "--verify-no-changes");
        AssertFlagged(commands, "pwsh", "-NoProfile");
        AssertFlagged(commands, "dotnet ef", "--configuration Release");
    }

    // -------------------------------------------------------- the README CI table

    [Fact]
    public void TheReadmeCiTable_ListsExactlyTheJobsTheWorkflowDefines()
    {
        var workflowJobs = WorkflowJobNames();
        Assert.NotEmpty(workflowJobs);

        var documented = ReadmeCiJobRows(workflowJobs);

        var missing = workflowJobs.Where(job => !documented.Contains(job, StringComparer.Ordinal)).ToList();
        var stale = documented.Where(job => !workflowJobs.Contains(job, StringComparer.Ordinal)).ToList();

        Assert.True(
            missing.Count == 0,
            "The workflow defines CI job(s) the README never documents, so nobody reading the "
            + "README knows they gate every push:" + Environment.NewLine +
            string.Join(Environment.NewLine, missing.Select(job => "  " + job)));

        Assert.True(
            stale.Count == 0,
            "The README documents CI job(s) that no longer exist in the workflow, so the table "
            + "describes a pipeline that is not the one that runs:" + Environment.NewLine +
            string.Join(Environment.NewLine, stale.Select(job => "  " + job)));
    }

    // ---------------------------------------------------------- the three live gates

    [Fact]
    public void TheThreeLiveGates_AreNamedInBothTheWorkflowAndTheReadme()
    {
        var workflow = ReadRepoText(_workflow);
        var readme = ReadRepoText(_readme);

        // A gate that exists only in CI is invisible to the reader; a gate documented but absent
        // from CI is worse than either, because the README becomes a promise nothing keeps. The
        // literals are the exact command strings, so a flag that stops being enforced fails here
        // instead of quietly becoming optional.
        var gates = new (string Name, string Literal)[]
        {
            ("formatting", "dotnet format NewVixSmart.slnx --verify-no-changes --no-restore"),
            ("text hygiene", "scripts/check-text-hygiene.ps1"),
            ("pending model changes", "migrations has-pending-model-changes"),
        };

        foreach (var (name, literal) in gates)
        {
            Assert.True(
                workflow.Contains(literal, StringComparison.Ordinal),
                $"The {name} gate is documented as a required check, but `.github/workflows/ci.yml` "
                + $"no longer contains `{literal}`. Either the gate was dropped from CI or the "
                + "documentation is promising a check nothing performs.");

            Assert.True(
                readme.Contains(literal, StringComparison.Ordinal),
                $"The {name} gate runs in CI (as `{literal}`) but README.md does not name it, so a "
                + "reader cannot tell which checks are mandatory before pushing.");
        }
    }

    // ------------------------------------------------------------------- helpers

    /// <summary>
    /// Asserts that exactly one documented command starts with <paramref name="prefix"/> and that
    /// it carries <paramref name="requiredFlag"/>. Both halves matter: a command that vanished
    /// from the manual and one that lost its Release/no-restore flag are both silent regressions.
    /// </summary>
    private static void AssertFlagged(
        IReadOnlyList<string> commands,
        string prefix,
        string requiredFlag)
    {
        var matches = commands
            .Where(command => command.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            matches.Count == 1,
            $"Expected AGENTS.md to document exactly one `{prefix} ...` command, found "
            + $"{matches.Count}. The manual's mandatory-verification block is the contract this "
            + "repository is operated by, and a missing or duplicated gate is a real regression.");

        var command = CollapseWhitespace(matches[0]);
        Assert.True(
            command.Contains(requiredFlag, StringComparison.Ordinal),
            $"The documented `{prefix}` command lost `{requiredFlag}`:" + Environment.NewLine +
            $"  {command}" + Environment.NewLine +
            "A verification command that runs the wrong configuration does not verify anything "
            + "about what CI ships.");
    }

    private static string ReadRepoText(string relativePath)
    {
        var full = Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"{relativePath} was expected at {full} but is not there.");
        return File.ReadAllText(full);
    }

    /// <summary>
    /// Every fenced code block in the document, one logical command per entry: backslash
    /// continuations are joined and trailing <c>#</c> comments dropped, because a comment is prose
    /// about a command and must not be mistaken for part of it.
    /// </summary>
    private static List<string> ParseFencedCommands(string markdown)
    {
        var commands = new List<string>();
        var buffer = new StringBuilder();
        var inside = false;

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (inside)
                {
                    commands.AddRange(JoinContinuations(buffer.ToString()));
                }
                else
                {
                    buffer.Clear();
                }

                inside = !inside;
                continue;
            }

            if (inside)
            {
                buffer.Append(line).Append('\n');
            }
        }

        return commands;
    }

    private static IEnumerable<string> JoinContinuations(string block)
    {
        var pending = new StringBuilder();

        foreach (var line in block.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();

            if (trimmed.EndsWith('\\'))
            {
                pending.Append(trimmed, 0, trimmed.Length - 1).Append(' ');
                continue;
            }

            pending.Append(trimmed);
            var command = StripComment(pending.ToString());
            if (command.Length > 0)
            {
                yield return command;
            }

            pending.Clear();
        }
    }

    private static string StripComment(string command)
    {
        var hash = command.IndexOf(" #", StringComparison.Ordinal);
        return (hash >= 0 ? command[..hash] : command).Trim();
    }

    /// <summary>
    /// Job keys from the <c>jobs:</c> mapping. The two-space anchor is what separates a job name
    /// from a step or a nested key, so the list is taken from structure rather than from a
    /// hand-maintained roster that would itself rot.
    /// </summary>
    private static List<string> WorkflowJobNames()
    {
        var jobs = new List<string>();
        var inJobs = false;

        foreach (var line in ReadRepoText(_workflow).Replace("\r\n", "\n").Split('\n'))
        {
            if (!inJobs)
            {
                inJobs = line.TrimEnd() == "jobs:";
                continue;
            }

            var match = Regex.Match(line, @"^  (?<name>[A-Za-z0-9][A-Za-z0-9_-]*):\s*$");
            if (match.Success)
            {
                jobs.Add(match.Groups["name"].Value);
            }
        }

        return jobs;
    }

    /// <summary>
    /// The first cells of the one README table that documents CI jobs. The table is located by
    /// finding a row naming a real job and then reading that whole table, so other tables whose
    /// first cell happens to be a backticked word (the roles table has <c>admin</c>) are not
    /// swept in.
    /// </summary>
    private static List<string> ReadmeCiJobRows(IReadOnlyCollection<string> workflowJobs)
    {
        var lines = ReadRepoText(_readme).Replace("\r\n", "\n").Split('\n');
        var row = new Regex(@"^\|\s*`(?<name>[A-Za-z0-9][A-Za-z0-9_-]*)`\s*\|", RegexOptions.Compiled);
        var known = workflowJobs.ToHashSet(StringComparer.Ordinal);

        for (var i = 0; i < lines.Length; i++)
        {
            var first = row.Match(lines[i]);
            if (!first.Success || !known.Contains(first.Groups["name"].Value))
            {
                continue;
            }

            var rows = new List<string>();
            for (var j = i; j < lines.Length && lines[j].StartsWith('|'); j++)
            {
                var match = row.Match(lines[j]);
                if (match.Success)
                {
                    rows.Add(match.Groups["name"].Value);
                }
            }

            return rows;
        }

        return [];
    }

    /// <summary>
    /// Whether a token could name a file in this repository. Deliberately shape-based rather
    /// than an allowlist of known extensions: an allowlist would blind this check to precisely
    /// the case it exists for - a solution or script renamed to an extension nobody listed -
    /// and it was caught doing that during the non-vacuity pass on this very test. A bare word
    /// like <c>Release</c> or a subcommand like <c>migrations</c> has no extension and no
    /// separator, so it is correctly ignored.
    /// </summary>
    private static bool LooksLikeRepoPath(string token) =>
        token.Contains('/')
        || token.Contains('\\')
        || Regex.IsMatch(Path.GetExtension(token), @"^\.[A-Za-z][A-Za-z0-9]{0,5}$");

    private static bool OnPath(string tool)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);

        return pathVariable
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => directory.Trim())
            .Any(directory => extensions.Any(extension =>
                File.Exists(Path.Combine(directory, tool + extension))));
    }

    private static string CollapseWhitespace(string text) =>
        Regex.Replace(text, @"\s+", " ");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NewVixSmart.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
