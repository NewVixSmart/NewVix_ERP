using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// اختباراتُ الحارسين الدفاعيَّين في <see cref="SetWriteGate"/>: الأول يمنع بيعَ فشلِ
/// إدراجِ صفِّ القفل كأنه سباقٌ مفقود، والثاني يمنع إرجاعَ قفلٍ يحصي صفًّا واحدًا لا صفًّا.
/// <para>
/// والحالَتان غيرُ قابليَّين للوصولِ عبر المسار العامِّ في التطبيق: لا شيء يحذف صفوفَ
/// <c>Lock.Set.</c>، ولا طلبٌ موازٍ يمكنه أن يرى الصفَّ مفقودًا بين الإدراجِ و<c>UPDATE</c>.
/// فالطريقُ الصادقُ هاهنا هو المِخططُ نفسُه: مُشعِثٌ يُركَّب بعد <c>EnsureCreated</c>
/// يصنعُ العطبَ في موضعِه الحقيقي، لا استدعاءٌ مباشرٌ لدوالٍ خاصة.
/// </para>
/// <para>
/// ولألا يمرَّ اختبارٌ صامتٌ لو تعطّل المُشعِث، يبني كلُّ اختبارٍ أداةَ برهانٍ خاصَّةً به
/// (صفُّ علامةٍ، أو فِرقُ حالةِ التتبُّع) لا تقوم الحالةُ المُختبَرةُ بدونها.
/// </para>
/// </summary>
public sealed class SetWriteGateGuardTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public SetWriteGateGuardTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext() => new(_options);

    /// <summary>
    /// يُركِّب مُشعِثًا في المِخططِ مباشرةً على الاتصالِ نفسِه، لا عبر الـ<c>DbContext</c>:
    /// حِملَةُ <c>EF1003</c> ترفض النصَّ المُركَّب، والمُشعِثُ مِخططٌ لا حالةُ تطبيق.
    /// </summary>
    private void InstallTrigger(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// فشلُ الإدراجِ ولم يبقَ صفٌّ خلفَه: هذا ليس سباقًا مفقودًا بل عطبٌ حقيقيٌّ (امتلاءُ
    /// حقلٍ، انقطاعُ اتصالٍ) فيجب أن يخرج كما دخل. ولو ابتُلع، لأمضى الحفظُ إلى الـ<c>UPDATE</c>
    /// تحته وهو يمسُّ صفًّا لا وجودَ له.
    /// <para>
    /// ودليلُ دخولِ كتلةِ الـ<c>catch</c> أن صفَّ الإدراجِ المضافَّ قد فُصِل فيها: لولاها
    /// لبقي <c>Added</c> في متتبِّعِ السياق بعد أن فشل الحفظ، وهذا ما يسقطه الاختبار إن
    /// زالت الكتلةُ كلُّها.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AnInsertFailureWithNoRowBehindItIsRethrownNotSwallowed()
    {
        var subject = "GuardProof." + Guid.NewGuid().ToString("N");
        var key = SetSubjects.LockRowKey(subject);

        using var db = CreateContext();
        InstallTrigger(
            "CREATE TRIGGER forced_lock_insert_failure BEFORE INSERT ON SystemSettings " +
            $"WHEN NEW.[Key] = '{key}' " +
            "BEGIN SELECT RAISE(ABORT, 'forced lock insert failure'); END;");

        var gate = new SetWriteGate(db);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => gate.AcquireAsync(subject));

        Assert.Contains("forced lock insert failure", ex.ToString());
        Assert.DoesNotContain(db.ChangeTracker.Entries(), e => e.State == EntityState.Added);
        Assert.False(
            await db.SystemSettings.AsNoTracking().AnyAsync(s => s.Key == key),
            "لم يُنشأ صفُّ القفل، فالإدراجُ فشل فعلًا والاختبارُ يختبر الفشلَ لا النجاح.");
    }

    /// <summary>
    /// فشلُ الإدراجِ ووجدنا الصفَّ خلفَه: طلبٌ موازٍ خَلَق الصفَّ قبلَنا، وهذا هو الشرطُ
    /// نفسُه الذي سعينا إليه، فيُتجاهل الفشلُ ويمضي القفل. ولو رُمي الفشلُ هنا لاختفى
    /// خلفُ <c>DbUpdateException</c> سباقٌ شرعيٌّ كان ينبغي أن يُقبل.
    /// <para>
    /// وأداةُ البرهانِ هنا صِدقُ المُشعِث: ما لم يُنشئ صفَّ العلامة، لم يفشل الإدراجُ
    /// أصلًا وكان الاختبارُ يقرأ نجاحًا بلا فشلٍ يُبتلع.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AnInsertFailureThatLeftTheRowBehindIsSwallowedAsTheLostRace()
    {
        var subject = "GuardProof." + Guid.NewGuid().ToString("N");
        var key = SetSubjects.LockRowKey(subject);
        var marker = "GuardRaceMarker." + Guid.NewGuid().ToString("N");

        using var db = CreateContext();
        InstallTrigger(
            "CREATE TRIGGER lost_lock_insert_race BEFORE INSERT ON SystemSettings " +
            $"WHEN NEW.[Key] = '{key}' " +
            "BEGIN " +
            $"INSERT INTO SystemSettings([Key], [Value], [UpdatedAt]) VALUES ('{marker}', NEW.[Value], NEW.[UpdatedAt]); " +
            "INSERT INTO SystemSettings([Key], [Value], [UpdatedAt]) VALUES (NEW.[Key], NEW.[Value], NEW.[UpdatedAt]); " +
            "SELECT RAISE(FAIL, 'forced lost primary-key race'); " +
            "END;");

        var gate = new SetWriteGate(db);
        await using (var tx = await gate.AcquireAsync(subject))
        {
            await tx.CommitAsync();
        }

        Assert.True(
            await db.SystemSettings.AsNoTracking().AnyAsync(s => s.Key == marker),
            "لم يُنشئ المُشعِث صفَّ العلامة، فالإدراجُ نجح بلا فشلٍ والاختبارُ لا يلمس الحارس.");
        Assert.True(
            await db.SystemSettings.AsNoTracking().AnyAsync(s => s.Key == key),
            "صفُّ القفل غير موجود بعد الفشل، فالحارسُ كان يجب أن يرمي لا أن يبتلع.");

        // ولو تبقّى صفُّ الإدراجِ المضافُّ في المتتبِّع، لعاد يُدرج في الحفظ التالي ويصدم
        // المفتاحَ الأساسيَّ — وهذا ما يفصله كتلةُ الـcatch وحدها.
        db.SystemSettings.Add(new SystemSetting
        {
            Key = "GuardAfterRace." + Guid.NewGuid().ToString("N"),
            Value = "1"
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// الـ<c>UPDATE</c> الذي يحصي صفًّا صفرًا قفلٌ لا يقفل شيئًا، وهو أسوأُ من عدمِ قفلٍ
    /// أصلًا: يُطمئنُّ المُستدعيَّ بعد أن نزعنا الأمانَ. والصفُّ هنا يُنشأ ثم يزول قبل
    /// الـ<c>UPDATE</c> — وهي الحالةُ نفسُها التي يصفُها الحارس — فيُرفض الحجزُ صراحةً.
    /// </summary>
    [Fact]
    public async Task ALockUpdateThatTouchesNoRowIsRefusedInsteadOfReturned()
    {
        var subject = "GuardProof." + Guid.NewGuid().ToString("N");
        var key = SetSubjects.LockRowKey(subject);

        using var db = CreateContext();
        InstallTrigger(
            "CREATE TRIGGER erase_lock_row_after_insert AFTER INSERT ON SystemSettings " +
            $"WHEN NEW.[Key] = '{key}' " +
            "BEGIN DELETE FROM SystemSettings WHERE [Key] = NEW.[Key]; END;");

        var gate = new SetWriteGate(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => gate.AcquireAsync(subject));

        Assert.Contains("تعذَّر حجز صفّ القفل", ex.Message);
        Assert.Contains(key, ex.Message);
        Assert.False(
            await db.SystemSettings.AsNoTracking().AnyAsync(s => s.Key == key),
            "صفُّ القفل ما زال موجودًا، فالاختبارُ لم يصنع الحالةَ التي يرفضها الحارس.");
    }
}
