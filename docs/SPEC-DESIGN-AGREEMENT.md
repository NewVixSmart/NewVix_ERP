# SPEC-DESIGN-AGREEMENT — دستور واجهة New Vix Smart

الحالة: **مُعتمَد v1.6** (2026-10-07) — نقطة الصفر: نُلغي بصمة الواجهة الحالية وننفذ
دستورًا جديدًا كاملًا بما يلتزم **بمعيار NewVix ERP UI Architecture & Design System
(الأقسام 1–178، سلطة إلزامية)**. أي قرار تصميم/تقني جديد يُضاف إلى هذا الملف ثم يُنفذ.

## 1. المدرسة والهوية

النمط: **نظام مؤسسي محايد + حافة 1بك + لون علامة واحد** (مستلهم من Stripe · Untitled
UI · Linear · Mercury · Tremor · Vercel · Xero/QBO/Zoho/FreshBooks).

| القاعدة | القرار |
|---|---|
| كونافا | بيضاء قريبة من الورق (فاتح) / كربون (داكن) |
| التفريق | حافة 1بك شعرية، وليس ظلالاً ثقيلة ولا تدرّجات ولا زجاجية |
| لون العلامة | أزرق أعمال Business Blue، محجوز للزر الرئيسي/الرابط/الحالة |
| زخرفة | ممنوعة: aurora، backdrop-blur على المحتوى، تدرّجات داخلية عريضة، خطوط نيون |
| الأرقام | غربية 0-9، tabular-nums في كل نقود/كمية/تاريخ |
| المفتاح | فاتح أساس + داكن أوَّل بنّية الجودة (مصمَّم بالنية لا عكس ألوان) — القرار v1.1 |

## 2. الرموز (CSS custom properties)

### 2.1 الألوان — الحياد (cool gray)

| الدور | فاتح | داكن | ملاحظة |
|---|---|---|---|
| `--bg-page` | `#F9FAFB` | `#0A0B0D` | |
| `--bg-surface` | `#FFFFFF` | `#131417` | بطاقات/جداول |
| `--bg-surface-hover` | `#F2F4F7` | `#191A1D` | ترويض صف/زر خفي |
| `--bg-surface-active` | `#EAECF0` | `#212226` | |
| `--border-subtle` | `#EAECF0` | `rgba(255,255,255,.06)` | شعرات الجداول |
| `--border-default` | `#D0D5DD` | `rgba(255,255,255,.10)` | حقول/مخططات |
| `--border-strong` | `#98A2B3` | `rgba(255,255,255,.16)` | |
| `--text-primary` | `#101828` | `#F2F4F7` | 15:1 على السطوح |
| `--text-secondary` | `#475467` | `#98A2B3` | ≥7:1 |
| `--text-muted` | `#667085` | `#7A8190` | ≥4.5:1 فقط |
| `--text-disabled` | `#98A2B3` | `#5A5F6B` | مُستثنى WCAG |
| `--fg-on-accent` | `#FFFFFF` | `#06111F` | |

### 2.2 لون العلامة — أزرق أعمال (معتمد)

| الدور | فاتح | داكن |
|---|---|---|
| `--accent-solid` | `#1570EF` (أبيض عليه 4.57:1 ✓) | `#2E90FA` |
| `--accent-fg` | `#FFFFFF` | `#06111F` (5.95:1 ✓) |
| `--accent-hover` | `#175CD3` | `#53B1FD` |
| `--accent-pressed` | `#1849A9` | `#1570EF` |
| `--accent-tint` | `#EFF8FF` | `rgba(46,144,250,.12)` |
| `--accent-tint-border` | `#B2DDFF` | `rgba(46,144,250,.35)` |
| `--accent-link` | `#1570EF` | `#53B1FD` (7.9:1 ✓) |
| `--focus-ring` | `0 0 0 3px rgba(21,112,239,.28)` | `0 0 0 3px rgba(83,177,253,.35)` |

### 2.3 الحالات (semantics) — تُستخدم كشارات ناعمة لا تملأ

| الدلالة | فاتح (نص على tint) | داكن | Tint فاتح | Tint داكن |
|---|---|---|---|---|
| نجاح | `#027A48` | `#17B26A` | `#ECFDF3` | `rgba(23,178,106,.12)` |
| تنبيه | `#DC6803` | `#F79009` | `#FFFAEB` | `rgba(247,144,9,.14)` |
| خطر | `#D92D20` | `#F97066` | `#FEF3F2` | `rgba(249,112,102,.14)` |
| معلومات | `#1570EF` | `#53B1FD` | `#EFF8FF` | `rgba(46,144,250,.12)` |

### 2.4 الزوايا

`--radius-sm: 4` · `--radius-md: 6` (أزرار/حقول/selects) · `--radius-lg: 8` (بطاقات/غلاف جدول)
· `--radius-xl: 12` (modals/popovers) · `--radius-full` (كبسولات/أفاتار).

### 2.5 الإرتفاعات — 3 مستويات متناظرة (أمن RTL، x≡0)

