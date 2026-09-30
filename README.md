# New Vix Smart — نظام إدارة تجاري عربي (دبل المحاسبة والمخزون)

نظام ويب متكامل لإدارة عمليات التداول التجاري باللغة العربية (RTL) على **ASP.NET Core 10 + Entity Framework Core + SQL Server** — يشمل إدارة الأصناف والمخزون (FIFO)، المبيعات والمشتريات، المرتجعات، الدفعات، المحاسبة ذات القيد المزدوج، القوائم المالية، الإقفال السنوي، الميزانيات، العمليات الجماعية، API عديمة الحالة (JWT)، والجداول/الباركود والتصدير.

> **العملة**: النظام **بالجنيه المصري فقط** (EGP). لا توجد عملات متعددة ولا أسعار صرف ولا فروق عملة؛ كل المبالغ بالجنيه المصري والعرض عبر `Services/Money.cs` والرمز `L.E`. القرار المعماري الكامل في [`docs/DECISION-EGP-ONLY.md`](docs/DECISION-EGP-ONLY.md).

> **الحالة**: المراحل المخططة P0–P4 مكتملة ومُتحقَّقة، واكتملت P5 (أمن → توثيق → القائمة العمرية وتنبيهات الاستحقاق → التدفق النقدي وكشوف الحساب)، و**M12 (حجز المخزون ← أذن التسليم ← الفوترة بعد التسليم)**. حزمة الاختبارات كاملة خضراء (`dotnet test NewVixSmart.slnx -c Release`)، بوابة إتاحة WCAG 2.2 AA مفعّلة، وتقارير تدقيق الأمان والمحاسبة مغلقة (0 حرج، 11 منخفضًا/متوسطًا قيد التلميع).

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
- **إتاحة**: WCAG 2.2 AA (مسح axe صفر انتهاكات على 99 مسارًا في الوضع الفاتح + 13 في الداكن)، RTL كامل، لوحة مفاتيح، `lang="ar" dir="rtl"`.

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
dotnet test  NewVixSmart.slnx -c Release        # الحزمة كاملة (SQLite، لا DB خارجي)

# تشغيل التطبيق (وضع التطوير — يستخدم launchSettings على :5165)
dotnet run --project src/NewVixSmart.Web
```

> **لماذا لا رقم للاختبارات هنا؟** رقمٌ مكتوب في README يتقادم مع أول اختبار يُضاف. العدد
> الحقيقي هو ما يطبعه `dotnet test` نفسه — وهو الأمر أعلاه. `DocClaimTests` يمنع عودة الرقم
> المكتوب، ويمنع تسمية بوابة بأمر غير `-c Release`.

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
cp .env.example .env    # ثم عبّئ SQL_SA_PASSWORD و JWT_KEY بقيمة قوية
docker compose up -d    # SQL Server 2022 + web على http://localhost:8080
```

- منفذ SQL Server مربوط بـ `127.0.0.1:1433` فقط (لا يُكشف على الشبكة). `db` و`web` و`db-backup` كلها `restart: unless-stopped`.
- `.env` يقرأ `SQL_SA_PASSWORD` (اسم المضيف) و`JWT_KEY`؛ الحاوية تستقبل `MSSQL_SA_PASSWORD` و`Jwt__Key` عبر `environment`. **لا تضع `Jwt__Key` في `.env`** — الاسم ثنائي الشرطة السفلية لا يصل إلى الحاوية تلقائيًا ولا يقرأه شيء.
- **تدوير كلمة مرور `sa`:** لا يستطيع المكوّن تغيير كلمة المرور دون knowing القديمة، لذا غيّرها داخل الحاوية ثم حدّث `.env` بالقيمة نفسها:
  ```bash
  docker compose exec db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$OLD_PASSWORD" -C -Q "ALTER LOGIN [sa] WITH PASSWORD = 'THE_NEW_ONE'"
  ```
  تحديث `SQL_SA_PASSWORD` في `.env` ثم `docker compose up -d db`. خادم SQL Server يتحقق من التعقيد مقابل نظام المضيف؛ استخدم قيمة قوية (حرف كبير وصغير ورقم ورمز، 16 حرفًا فأكثر).

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

`.github/workflows/ci.yml` يشغّل عند كل push/PR بسبع وظائف، وكلها مقيّدة بـ `timeout-minutes` ومثبّتة على SHA كامل للإجراءات:

