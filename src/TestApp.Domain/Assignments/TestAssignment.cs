using TestApp.Domain.Entities;
using TestApp.Domain.Events;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;

namespace TestApp.Domain.Assignments;

public readonly record struct TestAssignmentId(Guid Value)
{
    public static TestAssignmentId New() => new(Guid.CreateVersion7());
}

public abstract record AssignmentTarget
{
    private AssignmentTarget() { }

    public sealed record User(ExternalUserId UserId) : AssignmentTarget;
    public sealed record Group(ExternalGroupId GroupId) : AssignmentTarget;
}

public sealed record TestAssigned(
    TestAssignmentId AssignmentId,
    PublishedTestRevisionId RevisionId,
    AssignmentTarget Target,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed class TestAssignment : AggregateRoot<TestAssignmentId>
{
    public PublishedTestRevisionId RevisionId { get; private set; }
    public AssignmentTarget Target { get; private set; }
    public DateTimeOffset AvailableFrom { get; private set; }
    public DateTimeOffset? AvailableUntil { get; private set; }
    public int? AttemptLimit { get; private set; }

    private TestAssignment()
    {
        Target = null!;
    }

    private TestAssignment(
        TestAssignmentId id,
        PublishedTestRevisionId revisionId,
        AssignmentTarget target,
        DateTimeOffset availableFrom,
        DateTimeOffset? availableUntil,
        int? attemptLimit)
    {
        if (availableUntil is not null && availableUntil <= availableFrom)
            throw new ArgumentException("Availability end must be later than availability start.", nameof(availableUntil));
        if (attemptLimit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(attemptLimit));

        Id = id;
        RevisionId = revisionId;
        Target = target;
        AvailableFrom = availableFrom;
        AvailableUntil = availableUntil;
        AttemptLimit = attemptLimit;
    }

    public static TestAssignment Create(
        TestAssignmentId id,
        PublishedTestRevisionId revisionId,
        AssignmentTarget target,
        DateTimeOffset availableFrom,
        DateTimeOffset? availableUntil,
        int? attemptLimit,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(target);
        var assignment = new TestAssignment(id, revisionId, target, availableFrom, availableUntil, attemptLimit);
        assignment.Raise(new TestAssigned(id, revisionId, target, occurredAt));
        return assignment;
    }

    public bool IsAvailableAt(DateTimeOffset now) =>
        now >= AvailableFrom && (AvailableUntil is null || now <= AvailableUntil);
}
