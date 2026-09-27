# خطة البناء الشاملة — ترقية New Vix Smart إلى منظومة محاسبية/تشغيلية عالمية المستوى

> التاريخ: 2026-09-03
> النطاق: P0 (GL + FIFO) → P1 (مستودعات/نقل، RFQ→أمر شراء، API) → P2 (تقارير/باركود/تنبيهات، عملات/سيرك)
> النهج: تسلسل معماري صارم بنقاط تحقق بعد كل مرحلة، وتنفيذ عبر فريق Agents بعد إغالق النطاق
> **التحقق الدائم**: `dotnet build` 0W/0E + هجرات تطبَّق + `dotnet test` كلها PASS + Smoke E2E `CONSOLE_ERRORS: []`

---

## القرار المعماري (مهم — يُقرأ أولًا)

1. **النواة المحاسبية (GL) أولًا وقبل أي شيء.** دفتر الأستاذ العام (Debit/Credit + مخطط حسابات COA) هو العمود الفقري. كل العمليات الموجودة (فواتير بيع/شراء، مدفوعات، مرتجعات، جرد) ستولّد قيودًا تلقائيًا عبر محرك قرار واحد (GL engine). بدون هذا الأساس، كل ما بعده (تقارير/تكلفة/تكامل) يبني على رمل.
2. **الفقرة GL تُبنى كوحدة معزولة** (لا تُربك وحدة التشغيل الحالية): قرارات تُشتق من الأحداث الحقيقية للمستندات، وتُكتَب في جداول جديدة (`Accounts`, `GLAccountTypes`, `JournalEntries`, `JournalEntryLines`) بجوار الجداول الموجودة، مع أرقام الدفتر (`EntryNumber`) ذرّية.
3. **FIFO فوق المخزون:** طبقات تكلفة لكل صنف (`StockLayers`) تُعبأ عند الاستلام وتُستهلك عند البيع/المرتجع — بدل المتوسط البسيط الحالي. تبقى أرصدة `Item.CurrentCount/CurrentQuantity` كمصدر عرض، بينما التكلفة الحقيقية (COGS وتقييم المخزون) من الطبقات. **يجب أن تبقى كل الاختبارات الحالية (18) خضراء.**
4. **معاملة واحدة لكل عملية مركّبة:** إنشاء الفاتورة + طبقات FIFO + قيد GL في نفس `BeginTransactionAsync` (نمط `InventoryService` الحالي). لا ذرّية جزئية.
5. **لا Stop-the-world:** أعمدة/جداول جديدة فقط؛ لا تغيير في شكل الأعمدة الحالية للمرحلة P0 (تُضاف أعمدة الانتقال مثل `WarehouseId` في P1 بهجرات إسقاط بحرص).

---

## P0-A — مخطط الحسابات (COA) ومحرك القيود

### النموذج
- `Models/Accounting/GLAccount.cs`:
  - `Id`, `Code` (خطة فريدة), `Name` (عربي), `Type` (enum `GLAccountType`), `IsActive`, `ParentAccountId` (لتجميع محاسبة), `NormalBalance` (Debit/Credit).
  - أنواع: `Asset, Liability, Equity, Revenue, Expense` + `Contra*` اختياري.
- `Models/Accounting/GLAccountType.cs` — enum أعلاه.
- `Models/Accounting/JournalEntry.cs`:
  - `Id`, `EntryNumber` (فريدة، `GL-{seq}`), `Date`, `Description` (عربي), `Reference` (نوع + رقم مستند المصدر), `ReferenceType` (enum `JournalSource`: SaleInvoice/PurchaseInvoice/Receipt/Disbursement/SaleReturn/PurchaseReturn/OpeningStock/Adjustment), `ReferenceId`, `CreatedBy`, `CreatedAt`, `IsPosted`.
- `Models/Accounting/JournalEntryLine.cs`:
  - `Id`, `JournalEntryId`, `AccountId`, `Debit`, `Credit`, `Description`.
  - حارس في التطبيق: لكل قيد، مجموع Debit == مجموع Credit (يصحح السالب بالمبلغ المطلق).

### مخطط حسابات افتراضي (بذرة `SeedData`)
حوّل الحسابات الأساسية لعملك إلى COA:
| Code | الاسم | النوع | طبيعة |
|---|---|---|---|
| 1000 | النقد/الصندوق | Asset | Debit |
| 1100 | البنوك/الحسابات | Asset | Debit |
| 1200 | المدينون (عملاء) | Asset | Debit |
| 1300 | المخزون | Asset | Debit |
| 2000 | الدائنون (موردون) | Liability | Credit |
| 3000 | رأس المال | Equity | Credit |
| 4000 | إيرادات المبيعات | Revenue | Credit |
| 4100 | إيرادات مرتجعات البيع (Contra-Revenue) | Revenue | Credit |
| 5000 | مشتريات/تكلفة البضاعة (COGS) | Expense | Debit |
| 5100 | مرتجعات الشراء (Contra-Expense) | Expense | Debit |
| 5200 | الضريبة المسددة/الواجبة | Expense/Liability | Debit |

### محرك القيود `Services/AccountingService.cs` (واجهة `IAccountingService`)
طريقة واحدة صريحة `PostAsync(sourceType, sourceId, date, lines, user)` تتحقق:
- كل سطر `Debit>0` XOR `Credit>0`، والمقدار > 0.
- مجموع Debit == مجموع Credit.
- توليد `EntryNumber` ذرّي + حفظ.
و `HandleEventAsync(...)` يوزّع نوع الحدث إلى وظائف مرحلة مبكرة داخل معاملة المستند:
- SaleInvoice: دين المدينين (Customer) / دائن إيراد المبيعات.
- Payment(Receipt): دين النقد أو البنك / دائن المدينين.
- PurchaseInvoice: دين المخزون / دائن الدائنين.
- Payment(Disbursement): دين الدائنين / دائن النقد أو البنك.
- SaleReturn: دين مرتجعات البيع (Contra) / دائن المدينين.
- PurchaseReturn: دين الدائنين / دائن مرتجعات الشراء.
- جرد افتتاحي: دين/دائن المخزون و/أو رأس المال.

**التكامل:** استدعِ `AccountingService.HandleEventAsync` داخل `CreateSaleAsync`/`CreatePurchaseAsync`/المدفوعات/المرتجعات ضمن نفس المعاملة.

---

## P0-B — إدارة التكلفة FIFO

### النموذج
- `Models/Stock/StockLayer.cs`: `Id`, `ItemId`, `WarehouseId` (nullable في P1 قبل إضافة المستودعات — أو جهّز العمود الآن), `Qty`, `Count`, `UnitCost`, `DateReceived`, `RemainingQty`, `RemainingCount`.
  - قيود: `Remaining* >= 0`، عند الاستلام يُنشأ/يُدمج طبقة، عند البيع يُستهلك أقدمًا (ترتيب `DateReceived` ثم `Id`).
- يُضاف دومًا `[Timestamp]`/`RowVersion` على الفواتير لمنع تضارب التخصيص.

### التكامل في `InventoryService` (تُعدّل بلطف دون كسر الاختبارات)
- في `CreatePurchaseAsync`: بعد قبول البند، اعتمد/قسّم طبقات FIFO (إذا كانت `UnitPrice` مختلفًا لكل بند).
- في `CreateSaleAsync`: استهلك أقدم طبقات حتى تغطي الكمية/العدد — احسب `COGS` الحقيقي وأعطِه لمحرك القيود (هذا يغذي `Financial`).
- في `CreateSaleReturnAsync/CreatePurchaseReturnAsync`: أعد التدليل عكسيًا بما يعكس الطبقة المستهلكة (أعد إلى أعلى طبقة غير فارغة، أو ببساطة عدّل بـ UnitPrice المستند) — واجعله متسقًا ومستقرًا.

> **لا تثبت القيم من العميل**: التكلفة/الكمية في الطبقات تُدار خادميًّا. لا حقل `UnitCost` قابل للربط من الواجهة.

---

## P1-A — مستودعات متعددة + عمليات نقل/تحويل

### النموذج
- `Models/Stock/Warehouse.cs`: `Id`, `Code`, `Name`, `IsActive`, `CreatedAt`.
- `StockMovement.WarehouseId` (مطلوب) + `SourceWarehouseId/TargetWarehouseId` لحركات النقل (نوع نقل جديد `DocumentType.Transfer`).
- `Models/Stock/StockTransfer.cs` + `StockTransferItem.cs`: نقلم + كمية + طبقات FIFO حقيقية (التحويل لا يكسر التكلفة: ينقل طبقة أو يقسّم).

