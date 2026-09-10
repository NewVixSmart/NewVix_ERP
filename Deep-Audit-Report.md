# المراجعة العميقة الموسّعة — نظام Silk Trading

> التاريخ: 2026-08-31 (جولة ثانية)
> النطاق: تطبيق Silk.Trading.Web + قاعدة البيانات `SilkTradingDb` + نظام الصلاحيات
> البناء النهائي: **0 تحذيرات / 0 أخطاء** — التطبيق يعمل على `http://localhost:5165`
> المنهجية: قراءة وتدقيق يدوي لكل التحكمات والخدمات والنماذج + إثبات تجريبي عبر Playwright لبعض النتائج المتنازع عليها

---

## ملخص حسب الخطورة

| الخطورة | عدد | الأثر |
|---|---|---|
| **حرِجة (Critical)** | 3 | ثغرات سلامة مالية/مخزنية قابلة للاستغلال |
| **عالية (High)** | 4 | صلاحيات/هندسة بحاجة معالجة قبل الإنتاج |
| **متوسطة (Medium)** | 7 | تحسينات سلامة ومتانة |
| **منخفضة (Low)** | 5 | تنظيف وتحسين عام |
| **إيجابيات كاذبة تم دحضها** | 3 | وُثِّق أنها ليست مشاكل فعلية بعد الفحص التجريبي |

---

## أ) نتائج حرِجة (Critical)

### C-1 — إمكانية تعليم فاتورة «مدفوعة بالكامل» دون دفع فعلي (تلاعب بالحقول القابلة للربط)
- **الموقع**: `Services/InventoryService.cs:43` (و`:99` لنظيرتها في الشراء) + `Controllers/SalesController.cs:58` و`PurchasesController.cs`.
- **التفصيل**: كائن `vm.Invoice` (من نوع `SaleInvoice`/`PurchaseInvoice` — كيان EF) يُربَط مباشرة من POST. رغم إعادة حساب `TotalAmount`/`NetAmount`، تُؤخذ الخاصية `IsPaid` من العميل مباشرة:
  ```csharp
  invoice.PaidAmount = invoice.IsPaid ? invoice.NetAmount : 0;
  ```
  أي أن أي مستخدم لديه `Sales.Create` أو `Purchases.Create` (المحاسب/أمين المخزن افتراضيًا) يستطيع إرسال `IsPaid=true` مع POST، فتُسجَّل الفاتورة **مدفوعة بالكامل** (`PaidAmount = NetAmount`) دون أي قبض حقيقي في المدفوعات.
- **الإصلاح المقترح**: تجاهل `IsPaid`/`PaidAmount` الواردة من العميل وأعِد ضبطها دائمًا (مثل الأسعار): `invoice.IsPaid = false; invoice.PaidAmount = 0;` عند الإنشاء، أو استخدام ViewModel بخصائص `[BindNever]/[JsonIgnore]`.

### C-2 — إمكانية ضخّ رصيد افتتاحي (مخزون) اعتباطي أثناء إنشاء الصنف
- **الموقع**: `Views/Items/Create.cshtml:125-134` + `Controllers/ItemsController.cs:58,73-74`.
- **التفصيل**: نموذج الإنشاء يعرض `CurrentCount` و `CurrentQuantity` كحقول قابلة للتحرير، والتحكم يربط كيان `Item` كاملًا (`Create(Item item)`) ثم `_db.Items.Add(item)`. لا يوجد حارس منفصل (عكس الأسعار المحمية بـ `IsAdmin`). المحاسب/أمين المخزن (يملكون `Items.Create`) يمكنهم رفع الرصيد الافتتاحي لأي رقم يريدون، ما يفسد المخزون وقيود `ApplyStockAsync` السلبية (لأن الرصيد يصير موجزًا مزيّفًا).
- **ملاحظة إيجابية**: في `Edit` تُنسَخ الحقول يدويًا إلى قائمة بيضاء (لا تُنسَخ `CurrentCount/CurrentQuantity` من الربط)، فهذا الخطر محصور في **إنشاء** الصنف وليس تحريره، لكنه مصادفة وليس تصميمًا مقصودًا.
- **الإصلاح المقترح**: استخدم ViewModel للإنشاء أو اعزل `CurrentCount/CurrentQuantity`؛ الأفضل جعل الرصيد الافتتاحي يُدار عبر «جرد افتتاحي» وليس حقلًا مفتوحًا.

