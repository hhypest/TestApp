using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Queries;

public sealed record AdminAssignmentSummary(
    TestAssignmentId Id,
    PublishedTestRevisionId RevisionId,
    TestId TestId,
    string TestTitle,
    int RevisionVersion,
    AssignmentTargetType TargetType,
    string TargetId,
    ExternalUserId AssignedBy,
    DateTimeOffset AssignedAt,
    DateTimeOffset AvailableFrom,
    DateTimeOffset? AvailableUntil,
    int? AttemptLimit,
    AssignmentStatus Status,
    int AttemptCount,
    int InProgressCount,
    int CompletedCount,
    int PassedCount,
    int FailedCount,
    decimal? AveragePercentage);

public sealed record AdminAssignmentDetail(
    TestAssignmentId Id,
    PublishedTestRevisionId RevisionId,
    TestId TestId,
    string TestTitle,
    int RevisionVersion,
    AssignmentTargetType TargetType,
    string TargetId,
    ExternalUserId AssignedBy,
    DateTimeOffset AssignedAt,
    DateTimeOffset AvailableFrom,
    DateTimeOffset? AvailableUntil,
    int? AttemptLimit,
    AssignmentStatus Status,
    ExternalUserId? CancelledBy,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    int AttemptCount,
    int InProgressCount,
    int SubmittedCount,
    int TimedOutCount,
    int PassedCount,
    int FailedCount,
    decimal? AveragePercentage,
    decimal? BestPercentage);

public sealed record AdminAssignmentAttemptSummary(
    TestAttemptId AttemptId,
    ExternalUserId UserId,
    AttemptStatus Status,
    AttemptOutcome? Outcome,
    decimal? Earned,
    decimal? Maximum,
    decimal? Percentage,
    DateTimeOffset StartedAt,
    DateTimeOffset? DeadlineAt,
    DateTimeOffset? CompletedAt);

public sealed record GetAdminAssignmentsQuery(
    TestId? TestId = null,
    PublishedTestRevisionId? RevisionId = null,
    AssignmentTargetType? TargetType = null,
    string? TargetId = null,
    AssignmentStatus? Status = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<AdminAssignmentSummary>>;

public sealed record GetAdminAssignmentQuery(TestAssignmentId AssignmentId) : IQuery<AdminAssignmentDetail?>;

public sealed record GetAdminAssignmentAttemptsQuery(
    TestAssignmentId AssignmentId,
    AttemptStatus? Status = null,
    AttemptOutcome? Outcome = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<AdminAssignmentAttemptSummary>?>;

public interface IAssignmentAdminQueries
{
    Task<PagedResult<AdminAssignmentSummary>> GetAssignmentsAsync(
        TestId? testId,
        PublishedTestRevisionId? revisionId,
        AssignmentTargetType? targetType,
        string? targetId,
        AssignmentStatus? status,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<AdminAssignmentDetail?> GetAssignmentAsync(TestAssignmentId assignmentId, CancellationToken ct);

    Task<PagedResult<AdminAssignmentAttemptSummary>?> GetAssignmentAttemptsAsync(
        TestAssignmentId assignmentId,
        AttemptStatus? status,
        AttemptOutcome? outcome,
        int page,
        int pageSize,
        CancellationToken ct);
}

public sealed class GetAdminAssignmentsQueryHandler(IAssignmentAdminQueries queries)
    : IQueryHandler<GetAdminAssignmentsQuery, PagedResult<AdminAssignmentSummary>>
{
    public Task<PagedResult<AdminAssignmentSummary>> Handle(GetAdminAssignmentsQuery query, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        return queries.GetAssignmentsAsync(query.TestId, query.RevisionId, query.TargetType, Normalize(query.TargetId), query.Status, page, pageSize, ct);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class GetAdminAssignmentQueryHandler(IAssignmentAdminQueries queries)
    : IQueryHandler<GetAdminAssignmentQuery, AdminAssignmentDetail?>
{
    public Task<AdminAssignmentDetail?> Handle(GetAdminAssignmentQuery query, CancellationToken ct) =>
        queries.GetAssignmentAsync(query.AssignmentId, ct);
}

public sealed class GetAdminAssignmentAttemptsQueryHandler(IAssignmentAdminQueries queries)
    : IQueryHandler<GetAdminAssignmentAttemptsQuery, PagedResult<AdminAssignmentAttemptSummary>?>
{
    public Task<PagedResult<AdminAssignmentAttemptSummary>?> Handle(GetAdminAssignmentAttemptsQuery query, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        return queries.GetAssignmentAttemptsAsync(query.AssignmentId, query.Status, query.Outcome, page, pageSize, ct);
    }
}
