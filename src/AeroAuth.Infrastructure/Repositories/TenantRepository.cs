using System.Data;
using AeroAuth.Core.Entities;
using AeroAuth.Core.Exceptions;
using AeroAuth.Core.Interfaces;
using Dapper;

namespace AeroAuth.Infrastructure.Repositories;

/// <summary>
/// Base for repositories of tenant data. The organization filter is added here, from the signed-in user, never from the
/// request: an External AMO user always gets their own organization, an internal QA user gets all.
/// </summary>
public abstract class TenantRepository(IUnitOfWork uow, ITenantContext tenant) : BaseRepository(uow)
{
    /// <summary>The organization this call is limited to, or null for internal QA (all organizations).</summary>
    protected int? TenantId => tenant.ResolveScope();

    /// <summary>SQL condition that limits a query to the caller's organization. Always uses the @TenantId parameter.</summary>
    protected static string TenantFilter(string alias) => $"(@TenantId IS NULL OR {alias}.OrganizationId = @TenantId)";

    /// <summary>Parameters for a query: the caller's values plus @TenantId.</summary>
    protected DynamicParameters Params(object? values = null)
    {
        var p = new DynamicParameters(values);
        p.Add("TenantId", TenantId, DbType.Int32);
        return p;
    }

    /// <summary>Stops an External AMO user from writing a record for another organization.</summary>
    protected void EnsureWritable(ITenantEntity entity)
    {
        var scope = TenantId;
        if (scope is not null && entity.OrganizationId != scope)
            throw new TenantViolationException("You cannot write data for another organization.");
    }
}