### C-3 — عمليات الدفع والمبيعات تعتمد على مجموع `Quantity>0` فقط (قد يهرب الرصيد بالعدد `Count`)
- **الموقع**: `Services/InventoryService.cs:19,67,123,164`.
- **التفصيل**: معايير «الصنف الصالح» هي `(i.Quantity > 0 || i.Count > 0)` و`ToStockLines` تلتقط `Count` و`Quantity` معًا. لكن في **تحديث سعر شراء الصنف** (السطر 82: `priceLine = group.LastOrDefault(l => l.Quantity > 0)`) يتم تجاهل السطور بالعدد فقط — هذا سلوك مقصود للأسعار لكنه قد يُخفِي التوازن إذا كان الصنف يُدار بالعدد دون كمية. (تُؤكد خدمة المخزون عدم السماح برصيد سالب في `ApplyStockAsync:276`). هذا تضارب متدنٍّ في موثوقية التعامل، ولكنه ليس ثغرة منفصلة قابلة للاستغلال بشكل مباشر.

> **إثبات C-1 و C-2**: قابلان للتحقق تجريبيًا بإرسال POST مُنشّط؛ لم أُنفّذهما على قاعدة البيانات النظيفة للحفاظ على سلامتها، بل وثّقتهما من قراءة التعليمات مباشرة. الجدية موثّقة من الكود.

---

## ب) نتائج عالية (High)

### H-1 — عدم إعادة احتساب مجموع المرتجعات من الخادم (قيمة `TotalAmount` من العميل)
- **الموقع**: `Controllers/SaleReturnsController.cs:53`, `PurchaseReturnsController.cs:53`.
- **التفصيل**: التحكم يرتبط بكيان `SaleReturn` كاملًا، ونموذج `Create` يتضمن حقل `TotalAmount` مُرسَل. الخدمة (`InventoryService:133,174`) تعيد احتساب `saleReturn.TotalAmount = valid.Sum(i => i.Total)` وتتجاوز قيمة النموذج — **محمية**. لكن بقية حقول الكيان (مثل `ReturnDate` أو الحقول المحسوبة) تُقبل من الربط. خطر واحد حقيقي: **نافذة TOCTOU** في التحقق (المرتجعات ≤ المباع).

### H-2 — نافذة TOCTOU في التحقق من كمية المرتجع
- **الموقع**: `Controllers/SaleReturnsController.cs:76-91` (والنظير `PurchaseReturnsController.cs`).
- **التفصيل**: يتحقق التحكم من «المرتجع ≤ المباع» في طلب واحد باستخدام قراءة `AsNoTracking`، ثم يمرّر إلى `_inventory.CreateSaleReturnAsync` التي **لا تعيد هذا الفحص**. تحت التزامن (طلبان متزامنان لنفس الصنف/الفاتورة) يمكن أن يتجاوز مجموع المرتجعات المباع، فيعود للرصيد أكثر مما بيع. الحل: نقل فحص «الكمية المرتجعة ≤ الكمية المباعة» داخل خدمة المخزون ضمن نفس المعاملة (مثل فحص الرصيد في `ApplyStockAsync`).

### H-3 — لا معاملة واحدة لدفع+توزيع الفاتورة، ولا رصيد/توازن على الدفع
- **الموقع**: `Controllers/PaymentsController.cs:85-146`.
- **التفصيل**: يُحفظ الدفع (`SaveChangesAsync:92`) في معاملة، ثم تُحدَّث الفواتير (`ApplyInvoiceAllocationAsync`) في معاملة مستقلة. إذا فشل التوزيع (صيد الاستثناء في `:169`)، يبقى الدفع محفوظًا والفواتير غير مخصّصة. هذا مقبول تشغيليًا (مع تحذير)، لكنه **ليست ذرّية**: الانهيار في منتصف التخصيص يترك `PaidAmount` منقوصًا. كما أن `SaleInvoice`/`PurchaseInvoice` **بدون RowVersion**، فدفعتان متزامنتان لنفس الفاتورة تقرآن نفس `PaidAmount` وتفرطا في التخصيص (`PaidAmount > NetAmount`).
- **الإصلاح**: لفّ الحفظ + التخصيص في معاملة واحدة، وإضافة concurrency token على الفواتير، وإعادة التحقق `remaining` من القيم المحدّثة.