| المستوى | فاتح | داكن (السطح هو الارتفاع + شعرية) |
|---|---|---|
| L1 بطاقة مرتفعة | `0 1px 2px rgba(16,24,40,.06), 0 1px 3px rgba(16,24,40,.10)` | `#191A1D` + شعرية 1بك + `0 1px 2px rgba(0,0,0,.40)` |
| L2 popover/dropdown | `0 4px 8px -2px rgba(16,24,40,.10), 0 2px 4px -2px rgba(16,24,40,.06)` + شعرية `#EAECF0` | `#212226` + شعرية + `0 8px 24px rgba(0,0,0,.45)` |
| L3 modal/drawer | `0 24px 48px -12px rgba(16,24,40,.18), 0 0 0 1px rgba(16,24,40,.06)` | `#131417` + شعرية + `0 24px 48px rgba(0,0,0,.55)` |

الجداول **بدون ظل** (حافة 1بك فقط).

### 2.6 الخط والنص (Cairo، عربي RTL)

| النمط | الحجم/السطر | الوزن |
|---|---|---|
| عنوان صفحة | 28/40 | 700 |
| h1 | 24/38 | 700 · h2 20/32 (600) · h3 18/30 (600) · h4 16/26 (600) |
| جسم أساسي (افتراضي) | **14/24** (1.71 للعربية) | 400 |
| جسم غليظ / تسميات | 14/24 | 600 |
| زر/حقل | 14/20 | 500–600 |
| caption/بيانات جدول | **12/20** | 400 |

- الأوزان: 400/500/600/700 فقط.
- **`letter-spacing: 0` على العربية** (السالبة للتشكيلات الحرة Latin أرقام فقط، ونحظرها في هذا النظام).
- النقود/الكميات/التواريخ: `.num { font-variant-numeric: tabular-nums; font-feature-settings: "tnum"; }`.

### 2.7 التباعد والكثافة

- السلم: 4, 6, 8, 12, 16, 20, 24, 32, 40, 48, 64 (شبكة 8 الرئيسية، 4 الثانوية).
- الجداول: رأس 40بك ثابت · صف **40بك** افتراضي · **48بك مريح** عند اختيار المستخدم · **32بك كثيف** للدفاتر؛ خلية `padding-inline:16px`؛ شعرية `--border-subtle`؛ لا zebra (شريط `#F9FAFB`/`rgba(255,255,255,.02)` للدفاتر الكثيفة فقط).
- الأزرار: **40بك** افتراضي · 36 مضغوط · 48 hero · داخل الجداول 32×32 أيقوني.
- أهداف اللمس ≥44بك على الشاشات <768بك.

## 3. عقد الجدول الموحّد (كل قوائم الجداول تلتزم به)

1. غلاف `table-container`: حافة 1بك `--border-default`، زوايا 8بك، `overflow:auto` في **الوضعين** (ممنوع `overflow:hidden` في الداكن).
2. الرأس: `position:sticky; top:0` بخلفية **صلبة** `--bg-surface` (شفافية تكشف الصفوف ممنوعة)، z-index فوق الصفوف، `vertical-align:middle`، ارتفاع 40بك.
3. رتبة الأعمدة: هوية ← حالة ← تواريخ ← مبالغ عند الحافة النهائية.
4. العمود الأول (معرّف/كود): **تجميد ثابت** عندما يتجاوز عدد الأعمدة 8، بخلفية صلبة وحافة فاصلة.
5. محاذاة: النص `start`؛ النقود/الكميات/التواريخ `end` (تُعكس إلى اليسار في RTL) **مطابقة للرأس**، مع عزل `unicode-bidi` و`tnum`، و`nowrap` (لا التفاف للمبالغ).
6. الفرز: `th[aria-sort]` بلون `--accent` + خط سفلي 2بك، أيقونة `.th-sort` بمفاتيح تعمل بالكيبورد (`role=button`).
7. شريط/بثّ الأدوات: بحث + عامل حجم صفحة + عدّاد "صفحة X من Y" + **مفتاح كثافة** (40↔48بك) محفوظ في localStorage.
8. المجموع/الذيل: صف `tfoot` بوزن 600 وحدّ علوي.
9. الحالة: شارة كبسولة ناعمة واحدة (Tint+نص دلالي) — النظام الوحيد، ونُدمج `.badge-stock-*` فيه.
10. الإجراءات: عمود أخير، أزرار 32×32 أيقونية بسطر واحد (بدل تكديس 44بك).
11. الفارغ: لوحة CTA موحدة؛ التحميل: هيكل عظمي بصفوف بأبعاد الصف الحقيقية.
12. `tag: <caption>` (لا `visually-hidden` اختياريًا) قبل `<colgroup>`، `scope` لكل `th`.

## 4. أنماط الشاشات

