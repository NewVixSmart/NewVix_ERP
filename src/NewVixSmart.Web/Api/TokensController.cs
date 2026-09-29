using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using NewVixSmart.Web.Api.Dtos;
using NewVixSmart.Web.Infrastructure;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace NewVixSmart.Web.Api;

[ApiController]
[Route("api/auth")]
[IgnoreAntiforgeryToken]
public class TokensController : ControllerBase
{
    /// <summary>
    /// A bearer token is a bearer credential with no revocation channel of its own, so the
    /// damage window of a stolen copy is exactly its remaining lifetime. 15 minutes keeps that
    /// window small enough that a leak is a nuisance rather than a quarter of a shift, and
    /// <see cref="AccountController.Logout"/> rotates the security stamp, which invalidates every
    /// outstanding token for the user through the TokenStampChecks comparison in the JwtBearer
    /// OnTokenValidated event. Both properties are needed: a short TTL limits the theft that
    /// happens while the user is still logged in, the stamp rotation limits what survives logout.
    /// </summary>
    private const int DefaultAccessTokenMinutes = 15;

    /// <summary>Hard ceiling so a misconfigured Jwt__AccessTokenMinutes cannot silently reinstate a long-lived token.</summary>
    private const int MaxAccessTokenMinutes = 60;

    private readonly UserManager<IdentityUser> _userManager;
    private readonly IConfiguration _configuration;

    public TokensController(UserManager<IdentityUser> userManager, IConfiguration configuration)
    {
        _userManager = userManager;
        _configuration = configuration;
    }

    [HttpPost("token")]
    [AllowAnonymous]
    [EnableRateLimiting("token")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CreateToken([FromBody] TokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "اسم المستخدم وكلمة المرور مطلوبان" });

        var user = await _userManager.FindByNameAsync(request.Username);
        if (user == null)
            return Unauthorized(new { message = "Invalid credentials" });

        if (await _userManager.IsLockedOutAsync(user))
            return Unauthorized(new { message = "Invalid credentials" });

        if (!await _userManager.CheckPasswordAsync(user, request.Password))
        {
            await _userManager.AccessFailedAsync(user);
            return Unauthorized(new { message = "Invalid credentials" });
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        var roles = await _userManager.GetRolesAsync(user);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(TokenStampChecks.StampClaimType, await _userManager.GetSecurityStampAsync(user)),
        };
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            _configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key not configured")));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var accessTokenMinutes = Math.Clamp(
            _configuration.GetValue("Jwt:AccessTokenMinutes", DefaultAccessTokenMinutes),
            1, MaxAccessTokenMinutes);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(accessTokenMinutes),
            signingCredentials: creds);

        return Ok(new TokenResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAt = token.ValidTo
        });
    }
}
