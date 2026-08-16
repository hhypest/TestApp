using TestApp.Application.Abstractions;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Messaging.Abstractions;

namespace TestApp.Application.Queries;

/// <summary>
/// A single answer option as the student taking the test may see it.
/// </summary>
/// <remarks>
/// This type deliberately has no correctness member. It is the student-facing
/// counterpart of <see cref="ReviewerAnswerOptionView"/>, which does carry
/// <c>IsCorrect</c>; the two must never be interchanged. See ADR-028.
/// </remarks>
public sealed record AttemptAnswerOptionView(
    AnswerOptionId Id,
    string Text,
    int Order);

/// <summary>
/// A question as presented to the student, merged with that student's own saved answer.
/// </summary>
public sealed record AttemptQuestionView(
    QuestionId Id,
    string Text,
    QuestionType Type,
    decimal Points,
    int Order,
    DateTimeOffset? AnsweredAt,
    IReadOnlyList<AnswerOptionId> SelectedOptionIds,
    IReadOnlyList<AttemptAnswerOptionView> Options);

/// <summary>
/// Everything a student needs to take or resume one attempt: the questions from the
/// immutable revision the attempt was started against, their own saved responses, and
/// the timing information needed to run a countdown.
/// </summary>
/// <remarks>
/// <para>
/// Questions come from the <see cref="PublishedTestRevision"/> bound to the attempt,
/// never from the live <see cref="Test"/>, so editing the working test after publication
/// cannot change an attempt already in flight.
/// </para>
/// <para>
/// <see cref="ServerTime"/> is the authoritative clock reading that accompanied this
/// response. A countdown computed against it does not drift with the client's own clock.
/// Between <see cref="DeadlineAt"/> passing and the background expiration worker sweeping
/// the attempt, a student can legitimately observe <see cref="AttemptStatus.InProgress"/>
/// with <c>DeadlineAt &lt; ServerTime</c>; any write in that window answers
/// <c>409 attempt.expired</c>.
/// </para>
/// </remarks>
public sealed record AttemptPresentationView(
    TestAttemptId AttemptId,
    TestAssignmentId AssignmentId,
    PublishedTestRevisionId RevisionId,
    string TestTitle,
    int RevisionVersion,
    decimal PassingPercentage,
    int? TimeLimitMinutes,
    AttemptStatus Status,
    AttemptOutcome? Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset? DeadlineAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset ServerTime,
    IReadOnlyList<AttemptQuestionView> Questions);

/// <summary>Presentation for one attempt the caller owns (ATT-010 / UX-001).</summary>
public sealed record GetAttemptPresentationQuery(TestAttemptId AttemptId)
    : IQuery<AttemptPresentationView?>;

/// <summary>
/// The caller's still-running attempt for an assignment, if there is one (ATT-011).
/// </summary>
public sealed record GetActiveAttemptPresentationQuery(TestAssignmentId AssignmentId)
    : IQuery<AttemptPresentationView?>;

public sealed class GetAttemptPresentationQueryHandler(
    IReadModelQueries queries,
    ICurrentActor actor,
    IClock clock)
    : IQueryHandler<GetAttemptPresentationQuery, AttemptPresentationView?>
{
    public Task<AttemptPresentationView?> Handle(GetAttemptPresentationQuery query, CancellationToken ct) =>
        queries.GetAttemptPresentationAsync(query.AttemptId, actor.UserId, clock.UtcNow, ct);
}

public sealed class GetActiveAttemptPresentationQueryHandler(
    IReadModelQueries queries,
    ICurrentActor actor,
    IClock clock)
    : IQueryHandler<GetActiveAttemptPresentationQuery, AttemptPresentationView?>
{
    public Task<AttemptPresentationView?> Handle(GetActiveAttemptPresentationQuery query, CancellationToken ct) =>
        queries.GetActiveAttemptPresentationAsync(query.AssignmentId, actor.UserId, clock.UtcNow, ct);
}