- **لوحة التحكم**: 4 بطاقات KPI (عنوان 14بك/قيمة 28-36بك بعلامة ± ملونة) + جدولا الذمم بحشوات العمر (حالي/0-30/31-60/61-90/90+) بلون متصاعد (أخضر→أصفر→أحمر) + شريط توزيع نسب مئوية + جدول منخفض المخزون.
- **شريط جانبي**: عرض 264بك، خلفية سطحية، نشاط "حبة" بتأثير كبسولة خفيف، مجموعات قابلة للطي، بحث داخلي، ملف مستخدم سفلي.
- **شريط علوي**: ارتفاع 64بك، خلفية سطحية وحافة سفلية، عمليات الجهة الطرفية (مبدّل الوضع، إشعارات، مستخدم).
- **وثيقة (فاتورة/أمر)**: شبكة رؤوس وصفية + جدول بنود + كتلة مجاميع (المجموع/الضريبة/الإجمالي بخط بارز) + سطر تواريخ عمل + شريط إجراءات.
- **نموذج**: حقول 40بك، تسميات خارجية، صندوق تركيز 3بك، أزرار تأكيد/إلغاء (أزرق فاتح/داكن ترتيب).
- **الدخول**: خلفية ورقية هادئة + بطاقة مركزية 400بك + شعار + مبدّل وضع — بلا فوضى.
- **فارغ/بداية**: لوحة أيقونة + نص + إجراء؛ مخططات أعمدة وحالات "لا توجد بيانات" بنصموذج CTA.

## 5. قواعد RTL والعربية (غير قابلة للتفاوض)

1. خصائص منطقية فقط (`margin/padding/border/inset-inline-*`، `text-align:start|end`).
2. لا `left/right` مادي إلا داخل حُراس RTL مُثبتة.
3. لا `letter-spacing` على العربية؛ اقتباس/قرص الأنماط للأرقام واللاتينية فقط.
4. المبالغ/أرقام الفواتير/الحسابات معزولة `unicode-bidi` وبترتيب `12,345.67 ج.م` (`&#8207;`).
5. انعكاس الشكل لا المعنى: أسهم/شيفرونات تُقلب `scaleX(-1)`، وسائط (redo/undo/play) دلاليًا.
6. الجداول تنعكس كليًا (العمود 1 = أقصى اليمين)؛ تجميد العمود الأول عبر `inset-inline-start`.
7. ظلال متناظرة (الرقم من §2.5).
8. أرقام غربية ثابتة عبر locale (لا ٠-٩)، لا `locale` يقلب الأرقام في النصوص العربية.
9. انظر §2.7 لارتفاعات الحقول (حروف صاعدة/هابطة تحتاج ≥40بك).

## 6. الإتاحة WCAG 2.2 AA (بوابة axe: 99 فاتح + 13 داكن)

- نص ≥4.5:1 (كبير ≥3:1)؛ عناصر غير نصية ≥3:1؛ أهداف ≥44بك عند <768بك.
- تركيز مرئي `--focus-ring` في كل عنصر تفاعلي (كيبورد).
- `caption` + `scope` لكل جدول؛ تسميات الحقول؛ أخطاء النماذج بمطابقة حالة/إستدلال.
- وضعان متكافئان عبر نفس الرموز؛ `forced-colors` يُعيد الحالات إلى ألوان النظام؛ `prefers-reduced-motion` يوقف الحركة.
- لا تغيير معنى بلون وحده (شارات تحمل أيقونة/نص).
- التمرير: `:focus-visible` على الغلاف ذي `tabindex=0`، وشريط تمرير ظاهر (8بك) في الوضعين.

## 7. مقاييس النجاح للتنفيذ

- البوابات: build 0W/0E · test 0×فاشل · format 0 · hygiene PASS · ef «No changes» · a11y GATE PASS.
- قوائم التحقق النهائية من المعيار الـإلزامي (§151 Definition of Done، §177 FINAL QUALITY COMMAND) تُطبَّق في نهاية **كل** مرحلة.
- قياس DOM (مثال): صف الجدول 40بك (±2)، الرأس 40بك، المبالغ بلا التفاف، لا `overflow` أفقي مستحق في دفاتر >80% من الحالات، computed text-align header==cell، icon+value دلالية للمؤشرات.
- لا يكسر RTL، ولا hex خام في `.cshtml` (في CSS فقط كرموز).

## 8. التوفيق مع المعيار الهندسي (NewVix ERP UI Spec 1–178)

المعيار سندٌ إلزامي؛ هذا الدستور يُفصِّل تنفيذه. قرارات التوفيق المعتمدة:

| بند المعيار | التوفيق |
|---|---|
| §11 داكن الكانفاس أولًا "where appropriate" | القرار v1.1: **فاتح أساس + داكن أوَّل بنّية**؛ الإثبات: الروبو فاتح اليوم (`color-scheme: light` في site.css)، وبوابة الإتاحة 99 فاتحًا مقابل 13 داكنًا، وقيم الـBranding الافتراضية فاتحة. |
| §5/§144/§145/§147 لا إعادة-كتابة عمياء | نقطة الصفر = **إعادة جلد مرحلية** (خارطة §10) مع الحفاظ الكامل على العقود (§11) وعقود DOM/JS. |
| §15/§78/§162 Branding مركزي | `#00213F` **غير موجود** في الروبو (grep صفري). الأزرق المعتمد يُورَّد عبر `IBrandingService`/`RenderThemeCss` وتوكينز `--color-primary` — لا hardcode. |
| §24/§102/§103 أرقام وعملة | أرقام غربية + تنسيق عملة مركزي (`Money.Format`) + `tnum`؛ لا دمج يدوي `amount + " EGP"`. |
| §151/§177 بوابات | تُدمج كبوابات مع بوابات الريبو (build/test/format/hygiene/ef/a11y). |

