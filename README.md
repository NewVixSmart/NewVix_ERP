# New Vix Smart — نظام إدارة تجاري عربي (دبل المحاسبة والمخزون)

نظام ويب متكامل لإدارة عمليات التداول التجاري باللغة العربية (RTL) على **ASP.NET Core 10 + Entity Framework Core + SQL Server** — يشمل إدارة الأصناف والمخزون (FIFO)، المبيعات والمشتريات، المرتجعات، الدفعات، المحاسبة ذات القيد المزدوج، القوائم المالية، الإقفال السنوي، الميزانيات، العمليات الجماعية، API عديمة الحالة (JWT)، والجداول/الباركود والتصدير.

> **العملة**: النظام **بالجنيه المصري فقط** (EGP). لا توجد عملات متعددة ولا أسعار صرف ولا فروق عملة؛ كل المبالغ بالجنيه المصري والعرض عبر `Services/Money.cs` والرمز `L.E`. القرار المعماري الكامل في [`docs/DECISION-EGP-ONLY.md`](docs/DECISION-EGP-ONLY.md).

> **الحالة**: المراحل المخططة P0–P4 مكتملة ومُتحقَّقة، واكتملت P5 (أمن → توثيق → القائمة العمرية وتنبيهات الاستحقاق → التدفق النقدي وكشوف الحساب)، و**M12 (حجز المخزون ← أذن التسليم ← الفوترة بعد التسليم)**. **417 اختبارًا** أخضر، بوابة إتاحة WCAG 2.2 AA مفعّلة، وتقارير تدقيق الأمان والمحاسبة مغلقة (0 حرج، 11 منخفضًا/متوسطًا قيد التلميع).

---

## البنية

```
NewVixSmart.slnx
├── src/NewVixSmart.Web          # تطبيق MVC (net10.0) + API (JWT) + خدمات
│   ├── Api/                      # REST endpoints (JWT أو Cookie)
│   ├── Controllers/              # واجهات الواجهة (MVC)
│   ├── Services/                 # محركات المحاسبة/المخزون/التقارير (Services)
│   ├── Models/                   # جوهر البيانات (EF Core)
│   ├── Views/                    # Razor (RTL، إتاحة WCAG 2.2 AA)
│   ├── Migrations/               # هجرات EF Core
│   └── Dockerfile                # صورة إنتاج مرحلتين (net10.0)
├── tests/NewVixSmart.Web.Tests  # xUnit (SQLite في الذاكرة — لا يتطلب LocalDB)
├── aspire/                       # قوالب Aspire (AppHost / ServiceDefaults)
├── docs/                         # خطة البناء + سجلات الإكمال + نشر Azure
└── .github/workflows/ci.yml      # بناء + اختبار + فحص CVE + بناء صورة Docker
```

## الميزات الرئيسية

- **محاسبة ثنائية**: قيد مزدوج تلقائي لكل عملية (ملكية، دفع، مرتجعات، إقفال سنوي)، **تكلفة البضاعة المباعة (COGS) تُرحَّل تلقائيًا عند البيع**، قائمة دخل + ميزانية عمومية + ميزان مراجعة، دفتر تدقيق مركزي. كل القيود بالجنيه المصري.
- **مخزون FIFO**: طبقات التكلفة (`StockLayer`)، النقل بين المستودعات، تسوية الجرد، مرتجعات تُعيد الطبقات بسعر الشراء الأصلي.
- **تجارة**: عملاء/موردون، بيع/شراء، أوامر شراء (RFQ→PO→استلام)، شحنات، دفعات مع تخصيص **بالجنيه المصري فقط**، الأفرع، وعروض أسعار للعملاء (طباعة/PDF وتحويل جماعي مع ربط عروض الموردين).
- **إدارة**: مستخدمون + أدوار + نظام أذونات دقيق (`PermissionCatalog`), تعدد مستودعات، إعدادات، عمليات جماعية (Batch) بأفضل جهد، إقفال سنة مالية بقفل.
- **تقارير**: PDF (فاتورة/ملصق بباركود Code128)، XLSX (قوائم + تدقيق + ميزانيات + قائمة واريانس).
- **API**: `POST /api/auth/token` (JWT) + نقاط أصناف/مخزون/فواتير/مدفوعات/قيود مع مفاتيح أذونات `ApiAuthorize`.
- **إتاحة**: WCAG 2.2 AA (مسح axe صفر انتهاكات على 46 مسارًا)، RTL كامل، لوحة مفاتيح، `lang="ar" dir="rtl"`.

