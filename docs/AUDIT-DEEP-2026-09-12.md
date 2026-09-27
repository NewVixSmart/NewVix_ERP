# التدقيق العميق الشامل — الجولة الخامسة (كل ملف في المشروع)


> **Currency update (2026-09-27):** the fields and rules described below (`CurrencyId`,
> `ExchangeRate`, `BaseAmount`, `ExchangeRateAtSettlement`, `FxGain`/`FxLoss`, foreign-currency
> allocation and rate-based posting) no longer exist. Multi-currency was removed entirely —
> see [`DECISION-EGP-ONLY.md`](DECISION-EGP-ONLY.md). The findings are kept verbatim as the
> historical record of that round; they are resolved by removal, not by the fixes proposed here.

> **التاريخ:** 2026-09-12
> **النطاق:** كل ملف مكتوب يدويًا في المستودع — الكود المصدري (تحكمات/خدمات/نماذج/عروض/API)، البيانات والهجرات، الاختبارات، البنية (CI/Docker/Aspire/السكربتات)، والتوثيق والخطط.
> **المنهجية:** 5 فرق تدقيق متوازية قرأت كل ملف من مشاهدها سطرًا بسطر + **تحقق يدوي شخصي** من كل البنود الحرجة (قراءة مباشرة وإثبات حسابي) + إعادة أدوات التحقق كأدلة.
> **المستثنى من القراءة السطرية:** مكتبات الموردين `wwwroot/lib/*`، ملفات مصادر `*.map`، ملفات `Migrations/*.Designer.cs` و`AppDbContextModelSnapshot.cs` (مولّدة تلقائيًا — فُحصت للاتساق فقط)، سلسلة الفونتات/الباركود.
> **حالة الفحص الدائري:** build **Debug وRelease = 0W/0E**، اختبارات **128/128 PASS** (كلاهما)، `dotnet list package --vulnerable` = **صفر ثغرات**، بوابة axe على 40 صفحة `GATE: PASS`، 19 رابط تصدير يعملون.

---

## ملخص حسب الخطورة (موحّد بعد إزالة التكرار والتحقق)

| الخطورة | العدد | ملاحظة |
|---|---|---|
| **حرِجة (Critical)** | 3 | تشوّه الدفاتر في مسار تشغيلي عادي |
| **عالية (High)** | 12 | تشوّه أو ثغرة مشروطة/خاصة بمسار |
| **متوسطة (Medium)** | 24 | متانة وتكامل وحماية محدودة |
| **منخفضة (Low)** | 18 | تلميع وتنظيف وإتاحة |
| **معلومات (Info)** | 12 | توثيق/هيكلة (بلا إجراء إلزامي) |
| **ملاحظات دُحضت بعد التحقق** | 1 | «المستحق اليوم» خارج الفئتين — **غير صحيحة** |

العلامة **✓** = تحققتُ منها بنفسي بقراءة مباشرة؛ بدون علامة = تقرير فريق التدقيق.

---

# أ) نتائج حرجة — Critical

### C-1 ✓ — لا يُرحَّل قيد تكلفة البضاعة (COGS) عند البيع إطلاقًا
- **الموقع:** `Services/InventoryService.cs` (مسار `CreateSaleAsync`) + `Services/AccountingService.cs:17-19`
- **التفصيل:** `ConsumeFifoLayersAsync` يُقدِّر الكلفة ويُهمل الناتج؛ القيد الوحيد هو `Dr 1200 / Cr 4000` فقط. حساب المخزون (1300) لا ينخفض مع كل عملية بيع، ولا سجل COGS (5000)، فيتباعد دفتر المخزون عن الواقع مع كل عملية بيع وتُضخَّم أرباح قائمة الدخل. (المرتجعات فقط تحمل الكلفة — `AccountingService.cs:69-95`).
- **الأثر:** دفتر الأستاذ العام للمخزون/الأرباح غير مطابق للمخزون الفعلي منذ أول بيع؛ الميزانية لا تتوازن عند إقفال حقيقي بدقة.
- **التوصية:** قيد بيع كامل: `Dr 1200 / Dr 5000 / Cr 4000 / Cr 1300` داخل نفس معاملة `InventoryService` باستخدام ناتج `ConsumeFifoLayersAsync`، كما هو معمول به في المرتجع (`RecordSaleReturnWithCostAsync`).

