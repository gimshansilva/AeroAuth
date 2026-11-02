using AeroAuth.Core.Entities;
using AeroAuth.Core.Interfaces;

namespace AeroAuth.Infrastructure.Repositories;

/// <summary>Insert and read only. The table itself refuses updates and deletes.</summary>
public class AdminActionRepository(IUnitOfWork uow) : BaseRepository(uow), IAdminActionRepository
{
    public async Task<long> AddAsync(AdminActionLogEntry e) =>
        await ExecuteScalarAsync<long>(@"
            INSERT dbo.AdminActionsLog (UserId, ActionType, StaffId, FreezeReasonId, TargetTable, TargetKey, OldValue, NewValue, Notes)
            OUTPUT INSERTED.ActionId
            VALUES (@UserId, @ActionType, @StaffId, @FreezeReasonId, @TargetTable, @TargetKey, @OldValue, @NewValue, @Notes)", e);

    public Task<IReadOnlyList<AdminActionLogEntry>> ListAsync(int? staffId = null, int take = 100) =>
        QueryAsync<AdminActionLogEntry>(@"
            SELECT TOP (@take) ActionId, UserId, ActionType, StaffId, FreezeReasonId, TargetTable, TargetKey, OldValue, NewValue, Notes, ActionAt
            FROM dbo.AdminActionsLog WHERE (@staffId IS NULL OR StaffId = @staffId)
            ORDER BY ActionAt DESC, ActionId DESC", new { take = Math.Clamp(take, 1, 500), staffId });
}
