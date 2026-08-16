using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;

namespace TestApp.Application.Queries;

public interface IReadModelQueries
{
    Task<TestEditorView?> GetTestEditorViewAsync(
        TestId testId,
        ExternalUserId? ownerId,
        CancellationToken ct);

    Task<PagedResult<AssignmentSummary>> GetAssignmentsAsync(
        ExternalUserId userId,
        IReadOnlySet<ExternalGroupId> groups,
        int page,
        int pageSize,
        AssignmentStatus? status,
        CancellationToken ct);

    Task<PagedResult<AttemptSummary>> GetAttemptsAsync(
        ExternalUserId userId,
        int page,
        int pageSize,
        AttemptStatus? status,
        CancellationToken ct);

    Task<PagedResult<ReviewerResultSummary>> GetReviewerResultsAsync(
        TestId? testId,
        PublishedTestRevisionId? revisionId,
        AttemptOutcome? outcome,
        ExternalUserId? ownerId,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<ReviewerAttemptResultView?> GetReviewerAttemptResultAsync(
        TestAttemptId attemptId,
        ExternalUserId? ownerId,
        CancellationToken ct);

    Task<AttemptView?> GetAttemptAsync(
        TestAttemptId attemptId,
        ExternalUserId userId,
        CancellationToken ct);

    Task<AttemptResultView?> GetAttemptResultAsync(
        TestAttemptId attemptId,
        ExternalUserId userId,
        CancellationToken ct);

    /// <summary>
    /// Student-safe presentation of one attempt the caller owns, or <c>null</c> when the
    /// attempt does not exist or belongs to someone else.
    /// </summary>
    /// <param name="serverTime">
    /// Authoritative clock reading stamped onto the response; supplied by the Application
    /// layer so the read model stays a pure projection.
    /// </param>
    Task<AttemptPresentationView?> GetAttemptPresentationAsync(
        TestAttemptId attemptId,
        ExternalUserId userId,
        DateTimeOffset serverTime,
        CancellationToken ct);

    /// <summary>
    /// The caller's in-progress attempt for the given assignment, or <c>null</c> when
    /// there is none to resume.
    /// </summary>
    Task<AttemptPresentationView?> GetActiveAttemptPresentationAsync(
        TestAssignmentId assignmentId,
        ExternalUserId userId,
        DateTimeOffset serverTime,
        CancellationToken ct);
}