### التكامل
- `ApplyStockAsync` تقبل `WarehouseId`؛ فهرسة حركة المخزون لكل مستودع.
- شاشة UI: قائمة المستودعات + إنشاء نقل منتخب باختيار مصدر/هدف + الكمية.
- عرض أرصدة المخزون لكل مستودع (تقرير/جدول).
- لا يُسمح نقل أزيد من الرصيد (نفس مراقبة ApplyStock).

---

## P1-B — RFQ → أمر شراء → استلام → فاتورة

### النموذج/التدفق
- `Models/Purchases/PurchaseOrder.cs` + `PurchaseOrderItem.cs` (أمر شراء مبدئي الموافقة، قابل للاستلام الجزئي).
- خطوات متسلسلة: طلب اقتراض/عرض → تحويل إلى أمر شراء → استلام (نقل للمخزون) → تترجم فاتورة شراء من الاستلام.
- ربط `PurchaseInvoice` بـ `PurchaseOrderId` وإعادة استخدام `InventoryService` الموجودة عند الترحيل.

---

## P1-C — API عديمة الحالة (REST + JWT + OpenAPI)

- إضافة مشروع `NewVixSmart.WebApi` منفصل (يشارك نماذج/خدمات) أو تحويل التطبيق الحالي ليواجه HTTP API منفصلًا.
- توثيق JWT (بأذونات على أساس مفاتيح `Perm` الحالية) — نقاط لـ: الأصناف، المخزون (أرصدة/طبقات)، الفواتير، المدفوعات، القيود.
- Swagger/OpenAPI. عدم كشف الأسرى؛ استخدام `appsettings` عبر User Secrets.

---

## P2-A — تقارير جاهزة (PDF/XLSX) + باركود + تنبيهات استحقاق
- ترقية `ReportExportService` لإنتاج XLSX/PDF الحقيقيين (مكتبة موثوقة) بجانب CSV.
- باركود على البوليصات والبطاقات.
- تنبيه قائمة استحقاق (مهلات Net*) في لوحة/إشعار.

## P2-B — تعدد عملات/أفرع + سيرك بيانات
- عملة أساسية + سعر صرف + تحويل في القيود.
- أفرع/وحدات عمل (اختياري) بأرصدة منفصلة.

---

## تسلسل التنفيذ بأمر الأولوية (для Agents والجلسات)

| المرحلة | المهام | البوابة (قبل التالي) |
|---|---|---|
| **M0** | إعادة هيكلة نماذج/ADbContext للجداول الجديدة + COA seed | build 0W/0E + هجرة تطبَّق |
| **M1** | `AccountingService` + ربطه بكل العمليات (بيع/شراء/دفعات/مرتجعات/جرد) | **قيود صحيحة موثّقة** لكل نوع |
| **M2** | FIFO: `StockLayer` + استهلاك/إعادة تدليل داخل `InventoryService` | **18 اختبارًا خضراء** + COGS صحيح |
| **M3** | قوائم مالية: ميزان المراجعة + قائمة الدخل + الميزانية (مراجعة) | أرقام متوازنة في Smoke |
| **M4** | مستودعات + نقل/تحويل | ننقل يعمل بلا كسر تكلفة |
| **M5** | RFQ→أمر شراء→استلام | حلقة المشتريات كاملة |
| **M6** | API JWT + OpenAPI | نقطة تتضمن Sequelite/تلحق التحويل |
| **M7** | تقارير/باركود/تنبيهات | تصدير + تنبيه |
| **M8** | عملات/أفرع + اختبار شامل + مراجعة + Audit نهائي | الكل أخضر |

---

## قاعدة إلزامية لكل أجينت
- لا تعدّل بذرة/سلوك موجود دون سم̅ه (`dotnet test` كلها PASS بعد كل مرحلة).
- كل عمود جديد بهجرات إسقاط، لا تعديل يدوي للـ snapshot.
- لا ملاحظات في الكود إلا عند توثيق قرار معماري يحتاجه القارئ (لا تعليقات عادية).
- لا secrets مكتوبة؛ استخدم User Secrets/Env.
- اقتبس أدلة `file:line` في أي تقرير.

---

## سجل الإكمال (Completion Log)

### P0 — مكتمل ومتحقَّق (2026-09-03)

| المرحلة | ما أُنفذ | التحقق |
|---|---|---|
| M0+M1 | COA + `AccountingService` (دفتر مزدوج) + ربطه ببيع/شراء/دفعات/مرتجعات/جرد + seed الـ 10 حسابات | build 0W/0E؛ هجرة `AddGeneralLedger`؛ **25 اختبارًا** |
| M2 | FIFO `StockLayer` + استهلاك/إعادة تدليل + COGS API `GetConsumedCostAsync` | هجرة `AddStockFifoLayers`؛ **31 اختبارًا** |
| M3 | `FinancialReportService` + قوائم: ميزان المراجعة/الدخل/الميزانية + 3 أفعال `Reports/TrialBalance|IncomeStatement|BalanceSheet` + روابط قوائم | **31 اختبارًا**؛ الصفحات 200 و`CONSOLE_ERRORS: []` |

**النقطة الحالية**: P0 (النواة المحاسبية + التكلفة) مكتمل — **بوابة التحقق**: build 0W/0E، `dotnet test` 31/31، Smoke شامل `CONSOLE_ERRORS: []`، الصفحات المالية الثلاث ترسم بقيم صحيحة.

### P1 — مكتمل ومتحقَّق (2026-09-03)

| المرحلة | ما أُنفذ | التحقق |
|---|---|---|
| M4 | مستودعات `Warehouse` + نقل/تحويل `StockTransfer` + أذونات + هجرة `AddWarehousesAndStockTransfer` + **إصلاح seed** (توثيق WH-001/WH-002 قبل حارس GL) | **37 اختبارًا**؛ `/Warehouses` يعرض المستودعين؛ الصفحات 200 |
| M5 | مشتريات RFQ→أمر شراء→استلام: `PurchaseOrder/Item` + `ProcurementService` + `PurchaseOrders/PurchaseRequests` + تحويل استلامهم إلى فاتورة يمر عبر `CreatePurchaseAsync` (FIFO+GL) + هجرة `AddProcurementFlow` | **45 اختبارًا** |
| M6 | REST API (JWT HS256 + `api/auth/token`) + 10 أفعال `/api` + OpenAPI/Swagger + `ApiAuthorize` على أذونات المشروع؛ إعادة هيكلة المدفوعات إلى `IPaymentService` مشترك (واجهة MVC+API) | **45 اختبارًا**؛ تحقق حي: 401/400/200، فاتورة بيع + استلام + شراء عبر API |

### P2 — مكتمل ومتحقَّق (2026-09-03)

| المرحلة | ما أُنفذ | التحقق |
|---|---|---|
| M7 | تقارير PDF (QuestPDF)+ XLSX (ClosedXML) للقوائم المالية + مخزون/أصناف/مبيعات/مشتريات/مدفوعات + `Reports/Dashboard` (استحقاق + رصيد منخفض) + باركود Code128 SVG/`PrintLabel` + `Item.MinStock/Barcode` + هجرة `AddItemMinStockAndBarcode` | **54 اختبارًا**؛ PDF 65KB على نوع `application/pdf` + XLSX صحيحان؛ لوحة 200 |
| M8a | عملات `Currency` (SDG أساسي + USD) بعمولات/أسعار صرف + أفرع `Branch` (BR-001/002) تُطبع `BranchId` على GL والفواتير والمدفوعات + شحنات `Shipment`/التسليمات + هجرة `AddMultiCurrencyBranchesShipments` | **63 اختبارًا** |
| M8b | Audit نهائي شامل عبر 35 صفحة عبر كل الوحدات (أصناف/مبيعات/مشتريات/مرتجعات/مدفوعات/مستودعات/نقل/مشتريات RFQ/شحنات/إعدادات/تقارير مالية وتشغيلية) + 3 قوالب تصدير | Smoke: **35/35 صفحات 200**؛ `CONSOLE_ERRORS: []`؛ build 0W/0E؛ `dotnet test` **63/63** |

