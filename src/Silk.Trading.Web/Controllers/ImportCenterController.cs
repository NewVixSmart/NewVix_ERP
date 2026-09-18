using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Silk.Trading.Web.Extensions;
using Silk.Trading.Web.Services;
using Silk.Trading.Web.ViewModels.Import;

namespace Silk.Trading.Web.Controllers;

[Authorize]
[RequirePerm("ImportCenter.View")]
public class ImportCenterController : Controller
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

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
        if (entity is null) return NotFound();
        var bytes = await _import.DownloadTemplateAsync(key);
        return File(bytes, XlsxContentType, $"silktrading_template_{entity.FilePrefix}.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(string key, IFormFile file)
    {
        var entity = _import.FindEntity(key);
        if (entity is null) return NotFound();
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "اختر ملفًا أولاً";
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
        if (entity is null) return NotFound();
        var result = await _import.ImportAsync(key, payload, applyToken);
        if (result.Success)
            TempData["Success"] = result.Message;
        else
            TempData["Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }
}