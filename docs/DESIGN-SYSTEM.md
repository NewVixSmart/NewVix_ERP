# New Vix Smart — نظام التصميم (Design System) v1.0 — Light-only

> **بوابة §14 من `UI-EXECUTION-MANDATE.md`** + **إعادة التصميم الكاملة 2026-10** —
> هذا المستند يصف النظام الحالي: **ثيم فاتح واحد فقط**، حُذف الثيم الداكن نهائيًا
> (`dark-theme.css`، `_ThemeScript.cshtml`، سمات `<html>`، مفاتيح `data-theme-*`).
> القيم أدناه مطابقة لـ `wwwroot/css/tokens.css` الحالي.

## 1. قرارات اعتمدها المستخدم (تحصيل §0)

| القرار | النتيجة المعتمدة |
|---|---|
| 2.5 (antiforgery) | توثيق أن توكنًا واحدًا لكل `<form method="post">` مطلوب وظيفيًا؛ حذف التكرار الفعلي فقط (نفس النموذج/صفحات بلا إرسال). **لا** تحويل شامل لـ header token. |
| 2.1 (قائمة الـ20) | اعتماد القائمة المقترحة: الـ6 الحالية (Sales, Customers, Accounts, Payments, PurchaseReturns, Stock) + 14 جديدة (Items, Suppliers, PurchaseOrders, PurchaseRequests, SalesQuotes, SalesOrders, DeliveryOrders, SaleReturns, Warehouses, Categories, ItemTypes, Budgets, InventoryAdjustments, StockTransfers). وسم `data-server-paged` جديد في site.js لي opt-out من محرك client-side. |
| 3.1 (كثافة الجداول) | الآلية منجزة أصلًا (site.js `initTableDensity` + زر `[data-table-density-toggle]` في `_Layout:507`)؛ نعيد التمويل (styling) فقط عبر tokens ولا نلمس JS. |
| نطاق P4 | Home (Dashboard 3.4) + Sales (Index/Details) تجريبيها المرحلة الأولى؛ الباقي في P5. |

## 2. تصحيحات التوكنز بعد فحص التباين (مقابل الماندايت §3)

فحص AA على الأسطح الحقيقية أثبت فشل 3 قيم ماندة؛ التصحيح بداخل سلالة اللون نفسها مع المحافظة على الترتيب الهرمي:

| التوكن | قيمة الماندايت | القيمة النهائية | السبب |
|---|---|---|---|
| `--text-faint` (فاتح) | `#98A1AE` (2.61:1) | **`#5F6E85`** (≈5.2:1 على الأبيض) | تحت 4.5:1 على كل أسطح الوضع الفاتح |
| `--border-control` (جديد) | — | **`#7E8CA3`** (3.4:1 على الأبيض) | حواجز حقول الإدخال تحتاج 3:1 (WCAG 1.4.11) |
| `--accent` (فاتح) | `#B08A2E` (3.22:1) | كما هي (3.22:1) | زخرفي فقط — ممنوع حمل نص بشريا عليها؛ النص عبر `--accent-strong` |
| `--warning` (فاتح، خلفية badge) | `#9A6700` على `#FFF4D6` = 4.44:1 | **`#8F5F00`** (5.04:1 على badge) | badge نص صغير غير كبير (large text) |

> **حُذف**: صفوف التصحيح الداكنة (`--text-faint` داكن، `--primary-hover` داكن) — لا وضع داكن بعد إعادة التصميم.

ضوابط الاستخدام النهائية:
- `--text-faint` للتعليقات/الطوابع الأقل أهمية فقط، بشرط ألا يكون هو الناقل الوحيد لمعلومة حرجة.
- `--accent` للنقاط والخطوط والخلفيات الزخرفية؛ أي نص نحاسي (Brass) عبر `--accent-strong`.
- `--on-primary` أبيض؛ مع `--primary-hover` يبقى تباينه كافيًا على النص الأبيض.

## 3. التوكنز النهائية

### 3.1 الوضع الفاتح (`:root` — النطاق الوحيد)

