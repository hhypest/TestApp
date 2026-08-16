using System.Text.Json.Serialization;
using TestApp.Core.Monads;
using TestApp.Domain.Assignments;
using TestApp.Domain.Common;
using TestApp.Domain.Entities;
using TestApp.Domain.Events;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;

namespace TestApp.Domain.Attempts;

public readonly record struct TestAttemptId
{
    [JsonConstructor]
    public TestAttemptId(Guid value) => Value = StrongIdGuard.Ensure(value, "Attempt id", nameof(value));

    public Guid Value { get; }

    public static TestAttemptId New() => new(Guid.CreateVersion7());
}

public enum AttemptStatus { InProgress = 1, Submitted = 2, TimedOut = 3 }
public enum AttemptOutcome { Passed = 1, Failed = 2 }

/// <summary>
/// Points earned out of the points available on the published revision the attempt was started from.
/// </summary>
/// <remarks>
/// The constructor is the only construction path open to application code, so the range invariant holds for
/// every score the model produces. The private parameterless constructor exists solely for EF Core
/// materialisation of the owned <c>score_earned</c>/<c>score_maximum</c> columns — reading back a row that
/// was written before the invariant existed must not throw, which is the same materialisation contract the
/// aggregates themselves use.
/// </remarks>
public sealed record AttemptScore
{
    private AttemptScore() { }

    public AttemptScore(decimal earned, decimal maximum)
    {
        if (maximum < 0m)
            throw new ArgumentOutOfRangeException(nameof(maximum), maximum, "Maximum score cannot be negative.");
        if (earned < 0m)
            throw new ArgumentOutOfRangeException(nameof(earned), earned, "Earned score cannot be negative.");
        if (earned > maximum)
            throw new ArgumentOutOfRangeException(nameof(earned), earned, "Earned score cannot exceed the maximum score.");

        Earned = earned;
        Maximum = maximum;
    }

    public decimal Earned { get; private set; }
    public decimal Maximum { get; private set; }
    public decimal Percentage => Maximum <= 0 ? 0 : Math.Round(Earned / Maximum * 100m, 2);
}

public sealed record AttemptStarted : DomainEvent
{
    public AttemptStarted(TestAttemptId attemptId, TestAssignmentId assignmentId, PublishedTestRevisionId revisionId, ExternalUserId userId, Guid startRequestId, DateTimeOffset occurredAt) : base(occurredAt)
    { AttemptId = attemptId; AssignmentId = assignmentId; RevisionId = revisionId; UserId = userId; StartRequestId = startRequestId; }
    public TestAttemptId AttemptId { get; }
    public TestAssignmentId AssignmentId { get; }
    public PublishedTestRevisionId RevisionId { get; }
    public ExternalUserId UserId { get; }
    public Guid StartRequestId { get; }
}

public sealed record AttemptSubmitted : DomainEvent
{
    public AttemptSubmitted(TestAttemptId attemptId, AttemptScore score, AttemptOutcome outcome, DateTimeOffset occurredAt) : base(occurredAt)
    { AttemptId = attemptId; Score = score; Outcome = outcome; }
    public TestAttemptId AttemptId { get; }
    public AttemptScore Score { get; }
    public AttemptOutcome Outcome { get; }
}

public sealed record AttemptTimedOut : DomainEvent
{
    public AttemptTimedOut(TestAttemptId attemptId, AttemptScore score, AttemptOutcome outcome, DateTimeOffset occurredAt) : base(occurredAt)
    { AttemptId = attemptId; Score = score; Outcome = outcome; }
    public TestAttemptId AttemptId { get; }
    public AttemptScore Score { get; }
    public AttemptOutcome Outcome { get; }
}

public sealed class TestAttempt : AggregateRoot<TestAttemptId>
{
    private readonly List<QuestionResponse> _responses = [];
    public TestAssignmentId AssignmentId { get; private set; }
    public PublishedTestRevisionId RevisionId { get; private set; }
    public ExternalUserId UserId { get; private set; }
    public Guid StartRequestId { get; private set; }
    public AttemptStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? DeadlineAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public AttemptScore? Score { get; private set; }
    public AttemptOutcome? Outcome { get; private set; }
    public IReadOnlyCollection<QuestionResponse> Responses => _responses.AsReadOnly();

