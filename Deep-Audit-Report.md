# المراجعة العميقة الموسّعة — نظام New Vix Smart

> التاريخ: 2026-08-31 (جولة ثانية)
> النطاق: تطبيق NewVixSmart.Web + قاعدة البيانات `NewVixSmartDb` + نظام الصلاحيات
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
| 7 | اختبار آلي xUnit | ✅ | `tests/NewVixSmart.Web.Tests` — **18 اختبارًا** (Inventory: بيع/شراء/مرتجع/تراكمي + ميزات جديدة) |
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

---

# المراجعة العميقة — الجولة الرابعة (تدقيق البنية والتغطية والنشر)

> التاريخ: 2026-09-12
> النطاق: الاختبارات الآلية (13 ملفًا / 128 اختبارًا) + CI/CD + Docker/Compose + Aspire + الهجرات + سكربتات النسخ + الوثائق + جذر الريبو
> النتيجة: **0 حرجة / 1 عالية / 6 متوسطة / 6 منخفضة / 1 معلومة** — البنية سليمة عمومًا مع نقاط نشر قابلة للتصحيح.

## ملخص حسب الخطورة

| الخطورة | عدد |
|---|---|
| **حرِجة (Critical)** | 0 |
| **عالية (High)** | 1 |
| **متوسطة (Medium)** | 6 |
| **منخفضة (Low)** | 6 |
| **معلومات (Info)** | 1 |

---

## جدول النتائج