```
--bg                : #F4F6FA   خلفية الصفحة
--surface           : #FFFFFF   البطاقات/الجداول/الـSheet
--surface-2         : #F9FAFC   صف الزيبرا/المناطق المدمجة
--surface-elevated  : #FFFFFF   حوارات/القوائم المنبثقة (ظل)

--border-subtle     : #E9EDF3
--border            : #DCE2EB
--border-strong     : #C3CCD9   فاصل الجدول تحت الرأس
--border-control    : #7E8CA3   حواجز حقول الإدخال والأزرار المحددة (3:1)

--text              : #101B2C
--text-secondary    : #3E4A5C
--text-muted        : #667285
--text-faint        : #5F6E85   ← معدَّل AA

--primary           : #12345E   Navy (لون الهوية الأساسي)
--primary-hover     : #1B4177
--primary-active    : #0C2749
--on-primary        : #FFFFFF
--primary-subtle    : #EAF1FB   خلفية تفعيل القائمة/الحشوات

--accent            : #B08A2E   براس (زخرفي)
--accent-strong     : #8C6D1F   نص براس (AA على أبيض 4.86)

--success           : #0F7B4D   (نص/باكدج)
--warning           : #8F5F00   (نص) — مصوَّح من #9A6700 للـbadge
--danger            : #B42318
--info              : #175CD3
```

حالات الحشوات (Badge backgrounds):
```
--success-bg        : #E7F5EE   (نص success 4.72)
--warning-bg        : #FFF4D6   (نص 8F5F00 5.04)
--danger-bg         : #FEECEB   (نص danger 5.76)
--info-bg           : #EAF1FD   (نص info 5.27)
--neutral-bg        : #EEF1F5   (نص secondary)
```

### 3.2 الوضع الداكن — **مُلغى**

حُذف ضمن إعادة التصميم Light-only (2026-10): لا `--bg` داكن، لا
`data-theme="dark"`، ولا `dark-theme.css` ولا `_ThemeScript.cshtml`.
أي ظهور لوضع داكن (OS preference أو `color-scheme: dark`) يُعتبر انحرافًا.

## 4. القياس (Typography & Rhythm)

| الدور | العائلة | الحجم/الارتفاع | الوزن |
|---|---|---|---|
| H1 (عنوان الصفحة) | Cairo | 24/32 | 700 |
| H2 (أقسام داخل البطاقة) | Cairo | 18/26 | 700 |
| Body | Cairo | 14/22 | 400 |
| Table Cell | Cairo | 13.5/20 (tab) | 400 |
| Numeric/Currency | بيانات رقمية | 14/22 **نفس حجم الخلية المحيطة إن أمكن** | 500 |
| Label نموذج | Cairo | 13/18 | 600 |
| Caption/Faint | Cairo | 12/16 | 400 |

- الأرقام: `font-variant-numeric: tabular-nums`، اتجاه LTR عبر `.num` و `dir="ltr"` ومحاذاة نهاية البلوك في RTL بقوة.
- السطر المركزي للعملة: `Services/Money.cs` موجود — نمدّه بأداة موحّدة للعرض في القوالب فقط (لا نغير البنية).

Spacing scale: `--space-1..8` = 4/8/12/16/24/32/48/64.
Radius: `--radius-xs 4, sm 6, md 8, lg 12, xl 16`.
Elevation: `--shadow-1` (card)، `--shadow-2` (drawer)، `--shadow-3` (dialog/سطح عائم) — نسب عتامة منخفضة، لا ظلال ثقيلة.
Motion: `120ms` hover/focus، `180ms` انتقالات الحالة، `240ms` دخول/خروج الحوار. احترم `prefers-reduced-motion` (منجز أصلًا في count-up).
Focus: `2px` حلقة `--primary`، offset `2px`، لا إخفاء نهائي.

## 5. خريطة ربط Bootstrap vars ← التوكنز

تُطبَّق في `tokens.css` على `:root` (نطاق واحد، بلا `data-theme`) بالمرور على Bootstrap 5.3 semantic root vars:
- `--bs-body-bg` ← `--bg`؛ `--bs-body-color` ← `--text`
- `--bs-primary` ← `--primary`؛ `--bs-primary-rgb` ← انفصال RGB
- `--bs-primary-color` ← `--on-primary`
- `--bs-primary-bg-subtle` / `-border-subtle` / `-text` ← `--primary-subtle`/بطاقاتها
- `--bs-border-color` ← `--border`؛ `--bs-secondary-color` ← `--text-secondary`؛ `--bs-secondary-bg` ← `--surface-2`
- `--bs-emphasis-color` ← `--text`؛ `--bs-heading-color` ← `--text`
- `--bs-link-color` / `-hover` ← `--primary` / `--primary-hover` (مع ضمان تحوّل برند signature)
- `--bs-focus-ring-color` ← `rgba(primary, 0.28)`
- `--bs-table-bg`, `--bs-table-hover-bg` ← `--surface` / `--surface-2`
- النجاح/التحذير/الخطر/المعلومات + `-bg-subtle` و `-border-subtle` و `-text` ← السلالات أعلاه

> العلامات التجارية: تُعرَّف عبر `RenderThemeCss` في `BrandingService.cs` بـ `:root { ... }` **بنطاق واحد فاتح فقط** (لا نطاق داكن ولا `BrightenToLuminance`). اختبارات `BrandingServiceTests` تضمن: نطاق `:root {` موجود، وصفر `data-theme`.

