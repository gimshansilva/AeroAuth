using AeroAuth.Core.Entities;

namespace AeroAuth.Core.Interfaces;

/// <summary>Async CRUD every repository offers. Delete is a soft delete (deactivate) so history stays valid.</summary>
public interface IRepository<TEntity>
{
    Task<TEntity?> GetByIdAsync(int id);
    Task<IReadOnlyList<TEntity>> ListAsync();
    Task<int> CreateAsync(TEntity entity);
    Task<bool> UpdateAsync(TEntity entity);
    Task<bool> DeactivateAsync(int id);
}

public class StaffFilter
{
    public string? Search { get; set; }
    public string? ReviewStatus { get; set; }
    public bool? IsFrozen { get; set; }
    public int? StationId { get; set; }
}

public interface IStaffRepository : IRepository<Staff>
{
    Task<IReadOnlyList<Staff>> SearchAsync(StaffFilter filter);
    /// <summary>One staff member with AML and scope records, loaded in a single query with Dapper multi-mapping.</summary>
    Task<Staff?> GetWithDetailsAsync(int id);
    Task<int> AddAmlAsync(StaffAml aml);
    Task<int> AddScopeAsync(StaffScope scope);
    Task<bool> SetFrozenAsync(int id, bool frozen);
    Task<bool> SetReviewDecisionAsync(int id, string status, int reviewerUserId, string? comment);
}

public interface ITrainingSessionRepository
{
    Task<TrainingSession?> GetByIdAsync(int id);
    Task<IReadOnlyList<TrainingSession>> ListAsync(string? status = null);
    /// <summary>Inserts the session with its modules and attendees. Run it inside a transaction.</summary>
    Task<int> CreateAsync(TrainingSession session);
    Task<bool> SetReviewDecisionAsync(int id, string status, int reviewerUserId, string? comment);
    Task<IReadOnlyList<SessionAttendee>> GetAttendeesAsync(int sessionId);
}

public interface IReviewQueueRepository
{
    Task<int> EnqueueAsync(ReviewQueueItem item);
    Task<IReadOnlyList<ReviewQueueItem>> GetPendingAsync();
    /// <summary>Records the decision only if the item is still Pending. Returns false if someone else already decided it.</summary>
    Task<bool> DecideAsync(string itemType, int itemId, string status, int reviewerUserId, string? comment);
}

public interface ICertificateRepository
{
    Task<int> CreateAsync(Certificate certificate);
    Task<IReadOnlyList<Certificate>> ListBySessionAsync(int sessionId);
}

public interface ICounterRepository
{
    /// <summary>Next number of a counter (created on first use). organizationId null = global counter.</summary>
    Task<int> NextAsync(int? organizationId, string counterKey, int year);
}

public interface IAdminActionRepository
{
    Task<long> AddAsync(AdminActionLogEntry entry);
    Task<IReadOnlyList<AdminActionLogEntry>> ListAsync(int? staffId = null, int take = 100);
}

public interface IReferenceRepository
{
    Task<IReadOnlyList<ReferenceItem>> GetAircraftTypesAsync(bool includeInactive = false);
    Task<IReadOnlyList<ReferenceItem>> GetIssuingNaasAsync(bool includeInactive = false);
    Task<IReadOnlyList<ReferenceItem>> GetFreezeReasonsAsync(bool includeInactive = false);
}
