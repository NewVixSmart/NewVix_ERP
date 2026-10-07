using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Core;

namespace NewVixSmart.Web.Services;

/// <summary>أسماءُ المجموعات التي تُحرَّر مجموعةً واحدةً في كتابةٍ واحدة، فتراود عليها.</summary>
public static class SetSubjects
{
    /// <summary>إعدادات الطباعة كلُّها: مجموعةٌ تُستبدل دفعةً واحدة.</summary>
    public const string PrintSettings = "PrintSettings";

    /// <summary>صلاحيات مستخدمٍ واحد: مجموعةٌ تُحذف وتُعاد بناؤها دفعةً واحدة.</summary>
    public static string Permissions(string userId) => $"Permissions.{userId}";

    /// <summary>
    /// بادئةُ صفوف القفل. لا تتصادم مع أي مفتاحِ إعدادٍ حقيقي: لا <c>Print.</c>
    /// ولا <c>PrintStudio.</c> ولا <c>Theme.</c>.
    /// </summary>
    private const string _prefix = "Lock.Set.";

    /// <summary>
    /// مفتاحُ صفِّ القفل في <c>SystemSettings</c>. وطولُ المفتاح محدودٌ بـ<code>100</code>
    /// في <c>SystemSetting.Key</c>، فنجزّئُه ببصمةٍ إن تجاوزه الموضوع.
    /// <para>
    /// وهو معلَنٌ للعيان لا جزءًا خاصًّا من <see cref="SetWriteGate"/>: على المُتحقِّق أن
    /// يعرف ما الذي يُنشئُه القفلُ في الجدول، وإلا بقي أثرُه على بياناتِ التطبيق غيرَ
    /// مرئيٍّ لأحد، والاختبارُ لا يستطيع أن يقرّ بأنّه ينشئ صفًّا إن لم يعرف أين يبحث.
    /// </para>
    /// </summary>
    public static string LockRowKey(string subject)
    {
        var key = _prefix + subject;
        return key.Length <= 100
            ? key
            : _prefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject)));
    }
}

/// <summary>
/// عقدٌ يفرض قبل كتابةٍ متعدّةِ المفاتيح، فيمنع تزامنًا يمحو كتابةَ الأول بلا أثر.
/// </summary>
/// <remarks>
/// <para>
/// المشكلة: كان حارسُ البصمة في <c>SavePrinting</c> وفي <c>Permissions</c> مقارنةً بين
/// قراءتين منفصلتين وكتابةٍ ثالثة. فالطلبان المتزامنان يقرآن الحالةَ نفسَها، فيعدّان
/// التصريحَين متطابقَين، ثم يكتب الثاني فيمحو كتابةَ الأول بلا أثرٍ يُعلَم. والبصمةُ
/// تُوقف المُتصفَّحَ المتروك مفتوحًا — وهو الحالةُ الشائعة فعلًا — ولا تُوقف حاجزين
/// حقيقيَّين في اللحظةِ نفسِها.
/// </para>
/// <para>
/// الحلُّ: صفُّ قفلٍ محجوز في <c>SystemSettings</c> لكل مجموعة. يُقفل هذا الصفُّ
/// بـ<code>UPDATE</code> قبل القراءة، فيحمل قفلَه الحصريَّ حتى <c>COMMIT</c>؛ وطلبٌ
/// نظيرٌ يتوقَّف عند هذا الـ<code>UPDATE</code> قبل أن يقرأ شيئًا، فلا يستأنف إلا بعد
/// انتهاء الأول، فيقرأ الحالةَ الجديدةَ ويرفض.
/// </para>
/// <para>
/// و<c>Serializable</c> وحدَه لا يكفي، وهو سببُ اختيار صفِّ القفل: فالمعاملتان تأخذان
/// قفلَ قراءةٍ مشتركًا، ثم كلٌّ منهما تريد قفلَ الكتابة، فيموت أحدُهما قفلًا. أما هنا
/// فيؤخذ القفلُ الحصريُّ على صفٍّ واحدٍ قبل القراءة، فلا يبقى مجالٌ لدورة انتظارٍ متقاطعة.
/// </para>
/// <para>
/// ولا تختلط صفوفُ القفل بمفاتيحِ التطبيق: مفتاحُها يبدأ بـ<code>Lock.Set.</code> فلا
/// يبدأ بـ<code>Print.</code> ولا <code>PrintStudio.</code> ولا <code>Theme.</code>، وكلُّ
/// قارئاتِ <c>SystemSettings</c> تبحث بمفتاحٍ مطابقٍ تمامًا لا ببادئة، فالصفُّ الإضافي
/// لا يظهر عندها.
/// </para>
/// </remarks>
public interface ISetWriteGate
{
    /// <summary>
    /// يقفل مجموعةً ويبدأ معاملةً متسلسلة. على المُستدعي <c>CommitAsync</c> أو
    /// <c>RollbackAsync</c>، وتجاهُلُ المعاملةِ يردُّها إن لم تُلتزَم.
    /// </summary>
    Task<IDbContextTransaction> AcquireAsync(string subject, CancellationToken ct = default);