### C-2 ✓ — تخصيص مدفوعات العملات الأجنبية يخلط وحدات القياس فيفشل عمليًا
- **الموقع:** `Services/PaymentService.cs:130-227` (تفرعات `foreign`)
- **التفصيل (إثبات شخصي بمثال):** `remaining` يبدأ من `payment.BaseAmount` (بالدوار الأساسية)، لكن في الفرع الأجنبي:
  - `maxAlloc = outstanding * r1 / r0` يقارن مبلغً عملة الأوراق الأساسية بمبلغ بُعد أجنبي،
  - `allocate = min(remaining, maxAlloc)` ثم `remaining -= allocate` (خصم بمقياس أجنبي من رصيد أساسي)،
  - `invoiceBase = allocate * r0 / r1` يُنسب للفاتورة، و`partyBaseReduction += invoiceBase`.
  - **مثال ملموس:** دفع 100$ لفاتورة 100$ بنفس السعر 30 → `BaseAmount=3000`، `maxAlloc=100`، `allocate=100`، `remaining=2900` → بعد تغطية الفواتير كلها يبقى `remaining≈2900 > 0.01` → يُرفض الدفع برسالة «المبلغ أكبر من إجمالي المستحق». أي **سداد أجنبي مكافئ لا يكتمل أبدًا**، وعندما يمر (حالات جزئية) تُنسب `partyBaseReduction` بقيمة مغلوطة يُبنى عليها قيد `RecordFxSettlementAsync:39-58` فيتضخم `fxGain/fxLoss` بلا أساس اقتصادي.
- **الأثر:** مسار العملات الأجنبية معطّل/محاسبيًا خاطئًا من طرف إلى طرف (لم تُغطَّ بالاختبارات — 128 اختبارًا بخوارزمي العملة الأساسية فقط).
- **التوصية:** وحّدت وحدات: التخصيص كله بالأساس (عتبة `outstanding_base = outstanding × r0`، `invoiceBase` = نصيب الفاتورة بعملتها عبر سعر دفعها)، وأعد حساب `FxGain/FxLoss` بتلك المقادير، ثم أضف اختبارات نقدية FX end-to-end.

### C-3 ✓ — فواتير العملات الأجنبية تُرحَّل بقيمتها الأجنبية بلا تحويل إلى العملة الأساسية
- **الموقع:** `Services/AccountingService.cs:17-23` + `InventoryService.cs` (إنشاء الفواتير)
- **التفصيل:** `RecordSaleInvoiceAsync(entryDate, customerId, netAmount, …)` و`RecordPurchaseInvoiceAsync` تحمل `netAmount` فقط دون `CurrencyId/ExchangeRate`؛ تُرحَّل قيمتها كما هي إلى 1200/4000 و1300/2000. المقابل: المرتجعات (س 71/86) تحوّل (`value × rate`)، والمدفوعات تستخدم `BaseAmount`.
- **الأثر:** ذمم بغير عملة أساسية في الأستاذ لا تطابق كشف الأطراف ولا النقد؛ تحليلات العملة بالأساس (CashFlow/Aging) تختلف عن الأستاذ.
- **التوصية:** مرّر `LocalAmount = NetAmount × ExchangeRate` للقيدين (بنمط `RecordSaleReturnWithCostAsync`).

---

# ب) نتائج عالية — High

