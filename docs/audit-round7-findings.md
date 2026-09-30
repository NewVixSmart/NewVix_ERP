# مدقق الجولة السابعة — تقرير النتائج (Round-7 Audit)


> **Currency update (2026-09-27):** the fields and rules described below (`CurrencyId`,
> `ExchangeRate`, `BaseAmount`, `ExchangeRateAtSettlement`, `FxGain`/`FxLoss`, foreign-currency
> allocation and rate-based posting) no longer exist. Multi-currency was removed entirely —
> see [`DECISION-EGP-ONLY.md`](DECISION-EGP-ONLY.md). The findings are kept verbatim as the
> historical record of that round; they are resolved by removal, not by the fixes proposed here.

التاريخ: 2026-09-23
النطاق: مركز الاستيراد (ImportCenter)، المدفوعات (Payments)، التقارير المالية (Reports/Dashboard)، الطباعة وعروض الأسعار والمرتجعات (Print/Quotes/Returns).
قاعدة العمل: مراجعة الجولات السابقة مغلقة في `docs/audit-round6-findings.md` (الكمية مهيمنة على التقييم — H-2/M-1).

قرار (اختيار المستخدم): بدء الجولة السابعة بعد إغلاق السادسة.

---

## ملخص

| الحالة | العدد |
|---|---|
| تم إصلاحها واختبارها | 14 |
| تم فحصها وسحبها (لا خلل) | 1 |
| مؤجلة ومُصنَّفة ومنسدة في المستند | 15 |

رمز كل نتيجة `Xnn` (الإصلاح)، والرمز `Dnn` (المؤجلة).

---

## الاستيراد (ImportCenter — `src\NewVixSmart.Web\Services\ImportCenterService.cs`)

### X-01 (A8 Intermediate) — الفاصلة العشرية تفسَّر ×10 «أصبح 15 بدل 1.5»
**الخلل:** `NormalizeNumber` كانت تحذف كل الفواصل، فالقيمة `1,5` تصبح `15` صامتة (عشرة أضعاف).
**الإصلاح:** الفاصلة ذات خانة أو خانتين في نهاية الرقم تُعامل كفاصل عشري (`1,5` → `1.5`)؛ والفواصل الجماعية (>2 خانة) تُحذف كالمعتاد (`1,234.56` → `1234.56`).
**الاختبار:** `Import_Supplier_DecimalComma_IsNotMultipliedByTen` (عبر دفتر Excel يحافظ على الخلية `1,5`).

### X-02 (A3) — خلية فارغة في التحديث تمحو القيمة القائمة
**الخلل:** `OptNull` تُرجع null لخلية مفقودة، والتخصيص في `ApplySupplier/ApplyCustomer/ApplyItem/ApplyUnit/ApplyBranch/ApplyCurrency/ApplyAccount` كان غير مشروط ⇒ تحديث ملف «الاسم والكود فقط» يمسح الهاتف/العنوان/البريد/الضريبة/وحدات القياس/الوحدة الأم/الرمز النقدي/الحساب الأب.
**الإصلاح:** أُضيف `OptKeep(cells, key, current)` الذي يُبقي القيمة الحالية عند غياب الخلية؛ عُطِّل المسح غير المشروط للحقول الاختيارية؛ حوّلت مسارات العملة والوحدة الأب ووحدة الأصناف وحساب الأب إلى التحقق `cells.ContainsKey(key)`.
**الاختبار:** `Import_SupplierUpdate_BlankCell_KeepsExistingValue`.

### X-03 (A2) — سباق إدراج مكرر لنفس الاسم
**الخلل:** لا يوجد قيد فريد على أسماء المورّدين/العملاء/الأصناف أو الباركود؛ فإدراجان متوازيان لنفس الاسم يمرّران (cache قبل المعاملة).
**الإصلاح:** إضافة فهارس فريدة عبر هجرة `20260923154611_AddImportDedupeUniqueIndexes`:
- `Supplier.Name` (فريد)، `Customer.Name` (فريد)، `Item.Name` (فريد)، `Item.Barcode` (فريد بفلتر غير null).
- مطبقة في `AppDbContext` وبقية النموذج، والتحقق في نفس الملف أصبح تحديثاً وليس إدراجاً مزدوجاً.
**الاختبار:** `Import_Supplier_DuplicateName_SameBatch_BecomesUpdateNotInsert` (إنشاء 1 + تحديث 1، وصفّ واحد في الجدول).

