using Microsoft.AspNetCore.Mvc;

namespace NewVixSmart.Web.Extensions;

public static class PagerExtensions
{
    public const int PageSize = 50;

    public static int TotalPages(int total) =>
        Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));

    public static int NormalizePage(int page, int total) =>
        Math.Clamp(page, 1, TotalPages(total));

    public static void SetPager(this Controller controller, int page, int total, int pageItemCount, string? search = null)
    {
        controller.ViewBag.Page = page;
        controller.ViewBag.TotalPages = TotalPages(total);
        controller.ViewBag.TotalCount = total;
        controller.ViewBag.PageItemCount = pageItemCount;
        controller.ViewBag.Search = search;
    }
}
