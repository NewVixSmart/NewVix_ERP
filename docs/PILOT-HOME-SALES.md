# وصف تنفيذي — شاشتا Home (لوحة التحكم) و Sales (القائمة والتفاصيل)

> **بوابة §14 من `UI-EXECUTION-MANDATE.md`** — تسليم 4 من 4. يصف الشكل النهائي
> المستهدف قبل البناء الفعلي في P4، ليُعتمَد (أو يُعدَّل) قبل أي كود. يلتزم
> بأقصى القيود: لا مكوّنات وهمية، لا زجاجية، لا ظلال ثقيلة، لا invert()، صفر hex
> في القوالب (اللون من `tokens.css` حصرًا).

## 1. Home / Index (`Views/Home/Index.cshtml`) — «لوحة التحكم»

الرؤية: لوحة قيادة «Quiet Luxury» على توكينة `--bg`، بطاقات `--surface` بحدة `--radius-lg`،
ظل `--shadow-1` خفيف، لا ألوان صارخة إلا للدلالات. الترتيب:

```
PageHeader (Partial _PageHeader)
  العنوان: «لوحة التحكم» + سطر «صباح الخير، {المستخدم}` (data-greeting منجز) + تاريخ (data-clock)
  إجراءات: لا زرّ primary متعدد — رابط وقت الحاجة فقط (كل الأزرار ثانوية)

صف StatCards (وفق Model الحالي: TotalItems, TotalCustomers,
            TotalPurchaseAmount, TotalSaleAmount, PendingDeliveryCount)
  4 بطاقات KPI بتمويل: أيقونة 24px في دائرة tinted، رقم tabular-nums
  بـ data-count / data-count-formatted (العقدة A21 محفوظة — بدون pulse،
  فقط عدّ مضبوط بـ prefers-reduced-motion).

التفاصيل السفلية (من `Home/Index`) مقسّمة إلى لوحتين:
  «أحدث الفواتير» + «مستحقات قريبة» في جداول `.table-container` عادية
  (لا data-server-paged هنا — الجداول صغيرة داخلية).
```

متطلبات التوافق في هذه الشاشة فقط:
- `h1` واحد (الـ PageHeader)؛ بقية العناوين `h2`.
- كل `<table>`: `<caption class="visually-hidden">` + `th scope="col"`.
- `data-count` يبقى يعمل دون تغيير JS.

## 2. Sales / Index (`Views/Sales/Index.cshtml`) — «فواتير البيع» (قائد القوائم)

هذا المعيار الذي تُقاس عليه كل القوائم السيرفية الـ20 في P3/P5:

```
PageHeader (Partial _PageHeader)
  العنوان «فواتير البيع» + زر «فاتورة جديدة» btn-primary (بإذن Sales.Create)
  زر «رجوع» link عادي بأيقونة bi-arrow-right (اتجاه RTL) يعود لصفحة سابقة
  عند توفرها — لا يُخلط مع [data-auto-print]. أقل من فعل primary واحد للصفحة.

FilterBar (Toolbar)
  نفس نموذج البحث الحالي (search by رقم/عميل) بأصناف form-control + زر بحث
  btn-outline-primary. لا تغيير في action/querystring (عقد _Pager محفوظ:
  ?page=&search=).

DataTable
  <div class="table-container">
    <table class="table table-hover mb-0" data-server-paged>
      <caption class="visually-hidden">…</caption>
      thead+th scope ، tbody ، أعمدة رقمية بأصناف .num
  </div>
  (عقد A25: بدون data-server-paged كان محرّك client-side يمسها؛ الوسم يمنعه.)

Pager
  @await Html.PartialAsync("_Pager") — بدون تغيير في المنطق (PagerExtensions.PageSize=50)

EmptyState (Partial _EmptyState) عند !Model.Any() بدل النمط القديم.
```

## 3. Sales / Details (`Views/Sales/Details.cshtml`) — «تفاصيل فاتورة بيع» (قائد المستندات)

```
PageHeader: «فاتورة #...» + زر «رجوع» + أفعال (طباعة btn-outline-secondary
  + أيقونة printer + rel="noopener noreferrer" — الإصلاح 2.4) + أفعال
  حالة/ترحيل عبر confirmAction (لا تُمس).

إجمالي/خصم/صافي في 3 خانات KPI صغيرة داخل بطاقة واحدة.

جدول السطور: .table-container + .num للأرقام. StatusBadge لحالة الفاتورة.

تُضاف حالة دين/استحقاق من Model الموجود إن وجد — لا حقول جديدة مطلوبة.
```

## 4. ملاحظات Cross-cutting تنفَّذ أولاً في P4 على هاتين الشاشتين فقط

- أقصى زر primary واحد لكل صفحة (الماندايت §13).
- كل الحاويات `.table-container`؛ كل الجداول `table table-hover mb-0`.
- الأرقام المالية عبر `.num` + `Money.Format` (بلا تغيير توقيع الخدمة).
- لا hex خارج tokens.css وBranding (يُصلح RenderThemeCss في P1 قبل P4 — §3.4).
- لا `<i class="bi">` بلا `aria-hidden="true"` صريح (وسم 2.6 يقصد P1 لكن نلتزم به في P4 أيضًا).

## 5. حلّ تعارض الصور (لا لقطات هذه المرحلة)

لا توجد أداة لقطة شاشة موثوقة الآن؛ أُنجزت مقايسة بصرية عبر:
- جرد وصفي: `docs/UI-REVIEW.md` (قبل) مقابل هذا الوصف (بعد، نصيّ).
- بوابة a11y بعد البناء في P4 تؤكد عقود الإتاحة وحدها.
- صورتان يمكن إنتاجهما يدويًا من متصفح المستخدم بعد P4 للاعتماد النهائي (اختياري).
