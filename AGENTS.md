# AGENTS.md — قواعد العمل في هذا المستودع

نظام **New Vix Smart**: ASP.NET Core 10 MVC + EF Core + SQL Server، واجهة عربية RTL، محاسبة قيد مزدوج.
اقرأ `README.md` (بنية المشروع والتشغيل) و`ACCESSIBILITY.md` (بوابة الإتاحة) **قبل** أي تعديل.

## العملة: جنيه مصري واحد (EGP-only)

- النظام **بلعملة واحدة فقط**. لا تضيف `CurrencyId` ولا `ExchangeRate` ولا `BaseAmount` ولا أعمدة
  FX ولا جدول `Currencies` ولا شاشة عملات. الاعتماد على `Services/Money.cs` للعرض فقط
  (`Money.Format`، الرمز `L.E`).
- `AllocatedAmount` في جدولي التوزيعات هو المبلغ المُسوّى بالجنيه المصري (أُعيدت تسميته من
  `AllocatedBaseAmount` في هجة `DropMultiCurrency_EgpOnly`).
- حسابات فروق العملة `4400`/`8400` ملغاة: لا ت.seedها، و`AccountsService.SystemSeedCodes`
  لا تحتويها.
- قرار المعمارية الكامل: `docs/DECISION-EGP-ONLY.md`.

## أوامر التحقق الإلزامية

```bash
dotnet build NewVixSmart.slnx -c Release     # يجب: 0 Warning(s) و 0 Error(s)
dotnet test  NewVixSmart.slnx -c Release     # يجب: Failed: 0 (لا تخطِّ اختبارًا)
dotnet format NewVixSmart.slnx --verify-no-changes --no-restore   # بوابة التنسيق: exit 0
pwsh -NoProfile -File scripts/check-text-hygiene.ps1             # بوابة النص: exit 0
dotnet ef migrations has-pending-model-changes \
  --project src/NewVixSmart.Web/NewVixSmart.Web.csproj \
  --startup-project src/NewVixSmart.Web/NewVixSmart.Web.csproj \
  --configuration Release    # يجب: No changes have been made to the model
```

- **`dotnet test` هو `-c Release` دائمًا.** لا تشغّل `dotnet test` بلا `-c`: الإصدار Debug
  ليس هو المبني في CI، والنتيجة حينها لا تقول شيئًا عن ما يُشحن.
- شغّل `has-pending-model-changes` بعد أي تغيير على `Models/` أو `Data/AppDbContext.cs`؛
  إن ظهر تغيير أنشئ migration. ما بينه وبين `migrations add`: راجع الـmigration المطبَّقة ولا تعدّلها.
- **`dotnet format` يعمل على `.cs` وحده.** لا يفحص `.cshtml` ولا `.md` إطلاقًا. وكل ما يخص
  تلك الامتدادَين من ترميز/أسطر/مسافات هو `check-text-hygiene.ps1`، لا `dotnet format`.
- **`check-text-hygiene.ps1` يقرأ ولا يكتب**، وR8/R10 فيه تحذيرات لا أخطاء: الحكم عليها
  بشري. الاستثناءات: `wwwroot/lib` (مستورد من)، ومجلدات المخرجات، و`Migrations/*.Designer.cs`
  (يعيد EF كتابتها). اطبع القائمة بـ`-ListExclusions`.
- **`--no-restore` يحتاج restore سابقًا** (`dotnet restore` أو build). رتّبها كما في CI:
  restore ← format ← build.
- **سلسلة الهجرات لا تُفحص محليًا**: اختبارات `MigrationChainSqlServerTests` تُتخطّى بلا
  `NVS_TEST_SQLSERVER`. ما ينفّذ الـ38 هجرة على محرك حقيقي هو وظيفة `migration-chain-sqlserver`
  في CI. لا تعتمد على `has-pending-model-changes` كبديل: هو يقارن النموذج باللقطة، ولا ينفّذ هجرة.
- الاختبارات تعمل على SQLite في الذاكرة — لا تحتاج LocalDB ولا شبكة.
- لا تُرسل كودًا مع `dotnet build` يفشل أو مع اختبار متعثر؛ أصلح السبب الجذري بدل تعطيل الاختبار.

