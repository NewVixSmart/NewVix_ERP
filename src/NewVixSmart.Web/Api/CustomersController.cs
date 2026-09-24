using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewVixSmart.Web.Api.Dtos;
using NewVixSmart.Web.Data;
using NewVixSmart.Web.Extensions;

namespace NewVixSmart.Web.Api;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[IgnoreAntiforgeryToken]
public class CustomersController : ControllerBase
{
    private readonly AppDbContext _db;

    public CustomersController(AppDbContext db) => _db = db;

    [HttpGet("customers")]
    [ApiAuthorize("Customers.View")]
    public async Task<IActionResult> GetCustomers([FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search)
    {
        var query = _db.Customers
            .Where(c => c.IsActive)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 100) term = term[..100];
            query = query.Where(c => c.Name.Contains(term) || (c.Code != null && c.Code.Contains(term)));
        }

        query = query.OrderBy(c => c.Name);

        var ps = Math.Clamp(pageSize ?? 100, 1, 500);
        var p = Math.Max(1, page ?? 1);
        query = query.Skip((p - 1) * ps).Take(ps);

        var customers = await query
            .Select(c => new CustomerResponse
            {
                Id = c.Id,
                Name = c.Name,
                Code = c.Code,
                Phone = c.Phone,
                Email = c.Email,
                OpeningBalance = c.OpeningBalance,
                IsActive = c.IsActive
            })
            .ToListAsync();
        return Ok(customers);
    }
}
