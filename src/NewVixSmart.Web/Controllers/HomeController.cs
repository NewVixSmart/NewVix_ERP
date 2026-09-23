using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NewVixSmart.Web.Extensions;
using NewVixSmart.Web.Models;
using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly IDashboardService _dashboardService;
    public HomeController(IDashboardService dashboardService) => _dashboardService = dashboardService;

    [RequirePerm("Reports.Dashboard")]
    public async Task<IActionResult> Index()
    {
        return View(await _dashboardService.GetDashboardAsync());
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}