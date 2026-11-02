using AeroAuth.Api.Auth;
using AeroAuth.Api.Models;
using AeroAuth.Core.Interfaces;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AeroAuth.Api.Controllers;

[ApiController, Route("api/auth")]
public class AuthController(IUnitOfWork uow, TokenService tokens, IWebHostEnvironment env, ITenantContext me) : ControllerBase
{
    /// <summary>Development only: signs in as any active user in dbo.Users. Returns 404 in other environments.</summary>
    [HttpPost("dev-login"), AllowAnonymous]
    public async Task<IActionResult> DevLogin(DevLoginRequest req)
    {
        if (!env.IsDevelopment()) return NotFound();
        var user = await uow.Connection.QuerySingleOrDefaultAsync<AuthUser>(
            "SELECT UserId, IdentityName, DisplayName, Role, OrganizationId FROM dbo.Users WHERE IdentityName = @Identity AND IsActive = 1", new { req.Identity });
        if (user is null) return Unauthorized(new { error = "This identity is not mapped to an active user." });
        return Ok(new { token = tokens.Create(user), user });
    }

    [HttpGet("me"), Authorize]
    public IActionResult Me() => Ok(new { me.UserId, me.Role, me.OrganizationId });
}