## تدفق البيع: حجز ← تسليم ← فاتورة (M12)

مسار **AtInvoice** (المسار المفضّل والمحاسبي الصحيح: الإيراد يظهر عند التحقق من الفاتورة، لا عند التسليم):

```
أمر بيع  →  حجز مخزون  →  إذن تسليم  →  أمر تسليم  →  فاتورة تسليمات
  (SalesOrders)  (StockReservations)  (DeliveryIssues)  (DeliveryOrders)   (Sales)
```

| الخطوة | الشاشة | الإذن | الأثر |
|---|---|---|---|
| حجز كمية/عدد | `StockReservations` | `StockReservations.Create` | `Available = Current − Reserved` (نطاق الصنف عام) |
| إذن تسليم (3rd‑party) | `DeliveryIssues` | `DeliveryIssues.Create` | كشف منStdReservations → إذن/أمر |
| تنفيذ التسليم | `DeliveryOrders` | `DeliveryOrders.Deliver` | سحب FIFO + `Dr 5000 / Cr 1300` (تكلفة فقط) |
| فاتورة التسليمات | `Sales` | `Sales.Create` | `Dr 1200 = Gross` / `Cr 4000 = Gross−Tax` / `Cr 2055 = Tax` |

قواعد أساسية:
- الفاتورة تقبل **عدة أوامر تسليم** وتُفوتر الكميات **المسلَّمة غير المفوترة فقط** (`SalesOrderItem.UninvoicedQty/UninvoicedCount`).
- `Quantity` و`Count` بعدان مستقلان؛ قاعدة العرض `Quantity > 0 ? Quantity : Count`.
- أذون/أوامر التسليم **بلا أسعار** — التسعير يتم عند الفاتورة.
- **الإيراد لا يُرحَّل عند التسليم** في هذا المسار؛ التكلفة فقط.
- المسار القديم `SalesPostingMode.AtDelivery` باقٍ كما هو للسلوك التاريخي، والبيع المباشر في `Sales/Create` ما زال يرحّل الإيراد والمخزون فورًا (مع تنبيه في الشاشة).
- مسارات مستقلة مسموحة: حجز بلا أمر بيع، إذن بعميل، وفوترة تسليم بلا أمر بيع.
- **مرتجع** مقابل فاتورة AtInvoice يرحّل `Dr 5101 / Cr 2055 / Cr 1200 / Cr 1300 / Dr 5000` (يقلب التكلفة والإيراد معًا).

مرئية في: تبويبات حالات أوامر البيع + أعمدة «مطلوب/مسلَّم/مفوتر»، تفاصيل أمر البيع (الحجز والتقدم والتسليمات والفواتير)، كشوف العميل مع **قيمة التسليمات المعلّقة**، تقرير التسليمات المعلّقة في `Reports`، بطاقة «تسليمات معلّقة» في لوحة القيادة، وتصديرات XLSX/CSV للحجوزات وأذون التسليم.

## التشغيل محليًا

```bash
# البناء والاختبار (أوامر C# تُنفذ من حلّ المشروع)
dotnet restore NewVixSmart.slnx
dotnet build NewVixSmart.slnx -c Release        # 0W/0E (تحذيرات كأخطاء)
dotnet test  NewVixSmart.slnx -c Release        # 417 اختبارًا (SQLite، لا DB خارجي)

# تشغيل التطبيق (وضع التطوير — يستخدم launchSettings على :5165)
dotnet run --project src/NewVixSmart.Web
```

افتح `http://localhost:5165`. قاعدة البيانات التلقائية: `(localdb)\mssqllocaldb`/`NewVixSmartDb` — تُهيّأ وتـseeded عن الإقلاع (لا خطوات يدوية).

**الحسابات التجريبية** (تُنشأ عند الإقلاع):

| المستخدم | الدور | الكلمة |
|---|---|---|
| `admin` | Admin | `Admin@123` |
| `accountant` | Accountant | `Acc@12345` |
| `warehouse` | Warehouse | `War@12345` |

