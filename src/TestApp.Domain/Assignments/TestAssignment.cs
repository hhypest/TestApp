using TestApp.Core.Monads;
using TestApp.Domain.Common;
using TestApp.Domain.Entities;
using TestApp.Domain.Events;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;

namespace TestApp.Domain.Assignments;

public static class TestAssignmentLimits
{
    public const int CancelReasonMaxLength = 1000;
}

public readonly record struct TestAssignmentId(Guid Value)
{
    public static TestAssignmentId New() => new(Guid.CreateVersion7());
}

public enum AssignmentStatus
{
    Active = 1,
    Cancelled = 2
}

public enum AssignmentTargetType
{
    User = 1,
    Group = 2
}

public abstract record AssignmentTarget
{
    private AssignmentTarget() { }
    public sealed record User(ExternalUserId UserId) : AssignmentTarget;
    public sealed record Group(ExternalGroupId GroupId) : AssignmentTarget;
}

public sealed record TestAssigned : DomainEvent
{
    public TestAssigned(TestAssignmentId assignmentId, PublishedTestRevisionId revisionId, AssignmentTarget target, ExternalUserId assignedBy, DateTimeOffset occurredAt) : base(occurredAt)
    {
        AssignmentId = assignmentId;
        RevisionId = revisionId;
        Target = target;
        AssignedBy = assignedBy;
    }

    public TestAssignmentId AssignmentId { get; }
    public PublishedTestRevisionId RevisionId { get; }
    public AssignmentTarget Target { get; }
    public ExternalUserId AssignedBy { get; }
}

public sealed record TestAssignmentCancelled : DomainEvent
{
    public TestAssignmentCancelled(TestAssignmentId assignmentId, ExternalUserId cancelledBy, string? reason, DateTimeOffset occurredAt) : base(occurredAt)
    {
        AssignmentId = assignmentId;
        CancelledBy = cancelledBy;
        Reason = reason;
    }

    public TestAssignmentId AssignmentId { get; }
    public ExternalUserId CancelledBy { get; }
    public string? Reason { get; }
}

