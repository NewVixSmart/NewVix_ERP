# AGENTS.md — قواعد العمل في هذا المستودع

نظام **New Vix Smart**: ASP.NET Core 10 MVC + EF Core + SQL Server، واجهة عربية RTL، محاسبة قيد مزدوج.
اقرأ `README.md` (بنية المشروع والتشغيل) و`ACCESSIBILITY.md` (بوابة الإتاحة) **قبل** أي تعديل.

## أوامر التحقق الإلزامية

```bash
dotnet build NewVixSmart.slnx -c Release     # يجب: 0 Warning(s) و 0 Error(s)
dotnet test  NewVixSmart.slnx -c Release     # يجب: Failed: 0 (لا تخطِّ اختبارًا)
```

- شغّل `dotnet ef migrations has-pending-model-changes --project src/NewVixSmart.Web/NewVixSmart.Web.csproj` بعد أي تغيير على `Models/` أو `Data/AppDbContext.cs`؛ إذا ظهر تغيير أنشئ migration.
- الاختبارات تعمل على SQLite في الذاكرة — لا تحتاج LocalDB ولا شبكة.
- لا تُرسل كودًا مع `dotnet build` يفشل أو مع اختبار متعثر؛ أصلح السبب الجذري بدل تعطيل الاختبار.

## قيود معمارية

- **UI في `Views/`** لا يحتوي منطق عمل؛ منطق العمل في `Services/` (مُسجَّلة في `Program.cs`)، وEF queries داخل الخدمات لا داخل الـcontrollers.
- **لا تغيّر جسم الملف عند التعديل.** استخدم `apply_patch` / أداة التحرير: أي حذف وإعادة كتابة كامل يفسد التغييرات المتوازية. استثناء واحد: تنسيق mechanically بحت يُعيد نفس البنية (شريط فاصل مسافاتSoft wrap).
- **DDL**: أي تغيير في مخطط الجداول يحتاج migration جديدًا بالأسماء snake_case و`IX_` للفهارس، مع حذف الـmigration إن كان التطبيق فشل للتو. لا تغيّر migration مُطبَّقة بالفعل.
- **الخصائص المحسوبة لا تُستخدم في `Where` داخل EF**: الحقول المحسوبة في النماذج (مثل `PendingQty`, `UninvoicedQty`, `Available`) ليست mapped، فاستخدم الفروق الحقيقية للأعمدة داخل الاستعلام أو احسب بعد `ToListAsync()`.
- **المحاسبة**: قيد مزدوج إلزامي. التسليم في مسار `AtInvoice` يرحّل التكلفة فقط (`Dr 5000 / Cr 1300`); الفاتورة ترحّل الذمم والإيراد والضريبة. راجع `PrintPdfBuilder`/`AccountingService` قبل تغيير أي قيد.
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