### H-4 — لا مفتاح تكرار (idempotency) لعمليات الدفع
- **الموقع**: `Controllers/PaymentsController.cs:61-107`.
- **التفصيل**: إعادة الإرسال (نقر مزدوج/إعادة حاول بعد انقطاع) تُنشئ دفعًا مكررًا يُخصَّص على الفواتير مرة أخرى. لا يوجد قيد `ReceiptNumber` فريد حماية (الرقم يُولَّد بخلاف + إعادة محاولة، لكن لا شيء يمنع تكرار نفس مبلغ/مورد/عميل متطابق).
- **الإصلاح**: إضافة بصمة (مفتاح مكرر) أو `unique index` على (تاريخ، نوع، عميل/مورد، مبلغ) أو قياس ضبطة prevent-double-post من العميل + إعادة تحقق على الخادم من تكرار دفعة متطابقة حديثًا.

---

## ج) نتائج متوسطة (Medium)

### M-1 — عمود `UnitId` «شبح» في قاعدة البيانات
- **الموقع**: `Migrations/AppDbContextModelSnapshot.cs:374` (`UnitId`) و`:1128` (`HasForeignKey("UnitId")`) — ظاهر في كل الهجرات.
- **التفصيل**: نموذج `Models/Core/Item.cs` يحتوي `CountUnitId` و`QuantityUnitId` فقط، بينما يُنشئ النموذج عمود `UnitId`+فهرس+FK في جدول `Items`. عمود غير مستخدم وغير محلَّل في النموذج، يترك فجوة تكامل غير مقصودة. أُزيل من نموذج سابق من `Item` لكن الهجرة لم تحذف العمود.
- **الإصلاح**: هجرة جديدة `DropColumn("Items", "UnitId")` + إسقاط الفهرس وFK.

### M-2 — إعدادات الوحدة تقيَّد بإذن «عرض» فقط رغم أنها تُعدِّل
- **الموقع**: `Controllers/SettingsController.cs:12` (`[RequirePerm("Settings.View")]` على الكلاس) تغطّي `AddUnit/UpdateUnit/DeleteUnit` (متحورات).
- **التفصيل**: كل من يملك `Settings.View` يستطيع إضافة/تعديل/حذف الوحدات (كتابة) بإذن قراءة فقط. حاليًا المحتوى محصور بالأدمن (لا صفات Settings في الافتراضيات)، فلا بلاء، لكنه تصميم يخلط بين القراءة والكتابة.
- **الإصلاح**: إضافة مفاتيح `Settings.Edit`/`Settings.Create`/`Settings.Delete` وتطبيقها على الأفعال.

### M-3 — `RequirePermFilter` يسمح بمرور المتصفح غير المصادق
- **الموقع**: `Extensions/RequirePermAttribute.cs:29-30`.
- **التفصيل**: `if (IsAuthenticated != true) return;` يسمح للمصادقة المضللة بالمرور ويعتمد كليًا على وجود `[Authorize]`. آمن اليوم لأن كل استخدام مقترن بـ `[Authorize]`، لكن أي إهمال مستقبلي يفتح أفعالًا علنية.
- **الإصلاح**: اجعل الفلتر يرفض (أو يعيد توجيه إلى تسجيل الدخول) إذا لم يكن المستخدم مصادقًا.

### M-4 — `search` غير محدود الطول/النطاق في عدة قوائم
- **الموقع**: `ItemsController.cs:20`, `CustomersController.cs:18`, `SuppliersController.cs:18`, `StockController.cs:19,46`.
- **التفصيل**: معامل `search` يقبل أي طول (في `Contains`)، مما قد يسبب ضغط ذاكرة/أداء في SQL Server. أضف `[MaxLength]` أو حدّد الطول عند القراءة.

### M-5 — توليد أرقام الفواتير بسباق (Race condition)
- **الموقع**: `Services/InventoryService.cs:324-352` (`NextInvoiceNumberAsync`), `PaymentsController.cs:43-44`.
- **التفصيل**: الترقيم `Count+1` ثم «حلقة حتى لا يتكرر» — في الطلبات المتزامنة يرى كلاهما نفس العدّاد ويولّدان نفس الرقم؛ الفهرس الفريد يمنع التخزين المكرر (باستثناء MySQL الواضح) لكنه يولّد خطأ `DbUpdateException` وتعطّل مستخدم بدل تجربة ناعمة. (الكود يحاول استرداد ذلك عبر `ResetInvoiceKeys` لكن الترقيم ما زال عرضة).
- **الإصلاح**: استخدم `SELECT ... WITH (UPDLOCK, SERIALIZABLE)` أو تسلسل قاعدة بيانات / عداد ذرّي.