| # | ✓/— | file:line | العنوان | التفصيل/الأثر | التوصية |
|---|---|---|---|---|---|
| H-1 | ✓ | `Data/SeedData.cs:269-310,312-336` + `AccountingService.cs:97-103` | بذرة المخزون الفيزيائي بلا قيد افتتاحي ولا طبقات | الأصناف تُزرع بـ`CurrentCount/CurrentQuantity` دون `Dr 1300 / Cr 3000` ولا طبقات FIFO → رصيد مخزون بلا مقابل، وأول بيع يستهلك كلفة صفر، وعدم توازن الميزانية منذ البدء | قيد افتتاحي متوازن + طبقات FIFO مطابقة عند البذر |
| H-2 | — | `InventoryService.cs:400-432,434-438` | جرد النقصان لا يُرحَّل والأصناف العدّية (Count) لا تظهر بالأستاذ | القيد يُنشأ فقط عند الزيادة `addedQty>0 ∥ addedCount>0`؛ `amount = qty×cost` يتجاهل `count` ويُلغي `amount<=0` | ترحيل ثنائي صعودًا ونزولًا بالجمع بين الكمية والعدد |
| H-3 | — | `InventoryAdjustmentsController.cs:69-102` + `InventoryService.cs` | حذف الجرد لا يعكس الطبقات ولا القيد | يُعيد `Current` فقط، يُبقي `StockLayer` والأستاذ مشوقين؛ ويمنع الحذف عند أي حركة لاحقة | عكس المعاملة كاملة (طبقات+قيد) أو منع الحذف نهائيًا |
| H-4 | — | `FinancialReportService.cs:23,199-201` + `AccountsController.cs:118-133` | الحسابات المعطَّلة ذات الأرصدة تختفي من القوائم بالكامل | تقارير الميزان والقوائم ترشّح `IsActive` فتتساقط أرصدة مرحلة | اعرض الحسابات غير النشطة ذات الرصيد أو امنع تعطيل حساب له رصيد |
| H-5 | — | `InventoryService.cs:556-570` | مرتجع البيع يُعيد المخزون بسعر الشراء الحالي لا بالطبقة الأصلية | `RestoreSaleReturnLayersAsync` يستخدم `item.PurchasePrice` اللحظي → تشوّه قيمة المخزون وCOGS المرتجع | أعد فتح الطبقات المستهلكة/مرجع `SourceLayerId` أو معدّل FIFO المباع أصلًا |
| H-6 | — | `ProcurementService.cs:161-195` | أمر الشراء قابل لفوترة متعددة بلا حارس | لا توجد حالة `Invoiced` ولا `InvoicedQty` → إعادة الفوترة تكرّر المخزون والذمم | حالة/كمية معفيرة لكل بند داخل معاملة |
| H-7 | — | `InventoryService.cs:66-67,144-145` + `AccountingService.cs:17-23` | شروط الدفع OnReceipt لا تُسوَّى عند الفاتورة | المشتريات النقدية تُنشأ `PaidAmount=0/IsPaid=false` وتُقيَّد `Dr 1200` لا `Dr 1000`؛ تظهر فواتير معلّقة في الاستحقاق والتنبيهات | حقل «دفع عند الاستلام» ينشئ فاتورة+دفعة+قيد نقدي في معاملة واحدة |
| H-8 | — | `FiscalService.cs:53,75-77` + `FinancialReportService.cs:171-175,197-210` + `ReportsController.cs:357` | قيود الإقفال بتاريخ 31/12 تدخل ضمن نشاط الفترة | بعد الإقفال تظهر قوائم الدخل/الواريانس للسنوات المغلقة صفرية لأن القيود العاكسة داخلة النافذة | استبعاد `JournalSource.YearEndClose` من نوافذ أداء الفترة |
| H-9 | — | `FinancialReportService.cs:126-129` | الميزانية تُسقط الحسابات ذات الرصيد الموقَّع ≤ 0 | أصل بميزان عكسي/حساب مقابل/مخصص يضيع من الميزانية ويختل عرض التوازن | إدراجها بالقيم الموقعة مع قسم الافصاح |
| H-10 | — | `ReportService.cs:673-689,905-919` + `InventoryService.cs:249-255` | المرتجعات المرحّلة لا تُطبَّق على استحقاق الفاتورة ولا Aging/الكشف | الفاتورة تبقى «مستحقة» كاملة رغم عودة البضاعة؛ لا مسار استرداد نقدي (دفعة سالبة) | خفض استحقاق الفاتورة بالمرتجع وإدماجه في العمرية + سند استرداد |
| H-11 | ✓ | `ReportsController.cs:53-55,94-96` | المرتجعات المسودة (Draft) تُحسب نهائية | تقارير المبيعات/المشتريات وكشوف الأطراف تجمّع كل المرتجعات بلا `Status==Posted` | فلترة `ReturnStatus.Posted` في كل التجميعات |
| H-12 | ✓ | `Api/TokensController.cs:27-31` + `Program.cs:43-59` | إصدار JWT بتجاوز القفل (lockout) وبلا فحص حالة المستخدم | `CheckPasswordAsync` مباشرة بلا lockout/rate-limit → brute-force مفتوح على `/api/auth/token`؛ ولا `OnTokenValidated` فتوكن مقفول/محذوف يبقى صالحًا ساعة | `SignInManager.PasswordSignInAsync(lockoutOnFailure:true)` أو فحص مانوي + حدث `OnTokenValidated` |
| H-13 | — | `Data/SeedData.cs:32,36-37` | بذر كلمات مرور ثابتة معلنة (`Admin@123/Accountant@123/Warehouse@123`) | أي نشر يتركها = اختراق فوري؛ `EmailConfirmed=true` أيضًا | توليد عشوائي/تغيير إجباري عند أول دخول/تغذية من أسرار |
| H-14 | ✓ | `aspire/Vix.AppHost/Program.cs:3-8` مقابل `Program.cs:15` | اسم اتصال Aspire لا يطابق التطبيق | `AddDatabase("newvixsmart")` بينما التطبيق يقرأ `DefaultConnection` (LocalDB) → Aspire يفشل | `AddDatabase("DefaultConnection")` أو قراءة احتياطية |

