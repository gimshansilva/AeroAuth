using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AeroAuth.Api.Auth;

public class AuthUser
{
    public int UserId { get; set; }
    public string IdentityName { get; set; } = "";
    public string? DisplayName { get; set; }
    public string Role { get; set; } = "";
    public int? OrganizationId { get; set; }
}

public class TokenService(IConfiguration cfg)
{
    public string Create(AuthUser u)
    {
        var claims = new List<Claim> { new("sub", u.UserId.ToString()), new("name", u.DisplayName ?? u.IdentityName), new("role", u.Role) };
        if (u.OrganizationId is int org) claims.Add(new Claim("org", org.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!));
        var token = new JwtSecurityToken(cfg["Jwt:Issuer"], cfg["Jwt:Audience"], claims, expires: DateTime.UtcNow.AddHours(8),
                                         signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