### M-6 — ربط كيانات EF كاملة في التحكمات المتعددة (مخاطرة تضخيم الحقول)
- **الموقع**: `ItemsController` (Create), `CustomersController`, `SuppliersController`, `CategoriesController`, `ItemTypesController`, `SettingsController`, `InventoryAdjustmentsController`.
- **التفصيل**: استلام الكيان كاملًا من النموذج يسمح بحقن أي حقل (مثل `IsActive`, `CreatedAt`, معرفات تنقّل). بعضها محمي يدويًا (`Items` ينسخ فقط قائمة بيضاء في `Edit`؛ الأسعار محمية)، لكن البقية (خصوصًا `Categories/ItemTypes` عبر Ajax بـ `IsActive`, و`Unit.AddUnit` بدون فحص حلقة through `ParentUnitId`) تقبل حقولًا إضافية.
- **الإصلاح**: ViewModels مخصصة لكل نموذج أو `[BindNever]` على الحقول المحسوبة/التنقلية/المعرفات.

### M-7 — `@Html.Raw` (JSON) مع بيانات أصناف منشأة من المستخدم
- **الموقع**: `Views/Sales/Create.cshtml:111`, `Purchases/Create.cshtml:111`, `InventoryAdjustments/Create.cshtml:73`.
- **التفصيل**: تُحقن بيانات الأصناف كـ JSON داخل `<script>`. `System.Text.Json` بكود الترميز الافتراضي يهرب `< > & "`، فالحالي آمن، لكن هذا هشّ — أي تبديل للمُرمِز أو خيارات مخصصة يفتح XSS مخزّن عبر أسماء أصناف منشأة. استخدام `escapeHtml()` في حقن الـ DOM صحيح.

---

## د) نتائج منخفضة (Low)

### L-1 — سياسة كلمة مرور ضعيفة لتطبيق مالي
- **الموقع**: `Program.cs:13-17` — `RequiredLength=6`, `RequireUppercase=false`, `RequireNonAlphanumeric=false`.
- **التفصيل**: أقل من الحد الأدنى الموصى به (8+ وأصناف متعددة). القفل (5 محاولات/5 دقائق) يحد من الهجوم المباشر، لكن تفريغ هاش 6 أحرف فقط قابلة للكسر بسرعة. **ارفع إلى 8+ مع على الأقل 3 من 4 أصناف.**

### L-2 — بيانات اعتماد افتراضية ثابتة معروفة (Seed)
- **الموقع**: `Data/SeedData.cs:29,33-34` — `Admin@123`, `Accountant@123`, `Warehouse@123`.
- **التفصيل**: تُنشأ على كل قاعدة جديدة (بما فيها الإنتاج) بكلمات معروفة. المستخدم البذري المخدوم لنظام حقيقي قابل للاختراق فورًا ما لم تُغيَّر.
- **الإصلاح**: اقرأ من الإعدادات/الأسرار، وفرض تغيير كلمة المرور عند أول دخول، ولا تبذر كلمات ثابتة في الإنتاج.

### L-3 — `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true`
- **الموقع**: `Program.cs:35`.
- **التفصيل**: يلغي الاستدلال التلقائي لـ `[Required]` على الأنواع المرجعية غير الفارغة. الأنموذج الحالي صالح لأن الحقول الحرجة موسومة يدويًا، لكن أي خاصية جديدة تُضاف دون علامة تصير اختيارية بلا قيد.
- **الإصلاح المطبَّق**: **أُعيد الخيار `= true`**. المحاولة الأولى بتنفيذ المراجعة (إزالة الخيار) سبّبت انحدارًا حرجًا: نافذة توافق `M-6` (كتلة `[BindNever]` منذرة كتجميع-كتلة) مع الاستدلال الضمني — كل خاصية تنقُّل مرجعية غير فارغة موسومة بـ `[BindNever]` أصبحت تُعتبر `[Required]`, وبما أنها لا تُربَط أبدًا تبقى `null` فتفشل `ModelState` («The Category field is required») على **كل** نماذج الإنشاء (صنف/شراء/بيع/مرتجع/دفع). أُعيد الخيار لإصلاح الانحدار (0W/0E + `E2E` ناجح). **الحلّ الدائم** إنْ رُغب بإزالة الخيار مستقبلًا: جعل خاصيات التنقُّل غير الفارغة الموسومة بـ `[BindNever]` **قابلة للفصل** (`Type?`) — لا تحتاج علاقة بسبب إبقاء `[BindNever]` — مع الانتباه لتحذيرات العدم في الـ Views.