---

# ج) نتائج متوسطة — Medium

### المحاسبة والقوائم (مُدقَّقة من الفريق المحاسبي)
| # | file:line | العنوان | التوصية |
|---|---|---|---|
| M-1 | `FinancialReportService.cs:197-230` | لا تجميع حسب مستويات الحسابات (ParentAccountId) في القوائم | إضافة تجميع هرمي |
| M-2 | `FinancialReportService.cs:156-169` | `NetIncomeAsOfAsync` يشمل قيود الإقفال فتعود صفرية بعد الإغلاق | استبعاد مصدر الإقفال (مع H-8) |
| M-3 | `ReportsController.cs:146-147` + `ReportExportService.cs:93` | تقرير المدفوعات يعتمد `Amount` لا `BaseAmount` فيخلط العملات | المكافئ الأساسي + عمودي العملة/السعر |
| M-4 | `AccountingService.cs:61-67` + `IAccountingService.cs` | `RecordSaleReturnAsync/RecordPurchaseReturnAsync` (بلا كلفة) بلا استدعاء — مسار ميت خطير إن أُستخدم | حذفها أو توثيقها كخطر |
| M-5 | `AccountingService.cs:27,34` | كل وسائل الدفع غير النقدية تُرحَّل لحساب 1100 واحد بلا تمييز بنك | حساب فرعي/بنك لكل طريقة |
| M-6 | `AppDbContext.cs:216-220` | لا فهرس على `(JournalEntryId)/(AccountId)` — التقارير تمسح الجدول | فهرس مركّب |
| M-7 | `AppDbContext.cs:180-185` | فهرس التخصيص غير فريد `(PaymentId, InvoiceType, InvoiceId)` → مضاعفة PaidAmount مع إعادة المحاولة | فهرسة فريدة |
| M-8 | `AppDbContext.cs:120,149` + `JournalEntry.cs:26` | `SetNull` للفاتورة يترك القيود يتيمة (SourceId بلا FK) | Restrict + FK على (Source, SourceId) |
| M-9 | `SeedData.cs:183-185` + `AccountingService.cs:75,91` | حسابان ميتان 4100/5100 وسجل Named System رغم أن المرتجعات على 5101/5102 | استبعاد 4100/5100 من البذرة/النظام |
| M-10 | `FinancialReportService.cs:81-106` + `SeedData.cs:166-167` | مرتجعات البيع تُعرض «مصروفًا» لا خصمًا من الإيراد (نوع Expense وNormalBalance) | تصنيف مراجعات كحسابات مقابل إيراد/شراء |
| M-11 | `AccountingService.cs:17-23` + `InventoryService.cs:58` | الضريبة مدمجة في الإيراد/المخزون بلا حساب التزام ضريبي | حساب ضريبة منفصل |
| M-12 | `SalesController.cs:53` + `PurchasesController.cs:53` + `PaymentsController.cs:62` + `PaymentService.cs:258-261` | `ExchangeRate=1` افتراضي بلا إلزام — عملة أجنبية بسعر 1 تُعامل كأساسية | إلزام سعر صرف صحيح للعملات غير الأساسية |
| M-13 | `AccountingService.cs:39-59` | لا إعادة تقييم فروق العملات غير المحققة نهاية الفترة | ترحيل دوري عند الإقفال |
| M-14 | `ReportService.cs:795-837` | CashFlow: افتتاحية بالعملات المختلطة، نقد وبنك مجمّعان، بلا فلتر فرع | فصل نقد/بنك + ترشيح فرع + عملة موحّدة |
| M-15 | ✓ | `ReportService.cs:679-685,693-698` | Aging يُجمَّع بالاسم لا بالمعرّف (`GroupBy(Name)`) → دمج أطراف متشابهة؛ ولا افتتاحيات ولا مرتجعات (مع H-10) | GroupBy بالمعرّف + مراعاة الافتتاحية/المرتجعات |
| M-16 | `SeedData.cs:145-149` | السنتان الماليتان الجاريتان فقط تُبذران — إغلاق سنة أقدم يفشل بلا توجيه | توليد الفترات الناقصة تلقائيًا |
| M-17 | `SeedData.cs:172-173` | `return` شرطي يقطع بقية البذرة عند وجود حساب سابق → ترقية غير متسقة | بذر اسميًا لكل حساب لا شرطيًا |