| الوظيفة | ماذا تفعل | الصلاحيات |
|---|---|---|
| `build-and-test` | بوابة النص (`check-text-hygiene.ps1`) → restore → `dotnet format --verify-no-changes` → build Release (0W/0E) → `dotnet ef migrations has-pending-model-changes` → `dotnet test` → رفع TRX | `contents: read` فقط |
| `migration-chain-sqlserver` | خدمة SQL Server 2022 حقيقية، تُشغَّل عليها الحزمة كاملة، ثم يُقرأ ملف TRX للتأكد أن اختبارات `MigrationChainSqlServerTests` الستة نُفِّذت فعلًا (لا الاكتفاء بخروج صفر) → رفع TRX | `contents: read` فقط |
| `package-vulnerability` | `dotnet list ... package --vulnerable --include-transitive` (يفشل عند أي CVE) | `contents: read` فقط |
| `docker-image` | بناء صورة الويب + `docker inspect` (ExposedPorts + Healthcheck) + `/healthz` + بوابة إتاحة Playwright على 99 مسارًا فاتحًا و13 داكنًا | `contents: read` فقط |
| `codeql` | CodeQL v3 على C#، يرفع SARIF إلى Security | `contents: read` + `security-events: write` |
| `secret-scan` | gitleaks على كامل التاريخ، يعلّق على الـPR ويفتح تذكرة على الـpush | `contents: read` + `pull-requests: write` + `issues: write` |
| `dependency-review` | `fail-on-severity: moderate` + ملخص في تعليق الـPR عند الفشل | `contents: read` + `pull-requests: write` |

- `concurrency` على مستوى الملف: `cancel-in-progress: true` بمفتاح `workflow-PRnumber` — الدفع الجديد لنفس الـPR يوقف تشغيله السابق بدل انتظاره.
- `dotnet tool restore` يثبّت `dotnet-ef 10.0.11` من `.config/dotnet-tools.json` بدل `dotnet tool install --global` غير المقيّد.
- بوابة الإتاحة تستخدم `npm ci` (لا `npm install`) لأن `e2e/package-lock.json` مُودَع و`e2e/package.json` يستخدم نطاقات `^`.
- **PR من fork:** رمز `GITHUB_TOKEN` للقراءة فقط، فلا يمكن منح `security-events`/`pull-requests`/`issues` write. لذلك `codeql` و`secret-scan` يُتخطّيان صراحةً عند `head.repo.full_name != github.repository` بدل الفشل بـ`Resource not accessible by integration`. `dependency-review` يستمر (فرق التبعيات يعمل) لكن يتعذّر التعليق. الوظائف الأربع الباقية — `build-and-test` و`migration-chain-sqlserver` و`package-vulnerability` و`docker-image` — تغطّي الـfork كاملًا.

### البوابات الثلاث، وما لا تغطّيه كل واحدة
ثلاث بوابات مستقلّة، لكلٍّ منها نطاق لا تراه البقية:

| # | البوابة | ما تفرضه | **ما لا تغطّيه** |
|---|---|---|---|
| 1 | `dotnet format NewVixSmart.slnx --verify-no-changes --no-restore` | تنسيق ملفات `.cs`: `ENDOFLINE`، `IDE0011`، `WHITESPACE`، `FINALNEWLINE`، `CHARSET`، `IDE0161`، `IMPORTS`، `IDE0065`، وقواعد التسمية `IDE1006` (حقول خاصة `_camelCase` وأنواع PascalCase عند `warning`). الشجرة عند **0 مخالفة**. | **لا تفحص `.cshtml` ولا `.md` إطلاقًا** — proved by experiment: مخالفة مسافة زائدة في `_Layout.cshtml` تخرج `0`، ونفس المخالفة في ملف `.cs` تخرج `2`. و`IDE0055` (المُنسّق الكامل) `none`، فهي تفحص التخطيط لا رأي المُنسّق الكامل. |
| 2 | `pwsh -NoProfile -File scripts/check-text-hygiene.ps1` | 160 ملفًا في النطاق (**122** عرض Razor + **38** مستندًا): UTF-8 صارم بلا BOM، لا U+FFFD، لا CR، سطر نهائي واحد، لا مسافة/.tab لاحقة (عدا سطرين للمفاصل الصلبة في Markdown)، بلا tab في المسافة البادئة. **قراءة فقط**: تسمّي ولا تُصلح. | **تحذير لا منع** على `R8` (محارف من نص غريب داخل سطر عربي) و`R10` (tab كفاصل أعمدة) — تحكم بشري مطلوب، وهي لا تُحمرّر البناء. مستثناة: `wwwroot/lib` (مُستورد من)، `test-results/` و`TestResults/` و`artifacts/` و`screenshots/` (مخرجات)، و`Migrations/*.Designer.cs` (يعيد `dotnet ef migrations add` كتابته من جديد في كل مرة، فلا جدوى من تطبيعه). |
| 3 | وظيفة `migration-chain-sqlserver` | تُنفّذ سلسلة الـ38 هجرة على محرك **SQL Server حقيقي** (حاوية خدمة `mssql/server:2022`) ثم تُقرأ الـTRX للتأكد أن اختبارات `MigrationChainSqlServerTests` الستة `[SqlServerFact]` نُفِّذت كلها ولم تُتخطَّى. | **لا تُغني عن** فحص `has-pending-model-changes` (مقارنة النموذج بلقطة الهجرات) — تلك وظيفة `build-and-test`. ولا تعمل إلا على `ubuntu-latest`: محليًا تُتخطّى اختباراتها بـ`NVS_TEST_SQLSERVER` فارغًا، فوجودها في CI وحده هو ما يجعلها تُنفَّذ فعلًا. |

