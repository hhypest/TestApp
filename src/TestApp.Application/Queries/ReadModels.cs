using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Queries;

public sealed record AnswerOptionEditorView(AnswerOptionId Id, string Text, bool IsCorrect, int Order);
public sealed record QuestionEditorView(QuestionId Id, string Text, QuestionType Type, decimal Points, int Order, IReadOnlyList<AnswerOptionEditorView> Options);
public sealed record TestEditorView(
    TestId Id,
    string Title,
    TestStatus Status,
    decimal PassingPercentage,
    int? TimeLimitMinutes,
    IReadOnlyList<QuestionEditorView> Questions);

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
public sealed record GetMyAssignmentsQuery : IQuery<IReadOnlyList<AssignmentSummary>>;
public sealed record GetAttemptQuery(TestAttemptId AttemptId) : IQuery<AttemptView?>;
public sealed record GetAttemptResultQuery(TestAttemptId AttemptId) : IQuery<AttemptResultView?>;

public interface IReadModelQueries
{
    Task<TestEditorView?> GetTestEditorViewAsync(TestId testId, CancellationToken ct);
    Task<IReadOnlyList<AssignmentSummary>> GetAssignmentsAsync(ExternalUserId userId, IReadOnlySet<ExternalGroupId> groups, CancellationToken ct);
    Task<AttemptView?> GetAttemptAsync(TestAttemptId attemptId, ExternalUserId userId, CancellationToken ct);
    Task<AttemptResultView?> GetAttemptResultAsync(TestAttemptId attemptId, ExternalUserId userId, CancellationToken ct);
}

public sealed class GetTestEditorViewQueryHandler(IReadModelQueries queries)
    : IQueryHandler<GetTestEditorViewQuery, TestEditorView?>
{
    public Task<TestEditorView?> Handle(GetTestEditorViewQuery query, CancellationToken ct) => queries.GetTestEditorViewAsync(query.TestId, ct);
}

public sealed class GetMyAssignmentsQueryHandler(IReadModelQueries queries, TestApp.Application.Abstractions.ICurrentActor actor)
    : IQueryHandler<GetMyAssignmentsQuery, IReadOnlyList<AssignmentSummary>>
{
    public Task<IReadOnlyList<AssignmentSummary>> Handle(GetMyAssignmentsQuery query, CancellationToken ct) => queries.GetAssignmentsAsync(actor.UserId, actor.Groups, ct);
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