    private TestAttempt() { }

    private TestAttempt(TestAttemptId id, TestAssignmentId assignmentId, PublishedTestRevisionId revisionId, ExternalUserId userId, Guid startRequestId, DateTimeOffset startedAt, DateTimeOffset? deadlineAt, IEnumerable<QuestionId> questionIds) : base(id)
    {
        startedAt = startedAt.ToUniversalTime();
        deadlineAt = deadlineAt?.ToUniversalTime();
        if (startRequestId == Guid.Empty) throw new ArgumentException("Start request id cannot be empty.", nameof(startRequestId));
        if (deadlineAt is not null && deadlineAt <= startedAt) throw new ArgumentException("Deadline must be later than start time.", nameof(deadlineAt));
        AssignmentId = assignmentId;
        RevisionId = revisionId;
        UserId = userId;
        StartRequestId = startRequestId;
        Status = AttemptStatus.InProgress;
        StartedAt = startedAt;
        DeadlineAt = deadlineAt;
        _responses.AddRange(questionIds.Distinct().Select(QuestionResponse.Create));
    }

    public static TestAttempt Start(TestAttemptId id, TestAssignmentId assignmentId, PublishedTestRevisionId revisionId, ExternalUserId userId, Guid startRequestId, DateTimeOffset startedAt, DateTimeOffset? deadlineAt, IEnumerable<QuestionId> questionIds)
    {
        ArgumentNullException.ThrowIfNull(questionIds);
        var attempt = new TestAttempt(id, assignmentId, revisionId, userId, startRequestId, startedAt, deadlineAt, questionIds);
        attempt.Raise(new AttemptStarted(id, assignmentId, revisionId, userId, startRequestId, attempt.StartedAt));
        return attempt;
    }

    public bool IsExpiredAt(DateTimeOffset now) => DeadlineAt is { } deadline && now >= deadline;

    public Result<TestAttempt, DomainError> Answer(QuestionId questionId, IEnumerable<AnswerOptionId> optionIds, DateTimeOffset answeredAt)
    {
        answeredAt = answeredAt.ToUniversalTime();
        var active = EnsureWritable(answeredAt); if (active.IsFailure) return active;
        ArgumentNullException.ThrowIfNull(optionIds);
        var selected = optionIds.Distinct().ToArray();
        if (selected.Length == 0) return DomainError.Validation("attempt.answer.empty", "At least one answer option must be selected.");
        var response = _responses.SingleOrDefault(x => x.Id == questionId);
        if (response is null) return DomainError.NotFound("attempt.question.not_found", "Question is not part of this attempt.");
        response.Answer(selected, answeredAt); Touch(); return this;
    }

    public Result<TestAttempt, DomainError> ClearAnswer(QuestionId questionId, DateTimeOffset now)
    {
        now = now.ToUniversalTime();
        var active = EnsureWritable(now); if (active.IsFailure) return active;
        var response = _responses.SingleOrDefault(x => x.Id == questionId);
        if (response is null) return DomainError.NotFound("attempt.question.not_found", "Question is not part of this attempt.");
        response.Clear(); Touch(); return this;
    }

    public Result<TestAttempt, DomainError> Submit(DateTimeOffset submittedAt, AttemptScore score, bool passed)
    {
        submittedAt = submittedAt.ToUniversalTime();
        var active = EnsureInProgress(); if (active.IsFailure) return active;
        if (submittedAt < StartedAt) return DomainError.Validation("attempt.completed_at", "Completion time cannot be earlier than start time.");
        if (IsExpiredAt(submittedAt)) return DomainError.Conflict("attempt.expired", "The attempt deadline has expired.");
        Complete(AttemptStatus.Submitted, submittedAt, score, passed);
        Raise(new AttemptSubmitted(Id, score, Outcome!.Value, submittedAt));
        return this;
    }

