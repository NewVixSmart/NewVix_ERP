using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NewVixSmart.Web.Controllers;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Models.Accounting;
using NewVixSmart.Web.Models.Core;
using NewVixSmart.Web.Models.Forms;
using NewVixSmart.Web.Models.Purchases;
using NewVixSmart.Web.Models.Sales;
using NewVixSmart.Web.Models.Stock;
using Xunit;

namespace NewVixSmart.Web.Tests;

/// <summary>
/// انتشار رمز التزامن على سطور الحجز: من <c>[Timestamp]</c> على <see cref="StockReservationLine"/>
/// إلى <b>حقل النموذج</b> الذي يحمله إلى <b>معامِل الإجراء</b> الذي يستقبله. ثلاثة مواضع في
/// السلسلة، وكلٌّ منها ينكسر بصمت إن انفصل عن جاره.
/// <para>
/// ولماذا خمسة اختبارات لا اختبار واحد؟ لأنّها ثلاثة مستويات من الدليل لا مستوى واحد:
/// </para>
/// <list type="bullet">
/// <item>النموذج، بلا خادم ولا اتصال: هل العمود موجود فعلًا ورمز تزامن ومولَّد بالمحرّك؟</item>
/// <item>الانعكاس: هل الخصائص مصنّفة صحيحًا، وهل نموذج الطلب عاجز عن حمل رمز يرسله العميل؟</item>
/// <item>النصّ: هل الحقل المخفي في العرض والمعامل في الإجراء يحملان <i>الاسم نفسه</i>؟</item>
/// </list>
/// <para>
/// وحدود هذا الملف صريحة، لأنّ الجهل هنا أخطر من غياب الاختبار:
/// <b>لا يمكن لإثبات ضياع التحديث أن يعمل محليًّا.</b> الاختبارات تعمل على SQLite في الذاكرة،
/// ومزوّد SQLite لا يعرف <c>rowversion</c> فيسقط العمود كلّه ولا يفحصه، فأي كتابة قديمة تمرّ
/// بلا ملاحظة. لذلك الدليل الحقيقيّ في
/// {@link LostUpdateSqlServerTests} خلف <see cref="SqlServerFactAttribute"/>، وما هنا يثبت
/// <i>البنية</i> التي تجعل ذلك الدليل ذا معنى، لا المحرّك نفسه.
/// </para>
/// <para>
/// <b>و<code>SourceTextGuaranteeInventoryTests</code>:</b> الاختبار الرابع يقرأ ملفات المشروع
/// نصًّا، فهو من الناحية التقنية قارئ نصّيّ وينتمي إلى جرد ذلك الملف، ومُدرجٌ هناك على القيمة
/// <c>_razor</c>. ويقرأ ملفات مشروعه بالطريقة المعتمدة — <c>Path.Combine</c> مع
/// <c>"src", "NewVixSmart.Web"</c> — فتدلّه علامات الكشف فتُكتشفهventory قصدًا، لا هروبًا منها.
/// وهو <i>لا</i> يغيّر عدّاد الإنتاج الستّي، لأن ذلك يعدّ <c>_cSharp</c> و<code>_config</code>
/// فقط.
/// </para>
/// </summary>
public sealed class ConcurrencyPropagationTests
{
    /// <summary>
    /// اسم الحقل الذي يسافر فيه رمز التزامن. ليس خاصيةً على نموذج الطلب: لو كان خاصيةً لحقّها
    /// الموثِّر واشتُقّ الاسم تلقائيًّا، فصار الرمز جزءًا من النموذج مرّة أخرى. انفصال هذا الاسم
    /// عن معامل الإجراء هو الانكسار المغطّى بالاختبار الرابع.
    /// </summary>
    private const string _tokenFieldName = "orderRowVersion";

    /// <summary>عرض المبيعات الذي يرسل الرمز في نموذجه.</summary>
    private const string _salesCreateView = "Views/SalesOrders/Create.cshtml";

    /// <summary>عرض تعديل المشتريات الذي يرسل الرمز في نموذجه.</summary>
    private const string _purchaseEditView = "Views/PurchaseOrders/Edit.cshtml";

    /// <summary>وحدة البيع التي يستقبل الرمز فيها.</summary>
    private const string _salesController = "Controllers/SalesOrdersController.cs";

    /// <summary>وحدة الشراء التي يستقبل الرمز فيها.</summary>
    private const string _purchaseController = "Controllers/PurchaseOrdersController.cs";