### العمليات والمخزون (مُدقَّقة من فريق العمليات)
| # | file:line | العنوان | التوصية |
|---|---|---|---|
| M-18 | `InventoryService.cs:508-511` | سحب البيع يستهلك طبقات كل المستودعات (بلا فلتر WarehouseId) | ربط البيع بمصدر مخزون/فلتر مستودع |
| M-19 | `InventoryService.cs:352-353` | مرتجع الشراء يستهلك أقدم FIFO لا طبقة الفاتورة المشتراة | تقييد التخصيص بالفاتورة |
| M-20 | `InventoryService.cs:615-617,640-642` | التحقق «سبق إرجاعه» يشمل المسودات فيجمّد الحصة بلا تحرير/حذف مسودات | فلترة Posted + إتاحة إدارة المسودات |
| M-21 | `PaymentService.cs:49 + 263-273` | `HasDuplicatePaymentAsync` ليس ذرّيًا (نافذتا طلب متزامنتان تنجحان) | بصمة فريدة + قيد فريد |
| M-22 | `PaymentsController.cs:46,58` + `PaymentService.cs:100-106` | `ReceiptNumber` يُولَّد خارجيًّا ويُعاد فقط عند تعارض — هشّ تحت التزامن | التوليد خادميًّا داخل المعاملة |
| M-23 | `ProcurementService.cs:126-159` | `ReceiveOrderLineAsync` بلا معاملة صريحة/RowVersion | إغلاق في معاملة + تحقق Optimistic |
| M-24 | `BatchService.cs:19-64,66-95` | نتائج جزئية بلا مفتاح idempotency — إعادة التشغيل تكرّر المستندات | معرّف تشغيل قابل لإعادة الفحص |
| M-25 | `InventoryService.cs:817-947` | التحويلات بلا قيد محاسبي (بضائع بالطريق 1308) | قيد دخول/خروج + عكسه عند الوصول |
| M-26 | `InventoryService.cs:112-118` | `PurchasePrice` من آخر سطر `Quantity>0` فقط — أصناف يولِّد count لا تُحدَّث؛ ترتيب سطر يعتمد | الكلفة من طبقة الوارد + قناة نشطة |
| M-27 | `InventoryService.cs:83-90` + `AccountingService.cs:105-171` | استثناءات الأستاذ (قيد غير متوازن/حساب مفقود) تفلت كـ500 بلا رسالة عربية | اعرض الرسائل العربية وتعامل معها |

