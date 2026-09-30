using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Migrations;
using Xunit;
using Xunit.Abstractions;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// The behaviour of migration <c>20260929141121_WidenQuantityAndUnitPricePrecision</c> on a real
/// SQL Server engine, which is the only place any of this can be observed.
/// <para>
/// Why this file exists rather than more model-metadata assertions. The migration shipped a
/// <c>decimal(18,4)</c> where the intent was <c>decimal(20,4)</c>. The model agreed with the
/// migration, <c>has-pending-model-changes</c> agreed with the model, and the SQLite suite agreed
/// with everything, because a <c>decimal(p,s)</c> mapping is a promise the provider is free to
/// ignore and SQLite ignores it entirely. Nothing in that chain can see a range that shrank. The
/// engine can: it either accepts a sixteen-digit quantity or it raises
/// <c>Arithmetic overflow error converting numeric to data type numeric</c>. So the assertion has to
/// be a write, on SQL Server.
/// </para>
/// <para>
/// The rollback half has the same problem in a worse shape. Narrowing <c>decimal(20,4)</c> to
/// <c>decimal(18,2)</c> is not an error in SQL Server, it is a silent rounding with a zero exit
/// code, so 0.0001 becomes 0.00 and a fractional stock quantity becomes zero. The tests here pin the
/// guard that refuses it, including the part that matters operationally: a refused rollback must
/// leave the schema and the data exactly as they were, not half-applied.
/// </para>
/// <para>
/// <b>Where the cost of that guard is checked, and where it is only measured.</b> The guard is a
/// <c>WHILE</c> loop over its sixty columns that issues one dynamic <c>sp_executesql</c> probe per
/// column. So the property that keeps a rollback from taking hours is <i>one execution per
/// column</i>, not one per row, and that property is asserted twice without a stopwatch: once
/// structurally on the guard's own SQL, which needs no engine at all, and once by counting plan
/// cache executions against a table holding more rows than there are columns. The three-million-row
/// measurement that backs the claim up is a separate, opt-in test
/// (<see cref="SqlServerHeavyFactAttribute"/>) that reports its number instead of asserting a
/// wall-clock ceiling, because a timing assertion in a correctness suite fails on a slow runner and
/// not on a slow guard.
/// </para>
/// </summary>
public sealed partial class MigrationPrecisionSqlServerTests : IClassFixture<MigrationPrecisionDatabase>
{
    private readonly MigrationPrecisionDatabase _database;
    private readonly ITestOutputHelper _output;

    public MigrationPrecisionSqlServerTests(
        MigrationPrecisionDatabase database,
        ITestOutputHelper output)
    {
        _database = database;
        _output = output;
    }

    /// <summary>
    /// Scenario 1: the migration declares what it is supposed to declare. Read from
    /// <c>INFORMATION_SCHEMA</c> rather than from the model, because the model is the thing under
    /// suspicion - a migration and a model that agree on <c>decimal(18,4)</c> is exactly how the
    /// original defect reached a database.
    /// </summary>
    [SqlServerFact]
    public async Task MigrationPrecision_Up_DeclaresTwentyDigitsAtScaleFourAndThree()
    {
        await _database.ResetToBeforeAsync();
        await _database.ApplyUpAsync();

        var catalog = await ReadDecimalCatalogAsync(_database.CreateContext());

        Assert.Equal(60, MigrationPrecisionDatabase.WidenedColumns.Count);

        foreach (var (table, column) in MigrationPrecisionDatabase.WidenedColumns)
        {
            Assert.True(catalog.TryGetValue((table, column), out var declared),
                $"[{table}].[{column}] is missing after the migration ran.");

            Assert.Equal(20, (int)declared.Precision);
            Assert.Contains((int)declared.Scale, new[] { 3, 4 });

            // Stated as the number that actually matters, not as the precision: 20 - 4 = 16 and
            // 20 - 3 = 17, against the 16 integer digits a decimal(18,2) column carried. At
            // precision 18 these would have been 14 and 15, which is the bug.
            Assert.True(declared.Precision - declared.Scale >= 16,
                $"[{table}].[{column}] is decimal({declared.Precision},{declared.Scale}), holding only "
                + $"{declared.Precision - declared.Scale} integer digits where decimal(18,2) held 16.");
        }
    }

    /// <summary>
    /// Scenario 2, and the regression test for the original defect.
    /// <para>
    /// The shape of the claim has to be stated carefully, because "a 15-digit value used to work" is
    /// not the defect. The old schema was <c>decimal(18,2)</c>, which holds <b>sixteen</b> integer
    /// digits, so it accepted these values fine. The defect was the migration itself: it emitted
    /// <c>decimal(18,4)</c>, which holds only <b>fourteen</b>, so a 15- or 16-digit quantity that the
    /// database had always stored began raising <c>Error 8115</c> the moment this migration ran. The
    /// test therefore states the range budget as an equivalence - sixteen integer digits before,
    /// sixteen after - and the values are chosen to be storable at the new scale but never at
    /// <c>decimal(18,4)</c>.
    /// </para>
    /// <para>
    /// The 17-digit case is the other half and guards the opposite failure: if the precision were
    /// bumped to 21 or 28 by mistake, the range would quietly grow and this test would notice. The
    /// budget is asserted as a bound, not as a floor.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task MigrationPrecision_Up_KeepsTheSixteenIntegralDigitRangeOfTheOldSchema()
    {
        await _database.ResetToBeforeAsync();

        // Before: sixteen integer digits is the budget, seventeen is out of range. The probe carries
        // only two decimals, because that is what decimal(18,2) can hold exactly - a 16-digit value
        // with a fraction would round up into a 17th digit and overflow the very schema whose range
        // is being measured.
        await _database.WriteItemAsync("MP-old-16", "9999999999999999.99", "9999999999999999.99");
        await _database.AssertOverflowsAsync("99999999999999999.99");

        await _database.ApplyUpAsync();

        // After: the same budget, probed the same way, so the two are comparable.
        await _database.WriteItemAsync("MP-new-16", "9999999999999999.99", "9999999999999999.99");
        await _database.AssertOverflowsAsync("99999999999999999.99");

        // And the values the broken decimal(18,4) could never hold, now stored with four decimals.
        await _database.WriteItemAsync("MP-15", "999999999999999.9999", "999999999999999.999");
        await _database.WriteItemAsync("MP-16", "9999999999999999.9999", "9999999999999999.999");

        var fifteen = await _database.ReadItemAsync("MP-15");
        Assert.Equal(999999999999999.9999m, fifteen.Quantity);
        Assert.Equal(999999999999999.999m, fifteen.Price);

        var sixteen = await _database.ReadItemAsync("MP-16");
        Assert.Equal(9999999999999999.9999m, sixteen.Quantity);
        Assert.Equal(9999999999999999.999m, sixteen.Price);

        // And the upper bound still holds, so the range was preserved rather than inflated.
        await _database.AssertOverflowsAsync("99999999999999999.99");
    }

