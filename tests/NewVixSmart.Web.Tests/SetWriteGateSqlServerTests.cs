using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Services;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// الدليلُ على أن القفلَ الصفّي في <see cref="SetWriteGate"/> يقفلُ فعلًا، لا أنه مجرّد
/// معاملةٍ متسلسلة بلا أثر. والاختباراتُ القائمةُ على البصمة تُثبت الرفضَ عند
/// <em>التسلسل</em> (صفحةٌ متروكة مفتوحة)، وهي لا تقول شيئًا عن <em>التزاحم</em>:
/// فالطلبان المتزامنان يقرآن الحالةَ نفسَها فيعدّان التصريحَين متطابقَين ثم يكتب
/// الثاني فيمحو الأول.
/// </para>
/// <para>
/// ولهذا يحتاج البرهانُ محرّكًا حقيقيًّا واتصالَين مستقلَّين: فـ<code>SQLite</code>
/// يُسلسِل الكتابةَ كلَّها فلا يُظهر التزاحمَ أصلًا، حتى لو كان القفلُ مزيفًا.
/// </para>
/// </summary>
public sealed class SetWriteGateSqlServerTests : IClassFixture<MigrationChainFixture>
{
    private readonly MigrationChainFixture _fixture;

    public SetWriteGateSqlServerTests(MigrationChainFixture fixture) => _fixture = fixture;

    /// <summary>
    /// الطلبُ الثاني يجب أن يتوقَّف عند صفِّ القفل ما دام الأولُ لم يُلْتَم، ثم يمضي
    /// بعد انتهائه. وهذه هي الخاصيةُ التي تجعل المقارنةَ بعد القفل صحيحة: لا يُقرأ
    /// شيءٌ قبل أن يُطردَ rivalٌ سابق.
    /// <para>
    /// والمهمُّ في هذا السطر أن <c>peer</c> <em>لا تكتمل</em> ما دام المعاملةُ الأولى
    /// مفتوحة. ولو سقط الـ<code>UPDATE</code> الذي يحجز الصفَّ — وبقيت المعاملةُ
    /// المتسلسلةُ وحدَها — لكان الطلبُ الثاني ينتهي داخل مهلة الانتظار، فيسقط هذا
    /// الادعاءُ. فالانتظارُ هنا ليس تحمّلَ بطءٍ، بل هو الشرطُ المُقاس.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task TheGateHoldsAPeerRequestUntilTheFirstOneCommits()
    {
        var subject = "GateProof." + Guid.NewGuid().ToString("N");

        try
        {
            using var first = _fixture.Migrated();
            var firstGate = new SetWriteGate(first);
            await using var held = await firstGate.AcquireAsync(subject);

            using var peerContext = _fixture.CreateContext();
            var peerGate = new SetWriteGate(peerContext);

            var peer = Task.Run(async () =>
            {
                await using var tx = await peerGate.AcquireAsync(subject);
                await tx.CommitAsync();
            });

            // مهلةٌ أطولُ من أن يُنهي فيها الطلبُ الثاني عملَه لو لم يكن هناك قفلٌ أصلًا.
            await Task.Delay(750);
            Assert.False(peer.IsCompleted);

            await held.CommitAsync();

            await peer.WaitAsync(TimeSpan.FromSeconds(60));
            Assert.True(peer.IsCompletedSuccessfully);
        }
        finally
        {
            // هذه القاعدةُ مشتركةٌ بين الاختبارات، فلا نترك صفَّ قفلٍ خلفنا.
            using var cleanup = _fixture.CreateContext();
            var key = SetSubjects.LockRowKey(subject);
            var leftovers = await cleanup.SystemSettings.Where(s => s.Key == key).ToListAsync();
            cleanup.SystemSettings.RemoveRange(leftovers);
            await cleanup.SaveChangesAsync();
        }
    }

