# Silk Trading — نظام إدارة تجاري عربي (دبل المحاسبة والمخزون)

نظام ويب متكامل لإدارة عمليات التداول التجاري باللغة العربية (RTL) على **ASP.NET Core 10 + Entity Framework Core + SQL Server** — يشمل إدارة الأصناف والمخزون (FIFO)، المبيعات والمشتريات، المرتجعات، الدفعات متعددة العملات، المحاسبة ذات القيد المزدوج، القوائم المالية، الإقفال السنوي، الميزانيات، العمليات الجماعية، API عديمة الحالة (JWT)، والجداول/الباركود والتصدير.

> **الحالة**: المراحل المخططة P0–P4 مكتملة ومُتحقَّقة، واكتملت P5 (أمن → توثيق → القائمة العمرية وتنبيهات الاستحقاق → التدفق النقدي وكشوف الحساب). **201 اختبارًا** أخضر، بوابة إتاحة WCAG 2.2 AA مفعّلة، وتقارير تدقيق الأمان والمحاسبة مغلقة (0 حرج، 11 منخفضًا/متوسطًا قيد التلميع).

---

## البنية

```
Silk.Trading.slnx
├── src/Silk.Trading.Web          # تطبيق MVC (net10.0) + API (JWT) + خدمات
│   ├── Api/                      # REST endpoints (JWT أو Cookie)
│   ├── Controllers/              # واجهات الواجهة (MVC)
│   ├── Services/                 # محركات المحاسبة/المخزون/التقارير (Services)
│   ├── Models/                   # جوهر البيانات (EF Core)
│   ├── Views/                    # Razor (RTL، إتاحة WCAG 2.2 AA)
│   ├── Migrations/               # هجرات EF Core
│   └── Dockerfile                # صورة إنتاج مرحلتين (net10.0)
├── tests/Silk.Trading.Web.Tests  # xUnit (SQLite في الذاكرة — لا يتطلب LocalDB)
├── aspire/                       # قوالب Aspire (AppHost / ServiceDefaults)
├── docs/                         # خطة البناء + سجلات الإكمال + نشر Azure
└── .github/workflows/ci.yml      # بناء + اختبار + فحص CVE + بناء صورة Docker
```

## الميزات الرئيسية

- **محاسبة ثنائية**: قيد مزدوج تلقائي لكل عملية (ملكية، دفع، مرتجعات، صرف عملة، إقفال سنوي)، **تكلفة البضاعة المباعة (COGS) تُرحَّل تلقائيًا عند البيع**، قائمة دخل + ميزانية عمومية + ميزان مراجعة، دفتر تدقيق مركزي.
- **مخزون FIFO**: طبقات التكلفة (`StockLayer`)، النقل بين المستودعات، تسوية الجرد، مرتجعات تُعيد الطبقات بسعر الشراء الأصلي.
- **تجارة**: عملاء/موردون، بيع/شراء، أوامر شراء (RFQ→PO→استلام)، شحنات، دفعات مع تخصيص وتسوية أسعار صرف (FX) — **تُخصص الدفعات الأجنبية والوطنية بوحدات الفاتورة نفسها**، متعددة العملات والأفرع، وعروض أسعار للعملاء (طباعة/PDF وتحويل جماعي مع ربط عروض الموردين).
- **إدارة**: مستخدمون + أدوار + نظام أذونات دقيق (`PermissionCatalog`), تعدد مستودعات، إعدادات، عمليات جماعية (Batch) بأفضل جهد، إقفال سنة مالية بقفل.
- **تقارير**: PDF (فاتورة/ملصق بباركود Code128)، XLSX (قوائم + تدقيق + ميزانيات + قائمة واريانس).
- **API**: `POST /api/auth/token` (JWT) + نقاط أصناف/مخزون/فواتير/مدفوعات/قيود مع مفاتيح أذونات `ApiAuthorize`.
- **إتاحة**: WCAG 2.2 AA (مسح axe صفر انتهاكات على 41 صفحة)، RTL كامل، لوحة مفاتيح، `lang="ar" dir="rtl"`.

## التشغيل محليًا

```bash
# البناء والاختبار (أوامر C# تُنفذ من حلّ المشروع)
dotnet restore Silk.Trading.slnx
dotnet build Silk.Trading.slnx -c Release        # 0W/0E (تحذيرات كأخطاء)
dotnet test  Silk.Trading.slnx -c Release        # 201 اختبارًا (SQLite، لا DB خارجي)

# تشغيل التطبيق (وضع التطوير — يستخدم launchSettings على :5165)
dotnet run --project src/Silk.Trading.Web
```

افتح `http://localhost:5165`. قاعدة البيانات التلقائية: `(localdb)\mssqllocaldb`/`SilkTradingDb` — تُهيّأ وتـseeded عن الإقلاع (لا خطوات يدوية).

**الحسابات التجريبية** (تُنشأ عند الإقلاع):

| المستخدم | الدور | الكلمة |
|---|---|---|
| `admin` | Admin | `Admin@123` |
| `accountant` | Accountant | `Accountant@123` |
| `warehouse` | Warehouse | `Warehouse@123` |

> ⚠️ للدخول بالإنتاج غيّر كلمات سر الـ seed و `Jwt:Key`؛ الحارس يرفض إقلاع `Production` مع مفتاح placeholder (انظر أدناه).

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
ConnectionStrings__DefaultConnection="Server=sql;Database=SilkTradingDb;User Id=sa;Password=...;TrustServerCertificate=True" \
Cors__AllowedOrigins="https://app.example.com" \
ASPNETCORE_ENVIRONMENT=Production dotnet run --project src/Silk.Trading.Web --no-launch-profile
```

## CI / بوابة الجودة

`.github/workflows/ci.yml` يشغّل عند كل push/PR:
- **build-and-test**: restore → build Release (0W/0E) → `dotnet test` → رفع TRX.
- **package-vulnerability**: `dotnet list ... package --vulnerable --include-transitive` (يفشل عند أي CVE).
- **docker-image**: بناء صورة الويب + `docker inspect` (ExposedPorts + Healthcheck).

**الإتاحة (بوابة محلية يستحسن تشغيلها بعد تغيير الواجهة):** `node $env:TEMP\opencode\pw\p4cA11ySmoke.cjs` → تتوقع `GATE: PASS` (38 صفحة: 200 + صفر أخطاء console + صفر انتهاكات Critical/Serious). تفاصيل: `ACCESSIBILITY.md`.

## التوثيق

- `docs/BUILD-PLAN-README.md` — خطة المراحل M0→P5 وسجل الإكمال والتحقق لكل مرحلة.
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