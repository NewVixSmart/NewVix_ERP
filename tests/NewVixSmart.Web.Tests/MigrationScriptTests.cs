using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NewVixSmart.Web.Data;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// الاختبارات السابقة كانت تبني المخطط بـ<code>EnsureCreated()</code> على SQLite، فلا تُنفَّذ
/// سلسلة الترحيلات (<c>Migrate()</c>) ولا مرّة واحدة: أي migration يكسر SQL Server كان خفيًّا عن CI.
/// هذا الملف يقرؤ السلسلة نفسها التي سيطبّقها الإنتاج، ويقرأ SQL الذي يولّده مزوّد SQL Server،
/// فيفحصه دون أي قاعدة بيانات ودون أي اتصال — فحص حتمي وقابل لإعادة الإنتاج.
///
/// ما لا يستطيع هذا الملف إثباته: أن SQL Server يقبل التنفيذ فعليًّا. لذلك يكمّله
/// <c>MigrationChainSqlServerTests</c> الذي يطبّق السلسلة على محرّك حقيقي.
/// </summary>
public sealed class MigrationScriptTests
{
    /// <summary>
    /// آخر migration أضافت رمز نسخ لمرتجعَي البيع والشراء. يجب أن يبقى أثرها ظاهرًا في السلسلة.
    /// </summary>
    private const string AddReturnRowVersion = "20260928081433_AddReturnRowVersion";

    /// <summary>
    /// آخر migration: توسيع دقّة الكمية والسعر. 60 جملة <c>ALTER COLUMN</c> على 16 جدولًا.
    /// </summary>
    private const string WidenPrecision = "20260929141121_WidenQuantityAndUnitPricePrecision";

    private const int WidenAlterColumnStatements = 60;
    private const int WidenAlterColumnTables = 16;