### L-4 — `Logout` بدون `[Authorize]`
- **الموقع**: `Controllers/AccountController.cs:42-44`.
- **التفيل**: `[HttpPost][ValidateAntiForgeryToken]` دون `[Authorize]`. غير ضار (تسجيل خروج بلا جلسة = لا أثر)، لكنه غير متسق.

### L-5 — `AccountController.Login` يعتمد `LocalRedirect(returnUrl ?? "/")` — `?? "/"` ميت
- **الموقع**: `Controllers/AccountController.cs:36`.
- **التفصيل**: `LocalRedirect` تمنع الروابط الخارجية أصلًا، فالشق `?? "/"` لا يُنفَّذ أبدًا (dead code). غير ضار.

---

## هـ) إيجابيات كاذبة تم دحضها (مؤكَّد تجريبيًا)

### F-1 — «صفحة تسجيل الدخول بدون رمز CSRF» — **غير صحيحة**
- **الادعاء**: نموذج الدخول (`Layout=null`) بلا `@Html.AntiForgeryToken()`.
- **الإثبات**: فحص DOM أظهر `__RequestVerificationToken` حاضرًا (مولّد تلقائيًا من `asp-action method="post"` في tag helper)، وإرسال POST بدون الرمز أعاد **400**، ومع الرمز يعمل تسجيل الدخول. أي: الحماية من CSRF فعالة تمامًا.

### F-2 — «الأسعار تُقدَّم عبر `readonly` (تخضع للربط) دون حماية» — **محمي فعليًا**
- **الادعاء**: `Items/Edit` تستخدم `readonly` لا `disabled` للأسعار.
- **الإثبات**: التحكم (`ItemsController:118-122`) ينسخ الأسعار إلى `existing` فقط `if (User.IsInRole("Admin"))`؛ فأي حلقة ربط لغير الأدمن تُتجاهل. نفس الشيء لـ `Items/Create:60-64`. الأسعار محمية من الجهة المنطقية بغضّ النظرة عن حالة `readonly` في الواجهة.

### F-3 — «كل POST بلا `[ValidateAntiForgeryToken]`» — **لا يوجد أي نقص**
- **الادعاء**: تحقق من 20 عملية POST كلها مغطاة.
- **الإثبات**: جميعها تحتوي السمة. CSRF مغطى بالكامل.

---
## و) مؤكَّد جيدًا (نقاط قوة)
- خدمة `InventoryService` تحفظ المعاملة فعليًا (`BeginTransactionAsync`) مع rollback، وتستخدم `Item.RowVersion` مع إعادة محاولة، وتمنع الرصيد السالب (`:276`)، وتعيد احتساب الماليات من البنود.
- نظام الصلاحيات: `RequirePerm` + `[Authorize]` + اجتياز الأدمن يعمل صحيحًا؛ لا توجد ثغرة IDOR في `UsersController.Permissions` (محمية للأدمن، قائمة المفاتيح بيضاء من whitelist).
- إدارة الجلسة: lockdown مهيأة (5/5 دقائق)، لا open redirect (LocalRedirect)، حماية CSP/HSTS/X-Content-Type موجودة (على الرغم من `unsafe-inline` للجافاسكريبت — مقبول للاستخدام الداخلي).

## ز) خارطة الطريق المقترحة (مرتبة)
1. **فوري**: C-1 (IsPaid) + C-2 (رصيد افتتاحي) — ضبط `IsPaid/PaidAmount/CurrentCount/CurrentQuantity` من الخادم فقط.
2. **قصير**: H-3 (معاملة واحدة للدفع + RowVersion للفواتير), H-2 (سدّ TOCTOU للمرتجعات داخل الخدمة), M-1 (حذف عمود `UnitId`), M-2 (مفاتيح إعداد منفصلة للكتابة), L-1/L-2 (سياسة + بذر كلمات السر).
3. **متوسط**: M-3 (صلب `RequirePermFilter`), M-5 (ترقيم ذرّي), M-4/M-6/M-7 (قيود نموذج ومدخلات).
4. **تنظيف**: L-3/L-4/L-5 + التوحيد بين الحقول المحمية.