### X-04 (A1 — جزئي) — استيراد مستندات بلا ذرّية عند أخطاء التحقق
**الخلل:** مستندات بها أخطاء تُتجاوز ويُستورد سابقوها (ترحيل جزئي محيّر، وإعادة رفع تكرر المستندات الصحيحة).
**الإصلاح:** إعادة بناء `ImportDocumentAsync` بمرحلتين: تحقق مسبق من **كل** المستندات قبل تطبيق أي منها — وجود أي مستند خاطئ يوقف الاستيراد كاملاً برسالة توضح عدد المستندات وأمثلة الأخطاء (لا استيراد جزئي عند أخطاء التحقق). تبقى تعذّرات الخدمة (تالية للفحص) تُحتسب لكل مستند كما هو.
**ملاحظة معمارية مؤكدة:** الخدمات السفلى (`InventoryService.CreateSaleAsync` وغيرها) تفتح معاملة خاصة بها من الداخل، لذا الذرّية الكاملة لدفعة بأكملها غير ممكنة دون إعادة هيكلة؛ أساس الإصلاح هو بوابة «فحص-ثم-استيراد» وأتمة المستند الواحد.

### مؤجل (D)
- **D-01 (A4)** مرجع `ParentCode` المرسال/الموجود لاحقاً في الملف يُسقط. موصى به: تمريران أو قائمة روابط مؤجلة للحسابات كما في الوحدات. (تأثير منخفض — شجرة حسابية اسمها الكود في العادة)
- **D-02 (A5)** صلاحية وحيدة `ImportCenter.Import` تفتح كل وحدات الاستيراد للمستخدم الممنوح. موصى به: ربط بصلاحيات الوحدة المستهدفة أو قائمة حصرية. (قرار أمني/ميزة)
- **D-03 (A6)** كاش مرجعي يحمّل جداول كاملة للجلسة (12 جدولاً). موصى به: `AsNoTracking` + فهرسة أسماء فقط.
- **D-04 (A7)** والد وحدة غير موجود لا يُسلَّط عليه ضوء في المعاينة.
- **D-05 (A9)** مُوزّع CSV بسيط؛ يوصى بدعم فواصل سيميكولو/اقتباس للصيغ المتعددة.
- **D-06 (A10)** لا إعادة محاولة/رسالة مرجع معاصرة عند فشل الحفظ.

---

## المدفوعات (Payments — `src\NewVixSmart.Web\Services\PaymentService.cs`)

### X-05 (P5) — أخطاء غير بياناتية أثناء المعالجة ترجع 500
**الإصلاح:** `catch` شامل (باستثناء `DbUpdateException`/`DbUpdateConcurrencyException`) بعد معاملات مشكوك فيها يحوّل الفشل إلى نتيجة عربية ودية مع تسجيل `LogError` بدل 500.
**الاختبار:** التغطية القائمة في `DuplicatePaymentTests`/`ConcurrencyTests` تستمر عابرة.

### P1 — سُحب (لا خلل)
**الفحص المؤكد:** `RowVersion` موجود على `SaleInvoice` و `PurchaseInvoice` منذ هجرة `20260831220000_InvoiceRowVersionAndDropUnitId`، مع حلقة استرجاع `attempt=1..3` في `CreatePaymentAsync` تعيد قراءة الفاتورة بعد كل `DbUpdateConcurrencyException` (مع `ChangeTracker.Clear`). لا يوجد فقدان تحديث حقيقي للأرصدة عند دفعتين متزامنتين — السيناريو معالج. لا يحتاج تعديل.

### مؤجل (D)
- **D-07 (P2)** مفتاح التنقيب عن المكرر نافذة زمنية (دقيقتان)؛ تمنع المدفوعات المشروعة لنفس المبلغ في نفس الطرف وتتجاوز المكررات المتأخرة. موصى به: مجموع مفاتيح (طرف+نوع+عملة+تاريخ) مع فحص مبلغ تراكمي — قرار منتج.
- **D-08 (P3)** كسب/خسارة الصرف تُخزَّن معاً في القيد ويستويان محاسبياً (صافي). يُوثَّق السلوك الحالي لا يُغيَّر.
- **D-09 (P4)** هامش زيادة جزئية ناتج التقريب داخل حدود `0.005` المسموحة بالفعل؛ المبالغ لا تتجاوز المستحق. مراجعة ولم يظهر خلل عملي.
- **D-10 (P6)** `GetPaymentsAsync` بلا حد أعلى للقائمة داخل الذاكرة (صفحات موجودة في الواجهة).