### الأمان والبنية (مُدقَّقة من فريق الأمان + الجولة الرابعة)
| # | file:line | العنوان | التوصية |
|---|---|---|---|
| M-28 | `Program.cs` + `appsettings.json` | غياب HSTS وكوكيز/جلسة دون `SecurePolicy=Always` | `UseHsts` (خارج Dev) + `CookieSecurePolicy.Always` |
| M-29 | `Controllers/SettingsController.cs:43` + `Models/Accounting/Currency.cs:30` | ربط `IsBase` و`Id` في `AddCurrency` يتجاوز `SetBaseCurrency` | VM/DTO أو `[Bind]` + `currency.Id=0` (نمط AddUnit) |
| M-30 | `.github/workflows/ci.yml:29,36` | مسار رفع TRX لا يطابق موقع الملف → artifact فارغ | `--results-directory TestResults` |
| M-31 | `tests/NewVixSmart.Web.Tests/AgingTests.cs:33-34,87,138,111` | تشفير عربي مخرَّب (Mojibake) + مسافة بادئة شاذة | إعادة حفظ UTF-8 + format |
| M-32 | `src/NewVixSmart.Web/Dockerfile` | الحاوية تعمل root وcurl غير مربوط | مستخدم غير جذر + healthcheck مضمن |
| M-33 | غياب | لا اختبارات آلية للتفويض/JWT/CSRF (WebApplicationFactory) | إضافة Illuminate/pain لطبقة Authorize |
| M-34 | `BudgetAndAccountsTests.cs:267-287` | اختبار «الميزانية المغلقة تُمنع» ينحل عن مدلوله (الخارس في الواجهة لا DB) | إعادة تسمية + اختبار خارس فعلي |
| M-35 | غياب | مسارات التزامن (دفعتان متزامنتان/تعارض RowVersion/مكرر الدفع) بلا تغطية | اختبارات Task.WhenAll |
| M-36 | `Program.cs:163-166` | `script-src-attr 'unsafe-inline'` + `style-src 'unsafe-inline'` يوهن CSP | نقل المعالجات المضمّنة إلى nonce تدريجيًا |

---

# د) نتائج منخفضة — Low

