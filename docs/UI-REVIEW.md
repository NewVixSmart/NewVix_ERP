# مراجعة الواجهة — New Vix Smart

> مستند عمل غير ملتزم. الغرض منه: تقرأه وترشّح «أحتاج أغيّر هذا البند تحديدًا» بعلامة ملاحظة أو ردّ،
> وأنا أنفّذ ما تختاره بالضبط. لا يوجد قبل أي تعديل — هو مجرّد جرد ومواقف بقرارات مفتوحة.

- الحالة: **مراجعة استكشافية شاملة** (125 قالب، 3452 سطر `site.css`، 1156 سطر `dark-theme.css`، 1273 سطر `site.js`).
- كل بند فيه: **الدليل** (ملف/سطر) حتى تعرف «بيشاور على إيه» قبل أن تقرر.

---

## 1. الحالة الجيدة فعليًا (لا داعي للمسها إلا برغبة)

| الناحية | الدليل |
|---|---|
| إتاحة WCAG 2.2 AA مفعّلة ومقاسة (99 فاتح + 13 داكن، 0 خطيرة) | `ACCESSIBILITY.md`, `e2e/a11y-gate.cjs` |
| كل جدول له `<caption>` وكل `<th>` له `scope` (109 ↔ 109) | الجرد الكامل للجداول |
| RTL كامل `lang="ar" dir="rtl"` + خط Cairo + Bootstrap RTL | `_Layout.cshtml:2,11,13` |
| ثيم فاتح/داكن/نظام مع `prefers-color-scheme` ومنع وميض (FOUC) | `_Layout.cshtml:18-45`, `site.js:800-842` |
| جانب شريط قابل للطي + ثمانية (rail) + موبايل off-canvas مع `inert` وتركيز | `site.css:1059-1590`, `site.js:16-97` |
| تنبيهات عالمية `TempData` وقابلية إغلاق، و skip-link | `_Layout.cshtml:49,521-534` |
| شبكة أذونات دقيقة لكل رابط قائمة | `_Layout.cshtml:78-463` |
| كل مستندات الطباعة مستقلة `Layout=null` بثيم ورقي خفيف حتى في الداكن | `PrintDocument.cshtml`, `dark-theme.css:814-1013` |

---

## 2. مشكلات حقيقية — تحتاج قرارك

### 2.1 ترقيم الصفحات: 6 شاشات فقط من أصل ~26 قائمة
- المفهرسة (50/صفحة عبر `_Pager`): `Sales, Customers, Accounts, Payments, PurchaseReturns, Stock`.
- **الباقي بلا ترقيم إطلاقًا** ويعرض الكل: `Items, Suppliers, Warehouses, Users, SalesOrders, SalesQuotes, SaleReturns, Purchases, PurchaseOrders, PurchaseRequests, InventoryAdjustments, StockTransfers, StockReservations, DeliveryOrders, DeliveryIssues, Stock\Report, Stock\LowStock, Budgets, Fiscal`.
- الأثر: شاشة فيها 5000 صنف = صفحة واحدة ثقيلة. ادلة: `_Pager` مستخدم في 6 ملفات فقط (`Accounts\Index.cshtml:103`...)، والبنية جاهزة (`PagerExtensions.PageSize=50`).

**قراري:** توحيد ترقيم كل قوائم الـ20 الباقية بالسيرفر؟ نعم/لا — وهل نفس حجم الصفحة (50) أم صغير؟

### 2.2 الأنماط الخمسة للحالة الفارغة «لا توجد بيانات»
نفس المعنى بخمس صِيغ مختلفة:
1. `alert alert-info` — `DeliveryIssues\Index.cshtml:34`، `SalesQuotes\Index.cshtml:30`، ...
2. سطر داخل الجدول `text-center text-muted py-4` — `Accounts\Index.cshtml:97`، `Reports\Aging.cshtml:87`، ...
3. `<p class="mt-2">` أسفل أيقونة `font-size:3rem` — `Stock\Index.cshtml:68`، كل تقارير `Reports\*`، ...
4. صنف `.empty-state` (مُعرَّف في `site.css:2197`) — **مستعمل في 3 ملفات فقط**: `Home\Error.cshtml:6`, `Items\Index.cshtml:105`, `Backup\Index.cshtml:76`.
5. `text-muted small p-3` — `Batch\Index.cshtml:56,87`.

