using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Silk.Trading.Web.Api.Dtos;
using Silk.Trading.Web.Data;
using Silk.Trading.Web.Extensions;

namespace Silk.Trading.Web.Api;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SuppliersController : ControllerBase
{
    private readonly AppDbContext _db;

    public SuppliersController(AppDbContext db) => _db = db;

    [HttpGet("suppliers")]
    [ApiAuthorize("Suppliers.View")]
    public async Task<IActionResult> GetSuppliers([FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search)
    {
        var query = _db.Suppliers
            .Where(s => s.IsActive)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 100) term = term[..100];
            query = query.Where(s => s.Name.Contains(term) || (s.Code != null && s.Code.Contains(term)));
        }

        query = query.OrderBy(s => s.Name);

        if (page.HasValue)
        {
            var ps = Math.Clamp(pageSize ?? 100, 1, 500);
            var p = Math.Max(1, page.Value);
            query = query.Skip((p - 1) * ps).Take(ps);
        }

        var suppliers = await query
            .Select(s => new SupplierResponse
            {
                Id = s.Id,
                Name = s.Name,
                Code = s.Code,
                Phone = s.Phone,
                Email = s.Email,
                OpeningBalance = s.OpeningBalance,
                IsActive = s.IsActive
            })
            .ToListAsync();
        return Ok(suppliers);
    }
}