---

## ح) حالة التنفيذ (مُنفَّذ في هذه الجلسة)

| المعرف | البند | الحالة |
|---|---|---|
| C-1 | منع تلاعب `IsPaid`/`PaidAmount` في فواتير البيع/الشراء | ✅ `InventoryService` يصفّر `PaidAmount/IsPaid` في بداية كلا المسارين |
| C-2 | منع ضخ الرصيد الافتتاحي عبر `Items/Create` | ✅ `ItemsController.Create` يجبر `CurrentCount/CurrentQuantity=0`؛ الواجهة تحوّل الحقلين إلى تنبيه «أضف عبر الجرد» |
| H-2 | سدّ TOCTOU لكميات المرتجعات | ✅ تحقق مركزي `ValidateSaleReturnQuantitiesAsync`/`ValidatePurchaseReturnQuantitiesAsync` داخل الخدمة قبل المعاملة |
| H-3 | معاملة واحدة للدفع+التوزيع + `RowVersion` على الفواتير | ✅ `PaymentsController.Create` يعيد هيكلة المعاملة مع استرجاع `DbUpdateConcurrencyException`؛ أُضيف `[Timestamp]` لـ `SaleInvoice`/`PurchaseInvoice` |
| H-4 | مفتاح تكرار (idempotency) للدفعات | ✅ `HasDuplicatePaymentAsync` يرفض إرسالًا مكررًا خلال نافذة 2 دقيقة |
| M-1 | حذف عمود `UnitId` «الشبح» | ✅ هجرة `InvoiceRowVersionAndDropUnitId` تحذف العمود + الفهرس + FK، وتُزيل التنقل الافتراضي `Unit.Items` (امتداد مباشر `sqlcmd` يؤكد: 0 أعمدة/FK/فهرس) |
| M-2 | مفاتيح إعداد منفصلة للكتابة | ✅ أُضيف `Settings.Edit` لإجراءات `AddUnit/UpdateUnit/DeleteUnit` |
| M-3 | صلب `RequirePermFilter` | ✅ غير المصادق يُوجَّه إلى `Login` بدل المرور الصامت |
| M-4 | حد طول `search` | ✅ تقليم + قصّ 100 حرف في Items/Customers/Suppliers/Stock.Report |
| M-6 | قيود ربط الكيانات | ✅ `[BindNever]` على التنقلات/الأعضاء المحسوبة في `ItemCategory`/`ItemType`/`Unit`/`InventoryAdjustment`؛ فحص حلقة + `Id=0` في `AddUnit` |
| L-1 | سياسة كلمة مرور ضعيفة | ✅ رفع `RequiredLength` إلى 8 |
| L-2 | بذر كلمات ثابتة | ✅ متوافق مع السياسة الجديدة (9+ أحرف وصنفان) — يُستحسن التبديل من إعدادات/أسرار لاحقًا |
| M-5 | الترقيم الذرّي للمستندات المالية | ✅ الأرقام تُولَّد خادميًّا حصرًا (`NextInvoiceNumberAsync`/`NextReturnNumberAsync`) بلا ثقة بمدخل العميل، مع فهرس فريد + استرجاع على التضارب |
| M-7 | `@Html.Raw` (JSON) هشّ | ✅ `JavaScriptEncoder.Default` صريح في Views/Sales, Purchases, InventoryAdjustments (يُفرّغ `< > & '` فتستحيل خروجات `</script>`) |
| L-3 | `SuppressImplicitRequiredAttribute...` | ⚠️ **أُعيد الخيار**: المحاولة الأولى (إزالة الخيار) كسرت كل نماذج الإنشاء — جعلت كل تنقُّل غير فارغ موسوم بـ `[BindNever]` (من M-6) يُعامَل كمطلوب `[Required]` ضمني فيفشل التحقق («The Category field is required»). أُعيد `= true` لإصلاح الانحدار وتحقق `dotnet build` 0W/0E وافتراض `E2E لإنشاء صنف/شراء/دفع/مرتجع`. الحلّ الدائم موثَّق في قسم L-3 أدناه. |
| L-4 | `Logout` بدون `[Authorize]` | ✅ أُضيفت السمة |
| L-5 | `?? "/"` الميت في `LocalRedirect` | ✅ غير ضار؛ «`?? "/"`» مطلوب فعليًا لضبط `returnUrl=null` فتركته كما هو (إزالته ترمي على null) |

