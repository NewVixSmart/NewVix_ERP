# قائمة عقود DOM/JS الحالية — نتيجة P0 (لا تُكسَر في أي مرحلة)

> **بوابة §14 من `UI-EXECUTION-MANDATE.md`** — تسليم 3 من 4، ناتج التدقيق الفعلي
> لـ `wwwroot/js/site.js` (1273 سطرًا) و `wwwroot/js/tablist.js` (132 سطرًا) ومطابقتها
> بالقوالب. أي تصميم في P1–P7 يعيد تلوين/هيكلة **يحافظ** على هذه العقود إلا ما صرّحت
> به مرحلة محددة هنا.

## أ. عقود `site.js` (سجل كامل)

| # | العقد (selector / id / attr / state) | موضع JS | السلوك المحمي | مرحلة اللمس |
|---|---|---|---|---|
| A1 | `i.bi` (كل الأيقونات الديكورية) | site.js:6-11 | يضيف `aria-hidden="true"` تلقائيًا لأي `i.bi` بلا `aria-hidden` (يستثني `.visually-hidden`). **وسم 2.6: يُفضَّل أن يكون HTML يدويّا صريحًا؛ JS بلا تغيير** | P1 |
| A2 | قرصة CSRF عامة: `input[name="__RequestVerificationToken"]` | site.js:13-14 | يقرأ التوكن عند التحميل ويطبقه تلقائيًا في `postForm` (رأس `RequestVerificationToken`). النماذج الأخرى تحمل توكنها الرسمي — **لن نحذفه** | P1 (توثيق فقط) |
| A3 | Sidebar: `#sidebar`, `#mobileOverlay`, `#sidebarToggle`, `.show`, `inert`, `aria-hidden` | site.js:17-97 | فتح/إغلاق offcanvas، `aria-expanded`، قفل scroll، تركيز اختياري، إغلاق بـ Esc و Tab trap و `.nav-link` | P2 (Shell) |
| A4 | `window.toggleSidebar()`, `window.showToast(msg,type)` | site.js:50-61, 111-137 | أسماء موثّقة تستدعيها القوالب (أزرار) — البقاء كما هو؛ يتحوّل التلوين فقط | P2/P3 |
| A5 | Toast DOM: `.toast-region`, `.app-toast info/error/success`, `.toast-progress` | site.js:101-137 | يتسلم من CSS؛ لا تغيير HTML/JS | P3 |
| A6 | `postForm(url, data)`, `handleResult(res, err)` | site.js:140-162 | كل POST عبر JS | لا يُلمس أبدًا |
| A7 | Edit-name modal: `#editNameModal`, `#editNameForm`, `#editNameInput`, `#pageNewCat`, `#pageAddCatBtn` | site.js:168-223 | تحرير تصنيف/نوع صنف مع تحقّق RowVersion | P3 (تمويل) |
| A8 | Confirm modal: `window.confirmAction(msg, fn, okLabel)` + `#appConfirmModal`/`#appConfirmBtn`/`#appConfirmMsg` | site.js:225-299 | استبدال `confirm()`؛ يُستدعى من القوالب لأفعال الحذف/الترحيل | لا يُلمس |
| A9 | `select[data-auto-submit]` | site.js:301-306 | إرسال النموذج تلقائيًا عند التغيير (فلاتر Accounts/PurchaseOrders/Reports) — **تبقى** | لا يُلمس |
| A10 | `[data-auto-print]` | site.js:308-311 | `window.print()` لأزرار الطباعة — **الإصلاح 2.4 يُغيِّر الألوان/`rel` فقط** | P3 |
| A11 | Category create: `#pageNewCat`, `[data-create-category]`, `#pageAddCatBtn2` | site.js:313-341 | POST `/Categories/Create` | P3 |
| A12 | Manage tables: `#categoriesTable`, `#itemTypesTable`, `.btn-edit`, `.btn-del`, `td[data-name]`, `data-rowversion` | site.js:350-390 | تحرير/حذف مع conflicts | P3 (تمويل) |
| A13 | Skeleton: `table[data-load="true"]` + `.skeleton-row/.skeleton` | site.js:393-431 | تحميل يسحب خادمًا جزئيًا ويبني الهيكل | P3 |
| A14 | `#itemsBody` + `.item-select` (جداول الأسطر القابلة للتحرير) | site.js:444-451 | تنقية الصفوف الفارغة قبل submission في نماذج Sales/Purchase/Batch | لا يُلمس |
| A15 | Scrollable regions: `.table-container, .card-body.p-0, .table-responsive` → `tabindex=0` + `role=region` + label | site.js:453-469 | توافق axe scrollable-region-focusable | P1/P3: **يجب ألا نزيل `tabindex`/`role`** |
| A16 | Sidebar groups state: `vix-sidebar-sections`, `.nav-group[data-group]`, `.nav-group-header` `aria-expanded`, `.is-collapsed` | site.js:471-536 | حفظ فتح/إغلاق المجموعات في localStorage — **وضع rail و`#railToggle` و`.is-rail` و`vix-sidebar-rail` حُذفت نهائيًا** | P2 (Shell): **القائمة تبقى بهذا البنية** |
| A17 | Nav filter: `[data-nav-filter]`, `[data-nav-filter-clear]`, `[data-nav-empty]`, Ctrl+K | site.js:588-690 | بحث القائمة الجانبية | P2: **تبقى كعقد** |
| A18 | `form[data-confirm]`, `data-confirm-ok`, وسم `data-confirmed` | site.js:692-707 | اعتراض submission نحو confirmAction | لا يُلمس |
| A19 | `form[data-loading-submit]`, `data-loading-text` | site.js:709-719 | تعطيل زر الإرسال مع سبينر أثناء العمل | P3 (سبينر يُموَّل فقط) |
| A20 | `[data-password-toggle]` + `aria-controls` (Login) | site.js:729-742 | تبديل إظهار كلمة السر | P2/P3 |
| A21 | `[data-count]`, `data-decimals`, `data-count-formatted` | site.js:744-789 | عدّاد KPI مع احترام `prefers-reduced-motion` | P4 (الجديد Home) |
| A22 | Bulk select: `#selectAll`, `.mass-convert-check` | site.js:791-798 | MassConvert | لا يُلمس |
| A23 | ~~Theme~~ **مُلغى في إعادة التصميم Light-only** | — | حُذفت سمات الثيم و`localStorage theme-mode` و`__applyDarkThemeCss` و`dark-theme.css` و`_ThemeScript.cshtml`؛ زر `.theme-mode-btn` في topbar بقي **للكثافة فقط** (`[data-table-density-toggle]`) | مُنفّذ |
| A24 | `[data-clock]`, `[data-greeting]` | site.js:844-852, 721-727 | ساعت/تحية | P2/P4: تبقى |
| A25 | Client-side data tables: `.data-shell`, `.data-toolbar`, `.data-search`, `.data-size`(10/20/50/100), `.data-pager`, `.th-sort`, `aria-sort`, `localStorage vix-data-size`, `.nvs-frozen-col` (>8 أعمدة) | site.js:854-1183 | بحث/فرز/ترقيم clientside على الجداول غير partialed | **2.1/3.1: تبقى للجداول غير السيرفرية؛ تُضاف شرط `data-server-paged` فقط** |
| A26 | `window.refreshVixTables()` | site.js:1150-1159 | بعد إعادة تشغيل الجداول الهجينة | لا يُلمس (يُسمّى في A13 لجداول data-load) |
| A27 | Density: `[data-table-density-toggle]`, `localStorage vix-table-density` (compact/comfortable)، `applyTableDensity` | site.js:1186-1209 | **منجز كما جاء في 3.1** — تمويل عبر `.table` padding فقط | P3 (تمويل فقط) |
| A28 | Prefetch on hover/focus (a[href]، قواعد origin) | site.js:1224-1272 | تحسين أداء | لا يُلمس |

