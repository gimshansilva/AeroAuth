using AeroAuth.Core.Constants;
using AeroAuth.Core.Entities;
using AeroAuth.Core.Exceptions;
using AeroAuth.Core.Interfaces;

namespace AeroAuth.Core.Services;

/// <summary>Multi-step staff operations. Each one runs in a single transaction.</summary>
public class StaffService(IUnitOfWork uow, IStaffRepository staff, IReviewQueueRepository reviewQueue,
                          IAdminActionRepository adminActions, ITenantContext tenant)
{
    /// <summary>
    /// Saves a staff member with licenses and scope and puts them in the QA review queue, all or nothing.
    /// AMLEndorsed scope rows point at an AML of the same request through AmlIndex.
    /// </summary>
    public Task<int> SubmitAsync(Staff newStaff, IReadOnlyList<StaffAml> amls, IReadOnlyList<(StaffScope Scope, int? AmlIndex)> scopes)
    {
        foreach (var (scope, amlIndex) in scopes)
        {
            if (scope.ScopeBasis == ScopeBasis.AmlEndorsed && (amlIndex is null || amlIndex < 0 || amlIndex >= amls.Count))
                throw new ValidationFailedException("An AML endorsed scope must point at one of the submitted AML records.");
            if (scope.ScopeBasis == ScopeBasis.MoeTaskBased && amlIndex is not null)
                throw new ValidationFailedException("A MOE task-based scope must not point at an AML.");
        }

        return uow.ExecuteInTransactionAsync(async () =>
        {
            newStaff.CreatedByUserId = tenant.UserId;
            newStaff.ReviewStatus = ReviewStatus.Pending;
            var staffId = await staff.CreateAsync(newStaff);       // the tenant check happens inside the repository

            var amlIds = new List<(int Id, int NaaId)>();
            foreach (var aml in amls)
            {
                aml.StaffId = staffId;
                amlIds.Add((await staff.AddAmlAsync(aml), aml.NaaId));
            }

            foreach (var (scope, amlIndex) in scopes)
            {
                scope.StaffId = staffId;
                if (scope.ScopeBasis == ScopeBasis.AmlEndorsed)
                {
                    scope.StaffAmlId = amlIds[amlIndex!.Value].Id;
                    scope.BackingNaaId = amlIds[amlIndex.Value].NaaId;
                }
                await staff.AddScopeAsync(scope);
            }

            await reviewQueue.EnqueueAsync(new ReviewQueueItem
            {
                OrganizationId = newStaff.OrganizationId,
                ItemType = ReviewItemType.Staff,
                ItemId = staffId,
                Summary = $"New staff: {newStaff.FullName} ({newStaff.StaffNumber})",
                SubmittedByUserId = tenant.UserId
            });
            return staffId;
        });
    }

    /// <summary>Approves or rejects a pending staff submission. The queue item and the staff record change together.</summary>
    public Task DecideAsync(int staffId, string decision, string? comment)
    {
        if (decision is not (ReviewStatus.Approved or ReviewStatus.Rejected)) throw new ValidationFailedException("Decision must be Approved or Rejected.");
        if (decision == ReviewStatus.Rejected && string.IsNullOrWhiteSpace(comment)) throw new ValidationFailedException("A comment is required when rejecting.");

        return uow.ExecuteInTransactionAsync(async () =>
        {
            if (!await reviewQueue.DecideAsync(ReviewItemType.Staff, staffId, decision, tenant.UserId, comment))
                throw new ConflictException("This submission was already reviewed, or it does not exist.");
            if (!await staff.SetReviewDecisionAsync(staffId, decision, tenant.UserId, comment))
                throw new NotFoundException("Staff member not found.");
        });
    }

    /// <summary>Freezes an approved staff member and writes the Admin Actions Log entry in the same transaction.</summary>
    public Task FreezeAsync(int staffId, int freezeReasonId, string? notes) => uow.ExecuteInTransactionAsync(async () =>
    {
        if (!await staff.SetFrozenAsync(staffId, true))
            throw new ConflictException("Only an approved staff member who is not already frozen can be frozen.");
        await adminActions.AddAsync(new AdminActionLogEntry
        {
            UserId = tenant.UserId, ActionType = AdminActionType.Freeze, StaffId = staffId, FreezeReasonId = freezeReasonId, Notes = notes
        });
    });

    public Task UnfreezeAsync(int staffId, string? notes) => uow.ExecuteInTransactionAsync(async () =>
    {
        if (!await staff.SetFrozenAsync(staffId, false))
            throw new ConflictException("This staff member is not frozen.");
        await adminActions.AddAsync(new AdminActionLogEntry
        {
            UserId = tenant.UserId, ActionType = AdminActionType.Unfreeze, StaffId = staffId, Notes = notes
        });
    });
}