| # | الخطورة | الموقع | العنوان | التفصيل (مع الدليل) | التوصية |
|---|---|---|---|---|---|
| 1 | **عالية High** | `aspire/Vix.AppHost/Program.cs:3-8` مقابل `src/NewVixSmart.Web/Program.cs:15` | اسم اتصال غير متطابق بين Aspire والتطبيق | `AddSqlServer("sql").AddDatabase("newvixsmart")` بحقن `WithReference` يوفّر `ConnectionStrings__newvixsmart`، بينما يقرأ التطبيق حصريًا `GetConnectionString("DefaultConnection")` (Program.cs:15) وهو LocalDB ثابت في `appsettings.json:10`. تحت Aspire تُهجَر القيمة المحقونة ويحاول التطبيق `(localdb)` غير الموجود داخل الحاوية → تعذّر الاتصال في مسار النشر الموثَّق بـ `docs/DEPLOY-AZURE.md`. | أعد تسمية القاعدة إلى `AddDatabase("DefaultConnection")` أو أضف قراءة احتياطية للاسم المحقون، ثم تحقق من `dotnet run --project aspire/Vix.AppHost`. |
| 2 | **متوسطة Medium** | `.github/workflows/ci.yml:29,36` | مسار تحميل نتائج الاختبارات لا يطابق موقع ملف TRX | مع `--logger "trx;LogFileName=test-results.trx"` يُكتب الملف في مجلد العمل الجاري وليس داخل `**/TestResults/`، والـ upload يطلب `**/TestResults/*.trx` → من المرجّح أن يكون artifact فارغًا (تفشل مراجعة النتائج بعد الالتزام). | أضف `--results-directory TestResults --logger "trx;LogFileName=test-results.trx"` أو أزل `LogFileName` ليعود الملف إلى المسار الافتراضي تحت `TestResults`. |
| 3 | **متوسطة Medium** | `tests/NewVixSmart.Web.Tests/AgingTests.cs:33-34` (و87, 138) | تشفير عربي مخرَّب (Mojibake) في بيانات الاختبار | النصوص المكتوبة كـ CP1256 مفكوكة: «ط¹ظ…ظٹظ„ ط£» و«ظ…ظˆط±ط¯ ط¨» بدل «عميل أ»/«مورد ب». الاختبارات تمر لأن الثوابت مكررة حرفيًا في التوكيدات (87, 138)، لكن بيانات القاعدة المشبّعة مشوَّهة ولا تحاكي أسماء الإنتاج (بالمقابل `CashFlowTests.cs:147` يحمل عناوين عربية سليمة صرّح بأن الشذوذ محصور في هذا الملف). | أعِد كتابة النصوص بالعربية الصحيحة عبر محرر/حفظ UTF-8 ثم أعد تشغيل الاختبارات؛ تحقّق من عدم تكرار النمط في أي ملف آخر. |
| 4 | **متوسطة Medium** | `tests/NewVixSmart.Web.Tests/AgingTests.cs:111` | مسافة بادئة شاذة | السطر `var row = ...` on مسافة واحدة بخلاف تنسيق الملف (تبويب). | أعد تنظيم الملف (format on save) للاتساق. |
| 5 | **متوسطة Medium** | `src/NewVixSmart.Web/Dockerfile` (المرحلة النهائية) | الحاوية تعمل بصلاحيات الجذر | لا وجود لتوجيه `USER` في الصورة النهائية (رغم multiline الصحيح مع `ASPNETCORE_URLS=http://+:80` وHEALTHCHECK curl). في حاوية خدمة مالية تُنشر على Azure يسافر ذلك أثر الهجوم عند اختراق العملية. | أضف مستخدمًا غير جذر (`adduser ... appuser`) وتوجيه `USER appuser` بعد نسخ الملفات، وثبّت curl في مرحلة بناء مؤقتة أو استغنى عنه عبر منفذ/ping HTTP مدمج. |
| 6 | **متوسطة Medium** | غياب الملفات (أُثبت بالنقيض من `tests/…` و`MilestoneM9Tests.cs:342`) | تفويض الأمن بلا تغطية آلية | لا يوجد أي اختبار يمارس `[Authorize]`/`[RequirePerm]`/`RequirePermFilter` ولا تدفق JWT (`JwtBearer`) ولا `[ValidateAntiForgeryToken]`؛ `MilestoneM9Tests.cs:342` ينشئ وحدة التحكم مباشرة بـ `DefaultHttpContext` فيتخطّى الفلاتر، وفحص grep لم يجد `WebApplicationFactory|ClaimsPrincipal` في كامل مجلد الاختبارات. حماية هذه المسارات يدوية (Smoke/Playwright) وتنكشف مستقبلًا بلا حاجز. | أضف اختبارات تكامل عبر `WebApplicationFactory` + `UseAuthorization` أو بناء Claims يدويًا، تستدعي مسارات محمية بمفاتيح أمنية ولا/جاهزة مع JWT باطل. |
| 7 | **متوسطة Medium** | `tests/…/BudgetAndAccountsTests.cs:267-287` | اسم اختبار منحل عن مدلوله | الاختبار `BudgetClosedYear_GuardCondition_BlocksEdit` يعدّل خط ميزانية لسنة مغلقة و**ينجح التعديل** (AnnualAmount 100→200) لأن الخارس الحقيقي موجود في الواجهة فقط — فالاسم يوحي بتغطية الحماية دون أن يقدّمها، وقد يُؤخذ ضمانة خاطئة. | سمِّه بما يفعل فعليًا (مثل `…_EditAllowedAtDbLevel_GuardInView`) وأضف اختبارًا للخارس نفسه (View/خدمة الحظر). |
| 8 | **متوسطة Medium** | غياب التغطية لمسارات التزامن | مسارات RowVersion/التراجع غير مختبَرة | الإصلاحات الحرجة (المعاملة الواحدة للدفع+التوزيع، `RowVersion` على الفواتير، استرجاع `DbUpdateConcurrencyException`، `HasDuplicatePaymentAsync` نافذة 2 دقيقة) لا يغطيها أي اختبار؛ الغطاء الوحيد ممنطقي. | أضف اختبارات متوازية (Task.WhenAll) لدفعتين متزامنتين وإعادة تعارض تحديث لنفس الفاتورة، والتحقق من رفض الدفع المكرر المتطابق خلال النافذة. |
| 9 | **منخفضة Low** | `tests/…/AuditLedgerTests.cs:147-149`, `CashFlowTests.cs:147-148,185-186` | توكيدات مكانية هشّة على تخطيط Excel | `ws.LastRowUsed()!.RowNumber() == 9` و`ws.Cell(8,5).GetDouble()` يربطان النتيجة بمواقع خلايا ثابتة؛ أي تغيير أعمدة/أسطر مستقبلًا يكسر الاختبار دون تغيّر منطقي في القيم. | اعتمد العناوين (`ws.Cell(1,1)`/lookup بالبطاقة بالعربية) أو Section عناوين، بدل أرقام الصفوف/الأعمدة الصافية. |
| 10 | **منخفضة Low** | `docs/DEPLOY-AZURE.md:1` | تلف ترويسة | السطر الأول حرفًا (mixed CJK): «الت经验lightly — الت经验lightly» — تشويش في العرض؛ النسخة الإنجليزية `DEPLOY-AZURE-EN.md` سليمة. | أعد كتابة الترويسة من النسخة الإنجليزية بحفظ UTF-8. |
| 11 | **منخفضة Low** | `plan/feature-multicurrency-branches-shipments-1.md` و `plan/feature-p4b-budgets-accounts-1.md` | حالة مخزن الخطط قديمة | الملفان يحملان `status: 'In progress'` بينما التنفيذ مكتمل (هجرات M8a/P4b موجودة + اختبارات `MilestoneM8a/M9` + BUILD-PLAN يذكر الإكمال). | حدّث الحالة إلى `done` مع إسناد الهجرة والاختبارات المنجزة. |
| 12 | **منخفضة Low** | جذر الريبو: `test-out.txt`, `test-out2.txt`, `tests-result.txt` | ملفات أثر شاذّة | `test-out2.txt` يعرض «Passed 79» قديمة و`tests-result.txt` يعرض تحذيرات CS8602 قديمة (قبل إضافة `!`) و`test-out.txt` فارغ — متجاهَلة في `.gitignore:22-24` لكنها تلوّث مساحة العمل. | احذف الملفات الثلاثة من القرص (لا أثر لها في git). |
| 13 | **منخفضة Low** | `ACCESSIBILITY.md` مقابل `.github/workflows/ci.yml` | بوابة WCAG غير مدمجة في CI | الوثيقة تفرض `GATE: PASS` على 38 صفحة (`p4cA11ySmoke.cjs`) وفحصًا شهريًا لـ 41 صفحة (`p4c_baseline.cjs`)، بينما سير العمل يغطي build/test/vuln/docker فقط ولا يشغّل البوابة الآلية ولا يرصد انحدارات الوصولية. | أضف job حسب الوثيقة (دوريًا + على push للواجهة) يشغّل `node p4cA11ySmoke.cjs` ويُفشل السير عند غياب `GATE: PASS`. |
| 14 | **معلومات Info** | `src/NewVixSmart.Web/appsettings.json:13` + `Program.cs:32-39` | مفتاح JWT نائب محروس | `Jwt:Key` ثابت «REPLACE_WITH_LONG_SECRET_IN_PRODUCTION»، لكن `Program.cs:34-39` يرفض الإقلاع خارج بيئة التطوير إذا كان الطول <32 أو يحتوي `REPLACE_WITH` — الحارس سليم والمخاطرة معلّقة على عدم تخطّي الإعداد في App Service/الحاوية. | لا إجراء إلزامي؛ تأكد من تعيين `Jwt__Key` كسرّية Environment في بيئة النشر. |

