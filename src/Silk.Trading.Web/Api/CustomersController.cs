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
public class CustomersController : ControllerBase
{
    private readonly AppDbContext _db;

    public CustomersController(AppDbContext db) => _db = db;

    [HttpGet("customers")]
    [ApiAuthorize("Customers.View")]
    public async Task<IActionResult> GetCustomers()
    {
        var customers = await _db.Customers
            .Where(c => c.IsActive)
            .AsNoTracking()
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
