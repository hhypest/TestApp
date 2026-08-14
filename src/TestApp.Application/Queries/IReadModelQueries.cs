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
}