## 9. قرار التقنية (مبني على الأدلة — §171/§176)

**القرار: لا مكتبة UI جديدة. نبقي المكدّس الحالي ونعيد الجلد عبر توكينز CSS مركزي + overrides.**

| المعيار | الدليل |
|---|---|
| ERP suitability | المكدّس الحالي يشغّل 125 مشاهدة و33 controller و49 خدمة بلا نظام UI آخر. |
| RTL / A11y | `bootstrap.rtl.min.css` 5.3.3 راسخ + بوابة axe 99/13 خضراء سابقًا. |
| عقود JS | site.js (1232 سطرًا) وtablist.js معتمدان على selectors/DOM محدّدة — استبدال الإطار يكسرها (§80). |
| التكامل العميق | Bootstrap JS behaviors + jQuery في `_ValidationScriptsPartial` + ~136 سمة `style=` في العروض + CSP `'unsafe-inline'`. |
| التكلفة/الترخيص/الاستقرار | ترحيل لأي بديل = إعادة بناء كل العروض + إعادة معايرة الإتاحة بلا مكسب وظيفي. |
| AI-maintainability | التوكينز + Bootstrap المعرَّف runّفافي العالمي أسهل صيانة من مزيج CSS عشوائي. |

- **Alternatives considered**: Tailwind (يتطلب تفكيك Bootstrap + RB). shadcn/Radix (يتطلب React — بنية MVC). AG Grid (يبالغ للجداول المتوسطة الحالية؛ §43: لا ثقل بلا مبرر).
- الـ Print: يبقى سطحًا مستقلاً (§97): أقسام PRINT داخل site.css/dark-theme.css + `PrintDocument.cshtml` + QuestPDF — تُعاد مراجعتها في Phase 7 دون نقل مبكر.
- الأيقونات: عائلة واحدة Bootstrap Icons (§73).

## 10. خارطة الترحيل المرحلية (§145 مع بوابات)

| Phase | المحتوى | بوابة الخروج |
|---|---|---|
| 0 | baseline: إثبات PASS بوابة الإتاحة بالمانيفست 99+13 (الخط الأساس المفقود)، تسجيله بتاريخ، + إصلاح عيوب §12 | a11y PASS + build/test |
| 1 | Tokens: عائلة cool-gray في `site.css` PRIMITIVE/SEMANTIC (`--bg-page`… `--accent-*`) + remap المستهلكين الحاليين + الداكن عبر نفس الرموز | build · format · hygiene · ef · a11y |
| 2 | Core components: عقد الجدول الموحّد (§3)، الشارات، الأزرار، الحقول، modals، pagination فوق Bootstrap | نفس البوابات + قياسات DOM |
| 3 | Shared layout: sidebar/topbar/page header — الشكل الجديد فوق **نفس** العقود (§11) | نفس البوابات |
| 4 | شاشات منخفضة المخاطرة: Items/Customers/Suppliers/Stock | نفس البوابات |
| 5 | شاشات ERP المعقدة: Sales/Purchases/Accounting | نفس البوابات |
| 6 | Reports/Dashboard | نفس البوابات |
| 7 | Print: مراجعة أقسام الطباعة + PrintDocument | طباعة فعلية RTL |
| 8 | إزالة القديم: حذف `facelift.css` + `IRIDIS NIGHT` + زخارف `auth.css` — **مكتملة** | كامل البوابات + لقطات ×2 وضعين ✓ |

بعد كل مرحلة: التحقق من دليل §146 (Functionality/Contracts/JS/Validation/Permissions/RTL/Responsive/A11y/Print/Performance) + قياسات DOM + **لا commit ضخم** (§148 — commits صغيرة `feat(ui):`/`refactor(ui):`).

## 11. قائمة الحماية — عقود لا تُمس أثناء إعادة الجلد (من تدقيق 2026-10-06)