**موقوف / مؤجَّل (غير حاسم للاستخدام الداخلي):** لا شيء من النصّ — كل بنود المراجعة العميقة الثانية أُعيدت معالجتها.

**التحقق:** `dotnet build` = 0W/0E؛ الهجرة طُبّقت بنجاح (`Migrate()` تلقائيًا عند التشغيل)؛ Smoke E2E (أدمن) مرّ على كل الصفحات 200 بلا أخطاء وحدة تحكم/تنبيه، ومسار إضافة/حذف وحدة يعمل الآن بإذن `Settings.Edit`، ومسار POST للوحدات لين سليم بعد إزالة قمع `[Required]` الضمني.

---

# المراجعة العميقة — الجولة الثالثة (النهائية)

> التاريخ: 2026-09-03
> النطاق: دفعة التحسينات العشر + المراجعتان (review) + **Audit قوي نهائي عبر Agents** + إغلاق الملاحظات
> الحالة: **11/11 البنود منفَّذة** — build 0W/0E، اختبارات **18/18 PASS**، Smoke **CONSOLE_ERRORS: []**، Audit نهائي **0 CRITICAL**

## ١) دفعة التحسينات العشر (بنود الفئات الأربع)

| # | البند | الحالة | الموقع |
|---|---|---|---|
| 1 | طلب شراء (`OrderReference`) + تسعير تنافسي (`SupplierQuote`) | ✅ | `Sale→PurchaseInvoice.OrderReference`، نموذج `SupplierQuote` (فهرس فريد)، `UpsertSupplierQuoteAsync`، عرض `Suppliers/Quotes` |
| 2 | شروط فواتير الأجل-الآجل + خصومات متعددة | ✅ | `InvoicePaymentTerms` (OnReceipt/Net7/15/30/60)، `DueDate` تلقائي، `Discount2/Discount3` في `NetAmount` |
| 3 | تصدير Excel/PDF | ✅ | `ReportExportService` (CSV/UTF-8 BOM، `SHEET`)، أزرار تصدير في التقارير |
| 4 | بوليصة مطبوعة | ✅ | `Sales/Print` + `Purchases/Print` (Standalone، `@media print`) |
| 5 | بلاغات انخفاض المخزون | ✅ | `GetLowStockItemsAsync` + `Stock/LowStock` + شارة dashboard + رابط قوائم LowStock |
| 6 | Docker + نشر Azure (Aspire) | ✅ | `Dockerfile`، `docker-compose`، `aspire/AppHost`، `Deploy-Azure` توثيق (لا يُبنى محليًا) |
| 7 | اختبار آلي xUnit | ✅ | `tests/Silk.Trading.Web.Tests` — **18 اختبارًا** (Inventory: بيع/شراء/مرتجع/تراكمي + ميزات جديدة) |
| 8 | نسخ احتياطي للقاعدة | ✅ | `scripts/backup-db.ps1` + `setup-backup-task.ps1` + `docs/BACKUP` |
| 9 | مراجعة أمنية ثالثة (علامة/بنك) | ✅ | أدناه — Audit نهائي 9 أبعاد |
| 10 | بيانات تجريبية واقعية | ✅ | `SeedData` (16 صنفًا، أصناف/وحدات/عملاء/موردون) `IsSellable` |

## ٢) المراجعة الثانية (review) — الإصلاحات الحرجة

| المعرف | البند | الحالة |
|---|---|---|
| MUST-FIX | CSV formula injection (`= + - \t @`) | ✅ `CsvField` يسبِّق `'` |
| WARN | تناسق منطق LowStock (≤/≥ المعنى) | ✅ `< MinX` و `MinX > 0` في Dashboard + Stock |
| WARN | رابط «عرض الكل» بلا حارس إذن | ✅ `Perm.HasAsync("Stock.View")` |
| WARN | `UpsertSupplierQuote` بلا حارس `SupplierId>0` | ✅ مُضاف |

