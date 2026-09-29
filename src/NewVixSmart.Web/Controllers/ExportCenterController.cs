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
    private readonly IPermissionService _perms;

    public ExportCenterController(IExportCenterService export, IPermissionService perms)
    {
        _export = export;
        _perms = perms;
    }

    public async Task<IActionResult> Index()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in ExportCenterDatasets.Map.Keys)
        {
            if (await CanReadDatasetAsync(key))
            {
                allowed.Add(key);
            }
        }

        var vm = new ExportCenterViewModel
        {
            Items = _export.GetCatalog().Where(o => allowed.Contains(o.Key)).ToList()
        };
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> ExportXlsx(string key)
    {
        var option = _export.GetCatalog().FirstOrDefault(o => o.Key == key);
        if (option is null)
        {
            return NotFound();
        }

        if (!await CanReadDatasetAsync(key))
        {
            return Forbid();
        }

        var bytes = await _export.ExportXlsxAsync(key);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"newvixsmart_{key}_{DateTime.Today:yyyyMMdd}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportCsv(string key)
    {
        var option = _export.GetCatalog().FirstOrDefault(o => o.Key == key);
        if (option is null)
        {
            return NotFound();
        }

        if (!option.HasCsv)
        {
            return BadRequest();
        }

        if (!await CanReadDatasetAsync(key))
        {
            return Forbid();
        }

        var bytes = await _export.ExportCsvAsync(key);
        return File(bytes, "text/csv; charset=utf-8",
            $"newvixsmart_{key}_{DateTime.Today:yyyyMMdd}.csv");
    }

    /// <summary>
    /// A dataset is only exportable when the caller holds both the owning module's
    /// <c>Export</c> right and that module's read right, so holding
    /// <c>ExportCenter.View</c> alone never leaks data the caller cannot already read.
    /// </summary>
    private async Task<bool> CanReadDatasetAsync(string key)
    {
        if (!ExportCenterDatasets.TryGet(key, out var access))
        {
            return false;
        }

        return await _perms.HasAsync(access.ExportKey) && await _perms.HasAsync(access.SourceViewKey);
    }
}