## ب. عقود `tablist.js`

| عقد | موضع | ملاحظة الإبقاء |
|---|---|---|
| `[role="tablist"]` + `[role="tab"]` + وسم `data-tablist-roving` (idempotent) | tablist.js: بِرَمُّهُ كامل | **لا يضاف مستمع ثانٍ**؛ القالب الوحيد حاليا `Settings/Printing.cshtml:44-50` |
| `aria-selected` مصدر الحقيقة من الخادم؛ `aria-controls` يشير للوحة؛ `tabindex` متجوّل | tablist.js:23-66 | لا تغيير |

## ج. عقود الهيكل العامة (Layout/قوالب)

- `Views/Shared/_Layout.cshtml`:
  - C1: `<html lang="ar" dir="rtl">` (سطر 2) — **بلا سمات ثيم** (أُزيلت `data-theme-mode`/`data-theme`/`data-bs-theme`) و`meta color-scheme = light` فقط، ولا سكربت ثيم ولا `_ThemeScript.cshtml`.
  - C2: `@Html.AntiForgeryToken()` العام — **يبقى** (يرتكز عليه A2).
  - C3: بنية Sidebar: `#sidebar`, `#mainMenu`, `.sidebar-brand`, `.nav-filter`, `.nav-group[data-group]`, `.nav-link...` — بنية ثابتة (الإبقاء كما هو).
  - C4: Topbar `data-greeting`/`data-clock`/`.theme-mode-btn`/`[data-table-density-toggle]` — تبقى كل المعرفات؛ و`.theme-mode-btn` صار **زر الكثافة وحده** (لا `data-theme-value`).
  - C5: `mainContent` للـ skip-link — لا يُزال.
