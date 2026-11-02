using AeroAuth.Core.Entities;
using AeroAuth.Core.Exceptions;
using AeroAuth.Core.Interfaces;
using Dapper;

namespace AeroAuth.Infrastructure.Repositories;

public class StaffRepository(IUnitOfWork uow, ITenantContext tenant) : TenantRepository(uow, tenant), IStaffRepository
{
    private const string Columns = @"
        s.StaffId, s.OrganizationId, s.StationId, st.StationCode, s.StaffNumber, s.FullName, s.Email, s.SignaturePath, s.StampPath,
        s.IsActive, s.IsFrozen, s.IsTrainer, s.ReviewStatus, s.ReviewedByUserId, s.ReviewedAt, s.ReviewComment,
        s.CreatedByUserId, s.CreatedAt, s.UpdatedAt";

    private const string From = @"
        FROM dbo.Staff s
        JOIN dbo.Stations st ON st.StationId = s.StationId";

    // ---------------------------------------------------------------- read

    public Task<Staff?> GetByIdAsync(int id) =>
        QuerySingleOrDefaultAsync<Staff>($"SELECT {Columns} {From} WHERE s.StaffId = @id AND {TenantFilter("s")}", Params(new { id }));

    public Task<IReadOnlyList<Staff>> ListAsync() => SearchAsync(new StaffFilter());

    public Task<IReadOnlyList<Staff>> SearchAsync(StaffFilter f) =>
        QueryAsync<Staff>($@"SELECT {Columns} {From}
            WHERE {TenantFilter("s")}
              AND (@ReviewStatus IS NULL OR s.ReviewStatus = @ReviewStatus)
              AND (@IsFrozen IS NULL OR s.IsFrozen = @IsFrozen)
              AND (@StationId IS NULL OR s.StationId = @StationId)
              AND (@Search IS NULL OR s.FullName LIKE '%' + @Search + '%' OR s.StaffNumber LIKE '%' + @Search + '%')
            ORDER BY s.FullName",
            Params(new { f.ReviewStatus, f.IsFrozen, f.StationId, Search = string.IsNullOrWhiteSpace(f.Search) ? null : f.Search.Trim() }));

    /// <summary>
    /// Staff member with AML and scope records in ONE query, using Dapper multi-mapping (three types: Staff, StaffAml, StaffScope).
    /// Two LEFT JOINs multiply the rows (AML x scope), so the mapper adds each child only once.
    /// splitOn tells Dapper where the AML columns (StaffAmlId) and the scope columns (ScopeId) start.
    /// </summary>
    public async Task<Staff?> GetWithDetailsAsync(int id)
    {
        var sql = $@"
            SELECT {Columns},
                   a.StaffAmlId, a.StaffId, a.NaaId, n.Code AS NaaCode, a.AmlNumber, a.IssueDate, a.ExpiryDate,
                   sc.ScopeId, sc.StaffId, sc.AircraftTypeId, t.Name AS AircraftTypeName, sc.ScopeBasis, sc.StaffAmlId, sc.BackingNaaId,
                   sc.Category, sc.Level, sc.Location, sc.EffectiveFrom, sc.EffectiveTo
            {From}
            LEFT JOIN dbo.StaffAml a      ON a.StaffId = s.StaffId
            LEFT JOIN dbo.IssuingNaas n   ON n.NaaId = a.NaaId
            LEFT JOIN dbo.StaffScope sc   ON sc.StaffId = s.StaffId
            LEFT JOIN dbo.AircraftTypes t ON t.AircraftTypeId = sc.AircraftTypeId
            WHERE s.StaffId = @id AND {TenantFilter("s")}
            ORDER BY a.StaffAmlId, sc.ScopeId";

        var byId = new Dictionary<int, Staff>();
        await Connection.QueryAsync<Staff, StaffAml, StaffScope, Staff>(
            sql,
            (staff, aml, scope) =>
            {
                if (!byId.TryGetValue(staff.StaffId, out var parent)) byId[staff.StaffId] = parent = staff;
                if (aml is not null && parent.AmlRecords.All(x => x.StaffAmlId != aml.StaffAmlId)) parent.AmlRecords.Add(aml);
                if (scope is not null && parent.ScopeRecords.All(x => x.ScopeId != scope.ScopeId)) parent.ScopeRecords.Add(scope);
                return parent;
            },
            Params(new { id }),
            Transaction,
            splitOn: "StaffAmlId,ScopeId");

        return byId.Values.FirstOrDefault();
    }

    // ---------------------------------------------------------------- write