> **لا تقرأ هذا كأن البوابات تغطّي المستودع كله.** ملف `.cshtml` أو `.md` جديد لا يراه
> `dotnet format` إطلاقًا، فالترتيب السيئ للـusing في `.cs` لا يمرّ على البوابة رقم 2 أصلًا.
> اقرأ كل صف من العمود الأخير كحدّ لبوابته لا كوصف لها: اجتياز البوابات الثلاثة معنى أنه
> التالي مشمول، وليس أن كل شيء مشمول.

**الإتاحة (بوابة محلية يستحسن تشغيلها بعد تغيير الواجهة):** من داخل `e2e/`: `npm ci && npm run a11y` → تتوقع `GATE: PASS (99 light routes + 13 dark, ...)` (200 + صفر أخطاء console + صفر انتهاكات Critical/Serious). نفس ما تنفّذه وظيفة `docker-image` في CI بالضبط. التفاصيل: `ACCESSIBILITY.md`.

## التوثيق

- `docs/BUILD-PLAN-README.md` — خطة المراحل M0→P5 وسجل الإكمال والتحقق لكل مرحلة.
- `plan/feature-sales-reservation-delivery-invoice-flow-1.md` — خطة M12 (حجز المخزون ← أذن التسليم ← الفوترة بعد التسليم) مع 71 مهمة وقراراتها.
- `AGENTS.md` — قواعد العمل الإلزامية للوكلاء (قبل التعديل: راجع README وACCESSIBILITY.md؛ الاختبارات إلزامية).
- `docs/DEPLOY-AZURE.md` / `docs/DEPLOY-AZURE-EN.md` — نشر Azure App Service + SQL.
- `docs/BACKUP.md`, `scripts/` — نسخ احتياطي وجدولتها.

### النسخ الاحتياطي (انظر `docs/BACKUP.md`)

| البيئة | النسخة | الجدولة |
|---|---|---|
| حاوية Docker | `scripts/backup-db-container.ps1` | `docker compose --profile db-backup up -d db-backup` (يوميًا، احتفاظ 14 ملفًا) |
| LocalDB (تطوير) | `scripts/backup-db.ps1` | مهمة مجدولة عبر `scripts/setup-backup-task.ps1` |

- **النسخ الاحتياطي للحاوية لا تعمل إلا مع `--profile db-backup`** — الخدمة اختيارية عن قصد حتى لا تعمل حلقة يومية على جهاز لا يحتاجها.
- الملفات تُكتب في volume اسمه `sqlserver-backup` داخل الحاوية، أي داخل `DOCKER_VOLUME_DIR`. **انسخها خارج المضيف**: volume يختفي مع `docker compose down -v` ولا يوجد في أي نسخة احتياطية للمضيف. السكربت يطبع مسار الحاوية والمضيف لكل ملف.
- الاستعادة من الحاوية عبر `scripts/restore-db-container.ps1` فقط: يقرأ قائمة الملفات من الـbackup نفسه، يسأل عن التأكيد قبل الكتابة فوق أي قاعدة، ثم يشغّل `DBCC CHECKDB` بعد الاستعادة. **لا تسترجع `.bak` فوق قاعدة عبر `sqlcmd` مباشرة** — أسماء الملفات المنطقية لا تطابق بالضرورة، و`RESTORE` بلا `MOVE` يفشل أو ينشئ ملفات في مكان خاطئ.
- `scripts/backup-db.ps1` و`setup-backup-task.ps1` يدعمان **LocalDB فقط** ويرفضان أي خادم آخر. لنشر Docker استخدم سكربت الحاوية.
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