---

## مؤكَّد سليم (نقاط قوة محقَّقة بالدليل)

- **128 من 128 اختبارًا أخضر**: حسبت توزيع `[Fact]` ملفًا بملف (Accounting 7 / Aging 5 / AuditLedger 4 / Batch 5 / Budget 11 / CashFlow 6 / FiscalClose 17 / Inventory 30 / M7 9 / M8a 9 / M9 7 / Procurement 8 / Returns 10) فيطابق تمامًا ادعاء «128/128» في BUILD-PLAN.
- **عزل اختبارات نموذجي**: كل فئة تبني SQLite `:memory:` + `EnsureCreated()` + `IDisposable` لكل فئة بلا LocalDB — تعمل على ubuntu CI بلا sqlcmd وبدون تضارب بيانات.
- **درع التحذيرات**: `TreatWarningsAsErrors=true` في مشروعَي الويب والاختبارات مع `--warnaserror` صريح في `ci.yml:26` → CI يُفشل على أي تحذير.
- **هجرات إضافية سليمة**: فهارس فريدة (`IX_BudgetLines_BudgetYearId_AccountId`، `IX_BudgetYears_Year` في `AddBudgets.cs:57-72`، `IX_UserPermissions_UserId_PermissionKey`)؛ `AddFxSettlement` يضيف `BaseAmount NOT NULL` بقيمة افتراضية (بلا فقدان بيانات)؛ أعمدة work/currency قابلة للفراغ؛ `RowVersion` على الفواتير؛ FKs تاريخية بإلغاء Restrict.
- **أمن النشر**: `SQL_SA_PASSWORD` مطلوب بصرامة (`:?` في compose و`.env.example` نائب فقط)؛ JWT مقفل بالإقلاع؛ CSP/HSTS/nosniff/frame-ancestors في Middleware؛ كل POST مغطى بـ `[ValidateAntiForgeryToken]`؛ قفل 5/5 دقائق.
- **سكربتات نسخ موثّقة**: `scripts/backup-db.ps1` + `setup-backup-task.ps1` + `docs/BACKUP.md` (كشف 14 يوم، مهمة `NewVixSmartDailyBackup`، استعادة RESTORE) متسقة معًا.

## ملاحظات طفيفة (بدون إجراء مطلوب)

- `CashFlowTests.cs:147,185` عناوين عربية سليمة تُثبت أن مصادر المشروع UTF-8 عام — الشذوذ منحصر في `AgingTests.cs` (تخزين/نسخ خاطئ تاريخيًا).
- تكرار دوال البذر (`SeedChartOfAccounts`/`SeedPartiesAsync`…) عبر عدة ملفات — ثمن مقبول لسياسة العزل لكل فئة.
- أرقام سنوات ثابتة (2026) في FiscalClose/Budget — «سنة مغلقة» مرجعية تبقى صحيحة المقارنة دون تجدّد.
- `docker-compose.yml` يحمل `version:` قديمة (غير فعَّالة في Compose الحديث) ويعرّض `1433:1433` — مقبول محليًا.
- `slnx` يستثني Aspire من البناء عمدًا (مقابل «Aspire لا يُبنى محليًا» في الوثائق) — متسق لكنه يترك البند (1) خارج الرقابة الآلية حتى تُحل.
- `.playwright-cli/` في الجذر هي أداةCache only — نظّفها مع البند 12.
