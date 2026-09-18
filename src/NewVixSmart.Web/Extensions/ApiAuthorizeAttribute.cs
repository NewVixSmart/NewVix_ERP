using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NewVixSmart.Web.Services;

namespace NewVixSmart.Web.Extensions;

public sealed class ApiAuthorizeAttribute : TypeFilterAttribute
{
    public ApiAuthorizeAttribute(string key) : base(typeof(ApiAuthorizeFilter))
    {
        Arguments = new object[] { key };
        Order = -10;
    }
}

public sealed class ApiAuthorizeFilter : IAsyncAuthorizationFilter
{
    private readonly string _key;
    private readonly IPermissionService _permissions;

    public ApiAuthorizeFilter(IPermissionService permissions, string key)
    {
        _permissions = permissions;
        _key = key;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }
        if (await _permissions.HasAsync(_key)) return;
        context.Result = new ObjectResult(new { message = "Forbidden" }) { StatusCode = 403 };
    }
}
