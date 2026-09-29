using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.Extensions;

public sealed class RequirePermAttribute : TypeFilterAttribute
{
    public RequirePermAttribute(string key) : base(typeof(RequirePermFilter))
    {
        Arguments = new object[] { key };
        Order = -10;
    }
}

public sealed class RequirePermFilter : IAsyncAuthorizationFilter
{
    private readonly string _key;
    private readonly IPermissionService _permissions;

    public RequirePermFilter(IPermissionService permissions, string key)
    {
        _permissions = permissions;
        _key = key;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new RedirectToActionResult("Login", "Account", null);
            return;
        }
        if (await _permissions.HasAsync(_key))
        {
            return;
        }

        context.Result = new RedirectToActionResult("AccessDenied", "Account", null);
    }
}