    /// <summary>
    /// The boundary of the old <c>decimal(18,4)</c>: fourteen integral digits was the most it ever
    /// held, so this is the value the old schema could just about store and must still store.
    /// </summary>
    [SqlServerFact]
    public async Task MigrationPrecision_Up_StoresTheFourteenDigitQuantityTheOldSchemaCouldBarelyHold()
    {
        await _database.ResetToBeforeAsync();
        await _database.ApplyUpAsync();

        await _database.WriteItemAsync("MP-14", "99999999999999.9999", "99999999999999.999");

        var item = await _database.ReadItemAsync("MP-14");
        Assert.Equal(99999999999999.9999m, item.Quantity);
        Assert.Equal(99999999999999.999m, item.Price);
    }

    /// <summary>
    /// Scenario 3: the reason the migration exists. A quantity of 0.125 was silently stored as 0.13
    /// at <c>decimal(18,2)</c>, and a price of 12.345 as 12.35, so the stock valuation and the
    /// journal entry disagreed with the document the operator typed. Three and four decimal places
    /// must survive the write unchanged - compared as decimals, not as strings, so a formatting
    /// difference cannot be mistaken for a pass.
    /// </summary>
    [SqlServerTheory]
    [InlineData("0.125", "12.345")]
    [InlineData("1.2345", "98.765")]
    [InlineData("0.0001", "0.001")]
    [InlineData("-7.7777", "-0.555")]
    [InlineData("12.5000", "1234.560")]
    public async Task MigrationPrecision_Up_RoundTripsThreeAndFourDecimalQuantitiesUnchanged(string quantity, string price)
    {
        await _database.ResetToBeforeAsync();

        // The defect, on the pre-migration schema: 0.125 does not fit in decimal(18,2), so SQL Server
        // rounds rather than rejects. Recording what it stored is what makes the post-migration
        // equality below a claim about the change rather than a tautology. The check is conditional
        // because 12.5000 genuinely did survive decimal(18,2) - its fourth decimal is a zero - and
        // asserting otherwise would be asserting something false.
        await _database.WriteItemAsync("MP-before", quantity, price);
        var before = await _database.ReadItemAsync("MP-before");
        await _database.DeleteItemAsync("MP-before");
        if (NeedsMoreThanTwoDecimals(quantity))
        {
            Assert.True(before.Quantity != decimal.Parse(quantity, CultureInfo.InvariantCulture),
                $"decimal(18,2) unexpectedly stored {quantity} exactly; the premise of the widening is wrong.");
        }

        await _database.ApplyUpAsync();
        await _database.WriteItemAsync("MP-after", quantity, price);

        var after = await _database.ReadItemAsync("MP-after");
        Assert.Equal(decimal.Parse(quantity, CultureInfo.InvariantCulture), after.Quantity);
        Assert.Equal(decimal.Parse(price, CultureInfo.InvariantCulture), after.Price);
    }