- **الثيم**: `data-theme-mode`/`data-theme`/`data-bs-theme` على `<html>`؛ `localStorage['theme-mode']`؛ `window.__applyDarkThemeCss`؛ `link#dark-theme-css`؛ `meta[name=color-scheme]`.
- **Sidebar**: `#sidebar`/`#mobileOverlay`/`#sidebarToggle`؛ أصفان `.show`/`inert`/`aria-hidden`؛ عقد الطي المحفور site.css:811-833 (`.sidebar-nav .nav-group-body`, `.nav-group.is-collapsed`, `.nav-group-content`, `.sidebar.is-filtering`)؛ معرّف الجلد `vix-sidebar-sections`؛ `#mainMenu`/`.nav-link.active`؛ `[data-nav-filter]`/`[data-nav-filter-clear]`/`[data-nav-empty]`؛ `.nav-group[data-group]`. (**`#railToggle`/`.is-rail`/`vix-sidebar-rail` حُذفت نهائيًا في 2026-10-08**)
- **خطافات JS**: `data-confirm`/`data-confirm-ok` مع `#appConfirmBtn`/`#appConfirmMsg`؛ `data-confirm-delete`؛ `data-loading-submit`/`data-loading-text`؛ `data-password-toggle`؛ `data-auto-submit`؛ `data-auto-print`؛ `data-focus-target`/`data-create-category`؛ `table[data-load="true"]`+`aria-busy`+`.skeleton-row td`؛ الأرقام الحية `data-count`/`data-decimals`/`data-count-formatted`؛ `data-greeting`/`data-clock`؛ `[role="tablist"][data-tablist-roving]` و`[role="tab"]`.
- **أمان/أساسيات**: CSP + `@Context.GetCspNonce()` لأي `script/style` inline جديد؛ `@Html.AntiForgeryToken()`/`__RequestVerificationToken`؛ `skip-link`→`#mainContent`؛ `i.bi` + `aria-hidden` (site.js:6-11)؛ `--touch-target-min:44px` و`.page-link` 44px.
- **توكينز قائمة**: عائلتا `--em-*`/`--go-*`/`--n-*` و`--r-*` و`--sh-*` — لا تُستبدل إلا بـremap موحّد يخدم العائلة الجديدة.

## 12. عيوب مُرصودة تُصلح أثناء التنفيذ

- `aria-hidden` ناقص على `i.bi` في أزرار صفوف `Customers/Index.cshtml` (سطر 39-49) و`Suppliers/Index.cshtml` (سطر 33-42) — تعارض مع دستور الأيقونات.
- خط الأساس a11y مسجَّل بأرقام قديمة (46+11)؛ **التسجيل الجديد 99+13 واجب** (Phase 0).
- الزجاج/aurora الحالي (`facelift.css` + `IRIDIS NIGHT` + زخارف `auth.css`) — هو الهدف المرحلي للحذف (§165: aurora فقط للدخول/hero لا للجداول/المستندات؛ هنا يُحذف كليًا لصالح المحايد الورقي).

## 13. سجل القرارات (يُحدّث عند كل قرار جديد)

