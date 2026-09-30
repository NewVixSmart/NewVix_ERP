using System.Text.RegularExpressions;
using NewVixSmart.Web.Migrations;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// What the lossy-rollback guard <i>costs</i>, asserted as a property of the guard's own text rather
/// than as a stopwatch reading - and asserted on a machine with no SQL Server at all.
/// <para>
/// This file exists because the obvious place for that assertion is the wrong place.
/// <c>MigrationPrecisionSqlServerTests</c> owns the engine-side cost check, and it is
/// <c>[SqlServerFact]</c>-gated, which means on a machine with no engine its tests report a declared
/// skip. A cost claim that disappears along with the engine is a cost claim nobody checks, and this
/// one is checkable with nothing but the migration object: the guard is generated T-SQL, its shape
/// is what makes it affordable or ruinous, and reading its shape costs microseconds and no server.
/// </para>
/// <para>
/// What is asserted is the shape the production code actually has, which is worth stating plainly
/// because the easy thing to say is wrong. The guard is not one set-based scan. It is a
/// <c>WHILE</c> loop over its sixty columns that issues one dynamic <c>sp_executesql</c> per
/// column, each a <c>SELECT TOP (1) ... WHERE col &lt;&gt; ROUND(col, 2)</c> over an unseekable
/// predicate. So the affordability property is <i>one execution per column, never one per row</i>,
/// and that is what these tests pin.
/// </para>
/// </summary>
public sealed class TestEngineGuardCostShapeTests
{
    private static readonly Regex _listedColumn = new(@"N'(?<table>[^']+)',\s*N'(?<column>[^']+)'", RegexOptions.Compiled);

    /// <summary>
    /// The guard is exactly one batch, and the columns it lists are exactly the columns the
    /// migration alters - same set, same order.
    /// <para>
    /// The second half is a correctness assertion that happens to be free. A guard that missed a
    /// column would let a lossy rollback through on that column while every other test in the suite
    /// stayed green, because the behavioural tests only dirty the columns they happen to touch. The
    /// list is read out of the <c>INSERT INTO @affected</c> the engine receives, and compared
    /// against the <c>AlterColumn</c> operations <c>Up()</c> emits, so a column added to one and not
    /// the other is a red build rather than a silent hole in a financial guard.
    /// </para>
    /// </summary>
    [Fact]
    public void Guard_ProbesExactlyTheColumnsTheMigrationAlters_AndNothingElse()
    {
        var guard = MigrationPrecisionDatabase.GuardSql;
        var listed = _listedColumn.Matches(guard)
            .Select(m => (Table: m.Groups["table"].Value, Column: m.Groups["column"].Value))
            .ToList();

        Assert.NotEmpty(listed);
        Assert.Equal(MigrationPrecisionDatabase.WidenedColumns, listed);
        Assert.Equal(60, listed.Count);

        // The list is only half a loop; without a bound the WHILE below would run once and the
        // guard would vouch for nothing at all.
        Assert.Contains("DECLARE @last int = (SELECT COUNT(*) FROM @affected);", guard, StringComparison.Ordinal);
    }

    /// <summary>
    /// One dynamic-execution site, one loop, over columns - which is what makes a clean rollback
    /// cost sixty statements instead of sixty million.
    /// <para>
    /// Each of these is a specific rewrite this assertion is aimed at. A second <c>EXEC</c> site
    /// means a second statement per column. A second <c>WHILE</c> means some other thing is being
    /// iterated, and in a guard the only candidates are rows and columns, of which only one is
    /// affordable. A <c>TOP (1)</c> that became a bare <c>COUNT(*)</c> would not change the
    /// execution count but would forfeit the early exit that lets a dirty database stop at its first
    /// offending column. A cursor anywhere in the batch is the same loop written the expensive way.
    /// </para>
    /// </summary>
    [Fact]
    public void Guard_IssuesOneProbeExecutionPerColumnInsideOneLoop()
    {
        var guard = MigrationPrecisionDatabase.GuardSql;

        // One exec site, inside one loop, in that order. The ordering assertions are what make this
        // a statement about the loop's body rather than about the batch as a bag of keywords.
        Assert.Equal(1, Occurrences(guard, "EXEC sp_executesql"));
        Assert.Equal(1, Occurrences(guard, "WHILE "));
        Assert.Equal(0, Occurrences(guard, "CURSOR"));
        Assert.Equal(0, Occurrences(guard, "FETCH "));

        var loop = guard.IndexOf("WHILE ", StringComparison.Ordinal);
        var exec = guard.IndexOf("EXEC sp_executesql", StringComparison.Ordinal);
        var advance = guard.IndexOf("SET @ordinal = @ordinal + 1;", StringComparison.Ordinal);
        var closeLoop = guard.IndexOf("END;", advance, StringComparison.Ordinal);

        Assert.True(loop >= 0 && exec > loop && advance > exec && closeLoop > advance,
            "the probe must be executed inside the loop, and the loop must advance its column ordinal "
            + "after that execution.");

        // The loop is bounded by the column list and gives up at the first offending column, so the
        // number of executions cannot grow with the number of rows - offending or otherwise.
        Assert.Contains(
            "WHILE @ordinal <= @last AND @message IS NULL",
            guard,
            StringComparison.Ordinal);
        Assert.Contains("SELECT TOP (1) @value = [", guard, StringComparison.Ordinal);
    }

    /// <summary>
    /// The probe asks one column one question, and the question is the lossy one.
    /// <para>
    /// A cheaper predicate is not available - <c>col &lt;&gt; ROUND(col, 2)</c> is already the
    /// cheapest form there is, since nothing can index it - so what is worth pinning is that a
    /// rewrite has to be a deliberate one. The neighbour <c>col = ROUND(col, 2)</c> has identical
    /// cost and opposite index behaviour, and <c>CAST(col AS decimal(18,2)) = col</c> looks
    /// equivalent while being a lossy conversion inside the comparison itself. The probe string is
    /// extracted before those are ruled out, because the guard <i>does</i> legitimately
    /// <c>CAST</c> the offending value when it formats the message - and a rule that forbade the
    /// cast outright would have forbidden the useful one.
    /// </para>
    /// </summary>
    [Fact]
    public void Guard_PredicateIsTheLossyOneAndNotAConversionInDisguise()
    {
        var guard = MigrationPrecisionDatabase.GuardSql;

        var probeStart = guard.IndexOf("SET @probe = ", StringComparison.Ordinal);
        var probeEnd = guard.IndexOf("';", probeStart, StringComparison.Ordinal);
        Assert.True(probeStart >= 0 && probeEnd > probeStart, "the guard must build its probe as a string.");

        var probe = guard[probeStart..probeEnd];
        Assert.Contains("<> ROUND([", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("= ROUND([", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("CAST(", probe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CONVERT(", probe, StringComparison.OrdinalIgnoreCase);

        // The value is widened before the comparison so it happens in decimal(38,10) rather than in
        // the column's own scale, and the output parameter is declared to match. A mismatch here
        // would round the evidence out of the very message the guard exists to produce.
        Assert.Contains("DECLARE @offending decimal(38,10);", guard, StringComparison.Ordinal);
        Assert.Contains("N'@value decimal(38,10) OUTPUT'", guard, StringComparison.Ordinal);
    }

    private static int Occurrences(string haystack, string needle) =>
        Regex.Matches(haystack, Regex.Escape(needle), RegexOptions.CultureInvariant).Count;
}
