using AeroAuth.Core.Entities;
using AeroAuth.Core.Interfaces;

namespace AeroAuth.Infrastructure.Repositories;

public class ReviewQueueRepository(IUnitOfWork uow, ITenantContext tenant) : TenantRepository(uow, tenant), IReviewQueueRepository
{
    public async Task<int> EnqueueAsync(ReviewQueueItem item)
    {
        EnsureWritable(item);
        return await ExecuteScalarAsync<int>(@"
            INSERT dbo.ReviewQueue (OrganizationId, ItemType, ItemId, Summary, SubmittedByUserId)
            OUTPUT INSERTED.ReviewQueueId
            VALUES (@OrganizationId, @ItemType, @ItemId, @Summary, @SubmittedByUserId)", item);
    }

    public Task<IReadOnlyList<ReviewQueueItem>> GetPendingAsync() =>
        QueryAsync<ReviewQueueItem>($@"
            SELECT q.ReviewQueueId, q.OrganizationId, q.ItemType, q.ItemId, q.Summary, q.Status, q.SubmittedByUserId, q.SubmittedAt,
                   q.ReviewedByUserId, q.ReviewedAt, q.ReviewComment
            FROM dbo.ReviewQueue q
            WHERE q.Status = 'Pending' AND {TenantFilter("q")}
            ORDER BY q.SubmittedAt", Params());

    /// <summary>The WHERE clause only matches a Pending row, so two reviewers cannot both succeed: the second gets false.</summary>
    public async Task<bool> DecideAsync(string itemType, int itemId, string status, int reviewerUserId, string? comment) =>
        await ExecuteAsync($@"
            UPDATE q SET q.Status = @status, q.ReviewedByUserId = @reviewerUserId, q.ReviewedAt = SYSUTCDATETIME(), q.ReviewComment = @comment
            FROM dbo.ReviewQueue q
            WHERE q.ItemType = @itemType AND q.ItemId = @itemId AND q.Status = 'Pending' AND {TenantFilter("q")}",
            Params(new { itemType, itemId, status, reviewerUserId, comment })) > 0;
}
