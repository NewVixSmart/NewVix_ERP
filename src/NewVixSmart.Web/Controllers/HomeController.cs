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

    /// <summary>
    /// Renders 4xx/5xx responses through the site layout so every error page keeps a language
    /// declaration and a page title. Without it a bare NotFound() answers with an untitled,
    /// lang-less body, which fails WCAG 3.1.1 and 2.4.2.
    /// </summary>
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult StatusCode(int? code)
    {
        Response.StatusCode = code is >= 400 and <= 599 ? code.Value : StatusCodes.Status404NotFound;
        ViewData["Title"] = Response.StatusCode switch
        {
            StatusCodes.Status404NotFound => "الصفحة غير موجودة",
            StatusCodes.Status403Forbidden => "لا تملك صلاحية الوصول",
            StatusCodes.Status401Unauthorized => "يلزم تسجيل الدخول",
            >= 500 => "خطأ في الخادم",
            _ => "طلب غير صالح"
        };
        return View();
    }
}