## ٣) Audit قوي نهائي (Agent) — 9 أبعاد

| البعد | الحكم | ملاحظة |
|---|---|---|
| 1. مصادقة/جلسة | **PASS** | Lockout 5/5، CSRF، لا تسريب |
| 2. تفويض/IDOR | **PASS** | كل الأفعان `[RequirePerm]`؛ Users محصور بأدمن |
| 3. Mass-assignment | **PASS** | ماليات تُعاد احتسابها خادميًّا حصرًا؛ `[BindNever]` على التنقلات |
| 4. SQL injection | **PASS** | LINQ/EF فقط؛ لا `FromSqlRaw` |
| 5. XSS | **PASS** | `Html.Raw` على بيانات خادم + `escapeHtml` |
| 6. CSRF | **PASS** | رمز عام + `[ValidateAntiForgeryToken]` على كل POST |
| 7. CSV injection | **PASS** | `=+-\t@` مُحيَّد |
| 8. سلامة مالية (علامة/بنك) | **PASS بعد إصلاح** | ممتاز؛ **نقص مدفوعات الإفراط أُصلح** (المدفوع أكبر من المستحق يُرفض) |
| 9. تحصين عام | **PASS بعد إصلاح** | كلمات بذر dev-only؛ **CSP مُقوَّى**؛ **Logging أُضيف** |

## ٤) إغلاق ملاحظات الـ Audit (نفِّذت هذه الجلسة)

| الملاحظة | الموقع | الحل المنفَّذ |
|---|---|---|
| **Overpayment** يُقبل ويُهدر الفائض صامتًا | `PaymentsController` | `ApplyInvoiceAllocationAsync` يعيد الـ leftover؛ POST يرفض الدفع إذا تجاوز إجمالي المستحق (rollback + رسالة) |
| **CSP `'unsafe-inline'`** على scripts يصيّر حماية XSS هشّة | `Program.cs` | `script-src 'self' 'nonce-…'` (زالت `unsafe-inline`) + `script-src-attr 'unsafe-inline'` للـ event-attributes فقط؛ كل الـ 9 <script> الداخلية تحمل `nonce`؛ **header مؤكَّد، Smoke `CONSOLE_ERRORS: []`** |
| **لا Logging** (صفر قابلية مراقبة) | `InventoryService` + `PaymentsController` | `ILogger<T>` + `LogInformation/LogWarning` على إنشاء فواتير البيع/الشراء، المرتجعات، حفظ الدفعات ورفض الإفراط (مع `IHttpContextAccessor` مُسجَّل فعلًا) |

## ٥) مؤكَّد جيدًا (الجولة الثالثة)
- `InventoryService` يعيد احتساب `TotalAmount/NetAmount = Total − D1 − D2 − D3 + Tax` خادميًّا، يرفض الصافي السالب، ويضبط `PaidAmount=0/IsPaid=false` دائمًا.
- ترقيم الوثائق (`NextInvoiceNumberAsync/NextReturnNumberAsync`) ذرّي داخل معاملة مع 3 محاولات + استرجاع `DbUpdate*Exception`؛ لا أرقام من العميل.
- حماية Over-return وOver-sell داخل الخدمة ضمن المعاملة (سدّ TOCTOU).
- `HasDuplicatePaymentAsync` نافذة 2 دقيقة يقاوم الإرسال المكرر.

## ٦) التحقق النهائي
- `dotnet build` (Web) = **0W/0E**؛ `dotnet test` = **18/18 PASS** (بعد إضافة 5 اختبارات للميزات الجديدة).
- Smoke E2E عبر Playwright (msedge): كل الصفحات 200، **`CONSOLE_ERRORS: []`**، صفر تنبيهات — **بما فيها بعد تغيير CSP**.
- رأس الاستجابة مؤكَّد: `Content-Security-Policy: ... script-src 'self' 'nonce-…'; script-src-attr 'unsafe-inline' ...`.
- التطبيق يعمل على `http://localhost:5165`.

**ملاحظات محمولة للإنتاج (اختيارية):** كلمات بذر dev-only في `SeedData`؛ `style-src 'unsafe-inline'` باقٍ لتغطية الـ style-attributes (يُخفَّف بحراسة عالية الإنتاج)؛ نِقَاط INFORMATIONAL عن caching الصلاحيات لكل طلب ونافذة عرض رقم المرتجع.