**النتيجة النهائية**: اكتمل النطاق P0→P2 بالكامل عبر سلسلة Agents مع بوابة تحقق بعد كل مرحلة. **التحقق النهائي**: build 0W/0E؛ **63/63 اختبارًا**؛ Audit شاملة 35/35 صفحة بلا أخطاء توافقية؛ هجرات `AddGeneralLedger` → `AddStockFifoLayers` → `AddWarehousesAndStockTransfer` → `AddProcurementFlow` → `AddItemMinStockAndBarcode` → `AddMultiCurrencyBranchesShipments` طبِّقت جميعها على `NewVixSmartDb`.

**ملاحظات إرشادية**: العملة الأجنبية تُحفظ كلف بيانات وقتية إعلامية (مبلغ/سعر صرف مسجل لحظة الترحيل)، والدفتر GL يبقى بالعملة الأساسية كمصدر حقيقة وحيد. لاستخدام أمامي مُطبَّق: `admin/Admin@123`, `accountant/Accountant@123`, `warehouse/Warehouse@123`، والتطبيق على `http://localhost:5165`.

### P3 — مكتمل ومتحقَّق (2026-09-06)

| المرحلة | ما أُنفذ | التحقق |
|---|---|---|
| M9 | **ترحيل فارق الصرف (FX gain/loss):** حسابات GL 4400 (خسائر)/8400 (أرباح) مضافة؛ `Payment.CurrencyId/ExchangeRate/BaseAmount` + `PaymentAllocation` (تخصيص لكل فاتورة: `AllocatedBaseAmount/ExchangeRateAtSettlement/FxGain/FxLoss`)؛ `RecordFxSettlementAsync` تُصدر قيدًا متوازنًا (نقد/مدينون + 4400/8400)؛ Allocation FIFO مع سقف `outstanding×r1/r0` يحافظ على حارس الدفع الزائد ويسمح بالفرق؛ MVC فارق الصرف (dropdown عملة + BaseAmount حي + أعمدة في القوائم) + API `createPaymentRequest.currencyId`؛ بذرة 4400/8400 المعرفة؛ هجرة `AddFxSettlement` | build 0W/0E؛ **70 اختبارًا** (63 + 7)؛ صفحات الدفعات (index/create/لوحة) 200 و`CONSOLE_ERRORS: []`؛ هجرة مطبَّقة |
| M10 | **سجل تدقيق مركزي (Audit Ledger):** `Reports/AuditLedger` (صفحة + تصفية by/to/account/source) + `AuditLedgerXlsx` (تصدير XLSX مطابق للصفحة) + `GET /api/journal-entries` (API مُpaged بنفس التصفية مع `ApiAuthorize("AuditLedger.View")`)؛ `PermissionCatalog` يُضيف `AuditLedger: [View, Export]`؛ `PermissionDefaults` تُعطي Accountant صلاحيتي العرض والتصدير؛ بنية `AuditLedgerViewModel`/`AuditLedgerEntryViewModel`/`AuditLedgerLineViewModel` | build 0W/0E؛ **74 اختبارًا** (70 + 4)؛ صفحة AuditLedger + API journal-entries 200 و`CONSOLE_ERRORS: []` |
| M11 | **عمليات جماعية (Batch):** `BatchService` مع `RunSalesBatchAsync` و `RunAdjustmentBatchAsync` — كل مستند معاملة.atomicية (Sale→FIFO+GL+Stock)؛ Best-effort: مستند يفشل لا يُبطل الآخرين؛ لا تكرار GL (كل مستند قيد واحد بـ `NextEntryNumber`)؛ `BatchController` مع Index/Sales/Adjustment/Results + `RequirePerm("Batch.SalesCreate"/"Batch.AdjustmentCreate")`؛ `PermissionCatalog` Batch + `PermissionDefaults` (Accountant→الاثنين، Warehouse→Adjustment فقط) | build 0W/0E؛ **79 اختبارًا** (74 + 5)؛ صفحات Batch/Sales/Adjustment 200 و`CONSOLE_ERRORS: []` |
| M12 | **إقفال سنوي/ترحيل فترات (Fiscal Close):** `FiscalService` مع `CloseYearAsync` (يُحصّل أرباح/خسائر P&L إلى 3001 المحتجزة عبر قيود YearEndClose) و `ReopenYearAsync` (يحذف قيود الإقفال ويعيد الأرصدة)؛ حارس `AccountingService.PostAsync` في السطر 105 يمنع الترحيل في سنة مغلقة؛ قاعدة «أحدث سنة فقط تُغلق» (سطر 44-46)؛ حارس `IsPeriodClosedAsync` في `PaymentService` و `InventoryService`؛ `FiscalController` مع Index/Create/Close/Reopen + `RequirePerm("FiscalClose.Close"/"FiscalClose.Reopen")`؛ `AddFiscalPeriods` migration + حساب 3001 seed | build 0W/0E؛ **96 اختبارًا** (79 + 17)؛ صفحة Fiscal 200 و`CONSOLE_ERRORS: []`؛ اختبارات: صفر P&L بعد الإقفال، حماية السنة المغلقة، Reopen يعيد التوازن، القاعدة lamus kanun |
| M13 | **مراجعة نهائية P3 + Audit شامل + إكمال التوثيق** | انظر أدناه |

#### ملخص M13 — المراجعة النهائية

**الأ области (A) — مراجعة كود P3:**

| المنطقة | الحكم | التفاصيل |
|---|---|---|
| **M9 FX** | ✅ PASS | `RecordFxSettlementAsync` (AccountingService.cs:39-58) تُصدر قيدًا متوازنًا؛ حساب 4400 (خسائر) يُستخدم عند `fxLoss > 0.01m`، 8400 (أرباح) عند `fxGain > 0.01m` — اختيار صحيح بالعلامة؛ `PaymentService.ApplyInvoiceAllocationAsync` (سطر 130-256) يُo限制 بـ `outstanding×r1/r0` لا يسمح بالدفع الزائد؛ مُعاملة واحدة (BeginTransactionAsync)؛ لا تكرار FX — `RecordFxSettlementAsync` تُستدعى مرة واحدة داخل المعاملة (سطر 83-84)، والـ `HasDuplicatePaymentAsync` (سطر 49) تمنع الدفعات المكررة خلال دقيقتين |
| **M10 Audit** | ✅ PASS | `ReportsController.AuditLedger` (سطر 232-321) يُصفّي by/from/to/accountId/source — نفس التصفية في `ExportAuditLedgerXlsxAsync` (ReportService.cs:412-469)؛ API `JournalEntriesController.GetJournalEntries` (سطر 22-95) يستخدم `ApiAuthorize("AuditLedger.View")` + نفس التصفية |
| **M11 Batch** | ✅ PASS | `BatchService.RunSalesBatchAsync` (سطر 19-63) — كل مستند عبر `_inventory.CreateSaleAsync` في معاملة منفصلة (.atomic)؛ best-effort ناجح (اختبار `OneInvoiceFailsOnStock_OthersStillSucceed`)؛ لا تكرار GL — كل `CreateSaleAsync` يولّد EntryNumber فريدًا |
| **M12 Fiscal** | ✅ PASS | `FiscalService.CloseYearAsync` (سطر 35-99) يحصّل P&L إلى 3001؛ حارس PostAsync (AccountingService.cs:105-106) هو السلطة الوحيدة؛ القاعدة «أحدث سنة فقط» (سطر 44-46) مُ.getMethod؛ `ReopenYearAsync` (سطر 101-153) يحذف القيود بالكامل ويعيد `IsClosed=false` — اختبار `ReopenYear_RemovesCloseEntries_AndRestoresPlBalances` يثبت التوازن |
| **أذونات** | ✅ PASS | `PermissionCatalog` يُسجّل `Batch: [SalesCreate, AdjustmentCreate]` (سطر 41)، `FiscalClose: [Close, Reopen]` (سطر 42)، `AuditLedger: [View, Export]` (سطر 39)؛ `PermissionDefaults` تُعطي Accountant: Batch.الاثنين + FiscalClose.Close + AuditLedger.الاثنين (سطر 24-25، 22)؛ كل controller يستخدم `RequirePerm` |
| **أسرار** | ✅ PASS | `appsettings.json` Jwt:Key = `REPLACE_WITH_LONG_SECRET_IN_PRODUCTION` (سطر 13) — placeholder واضح؛ `appsettings.Development.json` Jwt:Key = `DevOnly-SuperSecretKey-DoNotUseInProduction-12345678` (سطر 9) —明确 للتطوير فقط؛ لا أسرار حقيقية مكتوبة |
| **TODOات عربية** | ✅ PASS | لا توجد TODOs معلّقة في كود P3 |

