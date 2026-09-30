using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Sales;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// مسح واحد <b>شامل</b> لرموز النسخ. الاختبارات السابقة موزّعة على ملفات مختلفة وكلٌّ منها
/// يفحص جدولًا واحدًا، فلا يلتقط أيٌّ منها إما <i>حذف</i> رمز نسخ من جدول مُغطّى في مكان آخر،
/// ولا <i>إضافة</i> رمز نسخ إلى جدولٍ لم يُرحَّل له شيء. هذا الملف يفحص المجموعة كاملة في
/// الاتجاهين: ما زاد وما نقص.
///
/// ملاحظة على المزوّد: الفحص هنا على <b>النموذج</b>، فهو لا يحتاج خادمًا ويعمل في كل تشغيل.
/// الدليل على أن هذه الرموز <i>تعمل</i> على محرّك حقيقي في
/// {@link MigrationChainSqlServerTests}، لا هنا.
/// </summary>
public sealed class ConcurrencyTokenModelTests
{
    /// <summary>
    /// الجداول الخمسة عشر التي يجب أن تحمل <c>RowVersion</c> من نوع <c>rowversion</c>. القائمة
    /// مقصودة <b>محدّدة</b>: إضافة جدول أو حذفه منها تفشل الاختبارات، فيُعاد النظر في قراره
    /// صراحةً بدل أن يمرّ صامتًا.
    /// </summary>
    private static readonly string[] ExpectedRowVersionTables =
    [
        "DeliveryIssues",
        "DeliveryOrders",
        "FiscalPeriods",
        "Items",
        "PurchaseInvoices",
        "PurchaseOrderItems",
        "PurchaseOrders",
        "PurchaseReturns",
        "SaleInvoices",
        "SaleQuotes",
        "SaleReturns",
        "SalesOrderItems",
        "SalesOrders",
        "StockLayers",
        "StockReservations"
    ];

    /// <summary>
    /// النموذج كما يراه مزوّد SQL Server، بلا أي اتصال: سلسلة الاتصال وهمية ولا تُفتح. لا
    /// يصحّ الفحص على SQLite، فمزوّد SQLite لا يعرف <c>rowversion</c> فيسقط منه العمود كلّه،
    /// فيمرّ المسح على قاعدة لا تمثّل الإنتاج. نفس سبب اختيار هذا الملف بلا خادم.
    /// </summary>
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=never-connected;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new AppDbContext(options);
    }


    /// <summary>
    /// الجداول التي تحمل عمود رمز نسخ يولّده المحرّك: رمز <c>[Timestamp]</c> من نوع
    /// <c>byte[]</c> (أو <c>IsRowVersion()</c>) - لا <c>ConcurrencyStamp</c> في هوية ASP.NET،
    /// فهو نصّ تديره التطبيق ولا يولّده المحرّك.
    /// </summary>
    private static List<string> RowVersionTables(AppDbContext db)
    {
        var tables = new List<string>();
        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var storeObject = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table);
            if (storeObject?.Name is not { } tableName)
            {
                continue;
            }

            if (entityType.GetProperties().Any(p => IsStoreGeneratedRowVersion(p, storeObject.Value)))
            {
                tables.Add(tableName);
            }
        }

        return tables;
    }

    private static bool IsStoreGeneratedRowVersion(IReadOnlyProperty property, StoreObjectIdentifier storeObject)
    {
        return property.IsConcurrencyToken
            && string.Equals(
                property.GetColumnType(storeObject),
                "rowversion",
                StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheRowversionTables_AreExactlyTheseFifteen()
    {
        using var db = CreateContext();
        var actual = RowVersionTables(db).OrderBy(t => t, StringComparer.Ordinal).ToList();
        var expected = ExpectedRowVersionTables.OrderBy(t => t, StringComparer.Ordinal).ToList();

        var missing = expected.Except(actual, StringComparer.Ordinal).ToList();
        var extra = actual.Except(expected, StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0 && extra.Count == 0,
            $"جداول rowversion لا تطابق القائمة: ناقص=[{string.Join(", ", missing)}] " +
            $"زائد=[{string.Join(", ", extra)}]");
        Assert.Equal(expected, actual);
        Assert.Equal(15, actual.Count);
    }

    [Fact]
    public void EveryRowversionToken_IsAConcurrencyToken_StoreGenerated_AndAByteArray()
    {
        using var db = CreateContext();
        var examined = 0;

        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var storeObject = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table);
            if (storeObject?.Name is not { } tableName)
            {
                continue;
            }

            foreach (var property in entityType.GetProperties())
            {
                if (!IsStoreGeneratedRowVersion(property, storeObject.Value))
                {
                    continue;
                }

                examined++;
                Assert.Equal("RowVersion", property.GetColumnName(storeObject.Value));
                Assert.True(property.IsConcurrencyToken, $"{tableName}.{property.Name} ليس رمز نسخ.");
                Assert.Equal(
                    ValueGenerated.OnAddOrUpdate,
                    property.ValueGenerated);
                Assert.Equal(typeof(byte[]), Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType);
            }
        }

        Assert.Equal(ExpectedRowVersionTables.Length, examined);
    }

    /// <summary>
    /// حارس ضد مسحٍ يمرّ دائمًا: رمز النسخ ليس كل رمز تزامن. <c>ConcurrencyStamp</c> في هوية
    /// ASP.NET رمز تزامن حقيقيّ، ومع ذلك لا يدخل القائمة؛ و<code>Customer</code> ليس له رمز
    /// نسخ أصلًا. لولاه لما نفع المسح.
    /// </summary>
    [Fact]
    public void TheSweep_SelectsOnlyStoreGeneratedRowVersion_AndNotEveryConcurrencyToken()
    {
        using var db = CreateContext();

        var identityUser = db.Model.FindEntityType(typeof(Microsoft.AspNetCore.Identity.IdentityUser))!;
        var stamp = identityUser.FindProperty("ConcurrencyStamp");
        Assert.NotNull(stamp);
        Assert.True(stamp!.IsConcurrencyToken);

        var customer = db.Model.FindEntityType(typeof(Customer))!;
        var customerStore = StoreObjectIdentifier.Create(customer, StoreObjectType.Table);
        Assert.NotNull(customerStore);
        Assert.DoesNotContain(
            customer.GetProperties(),
            p => IsStoreGeneratedRowVersion(p, customerStore.Value));

        var tables = RowVersionTables(db);
        Assert.DoesNotContain("AspNetUsers", tables);
        Assert.DoesNotContain("AspNetRoles", tables);
        Assert.DoesNotContain("Customers", tables);

        // وسيناريو الفحص: جدولٌ داخل القائمة فعلًا، فالمسح ليس فارغًا بالصدفة.
        Assert.Contains("FiscalPeriods", tables);
    }

    /// <summary>
    /// يمنع أن يبقى هذا الملفُ وحده دليلًا على رفض الكتابة القديمة: إن حُذف اختبار المحرّك
    /// الحقيقي أو تحوّل إلى <c>void</c> لا <c>Task</c>، يفشل هذا الاختبار.
    /// </summary>
    [Fact]
    public void TheSqlServerProof_OfRejectedStaleWrites_IsNotRemoved()
    {
        var method = typeof(MigrationChainSqlServerTests)
            .GetMethod(nameof(MigrationChainSqlServerTests.RowVersion_IsGeneratedByTheEngine_AndAStaleWriteIsRejected));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
        Assert.Contains(method.GetCustomAttributes(inherit: true), a => a is SqlServerFactAttribute);
    }
}