public sealed class TestAssignment : AggregateRoot<TestAssignmentId>
{
    public PublishedTestRevisionId RevisionId { get; private set; }
    public AssignmentTargetType TargetType { get; private set; }
    public string TargetId { get; private set; }
    public AssignmentTarget Target => TargetType switch
    {
        AssignmentTargetType.User => new AssignmentTarget.User(ExternalUserId.FromSubject(TargetId)),
        AssignmentTargetType.Group => new AssignmentTarget.Group(ExternalGroupId.FromExternalId(TargetId)),
        _ => throw new InvalidOperationException("Unknown assignment target type.")
    };
    public ExternalUserId AssignedBy { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }
    public DateTimeOffset AvailableFrom { get; private set; }
    public DateTimeOffset? AvailableUntil { get; private set; }
    public int? AttemptLimit { get; private set; }
    public AssignmentStatus Status { get; private set; }
    public ExternalUserId? CancelledBy { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancelReason { get; private set; }

    private TestAssignment() { TargetId = string.Empty; }

    private TestAssignment(
        TestAssignmentId id,
        PublishedTestRevisionId revisionId,
        AssignmentTarget target,
        ExternalUserId assignedBy,
        DateTimeOffset assignedAt,
        DateTimeOffset availableFrom,
        DateTimeOffset? availableUntil,
        int? attemptLimit) : base(id)
    {
        RevisionId = revisionId;
        (TargetType, TargetId) = target switch
        {
            AssignmentTarget.User user => (AssignmentTargetType.User, ValidateTargetId(user.UserId.Value, nameof(target))),
            AssignmentTarget.Group group => (AssignmentTargetType.Group, ValidateTargetId(group.GroupId.Value, nameof(target))),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        AssignedBy = assignedBy;
        AssignedAt = assignedAt;
        AvailableFrom = availableFrom;
        AvailableUntil = availableUntil;
        AttemptLimit = attemptLimit;
        Status = AssignmentStatus.Active;
    }

    public static Result<TestAssignment, DomainError> Create(
        TestAssignmentId id,
        PublishedTestRevisionId revisionId,
        AssignmentTarget target,
        ExternalUserId assignedBy,
        DateTimeOffset assignedAt,
        DateTimeOffset availableFrom,
        DateTimeOffset? availableUntil,
        int? attemptLimit)
    {
        ArgumentNullException.ThrowIfNull(target);
        assignedAt = assignedAt.ToUniversalTime();
        availableFrom = availableFrom.ToUniversalTime();
        availableUntil = availableUntil?.ToUniversalTime();
        if (availableUntil is not null && availableUntil <= availableFrom)
            return DomainError.Validation("assignment.window", "AvailableUntil must be later than AvailableFrom.");
        if (attemptLimit is <= 0)
            return DomainError.Validation("assignment.attempt_limit", "Attempt limit must be greater than zero.");

        var assignment = new TestAssignment(id, revisionId, target, assignedBy, assignedAt, availableFrom, availableUntil, attemptLimit);
        assignment.Raise(new TestAssigned(id, revisionId, target, assignedBy, assignment.AssignedAt));
        return assignment;
    }

    public bool IsTargetedTo(ExternalUserId userId, IReadOnlySet<ExternalGroupId> groups) =>
        TargetType switch
        {
            AssignmentTargetType.User => TargetId == userId.Value,
            AssignmentTargetType.Group => groups.Any(group => group.Value == TargetId),
            _ => false
        };

    public bool IsAvailableAt(DateTimeOffset now) =>
        Status == AssignmentStatus.Active && now >= AvailableFrom && (AvailableUntil is null || now <= AvailableUntil);

    public Result<TestAssignment, DomainError> ChangeAvailability(DateTimeOffset availableFrom, DateTimeOffset? availableUntil)
    {
        availableFrom = availableFrom.ToUniversalTime();
        availableUntil = availableUntil?.ToUniversalTime();
        if (Status != AssignmentStatus.Active)
            return DomainError.Conflict("assignment.cancelled", "Cancelled assignments cannot be changed.");
        if (availableUntil is not null && availableUntil <= availableFrom)
            return DomainError.Validation("assignment.window", "AvailableUntil must be later than AvailableFrom.");

        AvailableFrom = availableFrom;
        AvailableUntil = availableUntil;
        Touch();
        return this;
    }

    public Result<TestAssignment, DomainError> ChangeAttemptLimit(int? attemptLimit)
    {
        if (Status != AssignmentStatus.Active)
            return DomainError.Conflict("assignment.cancelled", "Cancelled assignments cannot be changed.");
        if (attemptLimit is <= 0)
            return DomainError.Validation("assignment.attempt_limit", "Attempt limit must be greater than zero.");

        AttemptLimit = attemptLimit;
        Touch();
        return this;
    }

    public Result<TestAssignment, DomainError> Cancel(ExternalUserId cancelledBy, DateTimeOffset cancelledAt, string? reason)
    {
        cancelledAt = cancelledAt.ToUniversalTime();
        if (Status == AssignmentStatus.Cancelled)
            return DomainError.Conflict("assignment.cancelled", "Assignment is already cancelled.");
        if (cancelledAt < AssignedAt)
            return DomainError.Validation("assignment.cancelled_at", "Cancellation time cannot be earlier than assignment time.");

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (normalizedReason?.Length > TestAssignmentLimits.CancelReasonMaxLength)
            return DomainError.Validation(
                "assignment.cancel_reason",
                $"Cancellation reason cannot exceed {TestAssignmentLimits.CancelReasonMaxLength} characters.");

        Status = AssignmentStatus.Cancelled;
        CancelledBy = cancelledBy;
        CancelledAt = cancelledAt;
        CancelReason = normalizedReason;
        Touch();
        Raise(new TestAssignmentCancelled(Id, cancelledBy, CancelReason, cancelledAt));
        return this;
    }

    private static string ValidateTargetId(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (value.Length > ExternalIdentityLimits.MaxIdentifierLength)
            throw new ArgumentException(
                $"Assignment target identifier cannot exceed {ExternalIdentityLimits.MaxIdentifierLength} characters.",
                parameter);
        return value;
    }
}