    public async Task<int> CreateAsync(Staff s)
    {
        EnsureWritable(s);   // an External AMO user can only create staff in their own organization
        // The database also rejects a station that belongs to a different organization (composite foreign key).
        return (await ExecuteScalarAsync<int>(@"
            INSERT dbo.Staff (OrganizationId, StationId, StaffNumber, FullName, Email, SignaturePath, StampPath, CreatedByUserId)
            OUTPUT INSERTED.StaffId
            VALUES (@OrganizationId, @StationId, @StaffNumber, @FullName, @Email, @SignaturePath, @StampPath, @CreatedByUserId)", s));
    }

    public async Task<bool> UpdateAsync(Staff s) =>
        await ExecuteAsync($@"
            UPDATE s
               SET s.StationId = @StationId, s.FullName = @FullName, s.Email = @Email, s.SignaturePath = @SignaturePath,
                   s.StampPath = @StampPath, s.IsActive = @IsActive, s.IsTrainer = @IsTrainer, s.UpdatedAt = SYSUTCDATETIME()
            FROM dbo.Staff s
            WHERE s.StaffId = @StaffId AND {TenantFilter("s")}",
            Params(new { s.StationId, s.FullName, s.Email, s.SignaturePath, s.StampPath, s.IsActive, s.IsTrainer, s.StaffId })) > 0;

    /// <summary>Soft delete: the person stays on file, because licenses, scope, training and the audit log refer to them.</summary>
    public async Task<bool> DeactivateAsync(int id) =>
        await ExecuteAsync($@"
            UPDATE s SET s.IsActive = 0, s.UpdatedAt = SYSUTCDATETIME()
            FROM dbo.Staff s WHERE s.StaffId = @id AND s.IsActive = 1 AND {TenantFilter("s")}", Params(new { id })) > 0;

    public async Task<int> AddAmlAsync(StaffAml aml)
    {
        await EnsureStaffInScopeAsync(aml.StaffId);
        return await ExecuteScalarAsync<int>(@"
            INSERT dbo.StaffAml (StaffId, NaaId, AmlNumber, IssueDate, ExpiryDate)
            OUTPUT INSERTED.StaffAmlId
            VALUES (@StaffId, @NaaId, @AmlNumber, @IssueDate, @ExpiryDate)", aml);
    }

    public async Task<int> AddScopeAsync(StaffScope scope)
    {
        await EnsureStaffInScopeAsync(scope.StaffId);
        return await ExecuteScalarAsync<int>(@"
            INSERT dbo.StaffScope (StaffId, AircraftTypeId, ScopeBasis, StaffAmlId, BackingNaaId, Category, Level, Location, EffectiveFrom, EffectiveTo)
            OUTPUT INSERTED.ScopeId
            VALUES (@StaffId, @AircraftTypeId, @ScopeBasis, @StaffAmlId, @BackingNaaId, @Category, @Level, @Location, @EffectiveFrom, @EffectiveTo)", scope);
    }

    /// <summary>Freeze or unfreeze. Only an approved person can be frozen, and only a frozen person can be unfrozen.</summary>
    public async Task<bool> SetFrozenAsync(int id, bool frozen) =>
        await ExecuteAsync($@"
            UPDATE s SET s.IsFrozen = @frozen, s.UpdatedAt = SYSUTCDATETIME()
            FROM dbo.Staff s
            WHERE s.StaffId = @id AND s.IsFrozen = @expected AND (@frozen = 0 OR s.ReviewStatus = 'Approved') AND {TenantFilter("s")}",
            Params(new { id, frozen, expected = !frozen })) > 0;

    /// <summary>Records the QA decision. Only a Pending record can be decided.</summary>
    public async Task<bool> SetReviewDecisionAsync(int id, string status, int reviewerUserId, string? comment) =>
        await ExecuteAsync($@"
            UPDATE s SET s.ReviewStatus = @status, s.ReviewedByUserId = @reviewerUserId, s.ReviewedAt = SYSUTCDATETIME(),
                         s.ReviewComment = @comment, s.UpdatedAt = SYSUTCDATETIME()
            FROM dbo.Staff s WHERE s.StaffId = @id AND s.ReviewStatus = 'Pending' AND {TenantFilter("s")}",
            Params(new { id, status, reviewerUserId, comment })) > 0;

    private async Task EnsureStaffInScopeAsync(int staffId)
    {
        var found = await ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.Staff s WHERE s.StaffId = @staffId AND {TenantFilter("s")}", Params(new { staffId }));
        if (found == 0) throw new NotFoundException("Staff member not found.");
    }
}
