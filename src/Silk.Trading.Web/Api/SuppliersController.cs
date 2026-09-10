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
    public async Task<IActionResult> GetSuppliers()
    {
        var suppliers = await _db.Suppliers
            .Where(s => s.IsActive)
            .AsNoTracking()
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