    /// <summary>
    /// سياق بلا اتصال: توليد السCRIPT لا يحتاج خادمًا، لكنه يحتاج مزوّد SQL Server نفسه، وإلا
    /// لولّد SQLite نصًّا لا يمثّل ما يُطبَّق في الإنتاج.
    /// </summary>
    private static AppDbContext CreateSqlServerModelContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=never-connected;Database=ScriptOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new AppDbContext(options);
    }

    private static string GenerateScript(MigrationsSqlGenerationOptions options)
    {
        return GenerateScript(null, null, options);
    }

    /// <summary>
    /// نص <paramref name="fromMigration"/>-exclusive إلى <paramref name="toMigration"/>-inclusive.
    /// توليد جزء بعينه من السلسلة هو ما يجعل "قبل" و"بعد" حقيقيَّين بدل التخمين.
    /// </summary>
    private static string GenerateScript(string? fromMigration, string? toMigration, MigrationsSqlGenerationOptions options)
    {
        using var db = CreateSqlServerModelContext();
        return db.Database.GetService<IMigrator>().GenerateScript(fromMigration, toMigration, options);
    }

    private static IReadOnlyList<string> MigrationChain()
    {
        using var db = CreateSqlServerModelContext();
        return db.Database.GetMigrations().ToList();
    }

    [Fact]
    public void TheChainIsDiscovered_NotEmpty_AndOrderedOldestFirst()
    {
        var chain = MigrationChain();

        Assert.NotEmpty(chain);
        // Oldest first: EF returns them in the order they must be applied.
        Assert.Equal(chain.OrderBy(id => id, StringComparer.Ordinal).ToList(), chain);
        Assert.Equal(chain.Distinct(StringComparer.Ordinal).Count(), chain.Count);
        Assert.Equal("20260827123602_InitialCreate", chain[0]);
    }

    [Fact]
    public void IdempotentScript_CarriesTheHistoryGuard_AndEveryMigrationId()
    {
        var chain = MigrationChain();
        var script = GenerateScript(MigrationsSqlGenerationOptions.Idempotent);

        Assert.Contains("CREATE TABLE [__EFMigrationsHistory]", script, StringComparison.Ordinal);
        Assert.Contains("IF NOT EXISTS (", script, StringComparison.Ordinal);
        Assert.Contains("[MigrationId]", script, StringComparison.Ordinal);
        Assert.Contains("[ProductVersion]", script, StringComparison.Ordinal);

        var missing = chain
            .Where(id => !script.Contains($"N'{id}'", StringComparison.Ordinal))
            .ToList();
        Assert.True(missing.Count == 0, "Migrations absent from the idempotent script:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void TheTwoNewestMigrations_ArePresentInTheChain()
    {
        var chain = MigrationChain();

        Assert.Contains(AddReturnRowVersion, chain);
        Assert.Contains(WidenPrecision, chain);
        Assert.True(
            chain.ToList().IndexOf(AddReturnRowVersion) < chain.ToList().IndexOf(WidenPrecision),
            "AddReturnRowVersion must be applied before WidenQuantityAndUnitPricePrecision.");
    }

    /// <summary>
    /// جوهر هذا الملف: لا تضييق لعمود عبر السلسلة كلها.
    /// التوسيع آمن على بيانات حيّة (القيم القديمة تُخزَّن كما هي)، أما التضييق فيقصّ بصمت أو يفشل
    /// عند التنفيذ. نقيسه على النص المولَّد فعليًّا: نتتبّع نوع كل عمود من أول <c>CREATE TABLE</c>
    /// حتى آخر <c>ALTER COLUMN</c>، ونتأكد أن كل تغيير إما إلى نفس النوع أو إلى نوع أعرض.
    /// </summary>
    [Fact]
    public void NoMigration_NarrowsAnExistingColumn()
    {
        var script = GenerateScript(MigrationsSqlGenerationOptions.Default);
        var seen = new Dictionary<(string Table, string Column), string>();
        var narrowed = new List<string>();
        var applied = 0;

        foreach (var statement in ColumnStatements.From(script))
        {
            switch (statement.Kind)
            {
                case ColumnStatementKind.Created:
                case ColumnStatementKind.Added:
                    seen[(statement.Table, statement.Column)] = statement.Type;
                    applied++;
                    break;

                case ColumnStatementKind.Renamed:
                    if (seen.TryGetValue((statement.Table, statement.From), out var renamedType))
                    {
                        seen.Remove((statement.Table, statement.From));
                        seen[(statement.Table, statement.Column)] = renamedType;
                    }

                    break;

                case ColumnStatementKind.Dropped:
                    seen.Remove((statement.Table, statement.Column));
                    break;

                case ColumnStatementKind.AlterColumn:
                    applied++;
                    if (!seen.TryGetValue((statement.Table, statement.Column), out var oldType))
                    {
                        // An ALTER COLUMN for a column this analysis never saw created means the
                        // script is not the shape we think it is; say so instead of passing quietly.
                        narrowed.Add($"{statement.Table}.{statement.Column}: ALTER COLUMN on an unknown column ({statement.Type})");
                        break;
                    }

                    if (ColumnType.IsNarrowing(oldType, statement.Type))
                    {
                        narrowed.Add($"{statement.Table}.{statement.Column}: {oldType} -> {statement.Type}");
                    }

                    seen[(statement.Table, statement.Column)] = statement.Type;
                    break;

                case ColumnStatementKind.TableDropped:
                    foreach (var key in seen.Keys.Where(k => k.Table == statement.Table).ToList())
                    {
                        seen.Remove(key);
                    }

                    break;
            }
        }

        Assert.True(applied > 0, "لم يُحلَّل أي عمود: تحلّل نص السلسلة مكسور.");
        Assert.True(narrowed.Count == 0,
            "تضييق عمود عبر الترحيلات يفقد بيانات حيّة بصمت:" + Environment.NewLine + string.Join(Environment.NewLine, narrowed));
    }

    [Fact]
    public void WidenPrecisionMigration_WidensScaleOnSixtyColumnsAcrossSixteenTables()
    {
        var chain = MigrationChain();
        var index = chain.ToList().IndexOf(WidenPrecision);
        Assert.True(index > 0, "The widening migration must not be the first one in the chain.");
        var beforeMigration = chain[index - 1];

        // Scope: only this migration, so the count is the migration's and not the chain's. If a
        // future migration also alters a column, this stays true and stays meaningful.
        var wideningScript = GenerateScript(beforeMigration, WidenPrecision, MigrationsSqlGenerationOptions.Default);
        var widening = ColumnStatements.From(wideningScript)
            .Where(s => s.Kind == ColumnStatementKind.AlterColumn)
            .ToList();

        Assert.Equal(WidenAlterColumnStatements, widening.Count);
        Assert.Equal(WidenAlterColumnTables, widening.Select(s => s.Table).Distinct().Count());

        var scales = widening
            .Select(s => ColumnType.Parse(s.Type))
            .Where(t => t.Family == ColumnTypeFamily.Decimal)
            .ToList();
        Assert.NotEmpty(scales);
        Assert.All(scales, t => Assert.Equal(18, t.Precision));
        Assert.All(scales, t => Assert.Contains(t.Scale, new[] { 3, 4 }));

        // "Widening" is only proven if the old type is visible in the chain *before* this
        // migration; the whole-chain text is not evidence of that.
        var before = ColumnStatements.From(GenerateScript(null, beforeMigration, MigrationsSqlGenerationOptions.Default))
            .Where(s => s.Kind is ColumnStatementKind.Created or ColumnStatementKind.Added or ColumnStatementKind.AlterColumn)
            .ToDictionary(s => (s.Table, s.Column), s => s.Type);
        var compared = widening.Where(s => before.ContainsKey((s.Table, s.Column))).ToList();
        Assert.Equal(WidenAlterColumnStatements, compared.Count);
        Assert.All(compared, s => Assert.Equal(
            ColumnTypeFamily.Decimal,
            ColumnType.Parse(before[(s.Table, s.Column)]).Family));
        Assert.All(compared, s => Assert.False(
            ColumnType.IsNarrowing(before[(s.Table, s.Column)], s.Type),
            $"{s.Table}.{s.Column}: {before[(s.Table, s.Column)]} ← {s.Type}"));
    }

    /// <summary>
    /// النص المولَّد لـ SQL Server لا يجوز أن يحمل بناءً لا يفهمه SQL Server. هذه القائمة هي
    /// ما كان SQLite في الغالب أن يبتلعه بصمت لو كانت السلسلة تُطبَّق عليه.
    /// </summary>
    [Theory]
    [InlineData("PRAGMA foreign_keys")]
    [InlineData("AUTOINCREMENT")]
    [InlineData("sqlite_")]
    [InlineData("INSERT OR REPLACE")]
    [InlineData("INSERT OR IGNORE")]
    [InlineData("datetime('now')")]
    [InlineData("julianday(")]
    [InlineData("INTEGER PRIMARY KEY")]
    [InlineData("`")]
    public void SqlServerScript_ContainsNoSqliteOnlyConstruct(string forbidden)
    {
        var script = GenerateScript(MigrationsSqlGenerationOptions.Default);

        Assert.DoesNotContain(forbidden, script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqlServerScript_IsSubstantial_AndUsesSqlServerDdl()
    {
        var script = GenerateScript(MigrationsSqlGenerationOptions.Default);

        // A guard against a silently empty or truncated script: without real DDL in it, every
        // assertion above would pass on nothing.
        Assert.Contains("CREATE TABLE [Items]", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [AspNetUsers]", script, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN", script, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX", script, StringComparison.Ordinal);
        Assert.Contains("rowversion", script, StringComparison.Ordinal);
        Assert.True(script.Length > 50_000, $"السلسلة المولَّدة قصيرة على غير المتوقع ({script.Length} حرفًا).");
    }

    [Fact]
    public void EveryConcurrencyToken_IsEmittedAsARowversionColumn()
    {
        var script = GenerateScript(MigrationsSqlGenerationOptions.Default);

        // [Timestamp] must reach the DDL as `rowversion`, otherwise SQL Server will not generate a
        // new value per update and no stale write will ever be rejected. A table introduced by a
        // later migration declares the column in its CREATE TABLE, an older one gains it by ADD,
        // so both shapes have to be checked against the script text itself.
        var rowVersionStatements = ColumnStatements.From(script)
            .Where(s => s.Column == "RowVersion" && s.Type.Equals("rowversion", StringComparison.OrdinalIgnoreCase))
            .Where(s => s.Kind is ColumnStatementKind.Created or ColumnStatementKind.Added)
            .ToList();

        Assert.NotEmpty(rowVersionStatements);
        foreach (var statement in rowVersionStatements)
        {
            var table = Regex.Escape(statement.Table);
            var optionalSchema = @"(?:\[[^\]]+\]\.)?";
            var pattern = statement.Kind == ColumnStatementKind.Created
                ? $@"CREATE TABLE {optionalSchema}\[{table}\] \((?:.|\r|\n)*?\[RowVersion\] rowversion"
                : $@"ALTER TABLE {optionalSchema}\[{table}\] ADD \[RowVersion\] rowversion";

            Assert.Matches(pattern, script);
        }
    }

    // ---------------------------------------------------------------------------------------
    // The narrowing rule has to be able to fail, or it is decoration. These run the same code
    // the sweep runs, on the shapes it is meant to catch and on the shapes it must ignore.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("decimal(18,2)", "decimal(18,2)", false, "نفس النوع ليس تضييقًا")]
    [InlineData("decimal(18,2)", "decimal(18,3)", false, "زيادة الخانات العشرية توسيع")]
    [InlineData("decimal(18,2)", "decimal(18,4)", false, "زيادة الخانات العشرية توسيع")]
    [InlineData("decimal(18,4)", "decimal(18,2)", true, "إنقاص الخانات العشرية يقصّ صامتًا")]
    [InlineData("decimal(20,4)", "decimal(18,4)", true, "إنقاص الدقة يقصّ صامتًا")]
    [InlineData("nvarchar(200)", "nvarchar(200)", false, "نفس الطول")]
    [InlineData("nvarchar(200)", "nvarchar(max)", false, "max أوسع من أي رقم")]
    [InlineData("nvarchar(200)", "nvarchar(100)", true, "تقصير النص يقصّ صامتًا")]
    [InlineData("int", "bigint", false, "توسيع الأعداد الصحيحة")]
    [InlineData("bigint", "int", true, "تصغير الأعداد الصحيحة يفقد بيانات")]
    [InlineData("datetime2(7)", "datetime2(7)", false, "نوع غير معمول عليه يبقى كما هو")]
    public void TheNarrowingRule_SeparatesWideningFromSilentDataLoss(string oldType, string newType, bool expectNarrowing, string because)
    {
        Assert.Equal(expectNarrowing, ColumnType.IsNarrowing(oldType, newType));
        Assert.False(string.IsNullOrWhiteSpace(because), "each case must state why it is what it is");
    }

    [Fact]
    public void TheColumnStatementReader_FindsCreatesAddsAltersAndDrops()
    {
        const string script = """
            CREATE TABLE [dbo].[Things] (
                [Id] int NOT NULL,
                [Name] nvarchar(200) NULL,
                [Qty] decimal(18,2) NOT NULL,
                CONSTRAINT [PK_Things] PRIMARY KEY ([Id])
            );
            ALTER TABLE [dbo].[Things] ADD [Note] nvarchar(50) NULL;
            ALTER TABLE [dbo].[Things] ALTER COLUMN [Qty] decimal(18,4) NOT NULL;
            ALTER TABLE [dbo].[Things] DROP COLUMN [Note];
            EXEC sp_rename N'[dbo].[Things].[Name]', N'Label', 'COLUMN';
            """;

        var statements = ColumnStatements.From(script).ToList();

        Assert.Equal(
            [
                (ColumnStatementKind.Created, "Things", "Id", "int"),
                (ColumnStatementKind.Created, "Things", "Name", "nvarchar(200)"),
                (ColumnStatementKind.Created, "Things", "Qty", "decimal(18,2)"),
                (ColumnStatementKind.Added, "Things", "Note", "nvarchar(50)"),
                (ColumnStatementKind.AlterColumn, "Things", "Qty", "decimal(18,4)"),
                (ColumnStatementKind.Dropped, "Things", "Note", ""),
                (ColumnStatementKind.Renamed, "Things", "Label", "")
            ],
            statements.Select(s => (s.Kind, s.Table, s.Column, s.Type)).ToList());
    }

    [Fact]
    public void TheColumnStatementReader_IgnoresKeysAndIndexesSoTypesAreNotMisread()
    {
        const string script = """
            CREATE TABLE [dbo].[Things] (
                [Id] int NOT NULL,
                CONSTRAINT [PK_Things] PRIMARY KEY ([Id]),
                INDEX [IX_Things_Id] NONCLUSTERED ([Id] ASC)
            );
            ALTER TABLE [dbo].[Things] ADD CONSTRAINT [FK_Things_Other] FOREIGN KEY ([Id]) REFERENCES [dbo].[Others] ([Id]) ON DELETE NO ACTION;
            CREATE UNIQUE INDEX [IX_Things_Id] ON [dbo].[Things] ([Id] ASC);
            """;

        var statements = ColumnStatements.From(script).ToList();

        Assert.Equal([(ColumnStatementKind.Created, "Things", "Id", "int")], statements.Select(s => (s.Kind, s.Table, s.Column, s.Type)).ToList());
    }
}

internal enum ColumnStatementKind
{
    Created,
    Added,
    AlterColumn,
    Dropped,
    Renamed,
    TableDropped
}

internal sealed record ColumnStatement(
    ColumnStatementKind Kind,
    string Table,
    string Column,
    string Type,
    string From = "");

/// <summary>
/// Reader for the column-level DDL inside a generated script. Deliberately narrow: it reports
/// only the statements that can change what a column can hold, in the order they appear - order
/// matters, because an <c>ADD</c> in one migration and an <c>ALTER COLUMN</c> in a later one is
/// exactly the shape a narrowing rule has to reason about.
/// </summary>
internal static class ColumnStatements
{
    /// <summary>Tables are written with or without the default schema, so the schema is optional.</summary>
    private const string TableRef = @"\[(?:[^\]]+\]\.\[)?(?<table>[^\]]+)\]";

    /// <summary>A type is a name plus an optional parenthesised argument list, e.g. <c>decimal(18,4)</c>.</summary>
    private const string TypeRef = @"(?<type>[A-Za-z_][A-Za-z0-9_]*(?:\([^)]*\))?)";

    /// <summary>
    /// The body ends at the statement terminator, or at <c>END;</c> when the create is wrapped in
    /// an idempotency guard.
    /// </summary>
    private const string BlockEnd = @"(?:\r?\n\);|\r?\nEND;)";

    private static readonly Regex CreateTable = new(
        @"CREATE TABLE " + TableRef + @" \((?<body>.*?)" + BlockEnd,
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex ColumnLine = new(
        @"^\s*\[(?<column>[^\]]+)\]\s+" + TypeRef,
        RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private static readonly Regex AlterColumn = new(
        @"ALTER TABLE " + TableRef + @" ALTER COLUMN \[(?<column>[^\]]+)\] " + TypeRef,
        RegexOptions.IgnoreCase);

    private static readonly Regex AddColumn = new(
        @"ALTER TABLE " + TableRef + @" ADD \[(?<column>[^\]]+)\] " + TypeRef,
        RegexOptions.IgnoreCase);

    private static readonly Regex DropColumn = new(
        @"ALTER TABLE " + TableRef + @" DROP COLUMN \[(?<column>[^\]]+)\]",
        RegexOptions.IgnoreCase);

    private static readonly Regex DropTable = new(
        @"DROP TABLE " + TableRef,
        RegexOptions.IgnoreCase);

    private static readonly Regex RenameColumn = new(
        @"EXEC sp_rename N'" + TableRef + @"\.\[(?<from>[^\]]+)\]', N'(?<to>[^\]]+)', 'COLUMN'",
        RegexOptions.IgnoreCase);

    public static IEnumerable<ColumnStatement> From(string script)
    {
        var found = new List<(int Index, ColumnStatement Statement)>();

        // A CREATE TABLE body has to be consumed whole: its column lines would otherwise be picked
        // up again as if they were something else, so the span is recorded and skipped later.
        var createSpans = new List<(int Start, int End)>();
        foreach (Match match in CreateTable.Matches(script))
        {
            var table = match.Groups["table"].Value;
            foreach (Match line in ColumnLine.Matches(match.Groups["body"].Value))
            {
                found.Add((line.Index,
                    new ColumnStatement(ColumnStatementKind.Created, table, line.Groups["column"].Value, line.Groups["type"].Value)));
            }

            createSpans.Add((match.Index, match.Index + match.Length));
        }

        bool InsideCreateTable(int index) => createSpans.Any(s => index >= s.Start && index < s.End);

        foreach (Match match in AlterColumn.Matches(script))
        {
            found.Add((match.Index,
                new ColumnStatement(ColumnStatementKind.AlterColumn, match.Groups["table"].Value, match.Groups["column"].Value, match.Groups["type"].Value)));
        }

        foreach (Match match in RenameColumn.Matches(script))
        {
            found.Add((match.Index,
                new ColumnStatement(ColumnStatementKind.Renamed, match.Groups["table"].Value, match.Groups["to"].Value, "", match.Groups["from"].Value)));
        }

        foreach (Match match in AddColumn.Matches(script))
        {
            if (InsideCreateTable(match.Index))
            {
                continue;
            }

            found.Add((match.Index,
                new ColumnStatement(ColumnStatementKind.Added, match.Groups["table"].Value, match.Groups["column"].Value, match.Groups["type"].Value)));
        }

        foreach (Match match in DropColumn.Matches(script))
        {
            found.Add((match.Index,
                new ColumnStatement(ColumnStatementKind.Dropped, match.Groups["table"].Value, match.Groups["column"].Value, "")));
        }

        foreach (Match match in DropTable.Matches(script))
        {
            found.Add((match.Index,
                new ColumnStatement(ColumnStatementKind.TableDropped, match.Groups["table"].Value, "", "")));
        }

        return found.OrderBy(f => f.Index).Select(f => f.Statement).ToList();
    }
}

internal enum ColumnTypeFamily
{
    Unknown,
    Integer,
    Decimal,
    CharLength,
    BinaryLength
}

/// <summary>
/// Type comparison good enough to decide one thing: can the new type hold every value the old
/// type could? Families it does not model are reported as "not narrowing" on purpose - the rule
/// exists to catch silent truncation, not to second-guess a deliberate change of family.
/// </summary>
internal readonly record struct ColumnType(ColumnTypeFamily Family, string Name, int Precision, int Scale, int Length, int IntegerRank)
{
    private static readonly Dictionary<string, int> IntegerRanks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tinyint"] = 1,
        ["smallint"] = 2,
        ["int"] = 3,
        ["bigint"] = 4
    };

    private static readonly HashSet<string> CharTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "char", "nchar", "varchar", "nvarchar", "sysname"
    };

    private static readonly HashSet<string> BinaryTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "binary", "varbinary"
    };

    public static ColumnType Parse(string sqlType)
    {
        var text = sqlType.Trim();
        var open = text.IndexOf('(', StringComparison.Ordinal);
        var name = (open < 0 ? text : text[..open]).Trim();
        var args = open < 0
            ? string.Empty
            : text[(open + 1)..text.IndexOf(')', StringComparison.Ordinal)];

        var numbers = args.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => int.TryParse(a, out var n) ? n : -1)
            .ToList();

        if (IntegerRanks.TryGetValue(name, out var rank))
        {
            return new ColumnType(ColumnTypeFamily.Integer, name, 0, 0, 0, rank);
        }

        if (name.Equals("decimal", StringComparison.OrdinalIgnoreCase) || name.Equals("numeric", StringComparison.OrdinalIgnoreCase))
        {
            return new ColumnType(ColumnTypeFamily.Decimal, name,
                numbers.Count > 0 ? numbers[0] : 18,
                numbers.Count > 1 ? numbers[1] : 0,
                0,
                0);
        }

        if (CharTypes.Contains(name))
        {
            var max = numbers.Count > 0 && numbers[0] == -1;
            return new ColumnType(ColumnTypeFamily.CharLength, name, 0, 0, max ? int.MaxValue : numbers.FirstOrDefault(), 0);
        }

        if (BinaryTypes.Contains(name))
        {
            var max = numbers.Count > 0 && numbers[0] == -1;
            return new ColumnType(ColumnTypeFamily.BinaryLength, name, 0, 0, max ? int.MaxValue : numbers.FirstOrDefault(), 0);
        }

        return new ColumnType(ColumnTypeFamily.Unknown, name, 0, 0, 0, 0);
    }

    public static bool IsNarrowing(string oldType, string newType)
    {
        var from = Parse(oldType);
        var to = Parse(newType);

        if (from.Family != to.Family)
        {
            return false;
        }

        return from.Family switch
        {
            ColumnTypeFamily.Integer => to.IntegerRank < from.IntegerRank,
            // A decimal loses data silently when either the total number of digits or the number
            // of digits after the point drops. Raising the scale while holding the precision
            // (18,2) -> (18,3) trades integral digits, but SQL Server raises "arithmetic
            // overflow" on such a row rather than truncating it, so it is not a silent loss.
            ColumnTypeFamily.Decimal => to.Precision < from.Precision || to.Scale < from.Scale,
            ColumnTypeFamily.CharLength => to.Length < from.Length,
            ColumnTypeFamily.BinaryLength => to.Length < from.Length,
            _ => false
        };
    }
}
