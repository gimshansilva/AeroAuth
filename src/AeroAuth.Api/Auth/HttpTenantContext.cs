using AeroAuth.Core.Constants;
using AeroAuth.Core.Exceptions;
using AeroAuth.Core.Interfaces;

namespace AeroAuth.Api.Auth;

/// <summary>Builds the tenant context from the signed-in user's token claims (sub, role, org).</summary>
public class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    private System.Security.Claims.ClaimsPrincipal User => accessor.HttpContext?.User ?? new();

    public int UserId => int.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : 0;
    public string Role => User.FindFirst("role")?.Value ?? "";
    public int? OrganizationId => int.TryParse(User.FindFirst("org")?.Value, out var o) ? o : null;
    public bool IsAdmin => Role == Roles.AdminQA;
    public bool IsExternalAmo => Role == Roles.ExternalAMO;

    public int? ResolveScope()
    {
        if (!IsExternalAmo) return null;                    // internal QA: all organizations
        return OrganizationId ?? throw new TenantViolationException("This account is not linked to an organization.");
    }
}
