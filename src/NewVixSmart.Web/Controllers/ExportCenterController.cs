using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Services;
using NewVixSmart.Web.ViewModels.Export;

namespace NewVixSmart.Web.Controllers;

[Authorize]
[RequirePerm("ExportCenter.View")]
public class ExportCenterController : Controller
{
    private readonly IExportCenterService _export;
    public ExportCenterController(IExportCenterService export) => _export = export;

    public IActionResult Index()
    {
        var vm = new ExportCenterViewModel { Items = _export.GetCatalog() };
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> ExportXlsx(string key)
    {
        var option = _export.GetCatalog().FirstOrDefault(o => o.Key == key);
        if (option is null) return NotFound();
        var bytes = await _export.ExportXlsxAsync(key);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"newvixsmart_{key}_{DateTime.Today:yyyyMMdd}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportCsv(string key)
    {
        var option = _export.GetCatalog().FirstOrDefault(o => o.Key == key);
        if (option is null) return NotFound();
        if (!option.HasCsv) return BadRequest();
        var bytes = await _export.ExportCsvAsync(key);
        return File(bytes, "text/csv; charset=utf-8",
            $"newvixsmart_{key}_{DateTime.Today:yyyyMMdd}.csv");
    }
}