    /// <summary>
    /// هل هذا الاستثناءُ تعارضُ تسلسلٍ يُترجَم إلى رفضٍ مفهوم لا إلى صفحة خطأ ٥٠٠؟
    /// </summary>
    static bool IsWriteConflict(Exception ex) =>
        ex is DbUpdateConcurrencyException
        || ex is SqlException { Number: 1205 or 3960 };
}

public sealed class SetWriteGate : ISetWriteGate
{
    private readonly AppDbContext _db;

    public SetWriteGate(AppDbContext db) => _db = db;

    public async Task<IDbContextTransaction> AcquireAsync(string subject, CancellationToken ct = default)
    {
        var key = SetSubjects.LockRowKey(subject);
        await EnsureRowAsync(key, ct);

        var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        try
        {
            // الـ`UPDATE` هو القفلُ نفسُه. قيمتُه لا تعني شيئًا؛ المهمُّ أنه يُنفَّذ قبل القراءة
            // فيحجز الصفَّ حصريًّا حتى الالتزام، فيتوقَّف النظيرُ عند هذا السطر لا عند قراءته.
            // ولو نُفِّذ بعد القراءة لبقي السباقُ قائمًا بين القراءتين.
            var locked = await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [SystemSettings] SET [UpdatedAt] = {DateTime.UtcNow} WHERE [Key] = {key}",
                ct);

            if (locked == 0)
            {
                // الصفُّ ثبتَ عند إنشائه ثم غاب قبل هذا السطر. ولأننا نملك معاملةً
                // فارغة، فالرفعُ هنا لا يترك كتابةً معلّقة. وأهمُّ ما فيها أن هذا خطأٌ
                // صريح بدل قفلٍ لا يقفل شيئًا: فإن سكتنا لمرّة، يمرُّ الحفظُ كأن قفلًا
                // هناك بينما لا قفلَ، ويعود الخسارةُ التي جئنا لنمنعها.
                throw new InvalidOperationException(
                    $"تعذَّر حجز صفّ القفل «{key}»؛ لم يعد صفّ القفل موجودًا بعد إنشائه، فلا يجوز حفظُ المجموعة دون قفل.");
            }

            return tx;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            await tx.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// يُنشأ صفُّ القفل إن لم يكن. إن صدمه مفتاحٌ أساسيٌّ من طلبٍ موازٍ فالصفُّ صار
    /// موجودًا الآن، وهو الشرطُ نفسُه الذي سعينا إليه.
    /// <para>
    /// ولا نُعامِل كلَّ <c>DbUpdateException</c> كأنه سباق إدراج، لأن في ذلك إخفاءَ
    /// عطبٍ حقيقيٍّ لا علاقة له بالمفتاح — امتلاءِ حقلٍ تجاوزَ حدَّه، أو انقطاعِ اتصالٍ
    /// عند الكتابة. فالقاعدةُ أن نتجاهل الفشلَ <em>إذا وجدنا الصفَّ</em>، وإلا نرميَه
    /// كما هو.
    /// </para>
    /// <para>
    /// وثمة سببٌ آخرُ للرفض: ما يُبتلع هنا لا يختفي، بل يمرُّ إلى الـ<code>UPDATE</code>
    /// تحته فيمسُّ صفًّا بلا وجودٍ فيُخرِج صفًّا لا يقفل شيئًا. وقفلٌ لا يقفل
    /// أسوأُ من حفظٍ بلا قفل، لأنه يُطمئنُّ بعد أن نزعنا الأمانَ.
    /// </para>
    /// <para>
    /// ولأن الاعتماد على رقمِ خطأٍ خاصٍّ بـSQL Server أو على نوعِ استثناءٍ خاصٍّ
    /// بـSQLite يربطُ هذا الملفَ بمزوِّدٍ لا يمرُّ به Web على الإطلاق، نسألُ عن النتيجةِ
    /// نفسِها: هل صار الصفُّ موجودًا الآن؟
    /// </para>
    /// </summary>
    private async Task EnsureRowAsync(string key, CancellationToken ct)
    {
        if (await _db.SystemSettings.AsNoTracking().AnyAsync(s => s.Key == key, ct))
        {
            return;
        }

        _db.SystemSettings.Add(new SystemSetting { Key = key, Value = "1", UpdatedAt = DateTime.UtcNow });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // نفصلُ الصفوفَ المضافةَ وحدَها. و`ChangeTracker.Clear()` كان سيُطاح بكائن
            // المستخدم المحمَّلٍ في `Permissions` لأنه متتبَّعٌ في هذا الـ`DbContext` أيضًا.
            foreach (var added in _db.ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Added)
                .ToList())
            {
                added.State = EntityState.Detached;
            }

            // الفشلُ بسبب سباقٍ على المفتاح: قارئُ الموازاة خَلَق الصفَّ قبلَنا.
            // وأما كلُّ فشلٍ آخرٍ فلا نُصمتُه.
            if (!await _db.SystemSettings.AsNoTracking().AnyAsync(s => s.Key == key, ct))
            {
                throw;
            }
        }
    }
}
