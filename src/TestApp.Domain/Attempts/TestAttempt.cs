using TestApp.Core.Monads;
using TestApp.Domain.Assignments;
using TestApp.Domain.Common;
using TestApp.Domain.Entities;
using TestApp.Domain.Events;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;

namespace TestApp.Domain.Attempts;

public readonly record struct TestAttemptId(Guid Value)
{
    public static TestAttemptId New() => new(Guid.CreateVersion7());
}

public enum AttemptStatus
{
    InProgress = 1,
    Submitted = 2,
    TimedOut = 3
}

public sealed record AttemptScore(decimal Earned, decimal Maximum)
{
    public decimal Percentage => Maximum <= 0 ? 0 : Math.Round(Earned / Maximum * 100m, 2);
}

public sealed record AttemptStarted : DomainEvent
{
    public AttemptStarted(TestAttemptId attemptId, TestAssignmentId assignmentId, PublishedTestRevisionId revisionId, ExternalUserId userId, DateTimeOffset occurredAt) : base(occurredAt)
    {
        AttemptId = attemptId;
        AssignmentId = assignmentId;
        RevisionId = revisionId;
        UserId = userId;
    }

    public TestAttemptId AttemptId { get; }
    public TestAssignmentId AssignmentId { get; }
    public PublishedTestRevisionId RevisionId { get; }
    public ExternalUserId UserId { get; }
}

public sealed record AttemptSubmitted : DomainEvent
{
    public AttemptSubmitted(TestAttemptId attemptId, AttemptScore score, DateTimeOffset occurredAt) : base(occurredAt)
    {
        AttemptId = attemptId;
        Score = score;
    }

    public TestAttemptId AttemptId { get; }
    public AttemptScore Score { get; }
}

public sealed record AttemptTimedOut : DomainEvent
{
    public AttemptTimedOut(TestAttemptId attemptId, DateTimeOffset occurredAt) : base(occurredAt) => AttemptId = attemptId;
    public TestAttemptId AttemptId { get; }
}

public sealed class TestAttempt : AggregateRoot<TestAttemptId>
{
    private readonly List<QuestionResponse> _responses = [];

    public TestAssignmentId AssignmentId { get; private set; }
    public PublishedTestRevisionId RevisionId { get; private set; }
    public ExternalUserId UserId { get; private set; }
    public AttemptStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public AttemptScore? Score { get; private set; }
    public IReadOnlyCollection<QuestionResponse> Responses => _responses.AsReadOnly();

    private TestAttempt() { }

    private TestAttempt(
        TestAttemptId id,
        TestAssignmentId assignmentId,
        PublishedTestRevisionId revisionId,
        ExternalUserId userId,
        DateTimeOffset startedAt,
        IEnumerable<QuestionId> questionIds) : base(id)
    {
        AssignmentId = assignmentId;
        RevisionId = revisionId;
        UserId = userId;
        Status = AttemptStatus.InProgress;
        StartedAt = startedAt;
        _responses.AddRange(questionIds.Distinct().Select(QuestionResponse.Create));
    }

    public static TestAttempt Start(
        TestAttemptId id,
        TestAssignmentId assignmentId,
        PublishedTestRevisionId revisionId,
        ExternalUserId userId,
        DateTimeOffset startedAt,
        IEnumerable<QuestionId> questionIds)
    {
        ArgumentNullException.ThrowIfNull(questionIds);
        var attempt = new TestAttempt(id, assignmentId, revisionId, userId, startedAt, questionIds);
        attempt.Raise(new AttemptStarted(id, assignmentId, revisionId, userId, startedAt));
        return attempt;
    }

    public Result<TestAttempt, DomainError> Answer(QuestionId questionId, IEnumerable<AnswerOptionId> optionIds, DateTimeOffset answeredAt)
    {
        var active = EnsureInProgress();
        if (active.Match(_ => false, _ => true))
            return active;

        ArgumentNullException.ThrowIfNull(optionIds);
        var selected = optionIds.Distinct().ToArray();
        if (selected.Length == 0)
            return DomainError.Validation("attempt.answer.empty", "At least one answer option must be selected.");

        var response = _responses.SingleOrDefault(x => x.Id == questionId);
        if (response is null)
            return DomainError.NotFound("attempt.question.not_found", "Question is not part of this attempt.");

        response.Answer(selected, answeredAt);
        return this;
    }

    public Result<TestAttempt, DomainError> ClearAnswer(QuestionId questionId)
    {
        var active = EnsureInProgress();
        if (active.Match(_ => false, _ => true))
            return active;

        var response = _responses.SingleOrDefault(x => x.Id == questionId);
        if (response is null)
            return DomainError.NotFound("attempt.question.not_found", "Question is not part of this attempt.");

        response.Clear();
        return this;
    }

    public Result<TestAttempt, DomainError> Submit(DateTimeOffset submittedAt, AttemptScore score)
    {
        var active = EnsureInProgress();
        if (active.Match(_ => false, _ => true))
            return active;
        if (submittedAt < StartedAt)
            return DomainError.Validation("attempt.completed_at", "Completion time cannot be earlier than start time.");

        Status = AttemptStatus.Submitted;
        CompletedAt = submittedAt;
        Score = score;
        Raise(new AttemptSubmitted(Id, score, submittedAt));
        return this;
    }

    public Result<TestAttempt, DomainError> Timeout(DateTimeOffset timedOutAt)
    {
        var active = EnsureInProgress();
        if (active.Match(_ => false, _ => true))
            return active;
        if (timedOutAt < StartedAt)
            return DomainError.Validation("attempt.completed_at", "Completion time cannot be earlier than start time.");

        Status = AttemptStatus.TimedOut;
        CompletedAt = timedOutAt;
        Raise(new AttemptTimedOut(Id, timedOutAt));
        return this;
    }

    private Result<TestAttempt, DomainError> EnsureInProgress()
        => Status == AttemptStatus.InProgress
            ? this
            : DomainError.Conflict("attempt.completed", "A completed attempt cannot be changed.");
}

public sealed class QuestionResponse : Entity<QuestionId>
{
    private readonly List<SelectedAnswerOption> _selectedOptions = [];

    public DateTimeOffset? AnsweredAt { get; private set; }
    public IReadOnlyCollection<SelectedAnswerOption> SelectedOptions => _selectedOptions.AsReadOnly();

    private QuestionResponse() { }
    private QuestionResponse(QuestionId questionId) : base(questionId) { }

    internal static QuestionResponse Create(QuestionId questionId) => new(questionId);

    internal void Answer(IEnumerable<AnswerOptionId> optionIds, DateTimeOffset answeredAt)
    {
        _selectedOptions.Clear();
        _selectedOptions.AddRange(optionIds.Select(SelectedAnswerOption.Create));
        AnsweredAt = answeredAt;
    }

    internal void Clear()
    {
        _selectedOptions.Clear();
        AnsweredAt = null;
    }
}

public sealed class SelectedAnswerOption
{
    public AnswerOptionId OptionId { get; private set; }

    private SelectedAnswerOption() { }
    private SelectedAnswerOption(AnswerOptionId optionId) => OptionId = optionId;

    internal static SelectedAnswerOption Create(AnswerOptionId optionId) => new(optionId);
}