## قيود معمارية

- **UI في `Views/`** لا يحتوي منطق عمل؛ منطق العمل في `Services/` (مُسجَّلة في `Program.cs`)، وEF queries داخل الخدمات لا داخل الـcontrollers.
- **لا تغيّر جسم الملف عند التعديل.** استخدم `apply_patch` / أداة التحرير: أي حذف وإعادة كتابة كامل يفسد التغييرات المتوازية. استثناء واحد: تنسيق mechanically بحت يُعيد نفس البنية (شريط فاصل مسافاتSoft wrap).
- **DDL**: أي تغيير في مخطط الجداول يحتاج migration جديدًا بالأسماء snake_case و`IX_` للفهارس، مع حذف الـmigration إن كان التطبيق فشل للتو. لا تغيّر migration مُطبَّقة بالفعل.
- **الخصائص المحسوبة لا تُستخدم في `Where` داخل EF**: الحقول المحسوبة في النماذج (مثل `PendingQty`, `UninvoicedQty`, `Available`) ليست mapped، فاستخدم الفروق الحقيقية للأعمدة داخل الاستعلام أو احسب بعد `ToListAsync()`.
- **المحاسبة**: قيد مزدوج إلزامي. التسليم في مسار `AtInvoice` يرحّل التكلفة فقط (`Dr 5000 / Cr 1300`); الفاتورة ترحّل الذمم والإيراد والضريبة. راجع `PrintPdfBuilder`/`AccountingService` قبل تغيير أي قيد. التوزيع في `PaymentService` oldest-first بنسبة 1:1 على `NetAmount` المتبقي، مع **تسامح صفر**: أي residuum أكبر من `0` يُرفض، لأن القيد يرحّل `Payment.Amount` كاملًا على الذمم بينما التوزيع يحدّ بـ`NetAmount` — فَتسامحُ الجنيهِ الواحدِ يجعلُ دفترَ الأستاذِ يختلفَ عن سجلِّ الذممِ الفرعيّ. عتبات `0.005` ممنوعة في التوزيع أيضًا. الفاتورة تصبح قابلة للتحصيل/تظهر في أعمار الذمم عبر `DeliveryOrder` مسلَّم **أو** `DeliveryIssue` بحالة `Issued` (مسار الفوترة بعد التسليم لا ينشئ `DeliveryOrder` مرتبطًا).
- **الأذونات**: كل إجراء جديد يحتاج `[RequirePerm("Module.Action")]` و`[Authorize]` عبر الـfilter. أضف الوحدة إلى `PermissionCatalog` (actions + `MenuModule`)، وإلى أدوار `PermissionDefaults`، ورابطًا في `_Layout.cshtml`. دور `Warehouse` يملك حجز/تسليم دون الحاجة إلى `Sales.Create`.
- **الاختبارات**: أضف اختبارًا في `tests/NewVixSmart.Web.Tests/` لكل حالة عمل/قيد جديد. اختبار الـround sweeps في `AuthorizationSweepTests.cs` يجب أن يبقى أخضرًا (كل إجراء محمي، وكل وحدة في الكتالوج).

## واجهة RTL وإتاحة

- الواجهة `dir="rtl"`, `lang="ar"`. لا تكسر ذلك.
- كل `<table>` يحتاج `<caption>` وكل `<th>` يحتاج `scope`؛ لا ألوان hex مباشرة — استخدم أصناف Bootstrap دلالية لتعمل في الوضعين الفاتح والداكن.
- النص المعروض للمستخدم عربي واضح؛ لا مصطلحات إنجليزية في الأزرار/العناوين.

## آداب الحفظ

- رسالة commit بنمط Conventional Commits: `feat(scope): وصف` بالعربية أو الإنجليزية حسب التغيير.
- لا تضع أسرارًا أو كلمات مرور في المستودع. قيم البذور في `appsettings.Development.json` للتطوير فقط.
- ملفات المخرجات المؤقتة (cookies، logs، screenshots) مكانها `$env:TEMP\opencode` ولا تدخل git.