---

## التقارير (Reports/Dashboard)

### X-06 (F1) — «الكشف العمري» يجمّع بمعرف الطرف لا بالاسم
**الخلل:** `AgingAsync` كانت «تجمّع باسول `x.Name`» (السطران 709/763 سابقاً) — طرفان متطابقا الاسم يُدمجان.
**الإصلاح:** نقل المعرف (`CustomerId`/`SupplierId`) في الصفوف والتجميع بمخرّف `PartyId` مع `g.First().Name` للعرض.
**الاختبار:** لا يمكن اختبار سيناريو التطابق الاسمي بعد فهرس الاسم الفريد (X-03) — التحقق عبر الكود المستندي والإشارة هنا.

### X-07 (F2) — لوحة المعلومات تضرب المستحقات بلا سعر صرف
**الخلل:** مجموع `OverdueReceivableTotal`/`DueSoon…`/`Payable…` كان `NetAmount − PaidAmount` دون `× (ExchangeRate ?? 1)` (مزيج عملات).
**الإصلاح:** ادخال `ExchangeRate` في الإسقاط وضرب فرق المستحق بسعر الصرف في الأربعة مجاميع.
**الاختبار:** `Dashboard_DueAlerts_ConvertsForeignCurrenciesToBase` (فاتورة €100 بسعر 3 ⇒ 300).

### X-08 (F5 + إضافي) — مجاميع لوحة المعلومات تعمل عند الجميع
**إصلاح مصاحب:** استعلاما `TotalSaleAmount`/`TotalPurchaseAmount` كانا يستدعيان `Math.Round` داخل `SumAsync` (لا يُترجَم على SQLite ولا بعض المزودات) — نُقلا إلى الحساب في العميل.

### مؤجل (D)

| البند | الوصف | ملاحظة |
|---|---|---|
| **D-11 (F3)** | رصيد التدفق النقدي الافتتاحي من جدول `Payments` فقط لا من قيود GL (1000/1100)؛ ينحرف عند ترحيل قيود يدوية. موصى به: الدمج من `JournalEntryLines`. | (إعادة هيكلة كبيرة) |
| **D-12 (F4)** | التدفق النقدي يحمّل كل المدفوعات في الذاكرة → ترحيل إلى تجميع على الخادم. | |
| **D-13 (F6)** | الكشف العمري يتجاهل `OpeningBalance` للطرف. موصى به: إضافة سطر افتتاحي // دمج. | |
| **D-14 (F7)** | تصنيف 5101 كحساب مصروف/مدين وإظهاره في قائمة الدخل مقابل 5102 يعكس الإيرادات. | موصى به: بيانات (seed) وطريقة العرض |
| **D-15 (F8)** | حلقة N+1 في ميزانية الانحراف. | |
| **D-16 (F9)** | Scoping فرعي في تقريرين. | تصحيح تنسيق (كان مكرراً). |
| **D-17 (F10)** | فرق بين `DueDate ?? InvoiceDate` في التقرير وشرط `DueDate.HasValue` في لوحة التقارير. | |

---

## المرتجعات وعروض الأسعار والطباعة

### X-09 (S1) — مرتجع بخطّي نفس الصنف يتجاوز الكمية المسلّمة
**الخلل:** المدققان `ValidateSaleReturnQuantitiesAsync`/`ValidatePurchaseReturnQuantitiesAsync` لا يجمعان الكميات داخل نفس المرتجع ⇒ سطران كل منهما 6 لعشر مسلّم تمرّران والاسترجاع 12.
**الإصلاح:** رفض أي `ItemId` مكرر داخل المرتجع في المدققين وفي إنشاء المسودة (بيع وشراء).
**الاختبار:** `SaleReturn_DuplicateItemLine_WithinSameReturn_IsRejected` و `PurchaseReturn_DuplicateItemLine_WithinSameReturn_IsRejected`.

