using AeroAuth.Core.Entities;
using AeroAuth.Core.Exceptions;
using AeroAuth.Core.Interfaces;

namespace AeroAuth.Infrastructure.Repositories;

public class TrainingSessionRepository(IUnitOfWork uow, ITenantContext tenant) : TenantRepository(uow, tenant), ITrainingSessionRepository
{
    private const string Select = @"
        SELECT t.SessionId, t.OrganizationId, o.Abbreviation AS OrganizationAbbreviation, t.StationId, t.TrainerStaffId, t.SessionDate,
               t.Venue, t.AttendanceSheetPath, t.Status, t.ReviewedByUserId, t.ReviewedAt, t.ReviewComment, t.CreatedByUserId, t.CreatedAt
        FROM dbo.TrainingSessions t JOIN dbo.Organizations o ON o.OrganizationId = t.OrganizationId";

    public Task<TrainingSession?> GetByIdAsync(int id) =>
        QuerySingleOrDefaultAsync<TrainingSession>($"{Select} WHERE t.SessionId = @id AND {TenantFilter("t")}", Params(new { id }));

    public Task<IReadOnlyList<TrainingSession>> ListAsync(string? status = null) =>
        QueryAsync<TrainingSession>($"{Select} WHERE (@status IS NULL OR t.Status = @status) AND {TenantFilter("t")} ORDER BY t.SessionDate DESC, t.SessionId DESC",
            Params(new { status }));

    public async Task<int> CreateAsync(TrainingSession s)
    {
        EnsureWritable(s);
        var trainerOk = await ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM dbo.Staff WHERE StaffId = @TrainerStaffId AND OrganizationId = @OrganizationId
              AND IsTrainer = 1 AND IsActive = 1 AND IsFrozen = 0 AND ReviewStatus = 'Approved'", s);
        if (trainerOk == 0) throw new ValidationFailedException("The trainer must be an approved, active, unfrozen trainer of this organization.");

        var id = await ExecuteScalarAsync<int>(@"
            INSERT dbo.TrainingSessions (OrganizationId, StationId, TrainerStaffId, SessionDate, Venue, AttendanceSheetPath, CreatedByUserId)
            OUTPUT INSERTED.SessionId
            VALUES (@OrganizationId, @StationId, @TrainerStaffId, @SessionDate, @Venue, @AttendanceSheetPath, @CreatedByUserId)", s);

        foreach (var m in s.Modules)
        {
            // the module type is copied from the material, so the two can never disagree
            var n = await ExecuteAsync(@"
                INSERT dbo.SessionModules (SessionId, MaterialId, ModuleType)
                SELECT @id, MaterialId, ModuleType FROM dbo.TrainingMaterials WHERE MaterialId = @MaterialId", new { id, m.MaterialId });
            if (n == 0) throw new ValidationFailedException($"Training material {m.MaterialId} does not exist.");
        }

        foreach (var a in s.Attendees)
        {
            var n = await ExecuteAsync(@"
                INSERT dbo.SessionAttendees (OrganizationId, SessionId, StaffId, MissingEmail)
                SELECT @OrganizationId, @id, StaffId, CASE WHEN Email IS NULL THEN 1 ELSE 0 END
                FROM dbo.Staff WHERE StaffId = @StaffId AND OrganizationId = @OrganizationId AND ReviewStatus = 'Approved'",
                new { id, s.OrganizationId, a.StaffId });
            if (n == 0) throw new ValidationFailedException($"Staff {a.StaffId} is not an approved staff member of this organization.");
        }
        return id;
    }

    public async Task<bool> SetReviewDecisionAsync(int id, string status, int reviewerUserId, string? comment) =>
        await ExecuteAsync($@"
            UPDATE t SET t.Status = @status, t.ReviewedByUserId = @reviewerUserId, t.ReviewedAt = SYSUTCDATETIME(), t.ReviewComment = @comment
            FROM dbo.TrainingSessions t WHERE t.SessionId = @id AND t.Status = 'Pending' AND {TenantFilter("t")}",
            Params(new { id, status, reviewerUserId, comment })) > 0;

    public Task<IReadOnlyList<SessionAttendee>> GetAttendeesAsync(int sessionId) =>
        QueryAsync<SessionAttendee>($@"
            SELECT a.AttendeeId, a.OrganizationId, a.SessionId, a.StaffId, a.MissingEmail, a.RefresherGap
            FROM dbo.SessionAttendees a
            WHERE a.SessionId = @sessionId AND {TenantFilter("a")}
              AND NOT EXISTS (SELECT 1 FROM dbo.Certificates c WHERE c.AttendeeId = a.AttendeeId AND c.Status = 'Issued')
            ORDER BY a.AttendeeId", Params(new { sessionId }));
}