    /// <summary>
    /// Closes the attempt because its own deadline has passed. The deadline is the authority: an attempt
    /// whose deadline has not been reached — including an attempt started from a revision without a time
    /// limit, which has no deadline at all — cannot be timed out through this path.
    /// </summary>
    public Result<TestAttempt, DomainError> Timeout(DateTimeOffset timedOutAt, AttemptScore score, bool passed)
    {
        timedOutAt = timedOutAt.ToUniversalTime();
        var active = EnsureInProgress(); if (active.IsFailure) return active;
        if (timedOutAt < StartedAt) return DomainError.Validation("attempt.completed_at", "Completion time cannot be earlier than start time.");
        if (!IsExpiredAt(timedOutAt)) return DomainError.Conflict("attempt.not_expired", "The attempt deadline has not expired yet.");
        return CompleteTimedOut(timedOutAt, score, passed);
    }

    /// <summary>
    /// Closes the attempt on an operator's authority rather than the clock's. Used by the manual
    /// administrator timeout, which exists precisely to end attempts the deadline will never end —
    /// an attempt with no time limit, or one that has to be closed early. See ADR-030.
    /// </summary>
    public Result<TestAttempt, DomainError> ForceTimeout(DateTimeOffset timedOutAt, AttemptScore score, bool passed)
    {
        timedOutAt = timedOutAt.ToUniversalTime();
        var active = EnsureInProgress(); if (active.IsFailure) return active;
        if (timedOutAt < StartedAt) return DomainError.Validation("attempt.completed_at", "Completion time cannot be earlier than start time.");
        return CompleteTimedOut(timedOutAt, score, passed);
    }

    private Result<TestAttempt, DomainError> CompleteTimedOut(DateTimeOffset timedOutAt, AttemptScore score, bool passed)
    {
        Complete(AttemptStatus.TimedOut, timedOutAt, score, passed);
        Raise(new AttemptTimedOut(Id, score, Outcome!.Value, timedOutAt));
        return this;
    }

    private void Complete(AttemptStatus status, DateTimeOffset at, AttemptScore score, bool passed)
    {
        Status = status; CompletedAt = at; Score = score; Outcome = passed ? AttemptOutcome.Passed : AttemptOutcome.Failed; Touch();
    }

    private Result<TestAttempt, DomainError> EnsureWritable(DateTimeOffset now)
    {
        var active = EnsureInProgress();
        if (active.IsFailure) return active;
        return IsExpiredAt(now) ? DomainError.Conflict("attempt.expired", "The attempt deadline has expired.") : this;
    }

    private Result<TestAttempt, DomainError> EnsureInProgress() => Status == AttemptStatus.InProgress ? this : DomainError.Conflict("attempt.completed", "A completed attempt cannot be changed.");
}

public sealed class QuestionResponse : Entity<QuestionId>
{
    private readonly List<SelectedAnswerOption> _selectedOptions = [];
    public DateTimeOffset? AnsweredAt { get; private set; }
    public IReadOnlyCollection<SelectedAnswerOption> SelectedOptions => _selectedOptions.AsReadOnly();
    private QuestionResponse() { }
    private QuestionResponse(QuestionId questionId) : base(questionId) { }
    internal static QuestionResponse Create(QuestionId questionId) => new(questionId);
    internal void Answer(IEnumerable<AnswerOptionId> optionIds, DateTimeOffset answeredAt) { _selectedOptions.Clear(); _selectedOptions.AddRange(optionIds.Select(SelectedAnswerOption.Create)); AnsweredAt = answeredAt.ToUniversalTime(); }
    internal void Clear() { _selectedOptions.Clear(); AnsweredAt = null; }
}

public sealed class SelectedAnswerOption
{
    public AnswerOptionId OptionId { get; private set; }
    private SelectedAnswerOption() { }
    private SelectedAnswerOption(AnswerOptionId optionId) => OptionId = optionId;
    internal static SelectedAnswerOption Create(AnswerOptionId optionId) => new(optionId);
}