### X-10 (S3) — قيمة المرتجع يأخذها العميل دون تطابق
**الخلل:** `UnitPrice` في سطر المرتجع قادم من العميل بالكامل ولو كانت الفاتورة مربوطة.
**الإصلاح:** عند الترحيل وارتباط `SaleInvoiceId`/`PurchaseInvoiceId` فسعر السطر يُستبدل بسعر سطر الفاتورة الأصلية ويعاد حساب `TotalAmount`.
**الاختبار:** `PostSaleReturn_UnitPrice_IsTakenFromInvoiceLine_NotClientValue` (سعر مرسل 9999 ⇒ 4×80=320).

### X-11 (S7) — عرض سعر سالب
**الإصلاح:** في `CreateAsync` رفض إذا `Discount > TotalAmount + Tax` قبل تثبيت `NetAmount`.
**الاختبار:** `CreateQuote_DiscountGreaterThanTotalPlusTax_IsRejected`.

### X-12 (S5/S6) — أتمة تحويل عرض→أمر
**الإصلاح:** في `ConvertToOrderAsync`:
- فحص عدد الصفوف المحدَّثة بواسطة `ExecuteUpdateAsync` النهائية؛ وإن كان صفراً (تحويل متزامن/حذف) حذف الأمر المُنشأ مسبقاً (سطوره ثم الأمر) وإرجاع خطأ.
- `DeleteCreatedOrderAsync` تُستخدم أيضاً في مسار الاستثناء قبل إعادة الرمي.
- منع حذف عرض في حالة «جارٍ التحويل» (`Converting`).
**الاختبار:** مسارات القائمة عابرة (`SalesQuoteTests`, `SalesQuotesEnhancementsTests`).

### X-13 (S8) — طباعة المبلغ بالكلمات للمبالغ ≥ مليار
**الخلل:** `IntegerWords` قسمة الملايين على `UnderThousand(millions)` مع `ArabicHundreds[hundreds]` يحطم الفهرس عند `hundreds ≥ 10` (أي ≥ 1 مليار) — `IndexOutOfRangeException`.
**الإصلاح:** طبقة ملايرز: `بلايين` → «مليار/ملياران/مليارات» + فصل `millions = (n/1e6) % 1000`.
**الاختبار:** `PrintPdfBuilderTests.AmountInWords_HugeAmounts_DoNotThrow` (1B، 2B، 3B، 1.0005B، 150B) و `AmountInWords_UnderBillion_StillWorks`.

### مؤجل (D)
- **D-18 (S2)** المرتجع غير المربوط بفاتورة يُمزّ الطريق على **كل** فحص الكميات (return مبكر). القرار: إلزام الربط عند الترحيل أو الاحتفاظ بالمستقل مع فحص مخزون — قرار منتج (الواجهة الحالية تسمح «اختياري»).
- **D-19 (S4)** إرجاع فائض بيع يسترجع بطبقة التكلفة الحالية `PurchasePrice`/سعر العميل بدل تكلفة الطبقة الأصلية (يبقى تطابقًا ضمنيًا بـ FIFO، لكن القيمة قد تختلف).
- **D-20 (S9/S10)** تباين صف «الإجمالي/الخصم» في بعض ملفات PDF (ماري) وغياب تصنيف العملة في الطباعة. (تجميل/وضوح)

---

## الملفات المعدّلة

- `src\NewVixSmart.Web\Services\ImportCenterService.cs` — X-01، X-02، X-04 (+ `OptKeep`).
- `src\NewVixSmart.Web\Data\AppDbContext.cs` + هجرة `20260923154611_AddImportDedupeUniqueIndexes` — X-03.
- `src\NewVixSmart.Web\Services\PaymentService.cs` — X-05.
- `src\NewVixSmart.Web\Services\ReportService.cs` — X-06.
- `src\NewVixSmart.Web\Services\DashboardService.cs` — X-07، X-08.
- `src\NewVixSmart.Web\Services\InventoryService.cs` — X-09، X-10.
- `src\NewVixSmart.Web\Services\SalesQuotesService.cs` — X-11، X-12.
- `src\NewVixSmart.Web\Services\PrintPdfBuilder.cs` — X-13.
- اختبارات: `ImportExportCenterTests`، `ReturnsFixtureTests`، `SalesQuoteTests`، `AgingTests`، جديد `PrintPdfBuilderTests`.

النتيجة: **285/285 اختباراً ناجحاً**، `/healthz` = 200، الهجرات مطبقة.