**ال Bereich B — التدقيق على النظام الحي:**

| البند | النتيجة |
|---|---|
| **Trial Balance متوازن** | ✅ Dr = 175.00، Cr = 175.00 — الميزان مُتزامن بالجنيه |
| **قيود غير متوازنة** | ✅ 0 قيود — كل قيد متوازن (Dr == Cr) |
| **P&L 2026** | ✅ Revenue = 100.00، Expense = 0 (لا مصاريف مُرحّلة)؛ صافي الدخل = 100.00 — متوافق مع الحسابات |
| **صفحات مُفحوصة** | ✅ **54 صفحة** (52 + Login + API) — جميعها 200 ما عدّا `/ItemTypes/Create` و `/Categories/Create` اللذان لا يملكان GET action (إنشاء عبر AJAX modal — by design) |
| **CONSOLE_ERRORS** | ✅ **[]** — صفر أخطاء console في كل الصفحات |
| **API Regression** | ✅ POST `/api/auth/token` → 200 + JWT صحيح؛ `GET /api/items` → 200؛ `GET /api/journal-entries` → 200 |

**الإجمالي النهائي:**

| البند | النتيجة |
|---|---|
| بناء (build) | 0W/0E |
| اختبارات | **96/96 PASS** |
| صفحات مُفحوصة | **54** (52 GET + Login POST + 3 API) |
| صفحات 200 | **52/52 GET** (Create صفحات AJAX by design — ليست أخطاء) |
| CONSOLE_ERRORS | **[]** (صفر) |
| Dr == Cr | **175.00 == 175.00** |
| قيد غير متوازن | **0** |
| API Regression | **3/3 PASS** |
| هجرات مطبَّقة | `AddGeneralLedger` → `AddStockFifoLayers` → `AddWarehousesAndStockTransfer` → `AddProcurementFlow` → `AddItemMinStockAndBarcode` → `AddMultiCurrencyBranchesShipments` → `AddFxSettlement` → `AddFiscalPeriods` |
| الملفات الرئيسية P3 | `PaymentService.cs`, `AccountingService.cs` (RecordFxSettlementAsync + PostAsync guard), `BatchService.cs`, `FiscalService.cs`, `ReportsController.cs` (AuditLedger + AuditLedgerXlsx), `JournalEntriesController.cs`, `PermissionCatalog.cs`, `PermissionDefaults.cs` |

**الحالة النهائية: P3 مكتمل — P0 + P1 + P2 + P3 جميعها مُنجزة ومُتحقَّقة. الخطة 100% مُنجزة.**

---

### P4e — تمتين الأداة والاعتماديات (Toolchain & Dependencies Hardening) — مكتمل ومُتحقَّق (2026-09-06)

**النطاق**: ترقية/تمتين الأداة والاعتماديات، إزالة CVEs والتحذيرات بشكل دائم؛ **بدون إضافة ميزات وبلا تغيير في المنطق التجاري**.

**أساس النظام (Baseline):**
- SDK المُستخدم: **.NET SDK 10.0.400** (المثبَّتة: 10.0.303 و 10.0.400 — أعلاهما هو النشط حاليًا). الـ Runtime 10.0.11.
- الهدف `net10.0` = أحدث **LTS مستقر** (اليوم 2026-09: لا يُستهدف .NET 11 preview بشكل متعمّد).
- حزما `Microsoft.AspNetCore.*` / `EntityFrameworkCore` عند `10.0.11` = نفس نسخة الـ shared framework في الـ SDK — حالية تمامًا.

**فحص الثغرات (CVE):**
- `dotnet list package --vulnerable --include-transitive` على كلا المشروعين: **لا توجد حزم ضعيفة** — صفر CVEs.
- **أُزيل الربط (pin) القديم `System.IO.Packaging 6.0.2`** (الذي كان يُسكّت NU1903) من ملفات المشروع — غير موجود في أي `.csproj` الآن. سلسلة ClosedXML/OpenXML تسحبه ترانزيتيًا عند **8.0.1** (نسخة مصحَّحة غير ضعيفة) — **NU1903 اختفى فعلًا** (build نظيف 0 تحذير).

**الترقيات المُنفَّذة (before → after):**

| الحزمة | قبل | بعد | ملاحظة |
|---|---|---|---|
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.11 | 10.0.11 | حالية (= shared framework) |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.11 | 10.0.11 | حالية |
| Microsoft.AspNetCore.Identity.UI | 10.0.11 | 10.0.11 | حالية |
| Microsoft.EntityFrameworkCore.Design | 10.0.11 | 10.0.11 | حالية |
| Microsoft.EntityFrameworkCore.SqlServer | 10.0.11 | 10.0.11 | حالية |
| Microsoft.EntityFrameworkCore.Sqlite (tests) | 10.0.11 | 10.0.11 | حالية |
| Microsoft.NET.Test.Sdk | 18.9.0 | 18.9.0 | حالية |
| xunit / xunit.runner.visualstudio | 2.9.3 / 4.0.0 | 2.9.3 / 4.0.0 | حالية |
| ClosedXML / QuestPDF | 0.105.1 / 2026.8.0 | 0.105.1 / 2026.8.0 | حالية |
| Swashbuckle.AspNetCore | 9.0.6 | **9.0.6 (مُبقاة عمدًا)** | انظر القرار أدناه |

> **قرار Swashbuckle (مُوثَّق):** النسخة الأحدث المتاحة `10.2.3` هي قفزة major مع **تغييرات كاسرة** (نقل namespace `Microsoft.OpenApi.Models` → `Microsoft.OpenApi`، وتغيير `AddSecurityRequirement` إلى تفويض `Func<OpenApiDocument, …>` — تتطلب تعديل `Program.cs`). وكون 9.0.6 **بلا CVEs وبلا تحذيرات**، وتوليد الوثائق Swagger تطوري في بيئة التطوير فقط، **أُبقيَ على 9.0.6** (آخر patch لخط 9.x) لتجنّب تغيير غير ضروري بلا فائدة تمتينية — لا Downgrade بل قرار إبقاء.

**التحذيرات (Warnings):**
- قبل: **0** / بعد: **0** (كلا المشروعين 0W/0E).
- **حُوِّل `TreatWarningsAsErrors=true` في كلا المشروعين** لإقفال التحذيرات **بشكل دائم**: أي تحذير مستقبلي يعَطِّل البناء. (تحقق: 0W/0E نظيف عند التفعيل).

**البناء والاختبار (تحقُّق إلزامي):**
- Build Web (Release): **0W/0E** ✅
- Build Tests (Release): **0W/0E** ✅
- `dotnet test` (Release): **96/96 PASS** — لا فشل جديد ✅
- Migrations: `dotnet ef migrations has-pending-model-changes` → **«No changes have been made to the model since the last migration»** — لا فروقات نموذج (الاعتماديات لا تُنتج diffs) ✅
- إقلاع التطبيق: أُعيد تشغيله عبر `dotnet run --no-build` → يستمع على `http://localhost:5165` (logs: `%TEMP%\opencode\app32.log`/`.err.log`) → **Home `/` = 200** و **Login `/Account/Login` = 200** ✅

**تغيير قسري في الكود بسب API:** لا يوجد — لم تُلمس `SeedData`/الخدمات/التحكمات/الآراء. التغيير الوحيد في `.csproj` (خاصية `TreatWarningsAsErrors`). لا حاجة لاختبارات انحدار جديدة لأن لا منطق تغيّر.

**ملاحظة نظامية (system-level caveat):** عند بناء المشروعين **بالتوازي في نفس الأمر**، يظهر قفل ملف على `NewVixSmart.Web.dll` (اختبارات *تشير* إلى مشروع الويب) — يُحلّ بالبناء تسلسليًا أو بأمر واحد للـ solution. ليس خطأ كود.

**الحالة النهائية: P4e مكتمل — أداة/اعتماديات مُحتّمة، صفر CVEs، صفر تحذيرات، build 0W/0E، اختبارات 96/96.**

---

### P4a — مرتجعات المبيعات/المشتريات المتكاملة (التكلفة + العملة + الحماية السنوية) — مكتمل ومُتحقَّق (2026-09-06)