    /// <summary>
    /// Scenario 4: a lossy rollback must abort loudly, name the culprit, and change nothing. The
    /// two sample values are the ones from the incident: a quantity that rounds to a different
    /// quantity, and one that rounds to zero - the second is the dangerous one, because a stock
    /// balance of 0.0001 becoming 0.00 is indistinguishable from a legitimately empty bin.
    /// <para>
    /// The "changes nothing" half is asserted twice, and both parts matter. The catalogue proves no
    /// <c>ALTER COLUMN</c> ran, so the guard really did fire before the first one rather than after
    /// the engine had already applied some. The read-back proves the data was not rounded on the
    /// way through.
    /// </para>
    /// </summary>
    [SqlServerTheory]
    [InlineData("123.4567", "123.4600")]
    [InlineData("0.0001", "0.0000")]
    public async Task MigrationPrecision_Down_WithFractionalData_AbortsNamingTheColumnAndChangesNothing(
        string offending, string wouldBecome)
    {
        await _database.ResetToBeforeAsync();
        await _database.ApplyUpAsync();
        await _database.WriteItemAsync("MP-dirty", offending, "0");
        await _database.DeleteAllProbeItemsExcept("MP-dirty");

        var catalogBefore = await ReadDecimalCatalogAsync(_database.CreateContext());

        var error = await Assert.ThrowsAnyAsync<DbException>(() => _database.ApplyDownAsync());

        var message = error.Message;
        Assert.Contains("cannot be rolled back", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Items", message, StringComparison.Ordinal);
        Assert.Contains("CurrentQuantity", message, StringComparison.Ordinal);

        // The sample value and the figure the rollback would have written instead. An operator
        // reading only the error should know exactly what they are about to lose.
        Assert.Contains(offending, message, StringComparison.Ordinal);
        Assert.Contains(wouldBecome, message, StringComparison.Ordinal);

        var catalogAfter = await ReadDecimalCatalogAsync(_database.CreateContext());
        foreach (var (table, column) in MigrationPrecisionDatabase.WidenedColumns)
        {
            Assert.Equal(catalogBefore[(table, column)], catalogAfter[(table, column)]);
            Assert.Equal(20, (int)catalogAfter[(table, column)].Precision);
        }

        var item = await _database.ReadItemAsync("MP-dirty");
        Assert.Equal(decimal.Parse(offending, CultureInfo.InvariantCulture), item.Quantity);

        // The history row is the third thing that must be untouched, and it is the one a naive
        // implementation gets wrong: EF deletes the __EFMigrationsHistory row before it runs the
        // operations, inside the transaction the guard then aborts. Passing here means the whole
        // migration - history included - rolled back, so `dotnet ef database update` can be retried
        // after the operator fixes the data, rather than reporting success and leaving the schema
        // stranded at 20 with no migration claiming it.
        Assert.True(await _database.IsAppliedAsync(),
            "the blocked rollback removed its own __EFMigrationsHistory row; the migration would be lost.");
    }

    /// <summary>
    /// Scenario 5, and the guard must not be a blanket veto. These values are exactly what
    /// <c>decimal(18,2)</c> can hold - the negative one included, since a sign is not a fractional
    /// digit - so a rollback over them is lossless and must be allowed through, data intact.
    /// </summary>
    [SqlServerFact]
    public async Task MigrationPrecision_Down_WithOnlyRepresentableData_SucceedsAndLeavesTheDataUnchanged()
    {
        await _database.ResetToBeforeAsync();
        await _database.ApplyUpAsync();
        await _database.WriteItemAsync("MP-clean", "-123.45", "100");
        await _database.WriteItemAsync("MP-zero", "0", "0.00");
        await _database.WriteItemAsync("MP-int", "7", "3");
        await _database.DeleteAllProbeItemsExcept("MP-clean", "MP-zero", "MP-int");

        await _database.ApplyDownAsync();

        var catalog = await ReadDecimalCatalogAsync(_database.CreateContext());
        foreach (var (table, column) in MigrationPrecisionDatabase.WidenedColumns)
        {
            Assert.Equal(18, (int)catalog[(table, column)].Precision);
            Assert.Equal(2, (int)catalog[(table, column)].Scale);
        }

        Assert.Equal((-123.45m, 100m), await _database.ReadItemAsync("MP-clean"));

        // Zero is the edge worth pinning separately: it is the value a fractional quantity would be
        // rounded to, so a guard that merely tested "is there a non-zero fractional value" would let
        // the destruction through.
        Assert.Equal((0m, 0m), await _database.ReadItemAsync("MP-zero"));
        Assert.Equal((7m, 3m), await _database.ReadItemAsync("MP-int"));

        // And the mirror image: a rollback that is allowed through must retire its history row, or
        // EF would try to run it again on the next update.
        Assert.False(await _database.IsAppliedAsync(),
            "the successful rollback left its __EFMigrationsHistory row behind.");
    }

    /// <summary>
    /// Scenario 6: money was never in scope, and the control has to be a column in a table this
    /// migration <i>does</i> alter. <c>PurchaseInvoiceItems.Discount</c> and
    /// <c>SaleInvoiceItems.Discount</c> sit in tables whose Quantity, Count and UnitPrice are all
    /// widened, so a blanket widen would drag them along; asserting on <c>Payments.Amount</c> alone
    /// would pass even if the alter statement had no idea which column it was touching.
    /// </summary>
    [SqlServerFact]
    public async Task MigrationPrecision_MoneyColumnsAreNeverTouchedByThisMigration()
    {
        // Structural first: the set of columns the migration alters, read out of the migration
        // itself, must not contain a money column at all.
        var altered = MigrationPrecisionDatabase.UpAlterColumns
            .Select(o => $"{o.Table}.{o.Name}")
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(60, altered.Count);
        foreach (var (table, column) in MoneyControls())
        {
            Assert.False(altered.Contains($"{table}.{column}"), $"this migration must not alter [{table}].[{column}].");
        }

        // Then behavioural, at both ends of the migration.
        await _database.ResetToBeforeAsync();
        var before = await ReadDecimalCatalogAsync(_database.CreateContext());
        await _database.ApplyUpAsync();
        var after = await ReadDecimalCatalogAsync(_database.CreateContext());
        await _database.ApplyDownAsync();
        var reverted = await ReadDecimalCatalogAsync(_database.CreateContext());

        foreach (var (table, column) in MoneyControls())
        {
            Assert.Equal(18, (int)before[(table, column)].Precision);
            Assert.Equal(2, (int)before[(table, column)].Scale);
            Assert.Equal(before[(table, column)], after[(table, column)]);
            Assert.Equal(before[(table, column)], reverted[(table, column)]);
        }
    }

    /// <summary>
    /// The opt-in half: what one guard probe actually costs at three million rows, reported.
    /// <para>
    /// Measured on SQL Server 2025 against a 3,000,000-row <c>decimal(20,4)</c> column, cold and
    /// clean: <b>CPU 641 ms, elapsed 51 ms, 9,822 logical reads</b>, scan count 13 (the plan goes
    /// parallel, which is why the wall-clock is a tenth of the CPU time). Extrapolated across all
    /// sixty columns that is roughly 3-5 seconds of elapsed time and ~590k logical reads. That is
    /// the right trade: <c>Down()</c> is a rare, destructive, operator-initiated action, and the
    /// alternative to paying those seconds is silently rounding live financial data.
    /// </para>
    /// <para>
    /// There is deliberately no wall-clock ceiling here any more, and that is the point of the
    /// move. The old 5000 ms bound was ten times the measured 51 ms, which sounds generous until
    /// you ask what a shared CI runner's disk does to a cold three-million-row scan: the
    /// false-failure mode is a red build that means "the runner was busy", which teaches everyone
    /// to re-run red builds and is worse than no guard at all. The regression the cost claim rests
    /// on - a guard that iterates rows instead of columns - is caught exactly and deterministically
    /// by <see cref="TestEngineGuardCostShapeTests"/>, which needs no engine and no clock.
    /// </para>
    /// <para>
    /// So this reports its numbers and asserts only the thing that is not timing: that the
    /// predicate finds nothing, so the figures describe a full scan rather than an early exit. The
    /// report goes to the test output, so an opt-in run shows the drift instead of only the verdict.
    /// </para>
    /// <para>
    /// A probe-execution count used to be reported here, read from <c>sys.dm_exec_query_stats</c>,
    /// and it was removed rather than kept as a diagnostic. That counter is not a stable fact on
    /// either engine this suite supports: measured here, the matching entries appeared and then
    /// vanished between consecutive readings on LocalDB (68 entries observed mid-run, none on the
    /// next connection) and on SQL Server 2022 a probe run and the count query in the following
    /// batch found nothing at all. The DMV populates asynchronously, its entries are keyed by text
    /// rather than by database, and <c>sys.dm_exec_query_stats.dbid</c> - the one column that would
    /// scope the count to a database - does not exist before SQL Server 2025. A number that can
    /// read as sixty or as nothing at random is not a measurement, and printing one would be worse
    /// than printing none.
    /// </para>
    /// </summary>
    [SqlServerHeavyFact]
    public async Task MigrationPrecision_Guard_ReportsItsCostOverThreeMillionRows()
    {
        // A private database, not the shared fixture. Three million rows would leave every other test
        // in this class fighting a 3M-row ALTER COLUMN on teardown, and the failures that causes look
        // like real regressions in tests that have nothing to do with cost.
        const int Rows = 3_000_000;
        var name = $"NewVixPrecisionFix_{Guid.NewGuid():N}";
        await _database.CreateScratchDatabaseAsync(name);

        try
        {
            await using var scratch = await _database.OpenScratchAsync(name);
            Assert.Equal(Rows, await _database.SeedCleanItemsAsync(scratch, Rows));

            var stopwatch = Stopwatch.StartNew();
            var measured = await TimeGuardPredicateAsync(scratch, "Items", "CurrentQuantity");
            stopwatch.Stop();

            // The one non-timing assertion, and it is the one that keeps the report honest: if the
            // seeded rows were lossy the predicate would have stopped at its first match and the
            // figures below would describe a handful of reads rather than a full scan.
            Assert.False(measured.Offending,
                "the benchmark rows were seeded with lossy values, so the guard would have exited early.");

            _output.WriteLine(
                $"one guard probe over {Rows:N0} rows of [Items].[CurrentQuantity]: "
                + $"{stopwatch.ElapsedMilliseconds} ms client-side, CPU {measured.CpuMilliseconds} ms, "
                + $"{measured.LogicalReads} logical reads"
                + $"- reported, not asserted: see {nameof(SqlServerHeavyFactAttribute)} for why");
        }
        finally
        {
            await _database.DropScratchDatabaseAsync(name);
        }
    }

    /// <summary>
    /// Scenario 7, and the test that would have caught the portability defect on its first run.
    /// <para>
    /// <c>DBCC CLONEDATABASE</c> hands back a database with <c>sys.databases.is_read_only = 1</c> -
    /// on LocalDB and on a full engine alike - and every write to it then fails with
    /// <c>Failed to update database "..." because the database is read-only</c>. That message names
    /// a database nobody asked about and never mentions the engine, which is why the failure read
    /// like a migration problem and cost a diagnosis to place. The helper clears the flag and
    /// verifies it; this test verifies the consequence by writing, on whatever engine is present.
    /// </para>
    /// <para>
    /// It writes rather than reads <c>is_read_only</c> because the flag and the ability are not the
    /// same claim, and only a write can tell them apart. The <c>UPDATE</c> is there for the same
    /// reason one level down: a database that accepts new pages but cannot modify existing ones is a
    /// state the flag alone would have called healthy.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task MigrationPrecision_ScratchDatabaseFromClone_IsWritableAndKeepsWhatItIsGiven()
    {
        var name = $"NewVixPrecisionFix_{Guid.NewGuid():N}";
        await _database.CreateScratchDatabaseAsync(name);

        try
        {
            await using var scratch = await _database.OpenScratchAsync(name);

            // CREATE TABLE rides along with the INSERT rather than replacing it: a clone still in a
            // restoring state accepts some DDL and refuses data changes, so the table's existence
            // narrows the diagnosis without ever standing in for it.
            await using (var create = scratch.CreateCommand())
            {
                create.CommandTimeout = 120;
                create.CommandText =
                    "CREATE TABLE [NvsWritabilityProbe] ([Id] int NOT NULL PRIMARY KEY, "
                    + "[Note] nvarchar(64) NOT NULL); "
                    + "INSERT INTO [NvsWritabilityProbe] ([Id], [Note]) VALUES (1, N'writable');";
                await create.ExecuteNonQueryAsync();
            }

            await using (var update = scratch.CreateCommand())
            {
                update.CommandText = "UPDATE [NvsWritabilityProbe] SET [Note] = N'updated' WHERE [Id] = 1;";
                await update.ExecuteNonQueryAsync();
            }

            await using (var read = scratch.CreateCommand())
            {
                read.CommandText = "SELECT [Note] FROM [NvsWritabilityProbe] WHERE [Id] = 1;";
                Assert.Equal("updated", (string?)await read.ExecuteScalarAsync());
            }
        }
        finally
        {
            await _database.DropScratchDatabaseAsync(name);
        }
    }

    /// <summary>
    /// Times the guard's predicate and reports the engine's own counters.
    /// <para>
    /// The counters come from <c>sys.dm_exec_query_stats</c>, not from <c>SET STATISTICS IO/TIME ON</c>.
    /// That is deliberate: the <c>SET STATISTICS</c> output reaches the client as info messages whose
    /// delivery depends on the connection's event plumbing, and when it does not arrive the numbers
    /// silently come back as zero. A performance guard that reports 0 on a broken read is worse than
    /// no guard, because it looks like a measurement. The plan cache always has the row.
    /// </para>
    /// </summary>
    private static async Task<(bool Offending, long CpuMilliseconds, long LogicalReads)>
        TimeGuardPredicateAsync(DbConnection connection, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(*) FROM [{table}] WHERE [{column}] <> ROUND([{column}], 2);
            """;
        var offending = false;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                offending = reader.GetInt32(0) > 0;
            }
        }

        // execution_count = 1 pins this to the statement just issued, so a concurrent workload on the
        // instance cannot lend us its numbers. sys.dm_exec_query_stats carries no text of its own, so
        // the statement text has to be pulled in with the table-valued form
        // sys.dm_exec_sql_text(sql_handle), which CROSS APPLY supplies per row. Joining the DMV as
        // if it were a table is a syntax error here, not a style preference.
        //
        // The LIKE pattern is built from a column name this test owns, never from input, so
        // interpolating it is safe; the bracket-quoting on the other side of the pattern would be
        // read as a parameter placeholder, hence the bare name.
        await using var stats = connection.CreateCommand();
        stats.CommandText = $"""
            SELECT TOP (1)
                [qs].[total_worker_time] / 1000 AS [CpuMs],
                [qs].[total_logical_reads] AS [LogicalReads]
            FROM sys.dm_exec_query_stats AS [qs]
            CROSS APPLY sys.dm_exec_sql_text([qs].[sql_handle]) AS [st]
            WHERE [qs].[execution_count] = 1
              AND [st].[text] LIKE N'%{column}%ROUND%'
            ORDER BY [qs].[total_worker_time] DESC;
            """;
        await using var statsReader = await stats.ExecuteReaderAsync();
        if (!await statsReader.ReadAsync())
        {
            throw new InvalidOperationException(
                "لم يُعثر على سجلّ إحصاءات للعبارة المقاسة؛ لا يمكن تأكيد قياس التكلفة.");
        }

        return (offending, statsReader.GetInt64(0), statsReader.GetInt64(1));
    }

    /// <summary>
    /// True when the figure carries a non-zero digit past the second decimal, i.e. when
    /// <c>decimal(18,2)</c> really did lose information by storing it. 12.5000 is representable at
    /// scale 2, so a blanket "the old schema always mangled it" claim would be false for it.
    /// </summary>
    private static bool NeedsMoreThanTwoDecimals(string figure)
    {
        var dot = figure.IndexOf('.');
        if (dot < 0)
        {
            return false;
        }

        return figure[(dot + 1)..].Length > 2 && figure[(dot + 3)..].Any(c => c != '0');
    }

    private static IReadOnlyList<(string Table, string Column)> MoneyControls() => [
        ("PurchaseInvoiceItems", "Discount"),
        ("SaleInvoiceItems", "Discount"),
        ("Payments", "Amount"),
        ("SaleInvoices", "NetAmount"),
        ("PurchaseInvoices", "NetAmount"),
        ("JournalEntryLines", "Debit"),
        ("JournalEntryLines", "Credit"),
        ("Suppliers", "OpeningBalance"),
    ];

    private static async Task<Dictionary<(string Table, string Column), (byte Precision, byte Scale)>> ReadDecimalCatalogAsync(
        AppDbContext db)
    {
        var catalog = new Dictionary<(string, string), (byte, byte)>();
        var connection = await OpenAsync(db);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TABLE_NAME, COLUMN_NAME, NUMERIC_PRECISION, NUMERIC_SCALE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo' AND DATA_TYPE = 'decimal';
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            catalog[(reader.GetString(0), reader.GetString(1))] =
                (Convert.ToByte(reader.GetValue(2), CultureInfo.InvariantCulture),
                 Convert.ToByte(reader.GetValue(3), CultureInfo.InvariantCulture));
        }

        return catalog;
    }

    private static async Task<DbConnection> OpenAsync(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        return connection;
    }
}

/// <summary>
/// The <see cref="TheoryAttribute"/> counterpart of <see cref="SqlServerFactAttribute"/>, declared
/// here rather than beside it so this file adds to the suite instead of editing a file another class
/// owns. Same contract: a declared skip, never a silent one.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SqlServerTheoryAttribute : TheoryAttribute
{
    public SqlServerTheoryAttribute()
    {
        if (SqlServerTestTarget.Resolve() is null)
        {
            Skip = SqlServerTestTarget.SkipReason;
        }
    }
}

/// <summary>
/// <c>[Fact]</c> for a measurement too costly and too machine-dependent to belong in the default
/// suite, but not for one that should be deleted: a correctness claim that holds in principle and
/// is merely expensive belongs here, where it runs on request and reports its number.
/// <para>
/// Two gates, and both announce themselves. No engine is
/// <see cref="SqlServerTestTarget.SkipReason"/>; the opt-in is
/// <see cref="SqlServerTestTarget.HeavyMeasurementSkipReason"/>, which names the variable and says
/// what the default suite still checks in its place. A test that is silently absent is worse than
/// one that is loudly optional, so neither gate is a bare <c>Skip = ""</c>.
/// </para>
/// <para>
/// It is not an <c>ExecutionCondition</c>, deliberately. An xUnit condition can cancel a test whose
/// fixture is already half-built - which for this class means a 3M-row database left behind - where
/// an attribute's skip is decided before the class fixture is asked to do any work at all.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SqlServerHeavyFactAttribute : FactAttribute
{
    public SqlServerHeavyFactAttribute()
    {
        if (SqlServerTestTarget.Resolve() is null)
        {
            Skip = SqlServerTestTarget.SkipReason;
        }
        else if (!SqlServerTestTarget.HeavyMeasurementsEnabled)
        {
            Skip = SqlServerTestTarget.HeavyMeasurementSkipReason;
        }
    }
}

/// <summary>
/// One database for the whole class, because applying the 38-migration chain takes about seven
/// seconds and this class would otherwise pay that once per test. The pair of states the tests need
/// - before the widening and after it - are exactly what the migration toggles between, so
/// <see cref="ResetToBeforeAsync"/> re-establishes the starting point by rolling that one migration
/// back rather than rebuilding the schema.
/// </summary>
public sealed class MigrationPrecisionDatabase : IAsyncLifetime
{
    private const string _widen = "20260929141121_WidenQuantityAndUnitPricePrecision";
    private const string _before = "20260928081433_AddReturnRowVersion";

    private string? _databaseName;
    private int _itemTypeId;
    private int _categoryId;

    /// <summary>
    /// The <c>AlterColumn</c> operations <c>Up()</c> emits, read out of the migration object rather
    /// than out of a model. <c>Up()</c> itself is <c>protected</c>, so <c>UpOperations</c> is the
    /// supported way to look at what the migration will do, and it is the same list the engine gets.
    /// </summary>
    public static IReadOnlyList<AlterColumnOperation> UpAlterColumns { get; } =
        new WidenQuantityAndUnitPricePrecision().UpOperations.OfType<AlterColumnOperation>().ToList();

    /// <summary>
    /// The 60 columns the migration widens, which is also how many probe executions a clean rollback
    /// costs. That equality is the whole cost claim: the guard loops over columns, so a database with
    /// three million rows pays the same number of executions as one with three rows. It is asserted
    /// against the guard's own text by <see cref="TestEngineGuardCostShapeTests"/>.
    /// </summary>
    public static IReadOnlyList<(string Table, string Column)> WidenedColumns { get; } =
        UpAlterColumns.Select(o => (o.Table, o.Name)).ToList();

    /// <summary>
    /// The raw T-SQL batches <c>Down()</c> emits alongside its <c>AlterColumn</c> operations. There
    /// is exactly one, and it is the lossy-rollback guard; exposing it is what lets the cost of the
    /// guard be asserted as a property of the guard's own text, on a machine with no SQL Server at
    /// all. <c>Down()</c> is <c>protected</c>, so <c>DownOperations</c> is the supported way in.
    /// </summary>
    public static IReadOnlyList<SqlOperation> DownSqlOperations { get; } =
        new WidenQuantityAndUnitPricePrecision().DownOperations.OfType<SqlOperation>().ToList();

    /// <summary>The guard batch, as the engine receives it.</summary>
    public static string GuardSql =>
        DownSqlOperations.Count == 1
            ? DownSqlOperations[0].Sql
            : throw new InvalidOperationException(
                $"the rollback guard must be one batch; found {DownSqlOperations.Count}.");

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        if (SqlServerTestTarget.Resolve() is null)
        {
            // No engine anywhere. Every test in this class is [SqlServerFact] and will be reported
            // as skipped with SqlServerTestTarget.SkipReason - but xUnit builds a class fixture
            // before it evaluates any skip, so throwing here would turn a declared skip into
            // thirteen failures on exactly the machine that has no SQL Server to skip for.
            return;
        }

        _databaseName = $"NewVixPrecisionFix_{Guid.NewGuid():N}";

        using var db = CreateContext();
        await db.Database.MigrateAsync(_before);

        // Items needs both lookups to exist before any row can be inserted, and no test wants to
        // reason about that.
        await using var connection = await OpenAsync();
        await using var lookup = connection.CreateCommand();
        lookup.CommandText = """
            INSERT INTO [ItemTypes] ([Name], [IsActive]) VALUES (N'precision-probe', 1);
            INSERT INTO [ItemCategories] ([Name], [IsActive]) VALUES (N'precision-probe', 1);
            """;
        await lookup.ExecuteNonQueryAsync();

        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT (SELECT MIN([Id]) FROM [ItemTypes]), (SELECT MIN([Id]) FROM [ItemCategories]);";
        await using var reader = await read.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        _itemTypeId = reader.GetInt32(0);
        _categoryId = reader.GetInt32(1);
    }

    public async Task DisposeAsync()
    {
        if (_databaseName is null)
        {
            return;
        }

        if (_databaseName.Contains(']') || _databaseName.Contains('['))
        {
            throw new InvalidOperationException($"اسم قاعدة غير صالح للحذف: {_databaseName}");
        }

        SqlConnection.ClearAllPools();
        using var connection = new SqlConnection(
            SqlServerTestTarget.WithDatabase(SqlServerTestTarget.ResolveRequired(), "master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID(N'{_databaseName}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{_databaseName}]; END";
        await command.ExecuteNonQueryAsync();
        _databaseName = null;
    }

    /// <summary>
    /// Back to the pre-widen schema with no probe rows left, so every test starts from the same
    /// place whatever ran before it. The probe rows go first: the guard refuses to roll back over
    /// fractional data, so resetting after a deliberately-dirty test would otherwise trip on the
    /// previous test's leftovers.
    /// </summary>
    public async Task ResetToBeforeAsync()
    {
        await DeleteAllProbeItemsAsync();
        if (await IsAppliedAsync())
        {
            await ApplyDownAsync();
        }
    }

    public async Task ApplyUpAsync()
    {
        using var db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(_widen);
    }

    public async Task ApplyDownAsync()
    {
        using var db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(_before);
    }

    public async Task<bool> IsAppliedAsync()
    {
        using var db = CreateContext();
        return (await db.Database.GetAppliedMigrationsAsync()).Contains(_widen, StringComparer.Ordinal);
    }

    public async Task WriteItemAsync(
        string code,
        string quantity,
        string price,
        byte quantityPrecision = 20,
        byte quantityScale = 4,
        byte pricePrecision = 20,
        byte priceScale = 3)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [Items]
                ([Code], [Name], [ItemTypeId], [CategoryId], [IsSellable], [IsActive], [CreatedAt],
                 [CurrentQuantity], [CurrentCount], [MinQuantity], [MinCount], [ReservedQuantity], [ReservedCount],
                 [PurchasePrice], [SalePrice])
            VALUES
                (@code, @code, @itemType, @category, 1, 1, SYSDATETIME(),
                 @quantity, 0, 0, 0, 0, 0, @price, @price);
            """;
        command.Parameters.Add(new SqlParameter("@code", SqlDbType.NVarChar) { Value = code });
        command.Parameters.Add(new SqlParameter("@itemType", SqlDbType.Int) { Value = _itemTypeId });
        command.Parameters.Add(new SqlParameter("@category", SqlDbType.Int) { Value = _categoryId });

        // Typed decimals, not strings or doubles: a 16-digit quantity does not survive a float, and
        // the point of these tests is that the engine receives the exact value. The precision is a
        // parameter so the overflow probes can widen the parameter to decimal(28,4) and let the
        // column - rather than the client-side parameter check - be the thing that rejects.
        command.Parameters.Add(new SqlParameter("@quantity", SqlDbType.Decimal)
        {
            Value = decimal.Parse(quantity, CultureInfo.InvariantCulture),
            Precision = quantityPrecision,
            Scale = quantityScale
        });
        command.Parameters.Add(new SqlParameter("@price", SqlDbType.Decimal)
        {
            Value = decimal.Parse(price, CultureInfo.InvariantCulture),
            Precision = pricePrecision,
            Scale = priceScale
        });

        await command.ExecuteNonQueryAsync();
    }

    public async Task<(decimal Quantity, decimal Price)> ReadItemAsync(string code)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT [CurrentQuantity], [SalePrice] FROM [Items] WHERE [Code] = @code;";
        command.Parameters.Add(new SqlParameter("@code", SqlDbType.NVarChar) { Value = code });

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"probe item [{code}] is not in the table.");
        return (reader.GetDecimal(0), reader.GetDecimal(1));
    }

    public Task DeleteItemAsync(string code) => ExecuteAsync($"DELETE FROM [Items] WHERE [Code] = N'{code}';");

    public Task DeleteAllProbeItemsAsync() => ExecuteAsync("DELETE FROM [Items];");

    /// <summary>Opens a raw connection to the fixture database, for tests that bypass EF.</summary>
    public async Task<DbConnection> OpenAsync()
    {
        var connection = CreateContext().Database.GetDbConnection();
        await connection.OpenAsync();
        return connection;
    }

    public Task<int> GetItemTypeIdAsync() => LookupIdAsync("ItemTypes");

    public Task<int> GetCategoryIdAsync() => LookupIdAsync("ItemCategories");

    private async Task<int> LookupIdAsync(string table)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT TOP (1) [Id] FROM [{table}] ORDER BY [Id];";
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Clones the fixture database into a fresh throwaway one, so a test can fill it with millions
    /// of rows without the other tests in the class inheriting them.
    /// </summary>
    public async Task CreateScratchDatabaseAsync(string name)
    {
        GuardDatabaseName(name);

        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(
            SqlServerTestTarget.WithDatabase(SqlServerTestTarget.ResolveRequired(), "master"));
        await master.OpenAsync();
        await using (var create = master.CreateCommand())
        {
            create.CommandTimeout = 300;
            create.CommandText = $"DBCC CLONEDATABASE (N'{_databaseName}', N'{name}') WITH NO_STATISTICS;";
            await create.ExecuteNonQueryAsync();
        }

        // Two things about a CLONEDATABASE clone, both measured rather than assumed:
        //
        //   * it comes back with sys.databases.is_read_only = 1, on LocalDB and on a full engine
        //     alike, because the clone is a restored snapshot. Every write to it then fails with
        //     "Failed to update database "..." because the database is read-only", which names the
        //     database and never the engine;
        //   * SET READ_WRITE is what clears that flag, and it is a no-op on an engine that already
        //     has it clear - measured on both engines, where the flag went 1 -> 0 and writes
        //     through afterwards.
        //
        // SET RECOVERY SIMPLE clears the restoring state rather than the flag, and is kept because a
        // clone left in restoring is not a database the rest of this fixture could use. Which of the
        // two statements is load-bearing was measured on LocalDB, where omitting SET READ_WRITE
        // fails the whole class outright; on SQL Server 2022 that omission was not reproduced, so
        // neither statement is claimed to be necessary in isolation. The flag is checked afterwards
        // either way, which is what makes the question moot.
        await using (var recover = master.CreateCommand())
        {
            recover.CommandTimeout = 300;
            recover.CommandText = $"ALTER DATABASE [{name}] SET RECOVERY SIMPLE; ALTER DATABASE [{name}] SET READ_WRITE;";
            await recover.ExecuteNonQueryAsync();
        }

        await AssertCloneIsWritableAsync(master, name);
    }

    /// <summary>
    /// Fails here, naming the engine, rather than three million rows later with an engine-shaped
    /// <see cref="SqlException"/> that says nothing about the engine.
    /// <para>
    /// This is the check that makes the failure legible rather than merely early. If a future
    /// engine, or a future LocalDB, refuses to take a clone out of read-only, the person reading
    /// the red build is told which engine they are on, that the clone is the read-only thing, and
    /// that pointing <c>NVS_TEST_SQLSERVER</c> at a full engine is the way out. A missing database
    /// is the same kind of dead end and is reported in the same breath.
    /// </para>
    /// </summary>
    private static async Task AssertCloneIsWritableAsync(SqlConnection master, string name)
    {
        await using var check = master.CreateCommand();
        check.CommandText =
            "SELECT CONVERT(int, [is_read_only]) FROM sys.databases WHERE [database_id] = DB_ID(@name);";
        check.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar) { Value = name });

        var readOnly = await check.ExecuteScalarAsync();
        if (readOnly is null)
        {
            throw new InvalidOperationException(
                $"قاعدة النسخة [{name}] غير موجودة بعد CLONEDATABASE. المحرّك: "
                + $"{SqlServerTestTarget.DescribeEngine()}.");
        }

        if (Convert.ToInt32(readOnly, CultureInfo.InvariantCulture) != 1)
        {
            return;
        }

        throw new InvalidOperationException(
            $"نسخة CLONEDATABASE [{name}] ما زالت للقراءة فقط بعد ALTER DATABASE ... SET READ_WRITE "
            + $"(sys.databases.is_read_only = 1). المحرّك: {SqlServerTestTarget.DescribeEngine()}. "
            + "المحرّك لا يسمح بتحويل نسخة CLONEDATABASE إلى وضع الكتابة، ولا يمكن للاختبارات التي "
            + $"تملأ قاعدةً مؤقتة أن تعمل عليه. استخدم {SqlServerTestTarget.ConnectionStringVariable} "
            + "للإشارة إلى محرّك SQL Server كامل.");
    }

    /// <summary>
    /// Runs the guard's own batch against the fixture database, exactly as a rollback would. It is
    /// a no-op on the schema - the guard only reads and throws - so it can be run against a live
    /// fixture without disturbing it, and it throws on its own if a row is lossy, which is why the
    /// callers seed exactly representable values first.
    /// </summary>
    public async Task RunGuardAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 300;
        command.CommandText = GuardSql;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Fills <c>Items</c> with <paramref name="rows"/> values that are exactly representable at
    /// <c>decimal(18,2)</c>, and returns the number of rows in the table afterwards.
    /// <para>
    /// The representability is the point, not a convenience. A single lossy value would let the
    /// guard exit at the first offending column, so a cost or execution-count measurement taken
    /// after it would describe an early exit rather than the full scan it claims to measure.
    /// </para>
    /// <para>
    /// The lookup rows are re-seeded from whatever the clone carried rather than trusted from it:
    /// whether they come across is a detail of <c>CLONEDATABASE</c>, and neither caller should have
    /// to know it.
    /// </para>
    /// </summary>
    public async Task<int> SeedCleanItemsAsync(int rows)
    {
        await using var connection = await OpenAsync();
        return await SeedCleanItemsAsync(connection, rows);
    }

    /// <summary>The same seeding, against a caller-supplied connection - a scratch clone's.</summary>
    public async Task<int> SeedCleanItemsAsync(DbConnection connection, int rows)
    {
        if (rows < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), rows, "عدد الصفوف لا يمكن أن يكون سالبًا.");
        }

        await using var seed = connection.CreateCommand();
        seed.CommandTimeout = 600;
        seed.CommandText = $"""
            DECLARE @itemType int = (SELECT MIN([Id]) FROM [ItemTypes]);
            DECLARE @category int = (SELECT MIN([Id]) FROM [ItemCategories]);
            IF @itemType IS NULL
            BEGIN
                INSERT INTO [ItemTypes] ([Name], [IsActive]) VALUES (N'cost-probe', 1);
                SET @itemType = CONVERT(int, SCOPE_IDENTITY());
            END;
            IF @category IS NULL
            BEGIN
                INSERT INTO [ItemCategories] ([Name], [IsActive]) VALUES (N'cost-probe', 1);
                SET @category = CONVERT(int, SCOPE_IDENTITY());
            END;

            WITH [n] AS (
                SELECT TOP ({rows}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS [i]
                FROM sys.all_objects a CROSS JOIN sys.all_objects b
            )
            INSERT INTO [Items]
                ([Code], [Name], [ItemTypeId], [CategoryId], [IsSellable], [IsActive], [CreatedAt],
                 [CurrentQuantity], [CurrentCount], [MinQuantity], [MinCount], [ReservedQuantity],
                 [ReservedCount], [PurchasePrice], [SalePrice])
            SELECT
                CONCAT(N'BULK-', [i]), CONCAT(N'bulk-', [i]), @itemType, @category, 1, 1,
                SYSDATETIME(), 2.0000, 0, 0, 0, 0, 0, 2.0000, 2.0000
            FROM [n];

            SELECT COUNT(*) AS [RowsSeeded] FROM [Items];
            """;
        await using var reader = await seed.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("لم يُعثر على عدد الصفوف بعد الزرع.");
        }

        return reader.GetInt32(0);
    }

    public async Task<DbConnection> OpenScratchAsync(string name)
    {
        GuardDatabaseName(name);
        var connection = new SqlConnection(
            SqlServerTestTarget.WithDatabase(SqlServerTestTarget.ResolveRequired(), name));
        await connection.OpenAsync();
        return connection;
    }

    public async Task DropScratchDatabaseAsync(string name)
    {
        GuardDatabaseName(name);

        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(
            SqlServerTestTarget.WithDatabase(SqlServerTestTarget.ResolveRequired(), "master"));
        await master.OpenAsync();
        await using var drop = master.CreateCommand();
        drop.CommandTimeout = 300;
        drop.CommandText =
            $"IF DB_ID(N'{name}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{name}]; END";
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// These names reach <c>ALTER DATABASE</c> and <c>DROP DATABASE</c> as identifiers, and a
    /// database name is the one thing that cannot be parameterised. The guard is what makes the
    /// interpolation in the callers acceptable, and it is deliberately paranoid: nothing but the
    /// prefix this test class invents may pass.
    /// </summary>
    private static void GuardDatabaseName(string name)
    {
        if (!name.StartsWith("NewVixPrecisionFix_", StringComparison.Ordinal)
            || name.Any(c => c is ']' or '[' or '\'' or ';' or ' ')
            || name.Length > 128)
        {
            throw new InvalidOperationException($"اسم قاعدة غير صالح للاستخدام في الاختبار: {name}");
        }
    }

    /// <summary>
    /// One DELETE with a NOT IN list, not one DELETE per kept row: three separate
    /// <c>WHERE Code &lt;&gt; 'a'</c> statements would each delete the rows the other two were keeping.
    /// </summary>
    public Task DeleteAllProbeItemsExcept(params string[] keep) =>
        ExecuteAsync($"DELETE FROM [Items] WHERE [Code] NOT IN ({string.Join(", ", keep.Select(k => $"N'{k}'"))});");

    /// <summary>
    /// Proves the value really was out of range on the schema as it stands, so a later acceptance is
    /// a change in behaviour rather than a value that was always in range.
    /// <para>
    /// The parameters are declared <c>decimal(28,4)</c> - the widest type SQL Server has - on purpose.
    /// If they were declared at the column's own precision the client would reject the value in
    /// <c>TdsParser</c> and raise <see cref="ArgumentException"/> before a single byte went to the
    /// server, which would prove nothing about the schema. Widening the parameter hands the decision
    /// to the engine's implicit conversion, which is the behaviour the test is about.
    /// </para>
    /// </summary>
    public async Task AssertOverflowsAsync(params string[] quantities)
    {
        foreach (var quantity in quantities)
        {
            var error = await Assert.ThrowsAnyAsync<DbException>(
                () => WriteItemAsync("MP-rejected", quantity, "0", 28, 4, 28, 3));
            Assert.Contains("overflow", error.Message, StringComparison.OrdinalIgnoreCase);
            await DeleteItemAsync("MP-rejected");
        }
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private string ConnectionString =>
        SqlServerTestTarget.WithDatabase(SqlServerTestTarget.ResolveRequired(), _databaseName!);
}
