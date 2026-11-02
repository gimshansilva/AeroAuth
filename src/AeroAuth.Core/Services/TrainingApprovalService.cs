using AeroAuth.Core.Constants;
using AeroAuth.Core.Entities;
using AeroAuth.Core.Exceptions;
using AeroAuth.Core.Interfaces;

namespace AeroAuth.Core.Services;

public record ApprovalResult(int SessionId, string Decision, IReadOnlyList<Certificate> Certificates);

public class TrainingApprovalService(IUnitOfWork uow, ITrainingSessionRepository sessions, IReviewQueueRepository reviewQueue,
                                     ICertificateRepository certificates, ICounterRepository counters, ITenantContext tenant)
{
    /// <summary>Saves a session with its modules and attendees and queues it for QA review, all or nothing.</summary>
    public Task<int> SubmitAsync(TrainingSession session) => uow.ExecuteInTransactionAsync(async () =>
    {
        session.CreatedByUserId = tenant.UserId;
        session.Status = ReviewStatus.Pending;
        var id = await sessions.CreateAsync(session);
        await reviewQueue.EnqueueAsync(new ReviewQueueItem
        {
            OrganizationId = session.OrganizationId, ItemType = ReviewItemType.TrainingSession, ItemId = id,
            Summary = $"Training session on {session.SessionDate:yyyy-MM-dd} ({session.Attendees.Count} attendees)",
            SubmittedByUserId = tenant.UserId
        });
        return id;
    });

    /// <summary>
    /// Approves a session in ONE transaction:
    ///  1. close the review queue item (fails if someone else already decided it),
    ///  2. mark the session approved,
    ///  3. for every attendee take the next number from the organization's counter and create a certificate record.
    /// If any step fails everything rolls back, including the counter numbers that were taken.
    /// </summary>
    public Task<ApprovalResult> ApproveAsync(int sessionId, string? comment) => uow.ExecuteInTransactionAsync(async () =>
    {
        var session = await sessions.GetByIdAsync(sessionId) ?? throw new NotFoundException("Training session not found.");

        if (!await reviewQueue.DecideAsync(ReviewItemType.TrainingSession, sessionId, ReviewStatus.Approved, tenant.UserId, comment))
            throw new ConflictException("This session was already reviewed.");
        if (!await sessions.SetReviewDecisionAsync(sessionId, ReviewStatus.Approved, tenant.UserId, comment))
            throw new ConflictException("This session was already reviewed.");

        var issued = new List<Certificate>();
        var year = session.SessionDate.Year;
        foreach (var attendee in await sessions.GetAttendeesAsync(sessionId))
        {
            var next = await counters.NextAsync(session.OrganizationId, CounterKeys.ProceduresCertificate, year);
            var cert = new Certificate
            {
                OrganizationId = session.OrganizationId, SessionId = sessionId, AttendeeId = attendee.AttendeeId,
                CertificateNumber = $"{session.OrganizationAbbreviation}/{CounterKeys.ProceduresCertificate}/{year}/{next:D4}",
                IssuedByUserId = tenant.UserId
            };
            cert.CertificateId = await certificates.CreateAsync(cert);
            issued.Add(cert);
        }
        return new ApprovalResult(sessionId, ReviewStatus.Approved, issued);
    });

    public Task RejectAsync(int sessionId, string comment) => uow.ExecuteInTransactionAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(comment)) throw new ValidationFailedException("A comment is required when rejecting.");
        if (!await reviewQueue.DecideAsync(ReviewItemType.TrainingSession, sessionId, ReviewStatus.Rejected, tenant.UserId, comment)
            || !await sessions.SetReviewDecisionAsync(sessionId, ReviewStatus.Rejected, tenant.UserId, comment))
            throw new ConflictException("This session was already reviewed, or it does not exist.");
    });
}