| # | file:line | العنوان |
|---|---|---|
| L-1 | `Program.cs:8` + `appsettings.json:8` | `AllowedHosts:"*"` (رأس Host غير مقيد) |
| L-2 | `Program.cs:226-233` + `CurrentBranchExtensions.cs` | `SetCurrentBranch` بلا تحقق من وجود/تفعيل الفرع |
| L-3 | `Api/PaymentsController.cs:35` | `request.Type.Equals` قد يرمي NRE على JSON بلا `type` |
| L-4 | `Extensions/ApiAuthorizeAttribute.cs` | Token-borne roles: سحب دور لا يسري قبل انتهاء الصلاحية |
| L-5 | `UsersController.cs:63-85` | مستخدم جديد `EmailConfirmed=true` فورًا بلا verify |
| L-6 | `SettingsController.cs:43-63,147-167,238-265` | `AddCurrency/AddBranch/AddUnit` لا تفحص `ModelState.IsValid` كليًا |
| L-7 | `Api/ItemsController.cs:55-67` + `Api/JournalEntriesController.cs:50` | لا حد لحجم/عدد بنود القوائم (DoS محتمل تحت المصادقة) |
| L-8 | `InventoryService.cs:186,231,289,334` | فلتر بنود المرتجع `!=0` يقبل السالب |
| L-9 | `InventoryService.cs:621-626,646-651` | `FirstOrDefault` يفحص أول بند فقط لصنف مكرر في الفاتورة |
| L-10 | `StockTransfersController.cs:49` | `ModelState.Clear()` يتجاوز `[Range]` |
| L-11 | `ShipmentsController.cs:74-99,120-139` | شحنة لا تتطابق فاتورتها مع الطرف |
| L-12 | `InventoryService.cs:900-903` | `BalanceBefore` لحركة الوارد بعد خصم المصدر (بيانات مضللة) |
| L-13 | `Views/Reports/Sales.cshtml:47` + `Purchases.cshtml:47` | زر PDF أيقونة بلا aria-label |
| L-14 | `Views/PurchaseOrders/Receive.cshtml:45` | زر استلام أيقونة بلا اسم ميسّر |
| L-15 | `Views/SaleReturns/Create.cshtml:86` + `PurchaseReturns/Create.cshtml:85` | `innerHTML` على عنصر قد يكون null بعد حذف كل الصفوف |
| L-16 | `Site.js:14-25,75-82,31` | `postForm` بلا `.catch` + `alert()` بنص استجابة خام |
| L-17 | `Views/Categories/Index.cshtml:33` وغيرها | أزرار تعديل/حذف بلا `aria-label`/`caption` في جداول القوائم |
| L-18 | `ReportExportService.cs` + `ReportService.cs:298-335` | تصدير CSV/XLSX بلا أعمدة عملة/سعر صرف/muneip BaseAmount |

---

# هـ) مؤكَّد سليم — نقاط قوة (مُثبَّتة بمرجع)