    /// <summary>
    /// القفلُ يحجز صفًّا واحدًا، والقارئون يبحثون بمفتاحٍ مطابقٍ لا ببادئة؛ فلا يظهر
    /// صفُّ القفل في بصمةِ الطباعة ولا في سمةِ الواجهة. ولو ظهر لأفسد الحسابَ نفسَه،
    /// فكلُّ كتابةٍ لقفلٍ كانت سترفض الحفظَ التاليَ بوصفه تغيّرًا.
    /// <para>
    /// والمهمُّ أيضًا أن <c>ResetPrinting</c> يستعيد الافتراضيَ بحذفٍ ببادئة، فلو شمل
    /// صفَّ القفل لا حذفَه، ثم يمرُّ الحفظُ التاليُ بـ<code>UPDATE</code> يمسُّ صفًّا
    /// غير موجود، فيُخرِج صفًّا لا يقفل شيئًا — أي خسارةٌ تعودُ عند أول استعادة.
    /// </para>
    /// </summary>
    [SqlServerFact]
    public async Task TheGateRowStaysInvisibleToTheSettingsItGuards()
    {
        var subject = "GateProof." + Guid.NewGuid().ToString("N");
        var lockKey = SetSubjects.LockRowKey(subject);

        using var db = _fixture.Migrated();
        db.SystemSettings.Add(new SystemSetting { Key = "PrintStudio.sales_invoice.PageSize", Value = "A5" });
        await db.SaveChangesAsync();

        try
        {
            var fingerprintBefore = await PrintFingerprintAsync(db);

            var gate = new SetWriteGate(db);
            await using (var tx = await gate.AcquireAsync(subject))
            {
                await tx.CommitAsync();
            }

            // الصفُّ موجود...
            Assert.True(
                await db.SystemSettings.AsNoTracking().AnyAsync(s => s.Key == lockKey),
                "لم يُنشأ صفُّ القفل، فلا قفلَ هنا أصلًا.");

            // ...لكنه لا يمسّ البصمة، لأنَّ أحدًا لا يقرؤه بمطابقة البادئة.
            Assert.Equal(fingerprintBefore, await PrintFingerprintAsync(db));

            // نفس شرطَ الحذفِ في `ResetPrinting`: يستعيد الافتراضيَ دون أن يمسّ القفل.
            var toDelete = await db.SystemSettings
                .Where(s => s.Key.StartsWith("PrintStudio.") || s.Key.StartsWith("Print."))
                .ToListAsync();
            db.SystemSettings.RemoveRange(toDelete);
            await db.SaveChangesAsync();

            Assert.True(
                await db.SystemSettings.AsNoTracking().AnyAsync(s => s.Key == lockKey),
                "الاستعادةُ حذفت صفَّ القفل، فيصبح الحفظُ بعدَها بلا قفلٍ أصلًا.");
            Assert.False(
                await db.SystemSettings.AsNoTracking().AnyAsync(s => s.Key == "PrintStudio.sales_invoice.PageSize"),
                "الاستعادةُ لم تحذف مفتاحَ الطباعة، فالاختبارُ لا يقارن شيئًا.");
        }
        finally
        {
            // هذه القاعدةُ مشتركةٌ بين الاختبارات، فلا نترك أثرًا يُغيِّر اختباراتٍ أخرى.
            var leftovers = await db.SystemSettings
                .Where(s => s.Key == lockKey || s.Key == "PrintStudio.sales_invoice.PageSize")
                .ToListAsync();
            db.SystemSettings.RemoveRange(leftovers);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>نفس خوارزمية <c>ComputePrintFingerprintAsync</c> في الخادم.</summary>
    private static async Task<string> PrintFingerprintAsync(AppDbContext db)
    {
        var rows = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key.StartsWith("PrintStudio.") || s.Key.StartsWith("Print."))
            .OrderBy(s => s.Key)
            .Select(s => new { s.Key, s.Value })
            .ToListAsync();

        var payload = string.Join("\n", rows.Select(r => $"{r.Key}={r.Value ?? string.Empty}"));
        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
    }
}
