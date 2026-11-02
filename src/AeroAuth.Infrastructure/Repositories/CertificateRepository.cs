using AeroAuth.Core.Entities;
using AeroAuth.Core.Interfaces;

namespace AeroAuth.Infrastructure.Repositories;

public class CertificateRepository(IUnitOfWork uow, ITenantContext tenant) : TenantRepository(uow, tenant), ICertificateRepository
{
    public async Task<int> CreateAsync(Certificate c)
    {
        EnsureWritable(c);
        return await ExecuteScalarAsync<int>(@"
            INSERT dbo.Certificates (OrganizationId, SessionId, AttendeeId, CertificateNumber, IssuedByUserId)
            OUTPUT INSERTED.CertificateId
            VALUES (@OrganizationId, @SessionId, @AttendeeId, @CertificateNumber, @IssuedByUserId)", c);
    }

    public Task<IReadOnlyList<Certificate>> ListBySessionAsync(int sessionId) =>
        QueryAsync<Certificate>($@"
            SELECT c.CertificateId, c.OrganizationId, c.SessionId, c.AttendeeId, c.CertificateNumber, c.Status, c.IssuedAt, c.IssuedByUserId
            FROM dbo.Certificates c WHERE c.SessionId = @sessionId AND {TenantFilter("c")} ORDER BY c.CertificateNumber", Params(new { sessionId }));
}