- **قيد مزدوج مفرض برمجيًا:** كل سطر إما مدين أو دائن وبالتوازن `Σد == Σج` ويُرفض القيد في سنة مغلقة (`AccountingService.cs:110-134`).
- **حماية التزامن متعددة الطبقات:** `[Timestamp] RowVersion` على الأصناف + إعادة محاولة 3 مع `DetachAll/ResetKeys` (`InventoryService.cs:13,760-774`)؛ فهارس فريدة على كل أرقام المستندات (`AppDbContext.cs` 104/118/133/147/167/173/201/208/213/224/244/257/274).
- **منع الرصيد السالب:** فحص شامل لكل الأسطر قبل أي تحوير (`InventoryService.cs:660-681`) ولو مع إشارات سالبة.
- **إعادة احتساب خادمية كاملة** للماليات (Total/Net/D1/D2/D3/Tax) مع حارس صافي سالب، و`PaidAmount/IsPaid=0` عند الإنشاء (`InventoryService.cs:57-67,127-135`).
- **إعادة تحقق كميات المرتجعات داخل المعاملة** مع تطابق الطرف وعدم تجاوز الكمية المباعة (`InventoryService.cs:606-654`) — سدّ TOCTOU القديم.
- **حماية فوق الدفع** في كلا المسارين مع تراجع (`PaymentService.cs:64-69`)؛ وتخصيص التوزيع بأقدم الفواتير (`:138-141,194-197`).
- **لا حقن SQL/Command:** كل الاستعلامات LINQ مُعلمة؛ لا `FromSqlRaw/ExecuteSqlRaw`.
- **CSRF كامل** في MVC (`[ValidateAntiForgeryToken]` على كل POST + رمز عام)؛ الـ API بلا cookies + CORS مقفول بلا أصول.
- **CSP بالـ nonce** لكل `<script>`؛ **JSON-in-script** عبر `JavaScriptEncoder.Default`؛ **escapeHtml** في DOM الديناميكي؛ **CSV** محيّد `=+-@\t`؛ **LocalRedirect**؛ **رسالة دخول موحّدة**؛ **قفل 5/5**؛ **Swagger منزوع في الإنتاج**؛ **مفتاح JWT مُحصَّن عند الإقلاع في non-Dev**.
- **حماية الحسابات النظامية** (تحرير/تعطيل/حذف مقيد) وإصدارية الميزانية للسنوات المغلقة (`AccountsService.cs:44-63,88-98`, `BudgetsController.cs:116-120`).
- **إتاحة:** axe على 40 صفحة `GATE: PASS` صفر Critical/Serious؛ برايل/كابشن/scope في التقارير المالية؛ print يدوي سليم؛ RTL boostrap.rtl.
- **البنية:** 128/128 (Debug وRelease)؛ عزل SQLite memory لكل فئة؛ `TreatWarningsAsErrors`؛ وثائق BACKUP/DEPLOY متسقة.

---

# و) توصية الأسبقية (خارطة إصلاح مقترحة)

**الدفعة 1 — حرج (تفقد المالية):**
C-1 (قيد COGS)، C-2 (إصلاح تخصيص FX) + اختبارات FX، C-3 (ترحيل الفواتير بالأساس) + اختبارات عملة أجنبية end-to-end.

**الدفعة 2 — عالي/تكافؤ:** H-1 (بذرة مخزون بقيد افتتاحي+طبقات)، H-2/H-3 (جرد النقص والحذف)، H-5 (مرتجع بطبقة الأصل)، H-6 (فوترة أمر شراء مرة واحدة)، H-7 (OnReceipt)، H-11 (فلتر Posted للمرتجعات)، H-12 (قفل JWT + OnTokenValidated)، H-14 (اسم اتصال Aspire).

**الدفعة 3 — متوسطة:** H-8/H-9/H-10 + M-1…M-15 (قوائم/فهارس/تجميع) ثم M-18…M-27 (FIFO/مستودع/دفعات/دفعة/Batch) ثم M-28…M-36 (أمان/بنية/اختبارات).

**الدفعة 4 — منخفضة/تلميع:** L-1…L-18 (أمان مضاعف وإتاحة وتنظيف ملفات الجذر `test-out*.txt`/`tests-result.txt`/`.playwright-cli`).

---

## سجل التحقق من الأدوات (في تاريخ التدقيق)

| الأداة | النتيجة |
|---|---|
| `dotnet build NewVixSmart.slnx -c Debug` | 0W/0E |
| `dotnet build NewVixSmart.slnx -c Release` | 0W/0E |
| `dotnet test -c Debug --no-build` | 128/128 PASS |
| `dotnet test -c Release --no-build` | 128/128 PASS |
| `dotnet list package --vulnerable --include-transitive` | صفر ثغرات (Web + Tests) |
| بوابة axe (Playwright) | 40 صفحة `GATE: PASS`، صفر Critical/Serious |
| روابط التصدير الحية | 19/19 = 200 بنوع الصحيح |

> هذا التقرير جولة خامسة تُكمل المسارات الأربع السابقة في `Deep-Audit-Report.md` (التي وثّقت إغلاقًا تاريخيًا لبنودها). لم تُجرَ أي تعديلات على الكود في هذه الجولة — **تدقيق فقط** بطلب «كل ملف وكل حرف».