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

**اكتشاف sqlcmd / sqlcmd discovery:** يبحث السكربت أولًا في `PATH`، ثم يفحص مسارات أدوات SQL Server الشائعة، ويمكن تمريره مباشرة عبر `-SqlCmd "C:\path\to\sqlcmd.exe"`.

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
```

- اسم المهمة / Task name: `NewVixSmartDailyBackup`
- المرور على الحساب الحالي / Runs as current user.

**عرض / Inspect**:
```powershell
Get-ScheduledTask -TaskName NewVixSmartDailyBackup
Start-ScheduledTask -TaskName NewVixSmartDailyBackup   # تشغيل فوري
```

---

## 3) الاستبقاء / Retention

الافتراضي `-RetainDays 14`:
- يحذف ملفات `.bak` الأقدم من 14 يومًا من مجلد النسخ.
- يمكن تغييره عند التشغيل أو في المهمة المجدولة.
- احتفِظ بمجلد النسخ في مكان به مساحة كافية ويفضّل على وحدة/نسخة مختلفة.

---

## 4) الاستعادة / Restore

### أ) عبر RESTORE DATABASE (sqlcmd)
```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "RESTORE DATABASE [NewVixSmartDb] FROM DISK = N'<path>\NewVixSmartDb_20260902_020000.bak' WITH REPLACE"
```

> ملاحظة: قد تكون قاعدة البيانات مقفلة بواسطة تطبيق قيد التشغيل؛ أغلق التطبيق قبل الاستعادة.

### ب) عبر SSMS (SQL Server Management Studio)
1. افتح الاتصال بـ `(localdb)\MSSQLLocalDB`.
2. انقر بزر الماوس الأيمن على **Databases → Restore Database**.
3. اختر **Device** ثم أضف ملف `.bak` المطلوب.
4. اختر قاعدة الهدف `NewVixSmartDb` واضغط **OK**.

---

## 5) إلغاء المهمة / Unregister the task

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
