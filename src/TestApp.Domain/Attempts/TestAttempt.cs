using TestApp.Domain.Assignments;
using TestApp.Domain.Entities;
using TestApp.Domain.Events;
using TestApp.Domain.Identity;
using TestApp.Domain.Tests;

namespace TestApp.Domain.Attempts;

public readonly record struct TestAttemptId(Guid Value) { public static TestAttemptId New() => new(Guid.CreateVersion7()); }
public enum AttemptStatus { InProgress = 1, Submitted = 2 }

public sealed record AttemptStarted : DomainEvent
{
    public AttemptStarted(TestAttemptId attemptId, TestAssignmentId assignmentId, ExternalUserId userId, DateTimeOffset occurredAt) : base(occurredAt)
    { AttemptId = attemptId; AssignmentId = assignmentId; UserId = userId; }
    public TestAttemptId AttemptId { get; }
    public TestAssignmentId AssignmentId { get; }
    public ExternalUserId UserId { get; }
}

public sealed record AttemptSubmitted : DomainEvent
{
    public AttemptSubmitted(TestAttemptId attemptId, DateTimeOffset occurredAt) : base(occurredAt) => AttemptId = attemptId;
    public TestAttemptId AttemptId { get; }
}

public sealed class TestAttempt : AggregateRoot<TestAttemptId>
{
    private readonly Dictionary<QuestionId, IReadOnlyCollection<AnswerOptionId>> _answers = [];
    public TestAssignmentId AssignmentId { get; private set; }
    public ExternalUserId UserId { get; private set; }
    public AttemptStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public IReadOnlyDictionary<QuestionId, IReadOnlyCollection<AnswerOptionId>> Answers => _answers;

    private TestAttempt() { }

    private TestAttempt(TestAttemptId id, TestAssignmentId assignmentId, ExternalUserId userId, DateTimeOffset startedAt)
        : base(id)
    {
        AssignmentId = assignmentId;
        UserId = userId;
        Status = AttemptStatus.InProgress;
        StartedAt = startedAt;
    }

    public static TestAttempt Start(TestAttemptId id, TestAssignmentId assignmentId, ExternalUserId userId, DateTimeOffset startedAt)
    {
        var attempt = new TestAttempt(id, assignmentId, userId, startedAt);
        attempt.Raise(new AttemptStarted(id, assignmentId, userId, startedAt));
        return attempt;
    }

    public void Answer(QuestionId questionId, IEnumerable<AnswerOptionId> optionIds)
    {
        EnsureInProgress();
        ArgumentNullException.ThrowIfNull(optionIds);
        var selected = optionIds.Distinct().ToArray();
        if (selected.Length == 0) throw new ArgumentException("At least one answer option must be selected.", nameof(optionIds));
        _answers[questionId] = selected;
    }

    public void Submit(DateTimeOffset submittedAt)
    {
        EnsureInProgress();
        if (submittedAt < StartedAt) throw new ArgumentOutOfRangeException(nameof(submittedAt));
        Status = AttemptStatus.Submitted;
        SubmittedAt = submittedAt;
        Raise(new AttemptSubmitted(Id, submittedAt));
    }

    private void EnsureInProgress()
    {
        if (Status != AttemptStatus.InProgress) throw new InvalidOperationException("A submitted attempt cannot be changed.");
    }
}
