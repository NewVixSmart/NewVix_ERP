# دليل الإلزام — حصيلة جلسات 2026-09/10

هذا الدليل يفصّل الشجرة غير الملتزمة (`git status`) بعد آخر commit `9ea0aef` (2026-10-02):
**90 معدّلًا + 21 غير متتبَّع = 111 ملفًا**، `2128+/484−`، وكل ما هو `??` متوقَّع
(6 هجرات × 2، `Services/SetWriteGate.cs`، حزمة الاختبارات الجديدة). لا أسرار في الشجرة ولا ملفات مؤقتة.

## الوضع قبل الإلزام (مُثبَت بالبوابات)

```text
dotnet build  NewVixSmart.slnx -c Release                      # 0 Warning(s) / 0 Error(s)
dotnet test   NewVixSmart.slnx -c Release                      # 950 Passed / 0 Failed / 1 Skipped
dotnet format NewVixSmart.slnx --verify-no-changes --no-restore # exit 0
powershell.exe -NoProfile -File scripts/check-text-hygiene.ps1  # PASS (blocking=0 warning=0 allowlisted=1)
dotnet ef migrations has-pending-model-changes --project src/NewVixSmart.Web --startup-project src/NewVixSmart.Web --configuration Release
# "No changes have been made to the model since the last migration"
```

## تسلسل الإلزام المقتَرح (7 commits، بالترتيب المنطقي)

### 1) `feat(db): هجرات القيود وفهارس اليومية ومفاتيح RowVersion`
الهجرات الست كاملة (`??`) + `Data/AppDbContext.cs` + `Migrations/AppDbContextModelSnapshot.cs`
+ نماذج RowVersion (GLAccount, BudgetLine, CompanyProfile, ItemCategory, ItemType, Unit,
Supplier, Customer, Warehouse, Branch, StockReservationLine, نماذج الطلبات).
**يُلزَم أولًا** لأنها تغيّر المخطط؛ تأكد قبل كل commit لاحق أن بوابة `has-pending-model-changes`
تبقى خضراء.

### 2) `feat(mutex): SetWriteGate لحماية الكتابات المتوازية`
`Services/SetWriteGate.cs` + تسجيله في `Program.cs` + مقاطع الكتابة المرتبطة في
`InventoryService.cs` + `ConcurrencyPropagationTests.cs` و`SetWriteGateGuardTests.cs`
و`SetWriteGateSqlServerTests.cs` و`StaleDeleteSqlServerTests.cs`.

### 3) `fix(repo): تعليق حذف الجرد بمستند القيد بدل الصنف (SourceDocumentId)`
`Models/Accounting/JournalEntry.cs` + مؤشر `SourceDocumentId` في `AppDbContext.cs`
+ `Services/AccountingService.cs` و`Services/IAccountingService.cs` + حارس
`Services/InventoryService.cs` + هجرة `20261006115732_AddJournalEntrySourceDocumentId.*`
+ الاختبارات الثلاثة في `FinancialIntegrityTests.cs` + `Fakes/ThrowingAccountingService.cs`.

### 4) `fix(import): فحص توازن القيد قبل الترحيل مع تسمية الرموز المتروكة`
`Services/ImportCenterService.cs` + `ImportDocumentIntegrityTests.cs`
(اختبار السباق 9000: `Import_JournalAccountVanishingBetweenParseAndApply_NamedInTheRejection`).

### 5) `fix(sec): بصمة الطباعة Fail-closed وأخطاء نصوص وأعلام`
`Controllers/SettingsController.cs` + `Views/Settings/Printing.cshtml`
+ `SecurityHardeningTests.cs` + `Controllers/UsersController.cs`
+ إزالة حارس RowVersion الميت: `Services/ProcurementService.cs` + `Models/Forms/PurchaseOrderFormModel.cs`
+ `ProcurementServiceTests.cs` + توحيد نمط `is { Length: > 0 }` في `Controllers/ItemsController.cs`.

### 6) `test(integrity): توسعة تغطية القيد المزدوج وخدمات المخزون والفوترة`
حزمة الاختبارات الجديدة/المعدَّلة: `InventoryVarianceAccountingTests`,
`JournalEntryLineInvariantTests`, `ReportOpenAmountRuleTests`, `PrecisionPathDeliveryTests`,
`ReturnPostingGuardTests`, `MilestoneM8aTests`, `AuditRound20CloseoutTests`,
`Round20FinancialSecurityTests`, `FiscalCloseTests`, `OperationsIntegrityTests`,
`TransactionApprovalConcurrencyTests`, `TransactionEnlistmentTests`, `DuplicatePaymentTests`
وغيرها؛ التوزيع الدقيق بين الحزم 1/6/7 يُؤكَّد بـ`git diff` لكل ملف عند كل commit.

### 7) `feat(a11y): تماسك واجهة RTL والإتاحة`
`Views/**`, `ViewModels/**`, بقية الـControllers غير المدرجة في الحزمة 5، `wwwroot/js/site.js`.

## تنبيهات
- **لا يُضمَّن** `appsettings.Development.json` (بذور للطور فقط) ولا `wwwroot/lib` (مستورد).
- لا أسرار/كلمات مرور؛ قيم البذور في `appsettings.Development.json` للتطوير فقط.
- الهجرات تُطبَّق على قاعدة Dev تلقائيًا عند إقلاع النظام (`db.Database.Migrate()` في
  `Program.cs`) — لا حاجة لأمر يدوي للتشغيل المحلي.
- رسائل Conventional Commits: `type(scope): وصف` بالعربية أو الإنجليزية حسب التغيير.
