using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// <see cref="MigrationScriptTests"/> يقرأ SQL المولَّد فقط؛ هذا الملف يطبّقه على محرّك SQL Server
/// حقيقي. لماذا الاثنين معًا: قراءة النص تثبت أن DDL مكتوب صحيحًا، وتطبيقه يثبت أن المحرّك يقبله.
/// الاختبارات الأخرى في هذه المجموعة كلّها تبني المخطّط بـ<code>EnsureCreated()</code> على SQLite،
/// فلا تُنفَّذ سلسلة الترحيلات ولا مرّة واحدة - وهذا الملف هو ما يمنع أن يبقى ذلك مجهولاً.
///
/// المحرك يُحدَّد بترتيب: <c>NVS_TEST_SQLSERVER</c> أوّلًا (وهو ما يستخدمه CI)، ثم LocalDB على
/// ويندوز. وإن لم يتوفّر أيٌّ منهما تُتخطّى الاختبارات <b>بسبب معلن</b> لا صامت؛ انظر
/// <see cref="SqlServerTestTarget.SkipReason"/> لسبب القبول أو الرفض بالاسم.
/// </summary>
public sealed class MigrationChainSqlServerTests : IClassFixture<MigrationChainFixture>
{
    private readonly MigrationChainFixture _fixture;

    public MigrationChainSqlServerTests(MigrationChainFixture fixture)
    {
        _fixture = fixture;
    }