    /// <summary>
    /// توقيع إجراء <c>Edit</c> المنشور: تُلتقط الترويسة، ثمّ يُفحص <i>قائمة معاملاته</i> عن
    /// الاسم. لماذا لا يُبحث في النصّ كلّه؟ لأنّ مطابقة الاسم في موضعٍ ليس معاملًا -تعليق، أو
    /// استدعاء <c>ApplyOrderRowVersionAsync</c>- لا يثبت شيئًا. و<code>[^)]*</code> آمن هنا
    /// لأنّ قائمة معاملات الإجراء لا تحوي أقواسًا.
    /// </summary>
    private static readonly Regex _editSignature = new(
        @"Task<IActionResult>\s+Edit\s*\((?<parameters>[^)]*)\)",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// النموذج كما يراه مزوّد SQL Server، بلا أي اتصال: سلسلة الاتصال وهمية ولا تُفتح. لا
    /// يصحّ الفحص على SQLite، فمزوّد SQLite لا يعرف <c>rowversion</c> فيسقط منه العمود كلّه،
    /// فيمرّ المسح على قاعدة لا تمثّل الإنتاج.
    /// </summary>
    private static AppDbContext ModelOnlyContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=never-connected;Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>
    /// <see cref="StockReservationLine"/> يحمل رمز تزامن الآن، لأنّ
    /// <c>StockReservationsService</c> يحمّل الحجوزات <i>متتبَّعة</i>
    /// (<c>Include(r =&gt; r.Items)</c> بلا <c>AsNoTracking</c>) ثم يزيد
    /// <c>ConsumedQuantity</c>/<c>ConsumedCount</c> على سطورها. لولا الرمز لأمكن لجلستين أن
    /// تقرآ السطر نفسه، فتمحو الكتابة الثانية الأولى بلا أن يعلم أحد. هذا الاختبار يثبت نصف
    /// العقد: نصفه المحرّكيّ في {@link LostUpdateSqlServerTests}.
    /// </summary>
    [Fact]
    public void StockReservationLine_CarriesAConcurrencyToken()
    {
        using var db = ModelOnlyContext();

        var entityType = db.Model.FindEntityType(typeof(StockReservationLine));
        Assert.NotNull(entityType);

        var storeObject = StoreObjectIdentifier.Create(entityType!, StoreObjectType.Table);
        Assert.NotNull(storeObject);
        Assert.Equal("StockReservationLines", storeObject!.Value.Name);

        var property = entityType!.FindProperty("RowVersion");
        Assert.NotNull(property);

        Assert.True(
            property!.IsConcurrencyToken,
            "StockReservationLine.RowVersion ليس رمز تزامن، فلماذا تُرفض الكتابة القديمة أصلًا؟");

        Assert.NotEqual(
            ValueGenerated.Never,
            property.ValueGenerated);
        Assert.Equal(
            ValueGenerated.OnAddOrUpdate,
            property.ValueGenerated);

        Assert.Equal(
            typeof(byte[]),
            Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType);

        Assert.Equal("RowVersion", property.GetColumnName(storeObject.Value));
    }

    /// <summary>
    /// الرمز مملوك للخادم: يُقرأ من قاعدة البيانات ولا يُقبل من نموذج. فوجود
    /// <c>[BindNever]</c> هو ما يمنع عميلًا من <i>اختيار</i> الرمز الذي يُقارَن به: بدونه
    /// يصبح الحارس صوريًّا. والاصطلاح هنا موروث من <see cref="StockReservation.RowVersion"/>،
    /// فنُبقي الاثنين متّسقين -فلو أُزيل أحدهما بقي الآخر وحده دليلًا أحاديّ.
    /// </summary>
    [Fact]
    public void StockReservationLine_RowVersionIsBindNever()
    {
        var line = typeof(StockReservationLine).GetProperty(nameof(StockReservationLine.RowVersion));
        Assert.NotNull(line);

        var reservation = typeof(StockReservation).GetProperty(nameof(StockReservation.RowVersion));
        Assert.NotNull(reservation);

        Assert.True(
            HasBindNever(line!),
            "StockReservationLine.RowVersion بلا [BindNever]: يقبله الموثِّر من الطلب، فيختاره العميل.");

        Assert.True(
            HasBindNever(reservation!),
            "StockReservation.RowVersion بلا [BindNever]: ينكسر الاتّساق مع سطر الحجز.");

        Assert.True(
            HasTimestamp(line!),
            "StockReservationLine.RowVersion بلا [Timestamp]: العمود ليس rowversion في القاعدة.");

        Assert.True(
            HasTimestamp(reservation!),
            "StockReservation.RowVersion بلا [Timestamp]: ينكسر الاتّساق مع سطر الحجز.");
    }

    /// <summary>
    /// حارس معماريّ مقصود ومكرَّر عمدًا من <see cref="OperationsIntegrityTests"/>: نماذج الطلب
    /// <b>عاجزة</b> عن حمل رمز تزامن يرسله العميل. الاختلاف مقصود: هناك يُفحَص ضمن مسحٍ
    /// واسعٍ لعيّنات من كل النماذج، وهنا واقفًا بذاته على هذا الرمز وحده - فهو أسهل ما
    /// أن يدخل الرمزُ في <c>RowVersion</c> الخاصّ بحقل
    /// الطلب ويُهمَل الحقل المخفي لأنّ الاثنين يبان متطابقين.
    /// </summary>
    [Fact]
    public void OrderFormModels_DoNotAcceptAClientSuppliedConcurrencyToken()
    {
        var sales = typeof(SalesOrderFormModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.DoesNotContain(
            sales,
            property => string.Equals(property.Name, "RowVersion", StringComparison.Ordinal));

        var purchase = typeof(PurchaseOrderFormModel).GetProperty(nameof(PurchaseOrderFormModel.RowVersion));
        Assert.NotNull(purchase);
        Assert.True(
            HasBindNever(purchase!),
            "PurchaseOrderFormModel.RowVersion موجود بلا [BindNever]: يرسله العميل متى شاء.");
        Assert.Equal(typeof(byte[]), purchase!.PropertyType);

        var purchaseLine = typeof(PurchaseOrderLineFormModel)
            .GetProperty(nameof(PurchaseOrderLineFormModel.RowVersion));
        Assert.NotNull(purchaseLine);
        Assert.True(
            HasBindNever(purchaseLine!),
            "PurchaseOrderLineFormModel.RowVersion موجود بلا [BindNever]: يرسله العميل متى شاء.");
        Assert.Equal(typeof(byte[]), purchaseLine!.PropertyType);
    }

    /// <summary>
    /// الحارس الذي لا يغطّيه أيّ اختبار سلوكيّ على SQLite: الاسم. العرض يرسل
    /// <c>name="orderRowVersion"</c>، والإجراء يستقبل <c>string? orderRowVersion</c>. وتنعدمين
    /// أحدهما -فإما تغيّر اسم الحقل، أو أُضيف معامل المعرّف فلم يبقَ مكان للرمز- لا يُعطي خطأً
    /// في أيّ مكان: النموذج يُربط، والتحديث يُحفظ، والرمز <b>لا يسمعه أحد</b>، فتنكسر حماية
    /// التزامن بصمت تامّ. وEF وSQLite لا يستطيعان كشف ذلك، فالاسم لا وجود له عندهما أصلًا؛
    /// ولهذا هو فحصٌ نصّيّ بامتياز.
    /// <para>ويُفحص معها ترميز الحقل: ما لا يمرّ عبر <c>Convert.ToBase64String</c> لا يفهمه
    /// <c>TryDecodeRowVersion</c>، فيُعدّ رفضًا ويُتجاهل الرمز بالصمت نفسه.</para>
    /// </summary>
    [Fact]
    public void OrderConcurrencyToken_TravelsAsASeparateFormField()
    {
        foreach (var view in new[] { _salesCreateView, _purchaseEditView })
        {
            var text = ReadRepositoryFile(view);

            Assert.Contains($"name=\"{_tokenFieldName}\"", text, StringComparison.Ordinal);
            Assert.Contains("Convert.ToBase64String", text, StringComparison.Ordinal);
        }

        foreach (var controller in new[] { _salesController, _purchaseController })
        {
            var text = ReadRepositoryFile(controller);

            var signatures = _editSignature
                .Matches(text)
                .Select(match => match.Groups["parameters"].Value)
                .ToList();

            Assert.NotEmpty(signatures);

            Assert.True(
                signatures.Any(parameters => parameters.Contains(_tokenFieldName, StringComparison.Ordinal)),
                $"{controller}: لا يوجد إجراء Edit يستقبل معاملًا اسمه \"{_tokenFieldName}\"، " +
                $"فالحقل المخفي في العرض يُرسَل ولا يُسمعه أحد. المعاملات الموجودة: [{string.Join(" | ", signatures)}].");
        }
    }

    /// <summary>
    /// العقد الدقيق لفكّ الرمز: يقبل base64 حقيقيًّا فقط، ويرفض <c>null</c> والفارغ والمسافات
    /// و<code>base64</code> المشوّه - وفي كل رفضٍ يخرج <c>rowVersion</c> <b>فارغًا</b> لا
    /// <c>null</c> ولا متروكًا على قيمة الاستدعاء السابق، لأنّ
    /// <c>_db.Entry(...).Property(...).OriginalValue</c> تُسنَد مباشرةً.
    /// <para>والاستدعاء ينعكس: الدالة <c>internal</c> ولا <c>InternalsVisibleTo</c> في المستودع،
    /// فلا سبيل لندائها من مشروع الاختبار مباشرةً، و<i>إضافة</i> <c>InternalsVisibleTo</c>
    /// تعديلٌ لملفٍّ لا يملكه هذا الملف. أمّا الانعكاس فيتجاوز الحاجز: استدعاء عضوٍّ غير
    /// عامٍّ عبر <c>Invoke</c> ينفَّذ على منصة .NET بلا فحص وصول، فيبقى السلوك مُختبَرًا فعلًا
    /// بدل أن يُترك لفحصٍ نصّيّ أضعف.</para>
    /// </summary>
    [Fact]
    public void TryDecodeRowVersion_AcceptsOnlyRealBase64_AndLeavesNothingBehindWhenItRefuses()
    {
        var decoder = typeof(SalesOrdersController).GetMethod(
            "TryDecodeRowVersion",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.NotNull(decoder);
        Assert.Equal(typeof(bool), decoder!.ReturnType);

        bool Decode(string? posted, out byte[] decoded)
        {
            var arguments = new object?[] { posted, null };
            var accepted = (bool)decoder.Invoke(null, arguments)!;
            decoded = (byte[])arguments[1]!;
            return accepted;
        }

        // سلسلةُ مدخلاتٍ مرفوضة: null وأخواتها من الفراغ، وbase64ٌ فاسد، وتجزئةٌ بلا ثمانية.
        var rejected = new string?[]
        {
            null,
            string.Empty,
            "   ",
            "\t",
            "not base64 at all!",
            "@@@@",
            "=",
            "A"
        };

        Assert.NotEmpty(rejected);

        foreach (var posted in rejected)
        {
            var accepted = Decode(posted, out var decoded);

            Assert.False(
                accepted,
                $"قُبِل مدخل غير صالح كأنه رمز تزامن: \"{posted ?? "<null>"}\".");

            Assert.Empty(decoded);
        }

        // ثمانية بايت: هكذا يكون طول عمود rowversion على SQL Server.
        var token = new byte[] { 0, 0, 0, 0, 0, 0, 7, 42 };

        Assert.True(
            Decode(Convert.ToBase64String(token), out var decodedToken),
            "رمز base64 صحيح رُفض: الحقل المخفي في العرض يكتب بـ Convert.ToBase64String، فلن يصل الرمز أبدًا.");

        Assert.Equal(token, decodedToken);
    }

    private static bool HasBindNever(PropertyInfo property) =>
        property.GetCustomAttributes(typeof(BindNeverAttribute), inherit: true).Length > 0;

    private static bool HasTimestamp(PropertyInfo property) =>
        property.GetCustomAttributes(typeof(TimestampAttribute), inherit: true).Length > 0;

    /// <summary>يقرأ ملفًا من جذر المستودع بمسار نسبي مكتوب بشرطة مائلة.</summary>
    private static string ReadRepositoryFile(string webProjectRelativePath)
    {
        var full = Path.Combine(RepositoryRoot(), "src", "NewVixSmart.Web", webProjectRelativePath);

        Assert.True(File.Exists(full), $"ملف المشروع المطلوب للحراسة غير موجود: {webProjectRelativePath}");

        return File.ReadAllText(full);
    }

    /// <summary>
    /// جذر المستودع: صعودًا من مجلّد التشغيل حتى ملف الحلّ. لا مسارٌ مطلقٌ مكتوبٌ يدويًّا،
    /// فهو ينكسر على أيّ جهاز غير هذا.
    /// </summary>
    private static string RepositoryRoot()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "NewVixSmart.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"تعذّر العثور على NewVixSmart.slnx بالبحث الصاعد من {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// حارسُ الصلاحيات مختلفٌ عن حرّاس إخوتِه: الحفظُ يحذفُ الصفوفَ ويعيد بناءها، فلا يبقى
    /// صفٌّ قديمٌ يحمل رمزَه. فالحارسُ مطابقةُ المجموعة المرسومة بالمجموعة المحفوظة.
    /// </summary>
    [Fact]
    public void PermissionsEdit_RefusesWhenGrantsDivergeFromWhatWasRendered()
    {
        var viewText = ReadRepositoryFile("Views/Users/Permissions.cshtml");
        var controllerText = ReadRepositoryFile("Controllers/UsersController.cs");

        Assert.True(
            viewText.Contains("name=\"renderedKeys\"", StringComparison.Ordinal),
            "Permissions.cshtml: المجموعة المعروضة لا تُرسل، فيفقد الخادمُ معرفةَ ما رُسم أصلًا.");

        // الحارس هنا مطابقةُ مجموعةٍ لا RowVersion، لأن الحفظ يحذف الصفوف ويعيد بناءها.
        Assert.True(
            controllerText.Contains("renderedKeys", StringComparison.Ordinal),
            "UsersController: لا يُقرأ ما رُسم أصلًا.");
        Assert.True(
            controllerText.Contains("SequenceEqual", StringComparison.Ordinal),
            "UsersController: لا مقارنة بين المجموعة المرسومة والمحفوظة، فيبتلع أحدُهما الآخر.");
    }

    /// <summary>
    /// إعداداتُ الطباعة تُحفظ إحدى عشرة مجموعةً دفعةً واحدة عبر مفاتيحَ مستقلّة، فلا رمزَ
    /// صفٍّ واحدٍ يحميها. فالحارسُ بصمةٌ واحدةٌ تُقارَن قبل الكتابة، والحارسُ يشمل
    /// <c>ResetPrinting</c> في بصمته، وإلا عاد ما استُعيد للتو فوق نموذجٍ قديم.
    /// </summary>
    [Fact]
    public void PrintSettingsEdit_RefusesWhenStoredLayoutsDivergeFromWhatWasRendered()
    {
        var viewText = ReadRepositoryFile("Views/Settings/Printing.cshtml");
        var controllerText = ReadRepositoryFile("Controllers/SettingsController.cs");

        Assert.True(
            viewText.Contains("name=\"printFingerprint\"", StringComparison.Ordinal),
            "Printing.cshtml: البصمة لا تُرسل، فلا يملك الخادمُ ما يقارن به.");

        Assert.True(
            controllerText.Contains("ComputePrintFingerprintAsync", StringComparison.Ordinal),
            "SettingsController: لا بصمة تُحسب أصلًا.");
        Assert.True(
            controllerText.Contains("printFingerprint", StringComparison.Ordinal),
            "SettingsController: لا تُقرأ البصمة المرسودة.");

        // ترتيبُ الفحص: يجب أن يقع قبل الحفظ، لا بعده.
        var guard = controllerText.IndexOf("ComputePrintFingerprintAsync()", StringComparison.Ordinal);
        var save = controllerText.IndexOf("SaveLayoutAsync", StringComparison.Ordinal);
        Assert.True(guard > 0 && save > 0 && guard < save,
            "SettingsController: الحارسُ بعد الحفظ، فالحمايةُ صورية.");
    }

    [Fact]
    public void MasterDataEdit_RoundTripsItsToken_AndRefusesAStaleWrite()
    {
        (string View, string Controller)[] pairs =
        [
            ("Views/Accounts/Edit.cshtml", "Controllers/AccountsController.cs"),
            ("Views/Customers/Edit.cshtml", "Controllers/CustomersController.cs"),
            ("Views/Suppliers/Edit.cshtml", "Controllers/SuppliersController.cs"),
            ("Views/Warehouses/Edit.cshtml", "Controllers/WarehousesController.cs"),
            ("Views/Settings/Branding.cshtml", "Controllers/SettingsController.cs")
        ];

        foreach (var (view, controller) in pairs)
        {
            var viewText = ReadRepositoryFile(view);

            Assert.Contains("name=\"RowVersion\"", viewText, StringComparison.Ordinal);
            Assert.Contains("Convert.ToBase64String", viewText, StringComparison.Ordinal);

            var controllerText = ReadRepositoryFile(controller);

            Assert.True(
                controllerText.Contains("OriginalValue = posted", StringComparison.Ordinal),
                $"{controller}: الرمز يصل بالعرض ولا يُسنَد إلى OriginalValue، فلا يفحصه EF.");
            Assert.True(
                controllerText.Contains("catch (DbUpdateConcurrencyException)", StringComparison.Ordinal),
                $"{controller}: لا يوجد تعاملٌ مع تعارض الكتابة، فينهار الطلب بصفحة خطأ 500.");
        }

        // البقيةُ تُغذّي الرمز من <c>data-*</c> في جدولٍ أو زرٍّ تعديل، لا من حقلٍ مخفيّ؛
        // فالمرساةُ فيها اسمُ السمة لا <c>name=\"RowVersion\"</c>.
        (string View, string Controller)[] attributePairs =
        [
            ("Views/Categories/Index.cshtml", "Controllers/CategoriesController.cs"),
            ("Views/ItemTypes/Index.cshtml", "Controllers/ItemTypesController.cs"),
            ("Views/Settings/Index.cshtml", "Controllers/SettingsController.cs"),
            ("Views/Budgets/Manage.cshtml", "Controllers/BudgetsController.cs"),
            ("wwwroot/js/site.js", "Controllers/CategoriesController.cs")
        ];

        foreach (var (view, controller) in attributePairs)
        {
            var viewText = ReadRepositoryFile(view);

            Assert.True(
                viewText.Contains("RowVersion", StringComparison.Ordinal)
                    || viewText.Contains("rowversion", StringComparison.Ordinal),
                $"{view}: لا يُرسَل الرمز إلى الخادم إطلاقًا، فلا حماية.");

            var controllerText = ReadRepositoryFile(controller);

            Assert.True(
                controllerText.Contains("OriginalValue = posted", StringComparison.Ordinal),
                $"{controller}: الرمز يصل بالعرض ولا يُسنَد إلى OriginalValue، فلا يفحصه EF.");
            Assert.True(
                controllerText.Contains("catch (DbUpdateConcurrencyException)", StringComparison.Ordinal),
                $"{controller}: لا يوجد تعاملٌ مع تعارض الكتابة، فينهار الطلب بصفحة خطأ 500.");
        }
    }

    /// <summary>
    /// الحذفُ والتعطيلُ يشاركان مع التعديل شرط <c>RowVersion</c>؛ فحمايتُهما من تعارض
    /// الكتابة لا تسري تلقائيًا على مسارات <c>Delete</c>، لأنها لا تستقبل رمزًا من النموذج
    /// بل تقرأ الصفَّ ثم تكتب، فيتولّد تزاحمٌ بين القراءة والحفظ. وبلا التقاطٍ يكون ذلك
    /// استثناءً غير معالَج أي صفحة خطأ ٥٠٠، لا رسالةً مفهومة. الاختبارُ يفحص كل دالةٍ
    /// تكتب على حاملِ رمز، ويطالبها بالتقاطٍ خاصٍّ بها لا بالتقاطٍ عابرٍ في الملف نفسه.
    /// </summary>
    [Fact]
    public void EveryDeleteAndDeactivatePathGuardsAgainstItsOwnWriteConflict()
    {
        string[] controllers =
        [
            "Controllers/AccountsController.cs",
            "Controllers/CategoriesController.cs",
            "Controllers/ItemTypesController.cs",
            "Controllers/WarehousesController.cs",
            "Controllers/SettingsController.cs",
            "Controllers/CustomersController.cs",
            "Controllers/SuppliersController.cs"
        ];

        foreach (var controller in controllers)
        {
            var text = ReadRepositoryFile(controller);

            foreach (var method in MethodsOf(text))
            {
                bool writesMasterRow =
                    RemovesARowFromDb(method.Body)
                    || method.Body.Contains("IsActive = false", StringComparison.Ordinal);

                if (!writesMasterRow)
                {
                    continue;
                }

                // الكفايةُ أن يحوي الدالةُ التقاطًا واحدًا مضلِّلًا: فدالةٌ فيها مساران
                // — تعطيلٌ وحذفٌ — تكتفي حارسًا في أحدهما وتترك الآخر ينهار بخطأ ٥٠٠.
                // فالمعيارُ هنا موضعُ كل `SaveChangesAsync` لا وجودُ الالتقاط في الجوار.
                foreach (Match flush in Regex.Matches(method.Body, @"SaveChangesAsync\("))
                {
                    Assert.True(
                        IsInsideConcurrencyGuardedTry(method.Body, flush.Index),
                        $"{controller}: «{method.Name}» يحفظ عند الموضع {flush.Index} خارج `try/catch` "
                        + "يلتقط DbUpdateConcurrencyException، فينهار الطلب بصفحة خطأ 500.");
                }
            }
        }
    }

    /// <summary>
    /// هل يقع موضعُ كتابةٍ ما داخل كتلة <c>try</c> يتبعها <c>catch (DbUpdateConcurrencyException)</c>؟
    /// <c>Remove(...)</c> و<code>IsActive = false</code> لا يكتبان في القاعدة حتى <c>SaveChangesAsync</c>،
    /// فموضعُ التفريغ (flush) هو ما يجب حمايته لا موضعُ نداء <c>Remove</c> إن كان خارجَ الكتلة.
    /// </summary>
    private static bool IsInsideConcurrencyGuardedTry(string body, int writeAt)
    {
        const string tryKeyword = "try";
        var at = 0;

        while ((at = body.IndexOf(tryKeyword, at, StringComparison.Ordinal)) >= 0)
        {
            at += tryKeyword.Length;
            var brace = body.IndexOf('{', at);

            if (brace < 0)
            {
                return false;
            }

            // لئلا نحسب `try` جزءًا من كلمةٍ أخرى، يجب ألا يسبق القوسَ سوى مسافات.
            if (body[at..brace].Trim().Length != 0)
            {
                continue;
            }

            var depth = 0;
            var close = -1;

            for (var i = brace; i < body.Length; i++)
            {
                if (body[i] == '{')
                {
                    depth++;
                }
                else if (body[i] == '}' && --depth == 0)
                {
                    close = i;
                    break;
                }
            }

            if (close < 0)
            {
                return false;
            }

            var guarded = body[(close + 1)..]
                .TrimStart()
                .StartsWith("catch (DbUpdateConcurrencyException)", StringComparison.Ordinal);

            // لا يكفي أن يسبق الحارسُ الكتابةَ: يجب أن تكون الكتابةُ داخل أقواسه، وإلا
            // سجّلَتْ كتلةُ `try` لاحقةٌ نفسها ضامنةً لكتابةٍ أسبقَها وبلا حارس.
            if (guarded && writeAt > brace && writeAt < close)
            {
                return true;
            }

            at = close + 1;
        }

        return false;
    }

    /// <summary>
    /// حذفُ صفٍّ من <c>DbContext</c> فقط: <c>_db.GLAccounts.Remove(...)</c> ونحوها.
    /// والبحث عن <c>".Remove("</c> وحدَها يُصِيب <c>Response.Headers.Remove(...)</c> في
    /// <c>PrintPreview</c>، وهو ليس كتابةً على قاعدة البيانات فلا يستحق حارسَ تزامن.
    /// </summary>
    private static bool RemovesARowFromDb(string body) =>
        Regex.IsMatch(body, @"_db\.[A-Za-z0-9_]+\.Remove\(");

    /// <summary>اسم الدالة المُعرَّفة بـ<see langword="public"/>, مع جسمها إلى القوس المُوازن.</summary>
    private static IEnumerable<(string Name, string Body)> MethodsOf(string controllerText)
    {
        const string signature = "public async Task<IActionResult> ";
        var at = 0;

        while ((at = controllerText.IndexOf(signature, at, StringComparison.Ordinal)) >= 0)
        {
            var nameStart = at + signature.Length;
            var name = controllerText[nameStart..].Split('(')[0].Trim();
            var bodyStart = controllerText.IndexOf('{', nameStart);

            if (bodyStart < 0)
            {
                yield break;
            }

            var depth = 0;
            var i = bodyStart;

            for (; i < controllerText.Length; i++)
            {
                if (controllerText[i] == '{')
                {
                    depth++;
                }
                else if (controllerText[i] == '}' && --depth == 0)
                {
                    break;
                }
            }

            yield return (name, controllerText[bodyStart..(i + 1)]);
            at = i + 1;
        }
    }
}

/// <summary>
/// الدليل الحقيقيّ على أنّ <c>StockReservationLine.RowVersion</c> يمنع ضياع التحديث - وهو
/// الوحيد القادر على ذلك: SQLite لا يولّد <c>rowversion</c> ولا يفحصه. اختبارٌ على SQLite
/// يُحقن الرمزُ فيه يدويًّا ويتغيّر بإيدينا لا بيد المحرّك، وقد سبق أن كُتب هنا بلا مقابلٍ
/// فبقي له مكانٌ واحد صحيح.
/// <para>
/// وسبب انفصاله عن <see cref="ConcurrencyPropagationTests"/> ظرفيٌّ لا ذوقيّ: صنف
/// <c>IClassFixture</c> يُبنى عند وجود اختبار غير متخطًّ في صنفه، و
/// <see cref="MigrationChainFixture"/> يستدعي <c>ResolveRequired</c> فيرمي استثناءً حين لا
/// يوجد محرّك. لو اجتمعا في صنفٍ واحد لفشلت اختبارات النموذج والنصّ -وهي عملٌ محليٌّ مشروع-
/// على أيّ جهازٍ بلا SQL Server. منفصلان: يُبنى الاثنان مرّةً واحدة في وظيفة
/// <c>migration-chain-sqlserver</c> فقط، ويبقى ما عداهما يعمل في كل تشغيلٍ محلّي.
/// </para>
/// </summary>
public sealed class LostUpdateSqlServerTests : IClassFixture<MigrationChainFixture>
{
    private readonly MigrationChainFixture _fixture;

    public LostUpdateSqlServerTests(MigrationChainFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// سيناريو الضياع كما يحدث في الخدمة: سياقان قرآ السطر نفسه ثمّ كتباه. الأول ينجح فيغيّر
    /// الرمز (المحرّك هو الذي يولّده)، والثاني يحمل الرمز القديم في شرط <c>WHERE</c> فلا يطابق
    /// صفًّا، فيُرفض بدل أن يمحو أولَه.
    /// </summary>
    [SqlServerFact]
    public async Task LostUpdate_OnAReservationLine_IsRejected()
    {
        int lineId;
        byte[] inserted;

        using (var seed = _fixture.Migrated())
        {
            var unit = new Unit { Name = "قطعة حجز متعارض" };
            var reservation = new StockReservation
            {
                ReservationNumber = "RSV-LOST-UPDATE",
                Status = StockReservationStatus.Active,
                Items =
                [
                    new StockReservationLine
                    {
                        Item = new Item
                        {
                            Name = "صنف حجز متعارض",
                            ItemType = new ItemType { Name = "نوع حجز متعارض" },
                            Category = new ItemCategory { Name = "تصنيف حجز متعارض" },
                            CountUnit = unit,
                            QuantityUnit = unit,
                            CurrentCount = 10m,
                            CurrentQuantity = 10m
                        },
                        Quantity = 10m,
                        Count = 10m
                    }
                ]
            };

            seed.StockReservations.Add(reservation);
            await seed.SaveChangesAsync();

            var seeded = Assert.Single(reservation.Items);
            lineId = seeded.Id;

            Assert.True(
                seeded.RowVersion is { Length: > 0 },
                "المحرّك لم يولّد RowVersion عند الإدراج: العمود ليس rowversion في قاعدة البيانات.");

            inserted = seeded.RowVersion!;
        }

        using var first = _fixture.CreateContext();
        using var second = _fixture.CreateContext();

        var fresh = await first.StockReservationLines.SingleAsync(l => l.Id == lineId);
        var stale = await second.StockReservationLines.SingleAsync(l => l.Id == lineId);

        // شرط السباق: السياقان قرآ الرمز نفسه، وإلا لم يكن الثاني قديمًا أصلًا.
        Assert.NotNull(stale.RowVersion);
        Assert.Equal(inserted, stale.RowVersion);
        Assert.NotNull(fresh.RowVersion);
        Assert.Equal(inserted, fresh.RowVersion);

        fresh.ConsumedQuantity += 4m;
        await first.SaveChangesAsync();

        Assert.NotNull(fresh.RowVersion);
        Assert.NotEqual(
            inserted,
            fresh.RowVersion!);

        // الآن يكتب الثاني واثقًا أنّ ما قرأه ما زال صحيحًا. وهو ليس.
        stale.ConsumedQuantity += 7m;
        var conflict = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        Assert.Contains(conflict.Entries, entry => entry.Entity is StockReservationLine);

        using var verify = _fixture.CreateContext();
        var settled = await verify.StockReservationLines.SingleAsync(l => l.Id == lineId);

        Assert.Equal(
            4m,
            settled.ConsumedQuantity);
    }

    /// <summary>
    /// ضياعُ التحديث على بياناتِ السجلّات الأساسية (عميل/مورد/حساب): المسارُ الذي فُحص في
    /// <c>LostUpdate_OnAReservationLine_IsRejected</c> نفسه، لكن على الجداول التي يحرّرها
    /// موظّفٌ من واجهة الويب لا خدمةٌ في الخلفية. والأهمّ أنّ الفشل يجب أن يقع على
    /// <c>SaveChangesAsync</c> لا على التحميل: لو حُمل الرمز المُحرَّف مع الكيان لَما اكتُشف
    /// الضياع أصلًا، ولَما صحّ أن يُقال إنّ الحماية تعمل.
    /// </summary>
    [SqlServerTheory]
    [InlineData("customers")]
    [InlineData("suppliers")]
    [InlineData("glAccounts")]
    public async Task LostUpdate_OnAMasterDataRow_IsRejected(string table)
    {
        int id;
        byte[] inserted;

        using (var seed = _fixture.Migrated())
        {
            switch (table)
            {
                case "customers":
                    var customer = new Customer { Name = "عميل متعارض" };
                    seed.Customers.Add(customer);
                    await seed.SaveChangesAsync();
                    id = customer.Id;
                    inserted = customer.RowVersion!;
                    break;
                case "suppliers":
                    var supplier = new Supplier { Name = "مورد متعارض" };
                    seed.Suppliers.Add(supplier);
                    await seed.SaveChangesAsync();
                    id = supplier.Id;
                    inserted = supplier.RowVersion!;
                    break;
                default:
                    var account = new GLAccount
                    {
                        Code = "1999",
                        Name = "حساب متعارض",
                        Type = GLAccountType.Asset,
                        NormalBalance = NormalBalance.Debit,
                        IsActive = true
                    };
                    seed.GLAccounts.Add(account);
                    await seed.SaveChangesAsync();
                    id = account.Id;
                    inserted = account.RowVersion!;
                    break;
            }

            Assert.True(
                inserted is { Length: > 0 },
                $"المحرّك لم يولّد RowVersion عند الإدراج في {table}: العمود ليس rowversion في قاعدة البيانات.");
        }

        using var first = _fixture.CreateContext();
        using var second = _fixture.CreateContext();

        // كلاهما يحمّل الكيان متتبَّعًا، تمامًا كما يفعل الـcontroller: رمزُه الأصلي هو ما قرأه.
        var fresh = await LoadMasterRowAsync(first, table, id);
        var stale = await LoadMasterRowAsync(second, table, id);

        var staleToken = RowVersionOf(stale);
        Assert.Equal(inserted, staleToken);
        Assert.Equal(inserted, RowVersionOf(fresh));

        RenameMasterRow(fresh, "الاسم الأول");
        await first.SaveChangesAsync();

        Assert.NotEqual(inserted, RowVersionOf(fresh));

        // الآن يكتب الثاني واثقًا أنّ ما قرأه ما زال صحيحًا. وهو ليس.
        RenameMasterRow(stale, "الاسم الثاني");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        using var verify = _fixture.CreateContext();
        var settled = await LoadMasterRowAsync(verify, table, id);
        Assert.Equal("الاسم الأول", NameOf(settled));
    }

    /// <summary>
    /// نفس الدليل على بقيّة الجداول التي يحرّرها المستخدم من الواجهة (تصنيف/نوع/وحدة/
    /// مستودع/فرع/سطر ميزانية). والفرقُ الوحيد أنّ سطر الميزانية لا يحمل <c>Name</c>
    /// فالحقل المُغيَّر فيه هو <c>AnnualAmount</c>، وله مفاتيح أجنبيةٌ تُزرع أولًا.
    /// </summary>
    [SqlServerTheory]
    [InlineData("itemCategories")]
    [InlineData("itemTypes")]
    [InlineData("units")]
    [InlineData("warehouses")]
    [InlineData("branches")]
    [InlineData("budgetLines")]
    public async Task LostUpdate_OnARemainingMasterDataRow_IsRejected(string table)
    {
        int id;
        byte[] inserted;

        using (var seed = _fixture.Migrated())
        {
            object saved = table switch
            {
                "itemCategories" => new ItemCategory { Name = "تصنيف متعارض" },
                "itemTypes" => new ItemType { Name = "نوع متعارض" },
                "units" => new Unit { Name = "وحدة متعارضة" },
                "warehouses" => new Warehouse { Code = "W-CONFLICT", Name = "مستودع متعارض" },
                "branches" => new Branch { Code = "B-CONFLICT", Name = "فرع متعارض" },
                _ => await SeedBudgetLineAsync(seed)
            };

            seed.Add(saved);
            await seed.SaveChangesAsync();

            id = (int)saved.GetType().GetProperty("Id")!.GetValue(saved)!;
            inserted = (byte[]?)saved.GetType().GetProperty("RowVersion")!.GetValue(saved)
                ?? throw new InvalidOperationException($"{table}: لا RowVersion بعد الإدراج.");

            Assert.True(
                inserted.Length > 0,
                $"المحرّك لم يولّد RowVersion عند الإدراج في {table}: العمود ليس rowversion في قاعدة البيانات.");
        }

        using var first = _fixture.CreateContext();
        using var second = _fixture.CreateContext();

        var fresh = await LoadRemainingMasterRowAsync(first, table, id);
        var stale = await LoadRemainingMasterRowAsync(second, table, id);

        var staleToken = RowVersionOf(stale);
        Assert.Equal(inserted, staleToken);
        Assert.Equal(inserted, RowVersionOf(fresh));

        SetMasterField(fresh, 111m);
        await first.SaveChangesAsync();
        Assert.NotEqual(inserted, RowVersionOf(fresh));

        SetMasterField(stale, 222m);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        using var verify = _fixture.CreateContext();
        var settled = await LoadRemainingMasterRowAsync(verify, table, id);
        Assert.Equal(111m, ReadMasterField(settled));
    }

    /// <summary>
    /// سطرُ الميزانية لا يُزرع وحده: مفتاحاه <c>BudgetYearId</c> و<code>AccountId</code>
    /// ليسا اختياريّين، وإلا رفضه المحرّك لا أن يثبت شيئًا.
    /// </summary>
    private static async Task<BudgetLine> SeedBudgetLineAsync(AppDbContext seed)
    {
        var year = new BudgetYear { Year = DateTime.Today.Year, IsActive = true };
        var account = new GLAccount
        {
            Code = "1998",
            Name = "حساب ميزانية متعارض",
            Type = GLAccountType.Expense,
            NormalBalance = NormalBalance.Debit,
            IsActive = true
        };
        seed.Add(year);
        seed.Add(account);
        await seed.SaveChangesAsync();

        return new BudgetLine { BudgetYearId = year.Id, AccountId = account.Id, AnnualAmount = 100m };
    }

    private static async Task<object> LoadRemainingMasterRowAsync(AppDbContext db, string table, int id)
        => table switch
        {
            "itemCategories" => await db.ItemCategories.SingleAsync(c => c.Id == id),
            "itemTypes" => await db.ItemTypes.SingleAsync(t => t.Id == id),
            "units" => await db.Units.SingleAsync(u => u.Id == id),
            "warehouses" => await db.Warehouses.SingleAsync(w => w.Id == id),
            "branches" => await db.Branches.SingleAsync(b => b.Id == id),
            _ => await db.BudgetLines.SingleAsync(l => l.Id == id)
        };

    /// <summary>
    /// الحقلُ المُغيَّر يختلف بين الجداول: خمسةٌ تحمل <c>Name</c>، وسطرُ الميزانية يحمل
    /// مبلغًا. القاسمُ المشتركُ المُقارَن عليه يجب أن يبقى واحدًا، فنستعمل قيمةً واحدةً
    /// للاثنين عبر <see cref="SetMasterField"/> و<see cref="ReadMasterField"/>.
    /// </summary>
    private static void SetMasterField(object entity, decimal marker)
    {
        var name = entity.GetType().GetProperty("Name");
        if (name is not null)
        {
            name.SetValue(entity, $"اسم بعد الحفظ {marker}");
            return;
        }

        entity.GetType().GetProperty("AnnualAmount")!.SetValue(entity, marker);
    }

    private static decimal ReadMasterField(object entity)
    {
        var name = entity.GetType().GetProperty("Name");
        if (name is not null)
        {
            // يُسترجع الرقم من الاسم، فيُقارَن بما كتبه SetMasterField لا بالنصّ كاملًا.
            var text = (string?)name.GetValue(entity)
                ?? throw new InvalidOperationException("الاسم المقروء فارغ، واختبارُ الحقل بلا معنى.");
            return decimal.Parse(
                text[(text.LastIndexOf(' ') + 1)..],
                System.Globalization.CultureInfo.InvariantCulture);
        }

        return (decimal)entity.GetType().GetProperty("AnnualAmount")!.GetValue(entity)!;
    }

    private static async Task<object> LoadMasterRowAsync(AppDbContext db, string table, int id)
        => table switch
        {
            "customers" => await db.Customers.SingleAsync(c => c.Id == id),
            "suppliers" => await db.Suppliers.SingleAsync(s => s.Id == id),
            _ => await db.GLAccounts.SingleAsync(a => a.Id == id)
        };

    private static byte[]? RowVersionOf(object entity)
        => entity.GetType().GetProperty("RowVersion")!.GetValue(entity) as byte[];

    private static void RenameMasterRow(object entity, string name)
    {
        switch (entity)
        {
            case Customer customer:
                customer.Name = name;
                break;
            case Supplier supplier:
                supplier.Name = name;
                break;
            case GLAccount account:
                account.Name = name;
                break;
        }
    }

    private static string? NameOf(object entity)
        => entity.GetType().GetProperty("Name")!.GetValue(entity) as string;
}