## 6. كتالوج المكونات (المطلوب 12 — §7)

مراجع ملفات مقترحة (لا تُكتب قبل اعتماد P1 إلا إذا فرضتها مرحلة سابقة):

| # | المكوّن | مسار الاقتراح | سلوك معلَّق بالتوكين |
|---|---|---|---|
| 1 | Button | `Views/Shared/Components/Button.cshtml` أو أصناف CSS في `components.css` | `btn`, `btn-primary/secondary/ghost/destructive`, `.btn-icon`, `.spinner-border` في-button (وُجد `data-loading-submit` منجزا في site.js — يدعّم التوكينز فقط) |
| 2 | PageHeader | `Views/Shared/_PageHeader.cshtml` (جديد) | حل لـ 2.3: خلفية `--surface`، حافة `--border`, العنوان H1، زر «رجوع» `bi-arrow-right` RTL، زر الإجراء `btn-primary` واحد كحد أقصى |
| 3 | DataTable | `Views/Shared/Components/DataTable.cshtml` + `.table-container` | `--surface`, hover `--surface-2`, `.num`, كثافة `data-table-density` (منجز)، freeze `nvs-frozen-col` |
| 4 | EmptyState | `Views/Shared/_EmptyState.cshtml` (جديد) | أيقونة 56px في دائرة tinted + عنوان + وصف + إجراء واحد |
| 5 | StatusBadge | `Views/Shared/_StatusBadge.cshtml` (جديد) | قاموس موحّد: draft/pending/approved/posted/cancelled/paid/partially-paid/overdue — ألوان tokens |
| 6 | Pager | `Views/Shared/_Pager.cshtml` (يُحدَّث شكليًا) | `PagerExtensions.PageSize=50` منجز؛ نعيد التمويل فقط |
| 7 | FormField | `Views/Shared/_FormField.cshtml` (جديد اختياري) | لصق `label` + `input` + `validation` على Bootstrap أصناف |
| 8 | Dialog/Drawer | site.js `#appConfirmModal` موجود؛ `components.css` | مكوّنات Bootstrap منجز؛ لا مضاهاة |
| 9 | Toolbar/FilterBar | `.filter-bar` في components.css | لوحات التصفية: زر بحث `--primary`, `select[data-auto-submit]` (عقد مُحافظ عليه) |
| 10 | StatCard KPI | `Views/Home/_StatCard.cshtml` (جديد) | `--surface`, رقم tabular-nums, `data-count` مدعوم منجزا |
| 11 | toast | `toast-region/app-toast` (منجز site.js) | تمويل عبر tokens |
| 12 | Skeleton | `.skeleton-row/.skeleton` (منجز site.js) | تمويل عبر `--surface-2` كمخطط موحد |

## 7. بنية الصفحة المنتظمة

- قائمة: `PageHeader` → `FilterBar` → `DataTable` → `_Pager` → `EmptyState`.
- نموذج: `PageHeader` → بطاقة واحدة أو أقسام مستقلة بعناوين H2؛ زرا حفظ/إلغاء في تذييل `--surface`.
- مستند: نمط مشترك واحد للمبيعات/المشتريات (تفاسير في `PrintStudio`/`PrintDocument.cshtml` لا تُمَس).
- أفعال مالية لا يمكن عكسها: `confirmAction()` موجودة (site.js) — نستدعيها حقيقةً مع `btn-danger`.

## 8. القواعد RTL/BiDi (أعلى أولوية)

- `.num`: `direction:ltr; unicode-bidi:embed; text-align:end` على التوكين `--num-dir`.
- سهم الرجوع في RTL = `bi-arrow-right`؛ أيقونات الترحيل تُقلب بمنطق بناء لا بعلاقة CSS مطلقة.
- المقياس — عدد يتم المحاذاة `-` كعلامة سالبة محفوظة.

## 9. الحالات المسموح إبقاؤها خارج التوحيد (موثقة)

- `Reports/*` (BalanceSheet, TrialBalance, IncomeStatement, BudgetVariance, AuditLedger, Sales, Purchases, Payments): `data-table-plain` + طباعة؛ أبجدياتها خارج التفصيل.
- `Sales/Create`, `PurchaseOrders/Create`, `SalesQuotes/Create`, `Batch/Sales`, `StockTransfers/Create`: جداول أسطر قابلة للتحرير (`#itemsBody`, `.item-select`) — عقد JS كامل محفوظ كما هو.
- `Items/PrintLabel.cshtml`: صفحة ملصق أصلية، لا تلمس (طباعة).
