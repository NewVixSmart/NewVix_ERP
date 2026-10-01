# قاعدة بيانات NewVixSmartDb — نظام النسخ الاحتياطي
# NewVixSmartDb — Database Backup System

يغطي هذا المستند النسخ الاحتياطي اليومي لقاعدة البيانات، والجدولة، والاستبقاء، وطريقة الاستعادة.
This document covers the daily database backup, scheduling, retention, and restore procedure.

**أساسيات / Basics**

| عنصر | قيمة |
|------|------|
| اسم قاعدة البيانات / Database | `NewVixSmartDb` |
| المثيل / Instance | `(localdb)\MSSQLLocalDB` |
| مجلد النسخ / Backup dir | `<repo>\backups` |
| صيغة الملف / File mask | `NewVixSmartDb_yyyyMMdd_HHmmss.bak` |
| مدة الاحتفاظ / Retention | 14 يوم (افتراضي) |

> **متطلب / Requirement:** تحتاج أداة `sqlcmd` لتشغيل النسخ. إذا لم تكن مثبتة، ثبّت "SQL Server Management Tools / SQLCMD Command Line Utilities" من [Learn — sqlcmd utility](https://learn.microsoft.com/sql/tools/sqlcmd/sqlcmd-utility).

---

## 1) التشغيل اليدوي / Manual run

```powershell
cd "C:\Users\engah\OneDrive\Documents\Default Project"

# نسخة افتراضية (NewVixSmartDb على (localdb)\MSSQLLocalDB)
.\scripts\backup-db.ps1

# تخصيص القيم / Custom values
.\scripts\backup-db.ps1 -Database NewVixSmartDb -BackupDir C:\Backups -RetainDays 7
```

سيتم إنشاء ملف باسم مثل `NewVixSmartDb_20260902_020000.bak` في مجلد `backups`.
يعود السكربت برمز خروج صفري (`0`) عند النجاح وغير صفري عند الفشل.

### ماذا يفعل السكربت قبل أن يقول "نجح"

النسخة الاحتياطية لا تُعتبر ناجحة بمجرد إنشاء ملف. بعد انتهاء `BACKUP DATABASE` يقرأ السكربت الملف الذي كتبه للتو:

| الفحص | ما الذي يمنعه |
|--------|----------------|
| `RESTORE VERIFYONLY` | لا يُبلّغ عن نجاح إلا إذا كان الملف سليمًا فعلًا؛ يُحذف الملف عند الفشل |
| `RESTORE HEADERONLY` | يتحقق من `DatabaseName` و`IsDamaged=0`، فلا يُبلّغ عن نسخة باسم غير المطلوب |
| `msdb.dbo.backupset` | يتحقق أن سجل الخادم نفسه يسجّل النسخة كغير تالفة |

إن فشل أي فحص، يُحذف الملف فلا يبقى شيء يشبه النسخة الاحتياطية، ويخرج السكربت برمز غير صفري.

> `-WithChecksum` (مُوصى به) يضيف page checksums. بدونها، ملف تالف البايتات يمرّ على `VERIFYONLY` كأنه سليم ويظهر الضرر متأخرًا كـ `Msg 824` على القاعدة المستعادة. مُقيس: تغيير 8 بايتات في منتصف ملف بلا checksums ⇒ «The backup set on file 1 is valid.» ثم استعادة ناجحة ثم تلف؛ ونفس التغيير على ملف `-WithChecksum` ⇒ `Msg 3189 Damage to the backup set was detected`.

**اكتشاف sqlcmd / sqlcmd discovery:** يبحث السكربت أولًا في `PATH`، ثم يفحص مسارات أدوات SQL Server الشائعة، ويمكن تمريره مباشرة عبر `-SqlCmd "C:\path\to\sqlcmd.exe"`.

### صلاحيات الكتابة على مجلد النسخ

`BACKUP DATABASE` يكتب الملف **بحساب خدمة SQL Server**، لا بحسابك. لذلك:

- مجلد ضمن `%TEMP%` قد ينجح فحص الكتابة لديك ثم يفشل بـ `Msg 3201` + `Operating system error 5` لأن حساب الخدمة لا يملك حقًا عليه. السكربت يذكر هذا صراحةً ويطبع أمر استعلام حساب الخدمة.
- مجلد مثل `...\MSSQL17.<INSTANCE>\MSSQL\Backup` قد لا تستطيع قراءته، وتكتبه الخدمة بنجاح. السكربت يتعامل مع ذلك كنجاح ولا يبلّغ عن «0 نسخة متبقية» وهو في الحقيقة لا يستطيع العدّ.

لمعرفة حساب الخدمة:
```powershell
Get-CimInstance Win32_Service | Where-Object Name -like 'MSSQL*' | Select-Object Name, StartName
```

---

## 2) الجدولة / Scheduling

ثبّت مهمة مجدولة في Windows لتشغيل النسخ يوميًا:

```powershell
# إنشاء / Register (افتراضي الساعة 02:00)
.\scripts\setup-backup-task.ps1

# وقت مختلف / Different time
.\scripts\setup-backup-task.ps1 -Time 03:30

# التشغيل حتى لو لم يسجّل دخول المستخدم
.\scripts\setup-backup-task.ps1 -Time 02:00 -RunWhetherLoggedOn

# تخصيص القاعدة والمجلد ونفس خيارات سكربت النسخ
.\scripts\setup-backup-task.ps1 -Database NewVixSmartDb -BackupDir C:\Backups `
    -RetainDays 30 -Instance '.\NEWVIX' -WithChecksum
```

- اسم المهمة / Task name: `NewVixSmartDailyBackup`
- المرور على الحساب الحالي / Runs as current user.
- `-StartWhenAvailable`: إذا كانت الجهاز مُطفأً أو نائمًا عند الموعد، تُنفَّذ عند أول فرصة بدل تخطّي اليوم بلا أثر.

> **`-File` لا `-Command`.** المعلمات المسجَّلة تستخدم `-File "<script path>"`. الشكل القديم `-Command "& "<path>""` يفشل مع أي مسار فيه مسافة: `C:\Users\...\Default Project\scripts\backup-db.ps1` يعطي `LastTaskResult = 1` ولا ينتج أي نسخة. مُقيس على هذا المستودع.

> **التجربة الإلزامية عند التسجيل.** السكربت يقرأ الأمر المسجَّل فعليًا من المهمة، يحلّله بقواعد `CommandLineToArgvW` (نفس قواعد Windows، لا قواعد PowerShell)، ثم **يشغّله ويحذف المهمة إن فشل أو لم ينتج ملفًا**. لا تُسجّل مهمة نسخ لا تعمل: تتحول إلى فشل صامت كل ليلة. لتخطّي ذلك: `-SkipTrialRun`.

> **`-RunWhetherLoggedOn` (S4U) لا يملك رمز شبكة.** مقبول لقاعدة محلية أو LocalDB؛ لا يصل إلى SQL Server على جهاز آخر.

**عرض / Inspect**:
```powershell
Get-ScheduledTask -TaskName NewVixSmartDailyBackup
Start-ScheduledTask -TaskName NewVixSmartDailyBackup   # تشغيل فوري
```

**كيف تتأكد أن التشغيل نجح فعلًا؟** رمز الخروج وحده لا يكفي (انظر `SkipTrialRun`):
```powershell
$t = Get-ScheduledTask -TaskName NewVixSmartDailyBackup
$t.Actions[0].Execute        # المسار التنفيذي
$t.Actions[0].Arguments      # وسائط الأمر كما نفّذها مجدول المهام
(Get-ScheduledTaskInfo -TaskName NewVixSmartDailyBackup).LastTaskResult   # 0 = نجاح
Get-ChildItem .\backups -Filter NewVixSmartDb_*.bak | Sort-Object LastWriteTime -Descending | Select-Object -First 3
```

---

## 3) الاستبقاء / Retention

الافتراضي `-RetainDays 14`:
- يحذف ملفات `.bak` الأقدم من 14 يومًا من مجلد النسخ.
- يمكن تغييره عند التشغيل أو في المهمة المجدولة.
- احتفِظ بمجلد النسخ في مكان به مساحة كافية ويفضّل على وحدة/نسخة مختلفة.

> **الاستبقاء محصور بقاعدة البيانات.** الحذف يطابق `<Database>_*.bak` فقط. السلوك القديم كان يحذف كل `*.bak` في المجلد، ف ضبط مجلد مشترك مع نظام آخر كان يمحو نسخ ذلك النظام بصمت. مُقيس: ملف `SilkTradingDb_20250101_000000.bak` في نفس المجلد كان يُحذف؛ الآن يبقى ويُذكر عدد الملفات التي لم تُمسّ.

---

## 4) الاستعادة / Restore

> **الاستعادة تُحذف فيها القاعدة الأصلية.** قبل الاستعادة خذ نسخة احتياطية جديدة إن كانت البيانات الحالية مهمة، وأغلق التطبيق الذي يستخدم القاعدة.

### أ) الأمر الصحيح: `WITH MOVE` و `-b`

```powershell
$src   = 'C:\...\backups\NewVixSmartDb_20260902_020000.bak'
$tgt   = 'NewVixSmartDb'
$stage = 'C:\...\restore-stage'

# 1) اقرأ أسماء الملفات المنطقية من النسخة نفسها
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -b -W -s "|" -Q "RESTORE FILELISTONLY FROM DISK = N'$src';"

# 2) استعد مع WITH MOVE (انظر أدناه لسبب إلزاميته)
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -b -Q @"
SET QUOTED_IDENTIFIER ON;
RESTORE DATABASE [$tgt] FROM DISK = N'$src'
  WITH MOVE N'<LogicalName_من_FILELISTONLY>' TO N'$stage\${tgt}.mdf',
       MOVE N'<LogicalName2>'                   TO N'$stage\${tgt}_log.ldf',
       REPLACE, RECOVERY;
"@
```

**`-b` إلزامي.** بدونه يطبع `sqlcmd` رسالة الخطأ ثم **يعود برمز خروج `0`**، فتبدو استعادة فاشلة ناجحة في أي سكربت يعتمد على `%ERRORLEVEL%`. مُقيس على هذا المستودع.

**`WITH MOVE` إلزامي للاستعادة إلى اسم مختلف.** بدونه تفشل العملية بـ `Msg 3156 / 1834` لأن ملفات النسخة ما زالت مرتبطة باسمها الأصلي وقاعدة البيانات الأصلية قائمة:

```
Msg 1834 ... The file 'C:\...\NewVixSmartDb.mdf' cannot be overwritten. It is being used by database 'NewVixSmartDb'.
Msg 3156 ... Use WITH MOVE to identify a valid location for the file.
Msg 3119/3013 ... RESTORE DATABASE is terminating abnormally.
```

> **الخطوة الموصى بها: تحقّق قبل أن تحذف شيئًا.** شغّل `RESTORE VERIFYONLY` على الملف **قبل** أي `DROP`. ملف مبتور (60%) يُرفض بـ `Msg 3241`، وملف فارغ بـ `Msg 3254`. التحقق بعد حذف القاعدة يكتشف أن النسخة الوحيدة غير قابلة للاستعادة **بعد** أن تكون القاعدة قد ذهبت.

### ب) التحقق قبل الاستعادة / Pre-flight check

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -b -Q "RESTORE VERIFYONLY FROM DISK = N'$src';"
# The backup set on file 1 is valid.
```

### ج) عبر SSMS (SQL Server Management Studio)
1. افتح الاتصال بـ `(localdb)\MSSQLLocalDB`.
2. انقر بزر الماوس الأيمن على **Databases → Restore Database**.
3. اختر **Device** ثم أضف ملف `.bak` المطلوب.
4. في **Files** تحقّق من تحديد *Move and overwrite the existing file(s)* إذا كان الاسم أو المسار مختلفًا.
5. اختر قاعدة الهدف `NewVixSmartDb` واضغط **OK**.

### د) فحص ما بعد الاستعادة

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -b -d NewVixSmartDb -Q "DBCC CHECKDB(NewVixSmartDb) WITH NO_INFOMSGS;"

# هل القاعدة جاهزة للاستعلام في أعمار الذمم؟
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C -b -W -Q "SELECT state_desc FROM sys.databases WHERE name = N'NewVixSmartDb';"
```

`VERIFYONLY` وحده **لا يثبت** أن الاستعادة تعمل؛ هو يثبت سلامة الوسائط فقط. العمليات داخل البيانات تحتاج فحصًا أعلاه.

---

## 5) قاعدة بيانات Docker Compose — مسار مختلف تمامًا

`backup-db.ps1` **لا يمكن** نسخ قاعدة بيانات `docker-compose.yml`: مصادقته بـ `-E` (مصادقة Windows المدمجة) ولا يستطيع تمرير كلمة مرور `sa` التي في ملف `.env` المستبعَد من git.

```powershell
# نسخة داخل الحاوية + نسخ إلى مجلد ./backups على المضيف
.\scripts\backup-db-container.ps1

# سرد النسخ المتاحة فقط، دون تغيير شيء
.\scripts\restore-db-container.ps1 -ListOnly

# استعادة (تحذير: تستبدل القاعدة بالكامل)
.\scripts\restore-db-container.ps1 -BackupFile <name>
```

**المتطلبات تُفحص في الخطوة الأولى:** كلا السكربتين يفحصان الآن المتطلبات في أول أسطرهما بدل الفشل لاحقًا برسالة تخدع:

| الحالة | ما كان يظهر | ما يظهر الآن |
|--------|--------------|--------------|
| `docker` غير موجود | `SQL_SA_PASSWORD variable is not set` | رسالة تسمّي Docker وتشرح أنه المسار الوحيد لقاعدة Compose |
| الـ CLI موجود والـ daemon متوقف | نفس الرسالة المضلّلة | `The Docker CLI is present (...) but the daemon is not responding` + `docker info` |
| الـ daemon يعمل و`docker compose` ناقص | فشل داخل `docker compose ps` | رسالة تسمّي إضافة Compose |

> **سلوك وقت التشغيل غير مُختبَر.** في بيئة التطوير هنا لا يعمل Docker daemon، لذا لم يُنفَّذ أيٌّ من مسارَي الحاوية: لا اكتشاف Compose ولا `sqlcmd` داخل الحاوية ولا صلاحيات unit 10001 ولا النسخ/الاحتفاظ/النسخ الخارجي للاستعادة. **لا تعتبر مسار الحاوية مُتحقَّقًا منه.**

> **الترتيب داخل `restore-db-container.ps1` مقصود.** فحص `VERIFYONLY` يقع **قبل** حذف القاعدة الهدف. ملف مبتور أو فارغ يُرفض والرسالة تقول `NOTHING HAS BEEN CHANGED` والقاعدة ما زالت قائمة. التحقق بعد الحذف يكشف أن النسخة غير قابلة للاستعادة **بعد** فقدان القاعدة.

---

## 6) التحقق من الدورة كاملة / Verifying the whole cycle

وجود الملف ورمز الخروج ليسا نسخة احتياطية. هذه الأداة تنشئ قاعدة بيانات مؤقتة، تشغّل `scripts/backup-db.ps1` الحقيقي، ثم تثبت أن الناتج يُستعاد:
```powershell
# من مجلد e2e/
npm run verify:backup

# أو مباشرة
node e2e\verify_backup_restore.cjs

#Against another local instance
node e2e\verify_backup_restore.cjs --instance ".\NEWVIX"
```

21 فحصًا، منها:

- ينشئ محتوى عربيًا (RTL، emoji، bidi) و`decimal(38,10)` و`money` و`datetime2` عند الحدود و`rowversion` وblob بحجم 1 MiB و60,000 صف.
- يبصم SHA-256 على كل ذلك **داخل SQL** حتى لا تمرّ العربية عبر codepage طرفية.
- يشغّل سكربت النسخ الحقيقي، ثم `VERIFYONLY` + `HEADERONLY` + `msdb` + قراءة حجم الملف.
- يستعيد **باسم قاعدة مختلف** مع `WITH MOVE`، ويوثّق أن الاستعادة **بدون** `WITH MOVE` تُرفض فعلًا.
- يقارن البصمة، والنص العربي صفًا صفًا، وقيم `rowversion`، ثم `DBCC CHECKDB`.
- **يفشل عمدًا**: تعديل 8 بايتات في منتصف الملف يجب أن يُرفض بـ `Msg 3189`، والملف المبتور بـ `Msg 3203`، ويؤكد أن قاعدة الهدف لم تُمَس.
- ينظّف قواعدته المؤقتة ويطبع `BACKUP_RESTORE_CYCLE_OK` أو `BACKUP_RESTORE_CYCLE_FAIL` (رمز خروج غير صفري).

> هذه الأداة تغطي مسار LocalDB/المثيل المحلي فقط، ولا تغطي مسار Docker (§5). قاعدة البيانات المؤقتة تُنشأ باسم `VixDrCycleSrc` وتُحذف؛ استخدم `--keep` للاحتفاظ بها.

### دورة الواجهة الكاملة (تهدم البيانات)

```powershell
$env:VIX_ALLOW_DESTRUCTIVE_RESET = '1'
node e2e\backup_full_cycle.cjs
```

`backup_full_cycle.cjs` يقود شاشة النسخ داخل التطبيق: إنشاء ← إعادة تعيين ← استعادة، **ويحذف كل بيانات التطبيق** في خطوة إعادة التعيين. لذلك يرفض التشغيل دون `VIX_ALLOW_DESTRUCTIVE_RESET=1` صراحةً. النسخة السابقة لم تكن تستطيع الفشل أبدًا: كانت تبتلع فشل تسجيل الدخول وتطبع `E2E_FULL_CYCLE_OK` دون أي تأكيد، فكان تشغيلها بالخطأ على قاعدة فيها بيانات يمحوها. الآن كل خطوة مُؤكَّدة ويلزمها عدد الصفوف نفسه بعد الاستعادة.

---

## 7) إلغاء المهمة / Unregister the task

```powershell
# إزالة المهمة المجدولة
.\scripts\setup-backup-task.ps1 -Unregister

# بديل يدوي
Unregister-ScheduledTask -TaskName NewVixSmartDailyBackup -Confirm:$false
```

---

## ملاحظات / Notes
- لا تُشغّل النسخ الاحتياطي والمهمة المجدولة أثناء تشغيل تطبيق مقفل لقاعدة البيانات؛ شغّلها في وقت خامل.
- تأكد من صلاحيات القراءة/الكتابة على مجلد `backups`.
- يمكنك فحص نجاح النسخ يوميًا عبر سجل الأحداث / Task Scheduler أو عبر وجود ملف `.bak` جديد.