- **Login.cshtml** (`Layout=null`) و **AccessDenied.cshtml** (`Layout=null`): بلا سكربت ثيم بعد إعادة التصميم، مع `#Password`, `.lg-*`, `[data-password-toggle]` — تبقى كما هي.
- **Print/PrintDocument.cshtml** و `Items/PrintLabel.cshtml`: ملكية الطباعة — خارج التصميم تماما (ماندايت §13: لا تُلمس).

## د. عقود تُنشأ (جديدة، مقترحة في هذه المرحلة)

| العقد الجديد | الغرض | المكان |
|---|---|---|
| `[data-server-paged]` على `<table>` في القوائم التسيرفية الـ20 | opt-out من A25 (client engine لا يلمسها) | site.js `buildDataTable` شرط إضافي + قوالب الـ14 الجديدة وملف الـ6 الحالية |
| `Views/Shared/_PageHeader.cshtml` (Partial) | توحيد رأس الصفحة (2.3) | القوالب |
| `Views/Shared/_EmptyState.cshtml` (Partial) | حالة فارغة موحدة (2.2 — القرار «ب») | القوالب |
| `Views/Shared/_StatusBadge.cshtml` (Partial) | شارة الحالة + القاموس | قوائم/تفاصيل |
| `Views/Shared/_StatCard.cshtml` (Partial) | KPI للـ Dashboard الجديد | Home/Index، Reports/Dashboard |

> **حُذف**: `Views/Shared/_ThemeScript.cshtml` — ضمن إعادة التصميم Light-only (حُذف الملف واستُبدل بـ`meta color-scheme=light` + نطاق `:root` واحد في Branding).

## هـ. ملاحظات P0 حرجة (الاكتشافات)

1. **A25 يمس قوائم السيرفر بالفعل**: الـ6 القوائم التسيرفية ذوات `_Pager` (Sales, Customers, Accounts, Payments, PurchaseReturns, Stock) الآن تُعالَج أيضًا محرّك client-side؛ النتيجة ترقيم مزدوج/بحث مزدوج. وسم `data-server-paged` هو **حلّ** هذه الازدواجية عند تفعيله في الـ6 + الـ14 الجديدة.
2. **Density منجزة (A27)**: لا حاجة لبنائها في P3 — تمويل فقط.
3. **antiforgery (A2/C2 + 44 توكنًا نموذجيًّا)**: لن يحذف أي توكن نموذج؛ نزيل فقط ما تكرر فعليًا في نفس النموذج عند وجوده. هذا هو مدى القرار 2.5 المعتمد.
4. **Login يستدعي site.js** (سطر 169) لكنه `Layout=null`: `data-loading-submit` على النموذج يعمل فيه لأنه يُشغل site.js — تصميمنا لا يكسر ذلك.
5. **`--text-faint` لون مقروء على الأسطح (تصحيح معتمد في DESIGN-SYSTEM §2)**.

## و. عمليات التحقق من العقود (لا تتغير)

- `dotnet build ... -c Release` → 0W/0E
- `dotnet test ... -c Release` → Failed: 0
- `scripts/check-text-hygiene.ps1` → exit 0
- a11y gate (`scripts/run-a11y-gate.ps1`) → أخضر في الفاتح فقط (حُذف فحص الداكن مع إلغاء الثيم الداكن)
- `dotnet ef migrations has-pending-model-changes` → No changes