    [SqlServerFact]
    public async Task TheWholeChain_AppliesToAnEmptyDatabase_AndLeavesNothingPending()
    {
        var db = _fixture.Migrated();

        // Nothing pending means every migration in the chain is in the history table: the
        // fixture created an empty database, so this cannot pass without the chain running.
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [SqlServerFact]
    public async Task ReRunningMigrate_OnAnUpToDateDatabase_ChangesNothing()
    {
        var before = await ReadHistoryAsync(_fixture.Migrated());
        Assert.NotEmpty(before);

        // The claim "the chain applies" is only half of the contract; the other half is that
        // applying it twice is a no-op. A data migration that would corrupt the database on a
        // second pass would sail through a first-pass-only check, so the second pass is
        // executed here rather than merely re-asked about.
        using var again = _fixture.CreateContext();
        await again.Database.MigrateAsync();

        Assert.Empty(await again.Database.GetPendingMigrationsAsync());
        Assert.Equal(before, await ReadHistoryAsync(again));
    }

    [SqlServerFact]
    public async Task EveryMigrationInTheChain_IsRecordedInTheHistoryTable()
    {
        var expected = _fixture.MigrationChain;
        Assert.NotEmpty(expected);

        var db = _fixture.Migrated();
        var applied = await ReadHistoryAsync(db);

        Assert.Equal(
            expected.OrderBy(id => id, StringComparer.Ordinal).ToList(),
            applied.OrderBy(id => id, StringComparer.Ordinal).ToList());
        Assert.All(applied, id => Assert.StartsWith("2026", id, StringComparison.Ordinal));
    }

    [SqlServerFact]
    public async Task RowVersion_IsGeneratedByTheEngine_AndAStaleWriteIsRejected()
    {
        // هذا هو العقد الحقيقي للـ <c>[Timestamp]</c>. الاختبار المسمى «يكتب بنجاح» على SQLite
        // كان يثبت العكس: SQLite لا يولّد <c>rowversion</c> ولا يفحصه، فأي كتابة قديمة تمرّ.
        int id;
        byte[] inserted;
        using (var seed = _fixture.CreateContext())
        {
            var period = new FiscalPeriod { Year = 2041, Name = "سنة عقد التزامن" };
            seed.FiscalPeriods.Add(period);
            await seed.SaveChangesAsync();

            id = period.Id;
            inserted = period.RowVersion ?? throw new InvalidOperationException(
                "المحرك لم يولّد RowVersion عند الإدراج: العمود ليس rowversion في قاعدة البيانات.");
        }

        using var db1 = _fixture.CreateContext();
        using var db2 = _fixture.CreateContext();
        var tracked1 = await db1.FiscalPeriods.SingleAsync(p => p.Id == id);
        var tracked2 = await db2.FiscalPeriods.SingleAsync(p => p.Id == id);

        tracked1.Name = "اسم من السياق الأول";
        await db1.SaveChangesAsync();
        Assert.NotNull(tracked1.RowVersion);
        Assert.NotEqual(inserted, tracked1.RowVersion!);

        tracked2.Name = "اسم من سياق قديم";
        var conflict = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
        Assert.Contains(conflict.Entries, e => e.Entity is FiscalPeriod);

        using var verify = _fixture.CreateContext();
        Assert.Equal("اسم من السياق الأول", (await verify.FiscalPeriods.SingleAsync(p => p.Id == id)).Name);
    }

    [SqlServerFact]
    public async Task TheAppliedCatalog_MatchesEveryDecimalTheModelDeclares()
    {
        // <c>has-pending-model-changes</c> يقارن المخطّط بلقطة اللقطة، ولا يقارنهما بقاعدة
        // بيانات طُبِّقت فعلًا. هذا هو الفارق الذي يبتلعه: عمود تغيّر في المخطّط ولم تُرحَّل
        // ترحيلته يبقى مطابقًا للقطة ومختلفًا عن reality.
        var db = _fixture.Migrated();
        var declared = DeclaredDecimals(db);
        Assert.NotEmpty(declared);

        var catalog = await ReadDecimalCatalogAsync(db);
        foreach (var (table, column, precision, scale) in declared)
        {
            Assert.True(
                catalog.TryGetValue((table, column), out var actual),
                $"الجدول {table} لا يحتوي العمود {column} بعد تطبيق السلسلة.");
            Assert.Equal(precision, actual.Precision);
            Assert.Equal(scale, actual.Scale);
        }
    }

    [SqlServerFact]
    public async Task EveryConcurrencyToken_IsARealRowversionColumnInTheAppliedDatabase()
    {
        var db = _fixture.Migrated();
        var tokenTables = new List<string>();

        var connection = await OpenAsync(db);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.name, c.name, ty.name
            FROM sys.tables t
            JOIN sys.columns c ON c.object_id = t.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.name = 'RowVersion';
            """;

        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                // `rowversion` و `timestamp` اسمان لنوع واحد في SQL Server (المعرّف 189)،
                // وكتالوج المحرّك يبلّغ عن الاسم المهجور. القبول بالاثنين لا يُضعف الفحص:
                // أيٌّ منهما يعني «رمز نسخ يُولَّد من المحرّك»، وهو ما هو العقد.
                Assert.Contains(reader.GetString(2), new[] { "rowversion", "timestamp" });
                tokenTables.Add(reader.GetString(0));
            }
        }

        // نفس الـ 15 جدولًا الذي يقرّره المخطّط، لا عدد أقلّ: نقص جدول يعني أن migration
        // للـ RowVersion لم تُطبَّق أو أن اسمًاChanged.
        var expected = ConcurrencyTokenTables(db);
        Assert.Equal(
            expected.OrderBy(t => t, StringComparer.Ordinal).ToList(),
            tokenTables.Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList());
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

    private static async Task<List<string>> ReadHistoryAsync(AppDbContext db)
    {
        var ids = new List<string>();
        var connection = await OpenAsync(db);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId];";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private static async Task<Dictionary<(string Table, string Column), (byte Precision, byte Scale)>> ReadDecimalCatalogAsync(AppDbContext db)
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
            // tinyint arrives as Int32, not Byte; Convert keeps the cast out of the assertions.
            catalog[(reader.GetString(0), reader.GetString(1))] =
                (Convert.ToByte(reader.GetValue(2), System.Globalization.CultureInfo.InvariantCulture),
                 Convert.ToByte(reader.GetValue(3), System.Globalization.CultureInfo.InvariantCulture));
        }

        return catalog;
    }

    private static List<(string Table, string Column, byte Precision, byte Scale)> DeclaredDecimals(AppDbContext db)
    {
        var declared = new List<(string, string, byte, byte)>();
        var pattern = new Regex(@"^decimal\((?<p>\d+),(?<s>\d+)\)$", RegexOptions.IgnoreCase);

        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var storeObject = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table);
            if (storeObject?.Name is not { } tableName)
            {
                continue;
            }

            foreach (var property in entityType.GetProperties())
            {
                var columnType = property.GetColumnType(storeObject.Value);
                if (columnType is null)
                {
                    continue;
                }

                var match = pattern.Match(columnType);
                if (!match.Success)
                {
                    continue;
                }

                declared.Add((
                    tableName,
                    property.GetColumnName(storeObject.Value) ?? property.Name,
                    byte.Parse(match.Groups["p"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    byte.Parse(match.Groups["s"].Value, System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        return declared;
    }

    private static List<string> ConcurrencyTokenTables(AppDbContext db)
    {
        var tables = new List<string>();
        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var storeObject = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table);
            if (storeObject?.Name is not { } tableName)
            {
                continue;
            }

            // `ConcurrencyStamp` في هوية ASP.NET رمز نسخ أيضًا لكنه `nvarchar` تديره التطبيق،
            // لا `rowversion` يولّده المحرّك. العقد الذي يهمّ هنا هو العمود من نوع rowversion.
            if (entityType.GetProperties().Any(p =>
                    p.IsConcurrencyToken &&
                    string.Equals(
                        p.GetColumnType(storeObject.Value),
                        "rowversion",
                        StringComparison.OrdinalIgnoreCase)))
            {
                tables.Add(tableName);
            }
        }

        return tables;
    }
}

/// <summary>
/// قاعدة واحدة لكل اختبارات هذا الملف: تُبنى مرة واحدة لكل تشغيل، وتُطبَّق عليها السلسلة مرة
/// واحدة، وتُحذف في النهاية. بطيئةٌ بقصدها - الغرض هو تطبيق 38 migration على محرّك حقيقي.
/// </summary>
public sealed class MigrationChainFixture : IDisposable
{
    private string? _databaseName;
    private IReadOnlyList<string>? _chain;

    /// <summary>سلسلة الترحيلات كما يراها EF، بترتيب التطبيق.</summary>
    public IReadOnlyList<string> MigrationChain
    {
        get
        {
            EnsureMigrated();
            return _chain!;
        }
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>قاعدة مُرحَّلة، وجاهزة للاستعمال. تُطبَّق السلسلة مرّة واحدة ثم تُعاد.</summary>
    public AppDbContext Migrated()
    {
        EnsureMigrated();
        return CreateContext();
    }

    private string ConnectionString =>
        SqlServerTestTarget.WithDatabase(SqlServerTestTarget.ResolveRequired(), _databaseName!);

    private void EnsureMigrated()
    {
        if (_databaseName is not null)
        {
            return;
        }

        var baseConnectionString = SqlServerTestTarget.ResolveRequired();
        _databaseName = $"NewVixSmart_MigrationChain_{Guid.NewGuid():N}";

        using var db = CreateContext();
        db.Database.Migrate();
        _chain = db.Database.GetMigrations().ToList();
    }

    public void Dispose()
    {
        if (_databaseName is null)
        {
            return;
        }

        // الاتصال من `master` لا من القاعدة نفسها: SQL Server يرفض حذف قاعدةٍ متّصل بها،
        // فمحاولة الحذف من داخلها تفشل وتُبقي قاعدةً عالقة على LocalDB وعلى CI.
        // تفريغ المجمّعات أولًا أيضًا: كل DbContext في هذا الملف مفتوح مجمَّع، والروابط
        // المجمَّعة تبقي جلسةً حيّة على القاعدة بعد إغلاق الكائن.
        //
        // و`DROP DATABASE` / `ALTER DATABASE` لا تقبل معاملات: الاسم مُعرَّف لا قيمة، فلا بدّ
        // من وضعه داخل النص بعد التحقّق منه.
        if (_databaseName.Contains(']') || _databaseName.Contains('['))
        {
            throw new InvalidOperationException($"اسم قاعدة غير صالح للحذف: {_databaseName}");
        }

        SqlConnection.ClearAllPools();
        var control = SqlServerTestTarget.WithDatabase(SqlServerTestTarget.ResolveRequired(), "master");
        using var connection = new SqlConnection(control);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID(N'{_databaseName}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{_databaseName}]; END";
        command.ExecuteNonQuery();
        _databaseName = null;
    }
}

/// <summary>
/// تحديد محرّك SQL Server الذي تُطبَّق عليه السلسلة، وإعلان سبب التخطي حين لا يوجد.
/// </summary>
internal static class SqlServerTestTarget
{
    /// <summary>متغيّر البيئة الذي يستخدمه CI (و Developers محليًّا).</summary>
    public const string ConnectionStringVariable = "NVS_TEST_SQLSERVER";

    /// <summary>
    /// متغيّر البيئة الذي يفعّل القياسات الثقيلة (مليونَا صف). منفصل عن
    /// <see cref="ConnectionStringVariable"/> عمدًا: «أي محرّك» قرار، و«اسمح بقياسٍ مُكلف» قرار
    /// ثانٍ، ودمجهما يعني أن من يريد قياسًا على قرصٍ أبطأ لا يستطيع تفعيله أصلًا.
    /// </summary>
    public const string HeavyMeasurementVariable = "NVS_TEST_SQLSERVER_MEASURE";

    private const string _localDbInstance = @"(localdb)\mssqllocaldb";
    private const string _localDbConnectionString = $"Server={_localDbInstance};Integrated Security=True";

    private static readonly Lazy<Probe> _probe = new(Run, isThreadSafe: true);
    private static readonly Lazy<string?> _engine = new(DescribeEngineCore, isThreadSafe: true);

    private const string _baseSkipReason =
        "لا يوجد محرّك SQL Server متاح. اضبط NVS_TEST_SQLSERVER على سلسلة اتصال (نحو: " +
        "Server=localhost,1433;User Id=sa;Password=***;TrustServerCertificate=True) " +
        "أو ثبّت LocalDB على ويندوز. الفحص النصّي للسلسلة في MigrationScriptTests يعمل في كل الحالات.";

    /// <summary>
    /// سبب التخطي كما يُعرض في نتائج الاختبار. نصٌّ ثابتٌ زائدُ ما تعلّمه <see cref="Run"/> عن
    /// المتغيّرات المرفوضة، حتى يعرف المطوّر أن <c>NVS_TEST_SQLSERVER</c> «مضبوطٌ لكنّه لا
    /// يعمل» وأن سبب رفضه مذكور، لا أن يظنّ أن المتغيّر غير مضبوط أصلًا.
    /// </summary>
    public static string SkipReason =>
        _probe.Value.Rejections.Count == 0
            ? _baseSkipReason
            : _baseSkipReason + Environment.NewLine
              + string.Join(Environment.NewLine, _probe.Value.Rejections);

    /// <summary>
    /// سبب تخطي القياسات الثقيلة. معلَنٌ كغيره، والسبب مُسمّى: هو قياسُ جدول بثلاثة ملايين صف
    /// على قرص CI مشترك، ولست مُتطلَّبًا في مجموعة صحّة.
    /// </summary>
    public const string HeavyMeasurementSkipReason =
        "القياس الثقيل (مليونَا صف) مُعطَّلٌ افتراضيًّا: هو أبطأ اختبار في المجموعة، ورقمه يعتمد على " +
        "قرص الجهاز لا على سلوك المحرّك. لتفعيله اضبط " + HeavyMeasurementVariable + "=1. ما يبقى في " +
        "المجموعة دائمًا هو الفحص البنيوي للـguard - ستّون تنفيذًا واحدًا لكل عمود، لا تنفيذًا لكل صف - " +
        "وهو لا يقيس وقتًا ولا يحتاج جدولًا كبيرًا.";

    public static string? Resolve() => _probe.Value.ConnectionString;

    public static string ResolveRequired() =>
        Resolve() ?? throw new InvalidOperationException(SkipReason);

    /// <summary>هل طُلبت القياسات الثقيلة صراحةً؟</summary>
    public static bool HeavyMeasurementsEnabled =>
        IsTruthy(Environment.GetEnvironmentVariable(HeavyMeasurementVariable));

    /// <summary>
    /// اسم المحرّك وإصداره ونوعه، ليُذكر في رسائل الفشل. الغرض أن رسالة
    /// «قاعدة البيانات للقراءة فقط» تصير مقروءة: المطوّر يعرف أي محرّك يجلس أمامه بدل أن يخمّن.
    /// </summary>
    public static string DescribeEngine() =>
        _engine.Value ?? "محرّك غير معروف (لم يُفتح اتصال به)";

    /// <summary>ينسخ سلسلة الاتصال ويستبدل قاعدة البيانات، فلا تلمس السلسلة الأصلية.</summary>
    public static string WithDatabase(string connectionString, string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = databaseName,
            ConnectTimeout = 120,
            TrustServerCertificate = true
        };
        return builder.ConnectionString;
    }

    private static Probe Run()
    {
        var rejections = new List<string>();

        var fromEnvironment = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            // المتغيّر يُفحص ولا يُقرأ كما هو. قراءته كما هو كان يحوّل خطأً مطبعيًا في اسم
            // Instance إلى 19 استثناءً خامًا داخل تهيئة الـfixtures، بدل تخطٍّ معلن يحيل إلى هذا
            // النص. الاتصال يُثبت هنا مرّة واحدة، فيُقرَّر التخطي قبل أن تُنشأ أي قاعدة بيانات.
            if (CanConnect(fromEnvironment, out var fromEnvironmentError))
            {
                return new Probe(fromEnvironment, rejections);
            }

            rejections.Add(
                $"{ConnectionStringVariable} مضبوط لكنه غير قابل للاتصال، فتم تجاوزه: {fromEnvironmentError}");
        }

        if (OperatingSystem.IsWindows())
        {
            if (CanConnect(_localDbConnectionString, out var localDbError))
            {
                return new Probe(_localDbConnectionString, rejections);
            }

            rejections.Add($"LocalDB ({_localDbInstance}) غير قابل للاتصال: {localDbError}");
        }

        return new Probe(null, rejections);
    }

    private static string? DescribeEngineCore()
    {
        var connectionString = Resolve();
        if (connectionString is null)
        {
            return null;
        }

        try
        {
            using var connection = new SqlConnection(WithDatabase(connectionString, "master"));
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CONVERT(nvarchar(128), @@SERVERNAME),
                       CONVERT(nvarchar(128), SERVERPROPERTY('Edition')),
                       CONVERT(nvarchar(32), SERVERPROPERTY('ProductVersion'));
                """;
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return $"«{reader.GetString(0)}» - {reader.GetString(1)} {reader.GetString(2)}";
        }
        catch (SqlException)
        {
            // الوصف زينة: لا يُسقط اختبارًا، ومن يطبع رسالة الفشل الحقيقية هو من يعرف المحرّك.
            return null;
        }
    }

