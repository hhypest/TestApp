using TestApp.Application.Abstractions;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Queries;

public sealed record ReviewerResultSummary(
    TestAttemptId AttemptId,
    TestAssignmentId AssignmentId,
    PublishedTestRevisionId RevisionId,
    TestId TestId,
    string TestTitle,
    int RevisionVersion,
    ExternalUserId UserId,
    AttemptStatus Status,
    AttemptOutcome? Outcome,
    decimal? Earned,
    decimal? Maximum,
    decimal? Percentage,
    DateTimeOffset StartedAt,
    DateTimeOffset? DeadlineAt,
    DateTimeOffset? CompletedAt);

public sealed record ReviewerAnswerOptionView(
    AnswerOptionId Id,
    string Text,
    int Order,
    bool IsCorrect,
    bool IsSelected);

public sealed record ReviewerQuestionResultView(
    QuestionId Id,
    string Text,
    QuestionType Type,
    decimal MaximumPoints,
    decimal EarnedPoints,
    int Order,
    DateTimeOffset? AnsweredAt,
    IReadOnlyList<ReviewerAnswerOptionView> Options);

public sealed record ReviewerAttemptResultView(
    TestAttemptId AttemptId,
    TestAssignmentId AssignmentId,
    PublishedTestRevisionId RevisionId,
    TestId TestId,
    string TestTitle,
    int RevisionVersion,
    ExternalUserId UserId,
    AttemptStatus Status,
    AttemptOutcome? Outcome,
    decimal? Earned,
    decimal? Maximum,
    decimal? Percentage,
    DateTimeOffset StartedAt,
    DateTimeOffset? DeadlineAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ReviewerQuestionResultView> Questions);

public sealed record GetReviewerResultsQuery(
    TestId? TestId = null,
    PublishedTestRevisionId? RevisionId = null,
    AttemptOutcome? Outcome = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<ReviewerResultSummary>>;

public sealed record GetReviewerAttemptResultQuery(
    TestAttemptId AttemptId) : IQuery<ReviewerAttemptResultView?>;

public sealed class GetReviewerResultsQueryHandler(IReadModelQueries queries, ICurrentActor actor)
    : IQueryHandler<GetReviewerResultsQuery, PagedResult<ReviewerResultSummary>>
{
    public Task<PagedResult<ReviewerResultSummary>> Handle(GetReviewerResultsQuery query, CancellationToken ct)
    {
        var (page, size) = Paging.Normalize(query.Page, query.PageSize);
        return queries.GetReviewerResultsAsync(
            query.TestId,
            query.RevisionId,
            query.Outcome,
            AuthorReadScope.OwnerFilter(actor),
            page,
            size,
            ct);
    }
}

public sealed class GetReviewerAttemptResultQueryHandler(IReadModelQueries queries, ICurrentActor actor)
    : IQueryHandler<GetReviewerAttemptResultQuery, ReviewerAttemptResultView?>
{
    public Task<ReviewerAttemptResultView?> Handle(GetReviewerAttemptResultQuery query, CancellationToken ct) =>
        queries.GetReviewerAttemptResultAsync(query.AttemptId, AuthorReadScope.OwnerFilter(actor), ct);
}