> ⚠️ هذه القيم موجودة في `appsettings.Development.json` للتطوير فقط ولا تُشحن مع `appsettings.json`. للإنتاج يجب تمرير `Seed__AdminPassword` و`Seed__AccountantPassword` و`Seed__WarehousePassword` (انظر `.env.example`) — الحارس يرفض إقلاع `Production` عند غيابها أو عند بقاء القيم الافتراضية، ويرفض كذلك `Jwt:Key` إن كان placeholder (انظر أدناه).

## الإنتاج (Docker + CORS)

```bash
# إنشاء ملف الاعتمادات (مطلوب؛ .env غائبة عن git)
cp .env.example .env    # ثم عبّئ SQL_SA_PASSWORD بقيمة قوية
docker compose up -d    # SQL Server 2022 + web على http://localhost:8080
```

**الأمان الإجباري (وقت الإقلاع):** البيئة غير-تطويرية ترفض:
- مفتاح JWT `<32` حرفًا أو يحوي `REPLACE_WITH` → «اضبط `Jwt__Key` عبر متغير بيئة أو User Secrets».
- اتصال DB يشير إلى `mssqllocaldb` أو فارغ → «اضبط `ConnectionStrings__DefaultConnection` لـ SQL Server حقيقي» (الافتراضي المحلي لا يُقلع في الحاوية).
- CORS مغلقة افتراضيًا؛ تُفعَّل بقائمة صريحة: `Cors__AllowedOrigins="https://app.example.com"`.

```bash
Jwt__Key="PUT-A-RANDOM-64+CHAR-SECRET" \
ConnectionStrings__DefaultConnection="Server=sql;Database=NewVixSmartDb;User Id=sa;Password=...;TrustServerCertificate=True" \
Cors__AllowedOrigins="https://app.example.com" \
ASPNETCORE_ENVIRONMENT=Production dotnet run --project src/NewVixSmart.Web --no-launch-profile
```

## CI / بوابة الجودة

`.github/workflows/ci.yml` يشغّل عند كل push/PR:
- **build-and-test**: restore → build Release (0W/0E) → `dotnet test` → رفع TRX.
- **package-vulnerability**: `dotnet list ... package --vulnerable --include-transitive` (يفشل عند أي CVE).
- **docker-image**: بناء صورة الويب + `docker inspect` (ExposedPorts + Healthcheck).

**الإتاحة (بوابة محلية يستحسن تشغيلها بعد تغيير الواجهة):** `node $env:TEMP\opencode\pw\p4cA11ySmoke.cjs` → تتوقع `GATE: PASS` (38 صفحة: 200 + صفر أخطاء console + صفر انتهاكات Critical/Serious). تفاصيل: `ACCESSIBILITY.md`.

## التوثيق

- `docs/BUILD-PLAN-README.md` — خطة المراحل M0→P5 وسجل الإكمال والتحقق لكل مرحلة.
- `plan/feature-sales-reservation-delivery-invoice-flow-1.md` — خطة M12 (حجز المخزون ← أذن التسليم ← الفوترة بعد التسليم) مع 71 مهمة وقراراتها.
- `AGENTS.md` — قواعد العمل الإلزامية للوكلاء (قبل التعديل: راجع README وACCESSIBILITY.md؛ الاختبارات إلزامية).
- `docs/DEPLOY-AZURE.md` / `docs/DEPLOY-AZURE-EN.md` — نشر Azure App Service + SQL.
- `docs/BACKUP.md`, `scripts/` — نسخ احتياطي وجدولتها.
- `Deep-Audit-Report.md`, `ACCESSIBILITY.md` — تدقيق أمني/محاسبي وإتاحة.

## أذونات (مختصر)

| الوحدة | الأذونات | الافتراضي |
|---|---|---|
| `Batch` | SalesCreate, AdjustmentCreate | Accountant + Warehouse(Adj) |
| `FiscalClose` | Close, Reopen | Accountant (Close) |
| `AuditLedger` | View, Export | Accountant |
| `ChartOfAccounts` | View, Create, Edit, Deactivate | Accountant (View) |
| `Budgets` | View, Manage | Accountant (View+Manage) |
| `SaleReturns`/`PurchaseReturns` | View, Create, Post | Accountant + Warehouse |
| `StockReservations` | View, Create, Release | Accountant + Warehouse |
| `DeliveryIssues` | View, Create, Issue | Accountant + Warehouse |