- v1 2026-10-06: المدرسة المحايدة، أزرق أعمال، كثافة 40بك + مفتاح تبديل، أرقام غربية، تجميد عمود المعرّف عند >8 أعمدة، وحفظ الدستور في docs/.
- v1.1 2026-10-06: استيعاب **معيار الواجهة 1–178** كسلطة إلزامية؛ فاتح أساس + داكن أوَّل بنّية الجودة (§11 "where appropriate")؛ قرار تقني مبني على الأدلة (إبقاء Bootstrap/site.js + إعادة جلد بالتوكينز، لا مكتبة جديدة)؛ خارطة ترحيل 0–8؛ قائمة حماية §11؛ إثبات a11y 99+13 خطوة 0.
- v1.2 2026-10-06: **Phase 0 اكتملت** — بوابة الإتاحة بالمانيفست الحالي: `GATE: PASS (99 light + 13 dark, 0 critical/serious, 0 empty, 4 documented-empty)`؛ سجل التشغيل: `%TEMP%\opencode\a11y-gate\20261007-002611`. هذا خط الأساس الواجب عدم هدمه.
- v1.3 2026-10-07: **Phase 1 (Tokens) اكتملت** — التنفيذ والبوابات:
  - التوكنز: PRIMITIVE (radius 4/6/8/12/16/999، إرتفاعات L1–L3 x≡0، cool-gray §2.1، Business Blue scale §2.2) في `site.css` + DARK TOKENS (كربون #0A0B0D/#131417/#191A1D، حدود rgba(255,255,255,.06/.10/.16)، accent #2E90FA + `--fg-on-accent:#06111F`) في `dark-theme.css`؛ أسماء legacy `--color-*`/`--bs-*` مبقاة بقيم جديدة، مع طبقة alias قياسية (`--accent`, `--fg-on-accent`, `--bg-page`, `--bg-surface`, radius specs).
  - `BrandingService`: defaults + preset `modern` → `#1570ef`/`#53b1fd`/`#ffffff`/`#f9fafb` (قاعدة التطوير فعلاً على preset modern — بلا هجرة؛ `RenderThemeCss` لا يبث كتلة داكنة فالداكن من dark-theme.css وحده).
  - إعادة تلوين الجلدين المؤقتين إلى المقياس الجديد دون حذف: `facelift.css` + `auth.css` + IRIDIS NIGHT 7b (استبدال سداسيات/```rgba``` الكحلية القديمة بأزرق الأعمال)؛ **وتحييد** gradient `.btn-primary` الليلة (7b) فوق التوكنز حتى يظهر الداكن الجديد؛ حذف الزجاج يبقى على خارطة Phase 3/5/8.
  - تسطيح `body`/`.login-stage`/`.dash-hero` (إزالة radial aura) من site.css وdark-theme.css.
  - البوابات: build `0W/0E` · test `950 Passed / 0 Failed (1 skipped = SqlServer)` · format نظيف · نصوص `PASS` · `has-pending-model-changes` نظيف · **a11y `GATE: PASS (99+13, 0 critical/serious, 0 empty, 4 documented-empty)`**؛ سجل: `%TEMP%\opencode\a11y-gate\20261007-010113`.
  - قياس computed (بروف Playwright): فاتح body `rgb(249,250,251)`، `.btn-primary` أبيض على تدرج Business Blue، `--accent:#1570ef`؛ داكن body `rgb(10,11,13)`، `.btn-primary` **مستو** `#2E90FA`/نص `#06111F`، `--accent:#2E90FA`, `--fg-on-accent:#06111F`, `--link:#53B1FD`. لقطات: `%TEMP%\opencode\shots\phase1-*.png`.
  - ملاحظة مؤجلة لعقد Phase 2: رأس الجدول الداكن ما زال #16233F (7b CONTENT TABLES)، وزوايا auth 20بك — تُعالج في Phase 2/4. كما أُنشئ قسم 2b (alias layer) داخل `site.css:root`.
- v1.4 2026-10-07: **Phase 2 (عقد الجدول §3) اكتملت** — التنفيذ والبوابات:
  - **الإطار (site.css §12)**: `.table-container` = حافة `--color-border-strong` + زوايا `--r-md` + `--sh-sm` + `max-height:min(70vh,980px)` + overflow؛ `thead th` صلب `--color-surface`/نص `--color-text-secondary` + sticky + `height:var(--table-row-h)`؛ `td` ارتفاع `var(--table-row-h)` + padding أفقي `var(--table-cell-px)`؛ tfoot 600 + حدّ علوي 2 بك؛ `.table .btn-sm` → 32×32 (أزرار صفوف)؛ فرز نشط بلون accent: `th[aria-sort]` عام + حاوية (خط سفلي 2 بك `--color-primary`، خلفية `--color-surface-tint`)؛ `.th-sort` صبط على ارتفاع الصف (إلغاء 44بك).
  - **كثافة 40↔48**: `--table-row-h:40px`/`--table-cell-px:.95rem` في `:root`، و`body[data-table-density="comfortable"]` → 48px/1.05rem؛ مبدِّل `[data-table-density-toggle]` في `_Layout` (نصّ كثافة/راحة + aria-pressed + حفظ)؛ يقرأ `initTableDensity` من site.js ويحفظ في `vix-table-density` ثم reload.
  - **تجميد عمود المعرّف عند >8 أعمدة**: `applyFirstColFreezeAll` في initDataTables يضيف `.nvs-frozen-col` على أول th/td، sticky `inset-inline-start:0` مع خلفية صلبة `!important` (تتحمل الـzebra) + فاصل `--color-border-strong` والحاوية تتحمل AirTable؛ أداة `.nvs-col-end` (end-align + nowrap + isolate) للمبالغ والتواريخ.
  - **الشارات §3.9**: `.badge-soft-{success,danger,warning,info}` + `.badge-stock-*` على التوكينز؛ `--color-info-bg/-text` مُشتقّتان من alert tokens في الوضعين.
  - **تعارض facelift (فاتح فقط)**: كان يتغلب على العقد (رأس `#FCFCFD`/نص `#3F4B5F`/padding 0.7rem 1.1rem، و`.table-container` داخل مجموعة الزجاج). أُخرج من الزجاج وأُعيدت قواعده على التوكينز/var؛ **قاعدة `thead tr:first-child th` انحصرت بـ`:not(:last-child)`** لصفّ المجموعة فقط (لوحة التحكم) بعد اكتشاف أن الخصوصية (0,4,4) كانت تكسر لون فرز accent (0,4,3) في كل الجداول أحادية الرأس.
  - **التشكيل**: داخل `.data-shell` يقفل `.table-container` على حافته/زواياه (الغلاف يؤطّر الأساس كما في الداكن) — الحدود/الزوايا موحّدة 8 بك في الوضعين.
  - **القيم المقيسة (computed، بروف Playwright لـ /Purchases)**: فاتح th `40.5بك`/أبيض/`#475467` + sticky، بعد الكثافة `48.5بك`، الفرز `#1263D2` + خط سفلي `2بك #1570EF`، `.btn-sm` 32×32، غلاف `8بك + rgba(16,24,40,.14)`؛ داكن رأس `#131417`/نص `#A8AEB8`/خلايا `#ECEDEF`، غلاف 8بك؛ **التجميد**: /Purchases (10 أعمدة) و/Reports/AuditLedger (11) مُجمّدتا المعرّف (sticky + `inset-inline-start:0` + فاصل 1بك)، بينما /Accounts و/Items (7) بلا تجميد (الحدّ ≥8 سليم). التدحرج الأفقي مع التجميد على لقطة `phase2-purchases-dark-frozen-scrolled.png`؛ الصفوف الأعلى من 40بك ظاهرة للتفاف المحتوى الطويل (سلوك طبيعي، لا قصّ).
  - **البوابات (الحالة النهائية)**: build `0W/0E` · test `950 Passed / 0 Failed (1 skipped = SqlServer)` · format نظيف · نصوص `PASS` · **a11y `GATE: PASS (99+13, 0 critical/serious, 0 empty, 4 documented-empty)`**؛ سجل: `%TEMP%\opencode\a11y-gate\20261007-015006`.
  - لقطات: `%TEMP%\opencode\shots\phase2-purchases-light.png`, `-light-comfortable.png`, `-dark.png`, `-dark-frozen-scrolled.png`, `phase2-dashboard.png`.
- v1.5 2026-10-07: **Phase 3+4+5 اكتملت (Layout/auth/خيار الوضع) + إصلاح حادثة الترميز** — التنفيذ والبوابات:
  - **Phase 3 (اللافتات/الشرائط/البطاقات)**: الشريط الجانبي/العلوي سطوح صلبة بلا `backdrop-filter` (شعرية 1بك فقط)؛ البطاقات/tiles/`data-shell` صلب `--color-surface`/`--color-surface-2`؛ modals/dropdown/popover صلب `--color-surface-2` + `--r-lg`/`--sh-sm`؛ أُزيل راديات الـaurora وكل تدرّجات chrome؛ `--color-surface-2` داكن `#191A1D`/فاتح `#EEF2F8`؛ الرادي `--r-sm:6px --r-md:8px --r-lg:12px`.
  - **Phase 4 (auth.css)**: استُبدل نصف قطر الصدفة 20بك → `--r-lg` (12بك)؛ `.lg-stage` مسطح (لا grid ولا aura)؛ hero = `--color-primary-darker` (فاتح)/`--accent-darker` (داكن) بلا صورة؛ monogram **حبيبة صلبة** (سطح + `--color-primary-darker` + `--sh-md`)؛ `.lg-submit` = `--color-primary` ونص **فاتح: أبيض / داكن: `--fg-on-accent #06111F`** (قرار §2.2، لا أبيض على accent داكن)؛ حلقة تركيز/hash/checkbox → `--color-primary`؛ لمسة ذهبية داكنة (eyebrow `--color-gold-strong`، dot/trust/secure/كلمة السر hover `--color-gold-bright`)؛ أُزيل `translateY`/`filter` من hover. أُزيل كل `#2e6fd8`/`#5bc8e8`؛ البقية المحتمَلة مقبولة (neutrals + `#53b1fd` حدّ pressed + أحمر الخطأ على الأسطح).
  - **Phase 5 (7b اختيار الوضع)**: chips صلبة؛ حدّ `#53B1FD` عند pressed (يتوافق dark-theme:519).
  - **حصيلة الفحص computed (بروف Playwright `%TEMP%\opencode\phase3_probe.cjs`)**: **ALL PASS** — فاتح: shell أبيض/12بك، hero `rgb(19,102,217)`، submit `rgb(21,112,239)`، monogram صلب؛ داكن: hero `rgb(24,73,169)`، submit `rgb(46,144,250)`/نص `rgb(6,17,31)`؛ dashboard/`/Customers`/`/Purchases` مسطّحة، backdrop-filters على الصفحة = 0، `.table-container` داكن `#131417`/8بك. لقطات: `%TEMP%\opencode\shots-p3\*.png`.
  - **حادثة ازدواج الترميز (CP1256)**: فشل 11 اختبار `AmountInWords` كان بسبب قراءة PS5.1 لملف .ps1 UTF-8 بلا BOM كـ ANSI وإعادة كتابته CP1256 (مثال: «فقط» → `ظپظ‚ط·`). أُصلح: 13 ملفًا عكست أنماطًا + 10 أسطر FFFD أُعيدت كتابتها + 7 أسطر من HEAD (`الخط indigo` في BrandingService «ملكي نيلي» + 6 أسطر PrintPdfBuilder) + **22 سطرًا فسادًا ملتزمًا في `Supplier.cs`/`Customer.cs`** (كانت مطابقة لـHEAD فصعب كشفها) عكسها فكّ `Rev` = CP1256.GetBytes ثم UTF8.GetString (الحارس: لا FFFD + عربية). **القاعدة الحديثة: سكربتات PS تكتب ASCII فقط، والملفات تُقرأ/تُكتب بـ`[IO.File]::ReadAllText/WriteAllText` (UTF8)**.
  - **طباعة (مراجعة Phase 6/7)**: لا تدرّجات/لا زجاج في أي قالب طباعة؛ `PrintDocument` يستخدم `@_accent`؛ `@media print` سليم (إخفاء الأدوات + ورق أبيض) — لم يُلمس (§11). `#0f766e` موجود فقط كقيمة preset في BrandingService («قائم سماوي») لا داعٍ لنقله.
  - **البوابات (الحالة النهائية)**: build `0W/0E` · test `950 Passed / 0 Failed (1 skipped = SqlServer)` · format نظيف · نصوص `PASS` (allowlist=1: دليل حادثة الترميز في Deep-Audit-Report) · ef `No changes` · **a11y `GATE: PASS (99+13, 0 critical/serious, 0 empty, 4 documented-empty)`**؛ سجل: `%TEMP%\opencode\a11y-gate\20261007-035350`.
- v1.6 2026-10-07: **Phases 6+7+8 اكتملت (التقارير/اللوحة، الطباعة، إزالة القديم)** — التنفيذ والبوابات:
  - **Phase 6 (Reports/Dashboard)**: جداول التقارير/دفتر الأستاذ على عقد §3 (تجميد معرّف AuditLedger 11 عمودًا و/Accounts 7 بلا تجميد كما سبق قياسه)، البطاقات KPI/الشارات على التوكينز.
  - **Phase 7 (Print)**: لا تدرّجات/لا زجاج في أي قالب طباعة؛ `PrintDocument` يستخدم `@_accent` مع توكينز الجذر؛ `@media print` في site.css (§13) والداكن (§5) يبقى سليمًا (ورق أبيض + إخفاء `[data-toolbar]`/الغلاف/الحافة)؛ **تحقق طباعة فعلية RTL** ببوابة Playwright: `html lang=ar dir=rtl`، `body direction:rtl`، `thead th` محاذاة `right` (RTL start)، `.no-print` يُخفى، صفر backdrop-blur/تدرّجات، جدول بـ`caption`+`scope` — **ALL PASS** (`%TEMP%\opencode\probe-p8`، لقطة `p8-print-preview.png`).
  - **Phase 8 (إزالة القديم)**: حُذف **`facelift.css`** كليًا (حذف الرابط من `_Layout.cshtml`)؛ **`IRIDIS NIGHT`** — 146 سطرًا من كتلة CONTENT TABLES §7b في dark-theme.css (961-1025، 1026-1112، 1182-1253) حُذفت ونُقلت بطاقات KPIs الداكنة إلى موقع واحد (583-633 بعلامة `4b`)، مع رأس الملف المُحدَّث وإضافة `forced-colors` §9 (1129-1141)؛ **زخارف auth.css** (.lg-stage::before، .lg-aura، .lg-hero::before/::after، lgFloat/lgGlow، كتلة `.lg-shell` المكررة 944-964) حُذفت والمراجع التقليم في `@media`؛ أُزيلت `div.lg-aura` من `Login.cshtml`/`AccessDenied.cshtml`. **العقد المقدس (CONTENT TABLES — 13 مجموعة كاملة) سليم** بعلامة الحماية 1014-1082: التحقق بالغربلة الآلية (سلامة الأقواس 310/310، المقدس 1014-1082، عقد RTL site.css:1553-1586، و`.lg-aura` null).
  - **قياسات computed بعد الإزالة (بروف Playwright `%TEMP%\opencode\phase3_probe.cjs` — ALL PASS)**: فاتح shell أبيض/12بك، toolbar/sidebar/hero مسطّحة بلا aurora ولا backdrop-filter (عدّاد الصفحة = 0)، `.btn-primary` فاتح `rgb(21,112,239)`/داكن `rgb(46,144,250)` مع text `rgb(6,17,31)`، monogram صلب؛ لقطات ×2 وضعين: `%TEMP%\opencode\shots-p8\p8-{root,auditledger,accounts,customers,sales}-{light,dark}.png`.
  - **إصلاح تراجع a11y مُكتشَف بعد الحذف**: بوابة axe كشفت 3 عقد `color-contrast` في `/Reports/AuditLedger` (خلايا `td.text-primary` داخل `tr.table-primary.fw-bold` في لطيف): `--color-code-on-primary` كان `#15437F` ثم أصبح `#FFFFFF` (أبيض على أزرق فاتح = 1.31:1). أعيدت قيمة `#15437F` (9.13:1 على `--bs-primary-bg-subtle:#EFF8FF`)؛ الداكن يبقى من dark-theme.css:464-465 (`#BEF0FC !important`). **الدرس: بوابة axe هي الكاشف الوحيد لهذه الانزلاقات — تُشغَّل كاملة بعد أي حذف كتلة CSS.**
  - **البوابات (الحالة النهائية)**: build `0W/0E` · test `950 Passed / 0 Failed (1 skipped = SqlServer)` · format نظيف · نصوص `PASS` (allowlisted=1) · ef `No changes` · **a11y `GATE: PASS (99 light + 13 dark, 0 critical/serious, 0 empty, 4 documented-empty)`**؛ سجل: `%TEMP%\opencode\a11y-gate\20261007-103723`.
- **اكتمل التنفيذ كاملًا (Phases 0–8)** — كل البوابات خضراء. أي عمل مستقبلي: صيانة/تحسين فوق الدستور، أو التوسع بالعقود المتبقية في SPEC (print layouts، أنماط المستندات الجديدة) باتباع نفس مسار الإثبات.