    private static bool CanConnect(string connectionString, out string error)
    {
        try
        {
            using var connection = new SqlConnection(WithDatabase(connectionString, "master"));
            connection.Open();
            error = string.Empty;
            return true;
        }
        catch (SqlException exception)
        {
            // الرسالة الأولى تكفي: أخطاء الاتصال تُعيد_message طويلًا لا يقول شيئًا زيادةً.
            error = FirstLine(exception.Message);
            return false;
        }
        catch (InvalidOperationException exception)
        {
            error = FirstLine(exception.Message);
            return false;
        }
    }

    private static string FirstLine(string message)
    {
        var line = message.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        return line.Length <= 300 ? line : line[..300];
    }

    private static bool IsTruthy(string? value) =>
        value is not null
        && (value.Equals("1", StringComparison.Ordinal)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    /// <summary>نتيجة الفحص: محرّكٌ مُختار، أو لا شيء مع قائمة أسباب الرفض.</summary>
    private sealed record Probe(string? ConnectionString, List<string> Rejections);
}

/// <summary>
/// <c>[Fact]</c> يشترط وجود محرّك حقيقي قابلًا للاتصال. التخطي هنا معلن في
/// <see cref="SqlServerTestTarget.SkipReason"/> وليس صامتًا: الفحص النصّي يبقى في كل تشغيل،
/// ووظيفة <c>migration-chain-sqlserver</c> في <c>ci.yml</c> تمنح هذا المتغيّر قيمةً على كل commit.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (SqlServerTestTarget.Resolve() is null)
        {
            Skip = SqlServerTestTarget.SkipReason;
        }
    }
}
