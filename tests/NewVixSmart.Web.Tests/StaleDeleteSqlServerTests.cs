using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Stock;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// الدليلُ على سبب التقاط <c>DbUpdateConcurrencyException</c> في مسارات الحذف، وهو غائبٌ عن
/// <c>LostUpdate_OnARemainingMasterDataRow_IsRejected</c> لأن ذلك يثبت refusals على
/// <c>UPDATE</c> فقط.
/// <para>
/// <c>RowVersion</c> يدخل شرط <c>DELETE</c> كما يدخل شرط <c>UPDATE</c>، فحذفٌ بصفٍّ محمَّلٍ
/// قبل تعديل زميلٍ يفشل. والـcatch الذي في الشيفرة مبنيٌّ على هذا الفشل تحديدًا: لو سقط هذا
/// <see cref="Assert.ThrowsAsync{T}"/> لسقط الدليلُ وبقيت سبعةُ حواجز في الشيفرة بلا سببٍ
/// مُثبَت — وهو أسوأُ من غيابها، لأن القارئَ يجدها فيفترض أنها محميّة.
/// </para>
/// <para>
/// يلزم المحرّكُ الحقيقيّ: SQLite لا يولّد <c>rowversion</c> ولا يفحصه، فاختبارٌ هناك
/// يمضي بلاشرطٍ فينجح واهمًّا أنه محميٌّ؛ فاعتمد على <c>IClassFixture</c> يمنح
/// اختبار غير متخطًّ في صنفه، و<a cref="MigrationChainFixture.ResolveRequired"/> يؤكّد أن
/// الاختبار لا يبني إلا على محرّكٍ مُرحَّل. وإن غاب المحرّك تخطّاه
/// <see cref="SqlServerTheoryAttribute"/> بإعلانٍ صريح.
/// </para>
/// <para>
/// كلُّ حالةٍ مستقلّةٌ بقيمٍ فريدة، فلا يتصادم اختباران على رمزِ كودٍ واحد.
/// </para>
/// </summary>
public sealed class StaleDeleteSqlServerTests : IClassFixture<MigrationChainFixture>
{
    private readonly MigrationChainFixture _fixture;

    public StaleDeleteSqlServerTests(MigrationChainFixture fixture)
    {
        _fixture = fixture;
    }

    [SqlServerTheory]
    [InlineData("itemCategories", "C")]
    [InlineData("itemTypes", "T")]
    [InlineData("units", "U")]
    [InlineData("warehouses", "W")]
    [InlineData("branches", "B")]
    public async Task StaleDelete_IsRejected_JustLikeAStaleUpdate(string table, string codePrefix)
    {
        int id;
        byte[]? inserted;

        using (var seed = _fixture.Migrated())
        {
            var saved = SeedRow(table, codePrefix);
            seed.Add(saved);
            await seed.SaveChangesAsync();

            id = IdOf(saved);
            inserted = RowVersionOf(saved);

            Assert.True(
                inserted is { Length: > 0 },
                $"المحرّك لم يولّد RowVersion عند الإدراج في {table}: العمود ليس rowversion في قاعدة البيانات.");
        }

        using var deleter = _fixture.CreateContext();
        using var editor = _fixture.CreateContext();

        // تحميلٌ سابقٌ للتحديث: يلتقط الحاذفُ النسخةَ قبل أن يكتبَ المحرِّرُ تغييرًا
        // للكتابة الأخرى. لو انقلبَ لكان الحذفُ صحيحًا ولم يرفع الاستثناء أصلًا.
        var stale = await LoadAsync(deleter, table, id);
        Assert.Equal(inserted, RowVersionOf(stale));

        var fresh = await LoadAsync(editor, table, id);
        SetName(fresh, $"معدَّل قبل الحذف {id}");
        await editor.SaveChangesAsync();

        RemoveRow(deleter, table, stale);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => deleter.SaveChangesAsync());

        // الصفُّ باقٍ: الرفضُ ليس صمتًا أفرغ الجدولَ، بل منعَ الحذف الفاشل.
        using var verify = _fixture.CreateContext();
        var settled = await LoadAsync(verify, table, id);
        Assert.Equal($"معدَّل قبل الحذف {id}", NameOf(settled));
    }

    private static object SeedRow(string table, string codePrefix) => table switch
    {
        "itemCategories" => new ItemCategory { Name = $"تصنيف محذوف {codePrefix}" },
        "itemTypes" => new ItemType { Name = $"نوع محذوف {codePrefix}" },
        "units" => new Unit { Name = $"وحدة محذوفة {codePrefix}" },
        "warehouses" => new Warehouse { Code = $"W-DEL-{codePrefix}", Name = $"مستودع محذوف {codePrefix}" },
        _ => new Branch { Code = $"B-DEL-{codePrefix}", Name = $"فرع محذوف {codePrefix}" }
    };

    private static async Task<object> LoadAsync(AppDbContext db, string table, int id) => table switch
    {
        "itemCategories" => await db.ItemCategories.SingleAsync(c => c.Id == id),
        "itemTypes" => await db.ItemTypes.SingleAsync(t => t.Id == id),
        "units" => await db.Units.SingleAsync(u => u.Id == id),
        "warehouses" => await db.Warehouses.SingleAsync(w => w.Id == id),
        _ => await db.Branches.SingleAsync(b => b.Id == id)
    };

    /// <summary>
    /// الحذفُ بـ<c>Remove</c> على الـ<c>DbSet</c> الصحيح؛ فـcast إلى <c>dynamic</c> وحده
    /// لا يكفي، لأنّ <c>Set&lt;T&gt;</c> مختلفون لا يشاركون <c>EntityEntry</c> نوعًا واحدًا.
    /// </summary>
    private static void RemoveRow(AppDbContext db, string table, object entity)
    {
        switch (entity)
        {
            case ItemCategory category:
                db.ItemCategories.Remove(category);
                break;
            case ItemType itemType:
                db.ItemTypes.Remove(itemType);
                break;
            case Unit unit:
                db.Units.Remove(unit);
                break;
            case Warehouse warehouse:
                db.Warehouses.Remove(warehouse);
                break;
            case Branch branch:
                db.Branches.Remove(branch);
                break;
            default:
                throw new InvalidOperationException($"{table}: نوعٌ غير متوقّع في RemoveRow: {entity.GetType().Name}.");
        }
    }

    private static void SetName(object entity, string name)
    {
        switch (entity)
        {
            case ItemCategory category:
                category.Name = name;
                break;
            case ItemType itemType:
                itemType.Name = name;
                break;
            case Unit unit:
                unit.Name = name;
                break;
            case Warehouse warehouse:
                warehouse.Name = name;
                break;
            case Branch branch:
                branch.Name = name;
                break;
            default:
                throw new InvalidOperationException($"نوعٌ غير متوقّع في SetName: {entity.GetType().Name}.");
        }
    }

    private static string NameOf(object entity) => entity switch
    {
        ItemCategory category => category.Name,
        ItemType itemType => itemType.Name,
        Unit unit => unit.Name,
        Warehouse warehouse => warehouse.Name,
        Branch branch => branch.Name,
        _ => throw new InvalidOperationException($"نوعٌ غير متوقّع في NameOf: {entity.GetType().Name}.")
    };

    private static int IdOf(object entity)
        => (int)entity.GetType().GetProperty("Id")!.GetValue(entity)!;

    private static byte[]? RowVersionOf(object entity)
        => entity.GetType().GetProperty("RowVersion")!.GetValue(entity) as byte[];
}