**قراري:** توحيد على صيغة واحدة (المقترح: `.empty-state` الموجود، أو تصميم جديد موحّد)؟ أيّها يعجبك: (أ) الحالي `.empty-state` (ب) تصميم جديد كليًا (ج) إبقاء التنوع؟

### 2.3 تنسيق الهيدر والوصول إلى القوائم
- 94 صفحة تستخدم `.page-header` — و3 صفحات خارج النمط: `Settings\Branding.cshtml:5-10`, `Settings\Printing.cshtml:19-30` (differ-wrap بـ`h3` وزر رجوع `btn-sm`)، و`PurchaseOrders\Edit.cshtml` بلا هيدر.
- أزرار العودة نصوص/أيونات متضاربة: «رجوع» (معظم Details)، «عودة» (`Budgets\Manage.cshtml:6`)، «كل التقارير» (9 تقارير)، «العودة للإعدادات»، «العودة لمركز الاستيراد»، واثنان **بلا أيقونة** (`DeliveryIssues\Details.cshtml:13`, `StockReservations\Details.cshtml:13`).

**قراري:** توحيد زر الرجوع (نص + أيقونة واحدة لكل السياقات)؟ وهل رفع الـ3 شواذ لـ`.page-header`؟

### 2.4 أزرار الطباعة وألوانها المتباينة بين شاشات متشابهة
- `btn-outline-success` — `Sales\Details.cshtml:12`, `Purchases\Details.cshtml:8`
- `btn-outline-primary` — `SalesOrders`, `DeliveryOrders`, `PurchaseOrders`, `SaleReturns`, `PurchaseReturns`, `SalesQuotes` (6 صفحات)
- `btn-outline-secondary` — `DeliveryIssues\Details.cshtml:14`
- و`rel` التهديد: `noopener noreferrer` في 3 → `noopener` في 5 → **بلا rel** في `DeliveryIssues\Details.cshtml:14`.

**قراري:** توحيد (لون + `rel="noopener noreferrer"`)؟

### 2.5 تكرار رمز مكافحة التزوير (Antiforgery)
- `_Layout.cshtml:48` يبثّ رمزًا عامًا **و** `asp-action post` يبثّ تلقائيًا **و** 45 موقعًا في القوالب يبثّونه يدويًا ثالثًا (مثال `Settings\Index.cshtml:42,130,156,173,212,230` — 6 مرات في صفحة واحدة، `Backup\Index.cshtml:18,58,63,92`).

**قراري:** تنظيف الـ45 موقعًا الزائدة (سلوكية لا بصرية)؟

### 2.6 `aria-hidden` على الأيقونات غير منتظم
- 669 أيقونة `<i class="bi">` و164 فقط عليها `aria-hidden="true"`. الـlayout متّسق؛ صفحات المحتوى غالبًا محذوفة (`Sales\Index.cshtml:6`...).

**قراري:** إضافة الجملة برمجيًا عبر `site.js` (للمهبلين حاليًا) أم تحريرها في القوالب؟

### 2.7 ألوان hex داخل القوالب — خطر الوضع الداكن (وخاصة شاشات الإعدادات)
مواقعها `Branding.cshtml:141-221` و`Printing.cshtml:185-200,356` (إعدادات ألوان الطباعة) — مقبولة كنمط تصميمي، لكن:
- **اكتشاف مهم:** `BrandingService.RenderThemeCss()` (سطر 188-221) يبثّ `:root{--color-primary…}` بلا scope، و`dark-theme.css` يكتب فوقها بـ`:root[data-theme="dark"]` الأعلى أولوية — أي **إعدادات العلامة (Branding) لا تسري في الوضع الداكن إطلاقًا**. تحقق: أنشئ في Branding لونًا مميزًا ثم بدّل داكن → لا أثر.
- صفحة `Branding.cshtml:169-172` فيها عينات ألوان `background:#f8f8fa` إلخ ستظهر مربعات بيضاء/غامقة في الداكن.

