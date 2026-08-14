using TestApp.Application.Abstractions;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Queries;

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

public sealed record QuestionResponseView(
    QuestionId QuestionId,
    IReadOnlyList<AnswerOptionId> SelectedOptionIds,
    DateTimeOffset? AnsweredAt);

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

public sealed record GetMyAttemptsQuery(
    int Page = 1,
    int PageSize = 20,
    AttemptStatus? Status = null) : IQuery<PagedResult<AttemptSummary>>;

public sealed record GetAttemptQuery(TestAttemptId AttemptId) : IQuery<AttemptView?>;
public sealed record GetAttemptResultQuery(TestAttemptId AttemptId) : IQuery<AttemptResultView?>;

public sealed class GetMyAttemptsQueryHandler(IReadModelQueries queries, ICurrentActor actor)
    : IQueryHandler<GetMyAttemptsQuery, PagedResult<AttemptSummary>>
{
    public Task<PagedResult<AttemptSummary>> Handle(GetMyAttemptsQuery query, CancellationToken ct)
    {
        var (page, size) = Paging.Normalize(query.Page, query.PageSize);
        return queries.GetAttemptsAsync(actor.UserId, page, size, query.Status, ct);
    }
}

public sealed class GetAttemptQueryHandler(IReadModelQueries queries, ICurrentActor actor)
    : IQueryHandler<GetAttemptQuery, AttemptView?>
{
    public Task<AttemptView?> Handle(GetAttemptQuery query, CancellationToken ct) =>
        queries.GetAttemptAsync(query.AttemptId, actor.UserId, ct);
}

public sealed class GetAttemptResultQueryHandler(IReadModelQueries queries, ICurrentActor actor)
    : IQueryHandler<GetAttemptResultQuery, AttemptResultView?>
{
    public Task<AttemptResultView?> Handle(GetAttemptResultQuery query, CancellationToken ct) =>
        queries.GetAttemptResultAsync(query.AttemptId, actor.UserId, ct);
}
