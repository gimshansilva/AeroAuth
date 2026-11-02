using AeroAuth.Core.Entities;
using AeroAuth.Core.Interfaces;

namespace AeroAuth.Infrastructure.Repositories;

public class ReferenceRepository(IUnitOfWork uow) : BaseRepository(uow), IReferenceRepository
{
    public Task<IReadOnlyList<ReferenceItem>> GetAircraftTypesAsync(bool includeInactive = false) =>
        QueryAsync<ReferenceItem>("SELECT AircraftTypeId AS Id, Name, IsActive FROM dbo.AircraftTypes WHERE (@all = 1 OR IsActive = 1) ORDER BY Name", new { all = includeInactive });

    public Task<IReadOnlyList<ReferenceItem>> GetIssuingNaasAsync(bool includeInactive = false) =>
        QueryAsync<ReferenceItem>("SELECT NaaId AS Id, Code AS Name, IsActive FROM dbo.IssuingNaas WHERE (@all = 1 OR IsActive = 1) ORDER BY Code", new { all = includeInactive });

    public Task<IReadOnlyList<ReferenceItem>> GetFreezeReasonsAsync(bool includeInactive = false) =>
        QueryAsync<ReferenceItem>("SELECT FreezeReasonId AS Id, Reason AS Name, IsActive FROM dbo.FreezeReasons WHERE (@all = 1 OR IsActive = 1) ORDER BY Reason", new { all = includeInactive });
}