**قراري:** (أ) إصلاح تعارض Branding مع الداكن (نطاق الـ`:root` بنطاق `[data-theme]` + تغطية الداكن)، (ب) تلوين عينات Branding بأصناف دلالية، (ج) تجاوز الآن؟

### 2.8 الجداول: 11 مجموعة أصناف و`table-container` مفقود في 28 ملفًا
- مجموعات `table…`: الأغلب `table table-hover mb-0` (70) — وشواذ متفرقة (`table-striped` في 2 فقط، `data-table-plain` في 3).
- الجداول غير المغلفة بـ`.table-container`: `Settings\Index`, `Categories\Index`, `ItemTypes\Index`, `Fiscal\Index`, `Home\Index`, `Reports\{Index,Aging,CashFlow}`, `Suppliers\Ledger`, `ImportCenter\Preview`, `Items\Details`, `Payments\Details`, كل جداول الأعمال اليدوية (Create grids)...

**قراري:** توحيد الصنف المغلف والـwrapper لكل القوائم؟ وبالنسبة للشواذ إن كانت مقصودة (إحكام التصميم) أعلنها وأبقيها.

### 2.9 شوارد CSS/JS متبقية بلا مستهلك
- CSS ميّت: `.auth-card, .login-aura, .aurora, .motes, .top-bar, .print-toolbar, .print-footer, .print-header-logo` وسطر `site.css` `.print-doc` (3268+) = 0 مستخدم في أي قالب.
- `site.js` كل دواله مستخدمة (لا dead JS) — ممتاز.
- `layout`/`auth.css` تضاعف نسخة السكربت الرأسي للثيم في `Login.cshtml:18` و`AccessDenied.cshtml:17` (مقبول لتجنب الوميض لكنه نسخ يدوي).

**قراري:** حذف الشوارد (نظافة)؟ أم إبقاء لأن «برنامج قادم» (جرّها فعلاً — عتبة قرار).

### 2.10 مصطلح متضارب واحد
- `Home\StatusCode.cshtml:22` «لوحة القيادة» مقابل «لوحة التحكم» في `_Layout.cshtml:82,307` و`Home\Index.cshtml:3`.

**قراري:** توحيد على أيّ الاسمين؟

---

## 3. أسئلة مفتوحة للتجربة البصرية (اختياري)

1. **كثافة الشريط والـtable-density**: توجد `vix-table-density` وتعمل — هل تريدها مفتوحة افتراضيًا أم مضغوطة؟
2. **أعمدة الجداول الفائضة**: الصق إحكام `overflow-x` عند ≤320 حالي؛ صفحات بـ12+ عمود (`SalesOrders`, `DeliveryOrders`) قد تحتاج وضع عمود شاشات الأعمدة (column chooser).
3. **حركة لوحة القيادة**: `stat-card` مع `count-up` و`pulse` — مقبولة؟ أم تريد تقليلها؟
4. **لوحة التحكم الرئيسية** (`Home\Index`): تصميم `.dash-hero` بمقياس سدسي — قابلة لإعادة التصميم كلية في مرحلة منفصلة لو رغبت.

---

## 4. كيف يُقرأ المستند لردّك

ردّك المثال:
> «2.1: نعم وحد تلقائيًا بحجم 50. — 2.2: (أ). — 2.7: إصلاح (أ). — 3.1: مضغوط.»

أو أضف ملاحظتك (مثال: «2.4: وحدهم كلهم accent primary»). البنود التي لا تردّ عليها تُترك كما هي دون مساس. كل بند مستقل — أنفّذ المختار فقط باختبارات وبوابات كاملة.
