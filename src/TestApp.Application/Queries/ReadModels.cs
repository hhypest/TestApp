using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Queries;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record AnswerOptionEditorView(AnswerOptionId Id, string Text, bool IsCorrect, int Order);
public sealed record QuestionEditorView(QuestionId Id, string Text, QuestionType Type, decimal Points, int Order, IReadOnlyList<AnswerOptionEditorView> Options);
public sealed record TestEditorView(TestId Id, string Title, TestStatus Status, decimal PassingPercentage, int? TimeLimitMinutes, IReadOnlyList<QuestionEditorView> Questions);

public sealed record AssignmentSummary(
    TestAssignmentId Id,
    PublishedTestRevisionId RevisionId,
    string TestTitle,
    int RevisionVersion,
    decimal PassingPercentage,
    int? TimeLimitMinutes,
    DateTimeOffset AvailableFrom,
    DateTimeOffset? AvailableUntil,
    int? AttemptLimit,
    AssignmentStatus Status);

public sealed record AttemptSummary(
    TestAttemptId Id,
    TestAssignmentId AssignmentId,
    PublishedTestRevisionId RevisionId,
    AttemptStatus Status,
    AttemptOutcome? Outcome,
    decimal? Percentage,
    DateTimeOffset StartedAt,
    DateTimeOffset? DeadlineAt,
    DateTimeOffset? CompletedAt);

public sealed record QuestionResponseView(QuestionId QuestionId, IReadOnlyList<AnswerOptionId> SelectedOptionIds, DateTimeOffset? AnsweredAt);
public sealed record AttemptView(
    TestAttemptId Id,
    TestAssignmentId AssignmentId,
    PublishedTestRevisionId RevisionId,
    AttemptStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? DeadlineAt,
    DateTimeOffset? CompletedAt,
    AttemptOutcome? Outcome,
    IReadOnlyList<QuestionResponseView> Responses);

public sealed record AttemptResultView(
    TestAttemptId Id,
    PublishedTestRevisionId RevisionId,
    AttemptStatus Status,
    AttemptOutcome? Outcome,
    decimal? Earned,
    decimal? Maximum,
    decimal? Percentage,
    DateTimeOffset StartedAt,
    DateTimeOffset? DeadlineAt,
    DateTimeOffset? CompletedAt);

public sealed record GetTestEditorViewQuery(TestId TestId) : IQuery<TestEditorView?>;
public sealed record GetMyAssignmentsQuery(int Page = 1, int PageSize = 20, AssignmentStatus? Status = null) : IQuery<PagedResult<AssignmentSummary>>;
public sealed record GetMyAttemptsQuery(int Page = 1, int PageSize = 20, AttemptStatus? Status = null) : IQuery<PagedResult<AttemptSummary>>;
public sealed record GetAttemptQuery(TestAttemptId AttemptId) : IQuery<AttemptView?>;
public sealed record GetAttemptResultQuery(TestAttemptId AttemptId) : IQuery<AttemptResultView?>;

public interface IReadModelQueries
{
    Task<TestEditorView?> GetTestEditorViewAsync(TestId testId, CancellationToken ct);
    Task<PagedResult<AssignmentSummary>> GetAssignmentsAsync(ExternalUserId userId, IReadOnlySet<ExternalGroupId> groups, int page, int pageSize, AssignmentStatus? status, CancellationToken ct);
    Task<PagedResult<AttemptSummary>> GetAttemptsAsync(ExternalUserId userId, int page, int pageSize, AttemptStatus? status, CancellationToken ct);
    Task<AttemptView?> GetAttemptAsync(TestAttemptId attemptId, ExternalUserId userId, CancellationToken ct);
    Task<AttemptResultView?> GetAttemptResultAsync(TestAttemptId attemptId, ExternalUserId userId, CancellationToken ct);
}

internal static class Paging
{
    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));
}

public sealed class GetTestEditorViewQueryHandler(IReadModelQueries queries)
    : IQueryHandler<GetTestEditorViewQuery, TestEditorView?>
{
    public Task<TestEditorView?> Handle(GetTestEditorViewQuery query, CancellationToken ct) => queries.GetTestEditorViewAsync(query.TestId, ct);
}

public sealed class GetMyAssignmentsQueryHandler(IReadModelQueries queries, TestApp.Application.Abstractions.ICurrentActor actor)
    : IQueryHandler<GetMyAssignmentsQuery, PagedResult<AssignmentSummary>>
{
    public Task<PagedResult<AssignmentSummary>> Handle(GetMyAssignmentsQuery query, CancellationToken ct)
    {
        var (page, size) = Paging.Normalize(query.Page, query.PageSize);
        return queries.GetAssignmentsAsync(actor.UserId, actor.Groups, page, size, query.Status, ct);
    }
}

public sealed class GetMyAttemptsQueryHandler(IReadModelQueries queries, TestApp.Application.Abstractions.ICurrentActor actor)
    : IQueryHandler<GetMyAttemptsQuery, PagedResult<AttemptSummary>>
{
    public Task<PagedResult<AttemptSummary>> Handle(GetMyAttemptsQuery query, CancellationToken ct)
    {
        var (page, size) = Paging.Normalize(query.Page, query.PageSize);
        return queries.GetAttemptsAsync(actor.UserId, page, size, query.Status, ct);
    }
}

public sealed class GetAttemptQueryHandler(IReadModelQueries queries, TestApp.Application.Abstractions.ICurrentActor actor)
    : IQueryHandler<GetAttemptQuery, AttemptView?>
{
    public Task<AttemptView?> Handle(GetAttemptQuery query, CancellationToken ct) => queries.GetAttemptAsync(query.AttemptId, actor.UserId, ct);
}

public sealed class GetAttemptResultQueryHandler(IReadModelQueries queries, TestApp.Application.Abstractions.ICurrentActor actor)
    : IQueryHandler<GetAttemptResultQuery, AttemptResultView?>
{
    public Task<AttemptResultView?> Handle(GetAttemptResultQuery query, CancellationToken ct) => queries.GetAttemptResultAsync(query.AttemptId, actor.UserId, ct);
}