| المرحلة | ما أُنفذ | التحقق |
|---|---|---|
| M-P4a | **إعادة هندسة المرتجعات إلى سير عمل Draft→Posted مع تكامل محاسبي كامل:** `ReturnStatus` (`Models/Accounting/ReturnStatus.cs`: Draft=0 «مسودة»/Posted=1 «مرحَّلة`)؛ حقول جديدة في `SaleReturn`/`PurchaseReturn` (`CurrencyId`/`Currency` FK تقييدي، `ExchangeRate decimal(18,6)`، `BranchId` FK تقييدي، `Status`، `PostedBy`، `PostedAt`)؛ هجرة `AddSalesReturnsAndPurchaseReturns` المطبَّقة | build 0W/0E؛ هجرة مطبَّقة على `NewVixSmartDb` |
| M-P4a | **دورة الحياة في `InventoryService`:** `Create*ReturnDraftAsync` (تُنشئ المسودة بلا أي أثر مخزني/محاسبي) و`Post*ReturnAsync` (تُرحّل القيد والـ FIFO والحركة معًا في معاملة واحدة — سطر 228/331 يرفضان الترحيل في سنة مغلقة **قبل** أي أثر جانبي؛ المسودة مسموحة في السنة المغلقة؛ الترحيل المزدوج مرفوض «مرحَّل بالفعل»)؛ `RestoreSaleReturnLayersAsync` (سطر 556) تعيد طبقات FIFO **بسعر الشراء الأصلي `Item.PurchasePrice`** لا بسعر البيع (إصلاح خلل تكلفة حقيقي)؛ مرتجع الشراء يستهلك الطبقات بسعر التكلفة عبر `ConsumeFifoLayersAsync` ويُمرّر COGS للقيد | `ReturnsFixtureTests` — طبقة مُستعادة بتكلفة 30 (سعر شراء) رغم بيعها بـ 80؛ رفض الترحيل في سنة مغلقة؛ منع الترحيل المزدوج؛ شبكة FIFO للمرتجع الشرائي |
| M-P4a | **تكامل GL في `AccountingService`:** حسابات العقد الجديدة **5101 «مرتجعات المبيعات» / 5102 «مرتجعات المشتريات»** (بذرة SeedData اصطلاحية قبل سطر الحراسة)؛ 4 أسطر متوازنة لمرتجع البيع (Dr 5101 V / Cr 1200 V / Dr 1300 C / Cr 5000 C) ومرتجع الشراء (Dr 2000 V / Cr 5102 V / Cr 1300 C / Dr 5000 C)؛ **تحويل العملة** `local = value × (exchangeRate ←← 1m)` مدوَّر 2 (سطر 71/86) بينما رجل التكلفة دائمًا محلي؛ طريقتا 2-سقر المتوافقتان مع الإصدار السابق (`RecordSaleReturnAsync`=5010/1200 و`RecordPurchaseReturnAsync`=2000/5102) | اختبار FX: 100×0.9→90 على 5101/1200 مع تكلفة 40 ثابتة محليًا |
| M-P4a | **الواجهة والأذونات:** `SaleReturnsController`/`PurchaseReturnsController` — إنشاء مسودة على POST (زرا «حفظ كمسودة»/«ترحيل») + إجراء `Post(int id)` جديد بـ `[RequirePerm("SaleReturns.Post"|"PurchaseReturns.Post")]`؛ آراء Index/Details (شارة حالة «مسودة»/«مرحَّلة» + زر ترحيل للمسودات) وCreate (زرا الحفظ/الترحيل)؛ `PermissionCatalog` يضيف `Post` لوحدتى المرتجعات و`PermissionDefaults` يمنحها لـ Accountant و Warehouse | صفحات SaleReturns/PurchaseReturns (Index/Details/Create) 200 و`CONSOLE_ERRORS: []`؛ **تحقّق حي E2E** (مسودة عبر Form → شارة «مسودة» + زر ترحيل سطرًا بسطر → ترحيل → شارة «مرحّل» وتلاشي الزر): قيد GL متوازن Dr(5101=100 + 1300=85) == Cr(1200=100 + 5000=85) — رجل التكلفة استخدم سعر الشراء 85 لا سعر البيع 100 |

**الاختبارات:** 96 قاعدة + 10 جديدة (`ReturnsFixtureTests` — مسودة بلا أثر؛ استعادة الطبقات بسعر الشراء؛ حارس السنة المغلقة للمسودة والترحيل والـ `Create*ReturnAsync` المباشر؛ منع الترحيل المزدوج؛ نقصان المخزون بتكلفة FIFO؛ 4 أسطر متوازنة للبيع/الشراء؛ تحويل العملة) → **106/106 PASS**.

| البند | النتيجة |
|---|---|
| بناء (build) | 0W/0E (web + tests) |
| اختبارات | **106/106 PASS** (96 + 10) |
| هجرة | `AddSalesReturnsAndPurchaseReturns` مطبَّقة |
| تصحيح خلل | إعادة الطبقات بسعر الشراء الأصلي (التكلفة) لا بسعر البيع |
| الملفات الرئيسية | `InventoryService.cs` (دورة المرتجعات)، `AccountingService.cs` (5101/5102 + FX)، `Models/Accounting/ReturnStatus.cs`، `SaleReturn.cs`/`PurchaseReturn.cs`، `AppDbContext.cs`، `SeedData.cs`، `PermissionCatalog.cs`/`PermissionDefaults.cs`, `SaleReturnsController.cs`/`PurchaseReturnsController.cs` + آراءهما |

**الملاحظة الهامة (قرار معماري):** المسودة تُحفظ أولًا (لإتاحة الإدخال الجزئي) لذا يَستبعد تحقق الكميات التراكمية (سطر 615-617) مسودة الحالية (`r.SaleReturnId != saleReturn.Id`) لمنع العد الذاتي — عثر الاختبار التراكمي على ذلك (7+7>10 رُفض بالخطأ قبل الإصلاح).**

---

### P4b — إدارة مخطط الحسابات + الميزانيات السنوية (الخطط مقابل الفعلي) — مكتمل ومُتحقَّق (2026-09-06)

| المرحلة | ما أُنفذ | التحقق |
|---|---|---|
| **النموذج** | **`BudgetYear`** (Id, Year فريدة, IsActive, CreatedBy/At) + **`BudgetLine`** (Id, BudgetYearId, AccountId, AnnualAmount) مع قيد فريد `(BudgetYearId, AccountId)` (السنوي فقط — لا توزيع شهري؛ الميزانية VS الفعلي على مستوى السنة)؛ هجرة **`AddBudgets`** المُطبَّقة على `NewVixSmartDb` | build 0W/0E؛ هجرة مطبَّقة؛ `has-pending-model-changes` = لا تغييرات |
| **حسابات CRUD** | **`AccountsController`** + آراء Index (قائمة مع تصفية النوع/الحالة + رصيد تشغيلي لكل حساب عبر استعلام خام مجمّع) / Create / Edit؛ الحساب الأب `ParentAccountId` (النموذج موجود أصلاً — لا جدول جديد) ؛ صلاحية `ChartOfAccounts: [View, Create, Edit, Deactivate]` | صفحات `/Accounts`, `/Accounts/Create` 200 + `CONSOLE_ERRORS: []` |
| **الحُرّاس** | خدمة **`AccountsService`** تُطبّق القواعد: (1) رمز الحساب الذي **له قيود مرحلة** أو **نظامي بذرة** لا يُغيَّر → «لا يمكن تغيير رمز حساب له قيود مرحلة» / «لا يمكن تغيير رمز حساب النظام»؛ (2) حذف مقيد للحساب المُرحِّل/النظامي → تعطيل بدل الحذف، والحذف الكامل لأصحاب الاستخدام الصفري فقط؛ (3) حساب نظامي (`1000..5100, 3001, 4400, 8400, 5101, 5102`) **لا يُعطَّل ولا يُحذف حتى لو غير مستخدم**؛ (4) رمز فريد حساسية-التجاهل | اختبارات إجبارية تغطي كل قاعدة |
| **الميزانيات** | **`BudgetsController`** + آراء Index (قائمة السنوات + تفعيل) / Manage (سنة) (جدول حسابات P&L فقط — «الرمز 4000-5999» الإيرادات والمصاريف) بحفظ الدفعة الكاملة (POST يحدّث/يُضيف خطوطًا بلا تكرار عبر القيد الفريد)؛ صلاحية `Budgets: [View, Manage]`؛ **الإقفال المالي**: سنة مغلقة تُعرض ولا تُعدَّل («السنة المالية N مغلقة — لا يمكن تعديل ميزانيتها») والميزانية لا تُعطّل الإقفال | صفحات `/Budgets`, `/Budgets/Manage←year=YYYY` 200 + `CONSOLE_ERRORS: []` |
| **تقرير الواريانس** | `Reports/BudgetVariance←year=YYYY` (منتقي سنة من سنوات الميزانية) — لكل حساب P&L ميزنته: Budget vs Actual = **`GetAccountYearlyActivityAsync`** (استعلام جديد للقراءة على `FinancialReportService` يعيد مجموع مدين/دائن مُرحَّل) ثم Actual وفق طبيعة الحساب؛ **اصطلاح الإشارة**: الواريانس = الفعلي − الميزانية (الموجب = إنجاز أعلى للمصروف، والموجب = إنفاق أعلى للمصروف)؛ صفوف الإجمالي للإيرادات والمصاريف منفصلة؛ **`BudgetVarianceXlsx`** تصدير ClosedXML مطابق لنمط `AuditLedgerXlsx`؛ قائمتا `IncomeStatement`/`BalanceSheet` **لم تُمَسّا** (الميزانية تقرير منفصل) | `/Reports/BudgetVariance` 200 + `CONSOLE_ERRORS: []`؛ تصدير XLSX صالح |
| **الأذونات/الشريط** | `PermissionCatalog`: وحدة `ChartOfAccounts` (View/Create/Edit/Deactivate) + `Budgets` (View/Manage)؛ `PermissionDefaults`: Accountant → `ChartOfAccounts.View` + `Budgets.View/Manage`؛ الشريط الجانبي أضاف «مخطط الحسابات» و«الميزانيات» تحت **الإدارة** و«واريانس الميزانية» تحت **القوائم المالية** | التحقق عبر Smoke |
| **الاختبارات** | `BudgetAndAccountsTests` — (أ) إنشاء/تعديل صفر-استخدام؛ تعديل حساب مُرحَّل رمزه مرفوض (خطأ عربي)؛ حذف مُرحَّل مرفوض/صفري مسموح؛ نظامي لا يُعطَّل/يُحذف؛ (ب) رمز مكرر حساسية-التجاهل؛ (ج) حفظ الميزانية السنوية + تكرار فريد يفرض إعادة كتابة + سنة مغلقة لا تعديل + P&L فقط (الأصل يُتجاهَل)؛ (د) واريانس إيراد/مصروف بحساب Exact (الفعلي−الميزانية) | **117/117 PASS** (106 + 11) |

**الحالة النهائية: P4b مكتمل — build 0W/0E (web + tests)، 117 اختبارًا أخضر، هجرة `AddBudgets` مطبَّقة، Smoke شامل بلا أخطاء console، القوائم المالية القياسية لم تتغير، لا تغيير في أي محرك ترحيل/مرتجعات/صرف/إقفال.**

| البند | النتيجة |
|---|---|
| بناء (build) | 0W/0E (web + tests، Release) |
| اختبارات | **117/117 PASS** (106 + 11) |
| هجرة | `AddBudgets` مطبَّقة + `has-pending-model-changes` نظيفة |
| الملفات الرئيسية | `Models/Accounting/BudgetYear.cs`, `BudgetLine.cs`, `AccountsController.cs`, `BudgetsController.cs`, `AccountsService.cs`, `FinancialReportService.cs` (GetAccountYearlyActivityAsync), `ReportService.cs` (ExportBudgetVarianceXlsx), `ReportsController.cs` (BudgetVariance/Xlsx), `PermissionCatalog.cs`, `PermissionDefaults.cs`, `_Layout.cshtml`, `SeedData.cs` (بذرة سنة الميزانية الحالية) |

---

### P4c — جولة الإتاحة والجودة الشاملة (WCAG 2.2 AA) — مكتمل ومُتحقَّق (2026-09-10)

**النطاق**: تدقيق وإصلاح (Audit-and-Fix) — تباين، نماذج، جداول، لوحة مفاتيح، ARIA، RTL، طباعة، إتاحة المخططات/الأكوان. **بلا تغيير في أي منطق تجاري/محاسبي** — آراء وCSS وJS فقط.

**المنهجية والأدوات:**
- فحص آلي شبه كامل بـ **axe-core (`@axe-core/playwright`)** على **41 صفحة** بأصناف `wcag2a/wcag2aa/wcag21aa/wcag22aa` (تسجيل دخول admin/Admin@123، متصفح Edge headless) — 41/41 = **0 انتهاكات** (بما فيها **0 Critical و 0 Serious**).
- فحص لوحة مفاتيح حي (Playwright): تسجيل الدخول كاملًا باللوحة، إضافة/حذف صفوف في `Batch/Sales` بأزرار Enter/Tab فقط (5→10→5 صفوف)، مؤشر تركيز مرئي، **بلا أي فخّ تركيز**.
- تحقق `lang="ar" dir="rtl"`، skip-link، `role="alert"`/`role="status"` على رسائل TempData، `:focus-visible` المخصص، بُعد الاستهداف ≥44px.

**الانتهاكات المُكتشَفة والمُصلَحة (2 موضعان فقط — كلاهما `color-contrast` serious):**

| الموضع | الصفحة | الإصلاح |
|---|---|---|
| `.btn-outline-success` (نص أخضر Bootstrap `#198754` على أبيض = ~3.0:1 < 4.5:1) | `/Items` (تصدير Excel)، `/Reports/*` | site.css: نص `--color-success-text` (#166534 ≥ 4.5:1) + hover بخلفية داكنة متباينة مع نص أبيض |
| `<code>` داخل صف حسب `.table-danger` (لون رمز يحمل تراث Bootstrap `--bs-code-color` الوردي على خلفية حمراء شاحبة) | `/Reports/Dashboard` (جدول PO المتأخرة) | site.css: `code` في صفوف `.table-danger/.table-warning/.table-info/.table-secondary` يرث لون الصف (`color: inherit`) |

**ما كانت سليمة أساسًا (من عمُسحات سابقة):** كل النماذج بعناوين `<label for>` مقترنة و`aria-invalid`/`aria-describedby` عند الأخطاء، جداول البيانات بـ`<th scope>` وعناوين `visually-hidden`، الأزرار الأيقونية بـ`aria-label`/`.visually-hidden`، باركود Code128 في `_Barcode.cshtml` بـ`role="img"`+`aria-label`، صفحات التقارير جدولية (لا canvas Chart.js)، نظام skip-link وحلقات التركيز المخصصة، وسائط الطباعة والـ reflow موجودة مسبقًا.

**التحقّق النهائي:**

| البند | النتيجة |
|---|---|
| صفحات مفحوصة (axe) | **41/41 = 0 انتهاكات** (0 Critical، 0 Serious) |
| GATE (p4cA11ySmoke.cjs) | **PASS** — 38 صفحة 200 + `CONSOLE_ERRORS: []` + صفر Critical/Serious |
| لوحة المفاتيح | ✅ تسجيل دخول وتشغيل Batch بالكامل باللوحة، بلا فخّ |
| `lang/dir` | ✅ `ar/rtl` |
| build | 0W/0E (لم يتغير كود C#) |
| اختبارات | **117/117 PASS** (لم تتغير — تغييرات UI فقط) |
| الملفات المتغيّرة | `wwwroot/css/site.css` فقط (إصلاحات تباين) |
| Backlog Moderate/Minor | لا شيء مُكتشف — المورد الأساسي نظيف (معتمد من مسح axe + لوحة المفاتيح) |

**البوابة الدائمة (standing gate):** `%TEMP%\opencode\pw\p4cA11ySmoke.cjs` — فشل عند أي انتهاك Critical/Serious أو خطأ console أو صفحة بلا 200. `ACCESSIBILITY.md` أُنشئ في جذر المستودع: هدف WCAG 2.2 AA، أمر البوابة، قاعدة «لا انحدار إتاحة».

**الحالة النهائية: P4c مكتمل — WCAG 2.2 AA متوافق في المسح الآلي + لوحة المفاتيح، صفر انتهاكات على 41 صفحة، بلا تغيير في المنطق، 117/117 اختبار أخضر.**

---

### P4d — Git + CI/CD + Docker — مكتمل ومُتحقَّق (2026-09-10)

**النطاق**: ربط المستودع بنظام إصدارات محترف (git)، خط CI تلقائي (build/test/تحصين/صورة)، وتثبيت/تحصين مسار الحاويات Docker.

**Git — التهيئة الأولى:**
- `git init` على جذر المستودع (لم يكن مستودعًا سابقًا) مع إعداد `user.name`/`user.email` محليًا للمستودع.
- **`.gitignore`** شامل (.NET + أدوات): `bin/ obj/ *.user TestResults/ .vs/`, نواتج محلية `test-out*.txt tests-result.txt *.log`, أدوات `node_modules/ .playwright-cli/`, وأسرار محلية `appsettings.Local.json *.pfx *.p12 .env .env.*` (مع استثناء `!.env.example`).
- **تدقيق سلامة قبل الـ commit الأول**: لا ملفات `.pfx`/`.p12`/`Secrets.json`/`.env` موجودة، `Jwt:Key` قيم placeholders موثقة، و`SA_PASSWORD` لم يعد مضمّنًا في compose (مطلوب من بيئة التشغيل).

**CI/CD — `.github/workflows/ci.yml`** (يركض على `ubuntu-latest`، .NET 10):

| الوظيفة | الخطوات |
|---|---|
| `build-and-test` | `actions/checkout@v4` + `actions/setup-dotnet@v4` (10.0.x) → `dotnet restore NewVixSmart.slnx` → `dotnet build -c Release --no-restore` (0W/0E إجباري عبر `TreatWarningsAsErrors`) → `dotnet test` (117/117) → رفع تقرير TRX |
| `package-vulnerability` | `dotnet list new-vix-smart.slnx package --vulnerable --include-transitive` — يفشل الـ job عند أي CVE |
| `docker-image` | `docker build` لصورة الويب من `src/NewVixSmart.Web/Dockerfile` + `docker inspect` للـ exposed-ports والـ HEALTHCHECK (مُحاكي بوابة الصورة — لا push) |

- **`NewVixSmart.slnx` حُدِّث**: أُضيف مشروع الاختبارات `tests/NewVixSmart.Web.Tests` إلى الصيغة (المسار الجديد `slnx` أصلاً بكناية src فقط) — الآن `dotnet test` و`dotnet build` من الـ slnx يشملان كلا المشروعين **عبر الأنظمة** (تأكيد محلي: build 0W/0E + **117/117 PASS** من الـ slnx مباشرة، الاختبارات Sqlite فلا حاجة لـ LocalDB في CI).

**Docker — التحصين وإعادة التحقق:**
- `src/NewVixSmart.Web/Dockerfile` **مُراجعة مسبقًا سليمة**: مرحلتان (sdk:10.0 → aspnet:10.0)، restore في طبقة مستقلة، `ASPNETCORE_URLS=http://+:80`، HEALTHCHECK عبر curl، المستوى الصغير الحجم — بقي بلا تغيير.
- **`.dockerignore`** جديد (يمنع `bin/obj/.git/.env/.github` من سياق البناء).
- **`docker-compose.yml` حُصِّن**: كلمة مرور SA المفترضة المضمّنة **أُزيلت** — الآن `SA_PASSWORD: "${SQL_SA_PASSWORD:←required}"` يرفض الإقلاع دون ضبطها، healthcheck يستخدم `$${SA_PASSWORD}` داخل الخادم، وسلسلة الاتصال تُبني من `${SQL_SA_PASSWORD}` عند compose؛ **`.env.example`** موثق (مفسوخ من `.git`).

**التحقّق النهائي:**

| البند | النتيجة |
|---|---|
| git | مستودع مُهيّأ + ملفا ignore + حماية أسرار (لا اعتماد مُدرج) |
| build من الـ slnx (Release) | **0W/0E** (web + tests) |
| اختبارات من الـ slnx | **117/117 PASS** |
| CI jobs | 3 وظائف (build/test, vuln scan, docker build) — ترجع فشلًا عند أي انحدار |
| Dockerfile | مُراجَع سليم بمرحلتين + HEALTHCHECK (لا تغيير) |
| compose | اعتمادات مُتغيِرة إجباريًا + `.env.example` |
| الملفات الجديدة | `.gitignore`, `.dockerignore`, `.env.example`, `.github/workflows/ci.yml`, تحديث `NewVixSmart.slnx` |

**الحالة النهائية: P4d مكتمل — Git + CI (build/test/vuln/docker) + Docker محصَّن؛ كل المراحل الخمس (P4e→P4a→P4b→P4c→P4d) منجزة → P4 مكتمل بالكامل.**

---

### P5a — ضغط الأمان: قفل JWT + CORS + تسليك CSP — مكتمل ومُتحقَّق (2026-09-10)

**النطاق**: تحصين طبقة الأمان المعرفي (config/startup) دون تغيير أي منطق تجاري — لا ميزات، لا تغيير في الصفحات.

| البند | الحالة قبل | ما أُنفذ | التحقق الحي |
|---|---|---|---|
| **JWT Key** | placeholder `REPLACE_WITH_LONG_SECRET_IN_PRODUCTION` في `appsettings.json` يُقبل في أي بيئة | حارس إقلاع جديد في `Program.cs` (سطر 33-39): في بيئة غير `Development`، إن كان المفتاح `<32` حرفًا أو يحوي `REPLACE_WITH` → `InvalidOperationException` «refusing to start in production with a placeholder or weak key» مع توجيه «اضبطه عبر Jwt__Key أو User Secrets» | ✅ Prod+placeholder: إقلاع مرفوض رسالة واضحة وخروج (port حر)؛ Prod+`Jwt__Key` قوي: يقلع ويُصدِر `200` |
| **CORS** | لا توجد أي سياسة (كل الطلبات بدون قيود عبرية منفذة) | سياسة اسمية `ApiCors` (أسطر 67-81): مغلقة افتراضيًا (`WithOrigins()` فارغة)، وإن ضُبط `Cors:AllowedOrigins` (فاصلة) تُتيح الأصول المذكورة فقط مع `GET/POST/PUT/DELETE/PATCH/OPTIONS` + أي headers + **بلا اعتماديات** (`DisallowCredentials`)؛ `app.UseCors("ApiCors")` بعد `UseRouting` | ✅ افتراضيًا: preflight من `evil.example.com` → 204 **بلا أي Access-Control-Allow-* ** (المتصفح يحظر)؛ مع تكوين origin: preflight من `app.example.com` → `Access-Control-Allow-Origin: https://app.example.com` + Allow-Methods + Allow-Headers (Authorization)، ومن evil → بلا رؤوس |
| **CSP + رؤوس** | قوية مسبقًا (nonce CSP، `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`) | لم تُلمس — أبقت كما هي (داخل middleware أسطر 132-144) | ✅ استمرار الأرحام بعد التغييرات |
| **AllowedHosts** | `"*"` | لم تُغيّر (توثيق ملاحظة: للـ Prod اضبطها) | — |

**الوثائق/الاستخدام (للـ Prod):**
```bash
# تشغيل إنتاج محصَّن
Jwt__Key="<64+ char random>" Cors__AllowedOrigins="https://app.example.com" ASPNETCORE_ENVIRONMENT=Production dotnet run --no-launch-profile
```

**البيع (gateway):** build slnx (Debug+Release) **0W/0E**؛ `dotnet test` من الـ slnx **117/117 PASS**؛ التطبيق Dev يعمل على `http://localhost:5165` (`app41.log`). الملف المتغيّر: `src/NewVixSmart.Web/Program.cs` فقط (حارس JWT + سياسة CORS + `UseCors`).

**الحالة النهائية: P5a مكتمل — JWT رافض للـ placeholder في Prod، CORS مغلقة افتراضيًا بقائمة أصول، CSP/Rؤوس أمنية سليمة؛ بلا تغيير منطقي وبلا انحدار (117/117).**

---

### P5b — README جذر احترافي للمستودع — مكتمل (2026-09-10)

`README.md` في جذر المستودع: نظرة عامة + بنية المشروع (slnx+src/tests/aspire/docs)، أبرز الميزات، تشغيل محلي خطوة-بخطوة (restore/build/test/run) مع عنوان `:5165` وcreds التجريبية الثلاث، قسم إنتاج (Docker عبر `.env`+`SQL_SA_PASSWORD` + CORS/JWT الحوافز)، بوابة CI والتضمين، فهرس التوثيق، وجدول الأذونات. بلا تغيير كود.

### P5c — القائمة العمرية (Aging A/R + A/P) وتنبيهات الاستحقاق — مكتمل ومُتحقَّق (2026-09-10)

**الميزة:** تحليل ذمم العملاء والموردين حسب عمر الاستحقاق + تنبيهات فورية في لوحة التحكم.

- **البيانات:** استُغلت البنية الجاهزة — `SaleInvoice`/`PurchaseInvoice` بكل من `PaymentTerms` (OnReceipt/Net7/Net15/Net30/Net60) و`DueDate` (nullable) و`NetAmount`/`PaidAmount` دون أي مهاجرة. مرجع الاستحقاق = `DueDate ←← InvoiceDate` (يشمل فواتير OnReceipt التي كانت مستثناة سابقًا في `ReportService.GetDashboardAsync`).
- **الجديد:**
  - `ViewModels/Reports/AgingReportViewModel.cs`: `AgingBucketRow` (لم يستحق / 1-30 / 31-60 / 61-90 / +90 / الإجمالي) + مجاميع A/R وA/P.
  - `ReportService.AgingAsync()`: تجميع الفواتير المفتوحة (مستحق > 0.005) لكل طرف، حذف المُسَدَّدة بالكامل؛ و`ExportAgingXlsxAsync()` بورقة لكل من ذمم العملاء والموردين.
  - `ReportsController`: `Aging` (عرض) + `AgingXlsx` (تصدير).
  - `Views/Reports/Aging.cshtml`: 4 بطاقات ملخص + جدولا A/R وA/P (scope/caption + aria-labelledby) + زر تصدير مُقيَّد بالصلاحية.
  - `PermissionCatalog`: وحدة `Aging` (View/Export) + إدخال قائمة جانبية «القائمة العمرية»؛ `PermissionDefaults`: إضافة إلى مدير الحسابات.
  - لوحة التحكم (`DashboardService` + `DashboardViewModel` + `Views/Home/Index.cshtml`): بطاقة «فواتير متأخرة أو تستحق خلال 7 أيام» لذمم العملاء والموردين (عدد + إجمالي) مع رابط للقائمة العمرية.
- **التحقق:** `dotnet build NewVixSmart.slnx` = 0W/0E؛ اختبارات **122/122 PASS** (117 سابقة + 5 جديدة في `AgingTests`: التقسيم العمري حسب تاريخ الاستحقاق، فواتير OnReceipt، استبعاد المسدد بالكامل، التجميع الفارغ، صلاحبة ملف XLSX)؛ بوابة Playwright+axe `GATE: PASS` على `/Reports/Aging` و`/` و`/Reports` (200 + صفر انتهاكات Critical/Serious + صفر أخطاء console).

### P5d — كشف التدفق النقدي + كشوف حسابات قابلة للتصدير — مكتمل ومُتحقَّق (2026-09-10)

**الميزة:** تقرير تدفق نقدي فعلي مبني على حركات الدفعات + تصدير كشوف حساب العملاء/الموردين (Excel).

- **التدفق النقدي (`/Reports/CashFlow`):**
  - `CashFlowReportViewModel` + `ReportService.CashFlowAsync`: رصيد افتتاحي (مجموع الحركات قبل الفترة)، مقبوضات/مصروفات الفترة، صافي التدفق، رصيد ختامي، وتوزيع حسب طريقة الدفع (نقداً/شيك/تحويل/بطاقة). المبلغ المرجعي = `BaseAmount` وإلا `Amount` (يعمل بالعملات المتعددة).
  - `Views/Reports/CashFlow.cshtml`: منتقي فترة (form) + 4 بطاقات + جدول طرق الدفع + جدول تفاصيل الحركات (قبض/صرف، الطرف، الطريقة، المرجع).
  - `CashFlowXlsx`: ورقة بملخص + توزيع الطرق + التفاصيل؛ الصلاحيات بنمط BudgetVariance (`Reports.View` للعرض، `Reports.Export` للتصدير) دون وحدة جديدة؛ بطاقة في فهرس التقارير + رابط في الشريط الجانبي.
- **كشوف الحساب:** `CustomersController.LedgerXlsx` + `SuppliersController.LedgerXlsx` (بصلاحية عرض العملاء/الموردين) عبر `ExportCustomerStatementXlsxAsync`/`ExportSupplierStatementXlsxAsync` — رصيد افتتاحي + سطور (فاتورة مدين / مرتجع دائن / دفعة دائن) مع رصيد جارٍ وختامي. زر «تصدير كشف حساب» في عرضَي الدفترين.
- **التحقق:** build 0W/0E؛ اختبارات **128/128 PASS** (122 سابقة + 6 جديدة: التدفق النقدي افتتاحي/فترة/ختامي، صفري، XLSX صالح، كشف عميل ومورد بالرصيد الختامي، طرف مجهول → فارغ)؛ بوابة `GATE: PASS` على `/Reports/CashFlow` و`/Reports` و`/` و`/Customers` و`/Suppliers` مع صفر انتهاكات Critical/Serious وصفر أخطاء console؛ روابط التحميل الثلاثة (`CashFlowXlsx`, `LedgerXlsx` للعميل والمورد) ترجع 200 بنوع الملف الصحيح.

### P6 — التدقيق الختامي والترميز النهائي — مكتمل ومُتحقَّق (2026-09-10)

مرحلة فحص شاملة بعد اكتمال P5 دون تعديلات جديدة على السلوك:

- **إتاحة:** فحص axe شامل عبر **40 صفحة** (38 سابقة + `Reports/Aging` + `Reports/CashFlow`) = `GATE: PASS` (200 + صفر انتهاكات Critical/Serious + صفر أخطاء console).
- **التصدير:** تحقق حي من **19 نقطة تصدير** بصيغة صحيحة — 13 XLSX (أصناف/مخزون/ميزان مراجعة/دخل/عمومية/سجل تدقيق/واريانس/أجينغ/تدفق نقدي/مبيعات/مشتريات/مدفوعات/كشفا عملاء وموردين) + 3 PDF (ميزان مراجعة/دخل/عمومية) + طباعة فاتورة بيع = الكل 200.
- **الجودة:** `dotnet build NewVixSmart.slnx` = 0W/0E؛ `dotnet test` = **128/128 PASS**؛ `dotnet list package --vulnerable --include-transitive` على الويب والاختبارات = **صفر ثغرات** (0 CVEs).
- **التوثيق:** تحديث README من 117 → 128 اختبارًا وإضفاء صيغة الاكتمال على P5.
- **النتيجة:** P6 مكتمل → مراحل P0–P6 منجزة بالكامل مع 128 اختبارًا أخضر وبوابة إتاحة مفعّلة.

### M12 ← حجز المخزون ← إذن/أمر التسليم ← الفوترة بعد التسليم (2026-09-27)

مسار بيع جديد AtInvoice مبني على 8 مراحل و71 مهمة (plan/feature-sales-reservation-delivery-invoice-flow-1.md):

- **الوحدات الجديدة:** StockReservations (View/Create/Release) وDeliveryIssues (View/Create/Issue) + modules في PermissionCatalog + روابط _Layout + أدوار Accountant وWarehouse.
- **المحاسبة:** التسليم في المسار الجديد يرحّل التكلفة فقط (Dr 5000 / Cr 1300); الفاتورة ترحّل Dr 1200 = Gross / Cr 4000 = Gross−Tax / Cr 2055 = Tax; المرتجع يرحّل 5101/2055/1200/1300/5000. المسار القديم AtDelivery باقٍ كما هو.
- **الكميات:** Quantity وCount بعدان مستقلان في ReservationLine وDeliveryOrderLine وSalesOrderItem؛ Available = Current − Reserved بنطاق الصنف؛ الفاتورة تفوتر المسلَّم غير المفوتر فقط وتقبل عدة أوامر تسليم.
- **الواجهات:** تبويبات حالات أوامر البيع + تقدم كل صنف، تفاصيل أمر البيع بالحجز/التسليمات/الفواتير، Customers/PendingDeliveries، قسم قيمة التسليمات المعلّقة في كشف العميل (XLSX + PDF)، كتلة التسليمات المعلّقة في Reports، بطاقة «تسليمات معلّقة» في لوحة القيادة، وتصديرات XLSX/CSV للحجوزات وأذون التسليم.
- **التحقق:** dotnet build = 0W/0E؛ dotnet test = **417/417 PASS**؛ dotnet ef migrations has-pending-model-changes = لا تغييرات؛ database update = مطبَّق؛ smoke test حي على http://localhost:5165 (تسجيل دخول + 12 مسار 200 + تصدير XLSX/CSV/PDF فعلي) بصفر استثناءات؛ بوابة الإتاحة الساكنة = كل <th> يحمل scope وكل جدول جديد يحمل <caption> بلا ألوان hex مباشرة؛ 0 CVE.