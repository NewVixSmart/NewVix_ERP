using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Silk.Trading.Web.Services;

namespace Silk.Trading.Web.Controllers;

[Authorize(Roles = "Admin")]
public class BackupController : Controller
{
    private const string ConfirmationWord = "حذف نهائي";
    private readonly IBackupService _backup;
    private readonly IBrandingService _branding;
    private readonly IPrintSettingsService _printSettings;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly ILogger<BackupController> _logger;

    public BackupController(IBackupService backup, IBrandingService branding, IPrintSettingsService printSettings,
        SignInManager<IdentityUser> signInManager, ILogger<BackupController> logger)
    {
        _backup = backup;
        _branding = branding;
        _printSettings = printSettings;
        _signInManager = signInManager;
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View(_backup.ListBackups());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create()
    {
        try
        {
            await _backup.CreateBackupAsync();
            TempData["Success"] = "تم إنشاء النسخة الاحتياطية بنجاح.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup create failed");
            TempData["Error"] = "حدث خطأ أثناء إنشاء النسخة الاحتياطية.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string fileName)
    {
        try
        {
            await _backup.DeleteAsync(fileName);
            TempData["Success"] = "تم حذف النسخة الاحتياطية بنجاح.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup delete failed for {FileName}", fileName);
            TempData["Error"] = "حدث خطأ أثناء حذف النسخة الاحتياطية.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(string fileName)
    {
        try
        {
            await _backup.RestoreAsync(fileName);
            _branding.Invalidate();
            _printSettings.Invalidate();
            await _signInManager.SignOutAsync();
            TempData["Success"] = "تمت الاستعادة بنجاح. يرجى تسجيل الدخول مرة أخرى.";
            return RedirectToAction("Login", "Account");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore failed from {FileName}", fileName);
            TempData["Error"] = "حدث خطأ أثناء استعادة النسخة الاحتياطية.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset(string confirmText)
    {
        if (!string.Equals(confirmText, ConfirmationWord, StringComparison.Ordinal))
        {
            TempData["Error"] = "كلمة التأكيد غير صحيحة. لم يتم تنفيذ إعادة الضبط.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await _backup.ResetSystemAsync();
            _branding.Invalidate();
            _printSettings.Invalidate();
            await _signInManager.SignOutAsync();
            TempData["Success"] = "تمت إعادة ضبط النظام بنجاح. تم حذف جميع البيانات. يرجى تسجيل الدخول مرة أخرى.";
            return RedirectToAction("Login", "Account");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System reset failed");
            TempData["Error"] = "حدث خطأ أثناء إعادة ضبط النظام.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Download(string fileName)
    {
        try
        {
            var bytes = await _backup.ReadBackupBytesAsync(fileName);
            return File(bytes, "application/octet-stream", Path.GetFileName(fileName));
        }
        catch
        {
            return NotFound();
        }
    }
}