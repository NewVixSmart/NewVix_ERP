using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Import;

namespace NewVixSmart.Web.Controllers;

[Authorize]
[RequirePerm("ImportCenter.View")]
public class ImportCenterController : Controller
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// Ceiling for a single upload, mirrored from the ImportCenterService side check. The service
    /// still owns the authoritative validation and must keep it - this layer exists only so an
    /// oversized body is refused BEFORE anything allocates for it.
    /// </summary>
    private const long MaxUploadBytes = 25L * 1024 * 1024;

    /// <summary>Envelope allowance over <see cref="MaxUploadBytes"/> for the multipart boundaries and the antiforgery field.</summary>
    private const long MaxUploadRequestBytes = MaxUploadBytes + 1024L * 1024;

    private readonly IImportCenterService _import;

    public ImportCenterController(IImportCenterService import) => _import = import;

    public IActionResult Index()
    {
        var vm = new ImportCenterViewModel
        {
            Items = _import.GetEntities()
                .Select(e => new ImportEntityItem(e.Key, e.NameAr, e.Description, e.Icon, e.Columns.Count, e.MatchNote))
                .ToList()
        };
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Template(string key)
    {
        var entity = _import.FindEntity(key);
        if (entity is null)
        {
            return NotFound();
        }

        var bytes = await _import.DownloadTemplateAsync(key);
        return File(bytes, XlsxContentType, $"newvixsmart_template_{entity.FilePrefix}.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    // Refused at the server, before the body is read into memory or spooled to disk, so a
    // multi-megabyte upload can no longer be used to force heap growth per concurrent request.
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    public async Task<IActionResult> Preview(string key, IFormFile file)
    {
        var entity = _import.FindEntity(key);
        if (entity is null)
        {
            return NotFound();
        }

        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "اختر ملفًا أولاً";
            return RedirectToAction(nameof(Index));
        }
        // Second, cheaper gate: the attributes above only bite on a real server, and the service
        // check only happens once the whole file is already a byte[] in memory. file.Length is the
        // declared length of an already-buffered IFormFile, so this is free and it is what keeps
        // the CopyToAsync + ToArray() below (~2x the file size of heap) from ever running.
        if (file.Length > MaxUploadBytes)
        {
            TempData["Error"] = "حجم الملف أكبر من الحد المسموح به (25 ميجابايت)";
            return RedirectToAction(nameof(Index));
        }
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var vm = await _import.ParseAsync(key, file.FileName, ms.ToArray());
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePerm("ImportCenter.Import")]
    public async Task<IActionResult> Apply(string key, string payload, string applyToken)
    {
        var entity = _import.FindEntity(key);
        if (entity is null)
        {
            return NotFound();
        }

        var result = await _import.ImportAsync(key, payload, applyToken);
        if (result.Success)
        {
            TempData["Success"] = result.Message;
        }
        else
        {
            TempData["Error"] = result.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
