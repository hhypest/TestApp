using TestApp.Domain.Assignments;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using Xunit;

namespace TestApp.Domain.Tests.Unit;

/// <summary>
/// Targeting, availability-window and cancellation rules of <see cref="TestAssignment"/>.
/// </summary>
public sealed class AssignmentLifecycleTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-16T10:00:00Z");
    private static readonly ExternalUserId Admin = ExternalUserId.FromSubject("admin-1");
    private static readonly ExternalUserId StudentId = ExternalUserId.FromSubject("student-1");
    private static readonly ExternalGroupId StudentsGroup = ExternalGroupId.FromExternalId("students");

    private static TestAssignment NewAssignment(
        AssignmentTarget? target = null,
        DateTimeOffset? availableFrom = null,
        DateTimeOffset? availableUntil = null,
        int? attemptLimit = null) =>
        TestAssignment.Create(
            TestAssignmentId.New(),
            PublishedTestRevisionId.New(),
            target ?? new AssignmentTarget.User(StudentId),
            Admin,
            Now,
            availableFrom ?? Now,
            availableUntil,
            attemptLimit)
        .Match(assignment => assignment, error => throw new Xunit.Sdk.XunitException(error.Message));

    // ---------------------------------------------------------------- creation

    [Fact]
    public void Created_assignment_starts_active_and_records_its_target()
    {
        var assignment = NewAssignment();

        Assert.Equal(AssignmentStatus.Active, assignment.Status);
        Assert.Equal(AssignmentTargetType.User, assignment.TargetType);
        Assert.Equal(StudentId.Value, assignment.TargetId);
        Assert.Equal(Admin, assignment.AssignedBy);
    }

    [Fact]
    public void A_group_assignment_records_the_group_target()
    {
        var assignment = NewAssignment(new AssignmentTarget.Group(StudentsGroup));

        Assert.Equal(AssignmentTargetType.Group, assignment.TargetType);
        Assert.Equal(StudentsGroup.Value, assignment.TargetId);
        Assert.IsType<AssignmentTarget.Group>(assignment.Target);
    }

    [Fact]
    public void Create_accepts_an_open_ended_window_and_an_unlimited_attempt_count()
    {
        var assignment = NewAssignment(availableUntil: null, attemptLimit: null);

        Assert.Null(assignment.AvailableUntil);
        Assert.Null(assignment.AttemptLimit);
    }

    [Fact]
    public void Create_rejects_a_window_that_ends_exactly_when_it_starts()
    {
        var result = TestAssignment.Create(
            TestAssignmentId.New(),
            PublishedTestRevisionId.New(),
            new AssignmentTarget.User(StudentId),
            Admin,
            Now,
            Now,
            Now,
            null);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("assignment.window", error.Code);
    }

    [Fact]
    public void Create_normalizes_incoming_timestamps_to_utc()
    {
        var offsetTime = new DateTimeOffset(2026, 8, 16, 15, 0, 0, TimeSpan.FromHours(5));
        var assignment = NewAssignment(availableFrom: offsetTime);

        Assert.Equal(TimeSpan.Zero, assignment.AvailableFrom.Offset);
        Assert.Equal(offsetTime.ToUniversalTime(), assignment.AvailableFrom);
    }

    // --------------------------------------------------------------- targeting

    [Fact]
    public void A_user_assignment_targets_only_that_user()
    {
        var assignment = NewAssignment(new AssignmentTarget.User(StudentId));

        Assert.True(assignment.IsTargetedTo(StudentId, new HashSet<ExternalGroupId>()));
        Assert.False(assignment.IsTargetedTo(ExternalUserId.FromSubject("student-2"), new HashSet<ExternalGroupId>()));
    }

    [Fact]
    public void A_group_assignment_targets_any_member_of_that_group()
    {
        var assignment = NewAssignment(new AssignmentTarget.Group(StudentsGroup));
        var member = new HashSet<ExternalGroupId> { StudentsGroup };
        var other = new HashSet<ExternalGroupId> { ExternalGroupId.FromExternalId("teachers") };

        Assert.True(assignment.IsTargetedTo(ExternalUserId.FromSubject("anyone"), member));
        Assert.False(assignment.IsTargetedTo(StudentId, other));
        Assert.False(assignment.IsTargetedTo(StudentId, new HashSet<ExternalGroupId>()));
    }

    // ------------------------------------------------------------ availability

    [Fact]
    public void IsAvailableAt_is_inclusive_at_both_ends_of_the_window()
    {
        var from = Now;
        var until = Now.AddDays(1);
        var assignment = NewAssignment(availableFrom: from, availableUntil: until);

        Assert.False(assignment.IsAvailableAt(from.AddTicks(-1)));
        Assert.True(assignment.IsAvailableAt(from));
        Assert.True(assignment.IsAvailableAt(until));
        Assert.False(assignment.IsAvailableAt(until.AddTicks(1)));
    }

    [Fact]
    public void An_open_ended_assignment_stays_available_indefinitely()
    {
        var assignment = NewAssignment(availableUntil: null);

        Assert.True(assignment.IsAvailableAt(Now.AddYears(5)));
    }

    [Fact]
    public void A_cancelled_assignment_is_never_available()
    {
        var assignment = NewAssignment();
        Assert.False(assignment.Cancel(Admin, Now.AddMinutes(1), "superseded").TryGetError(out _));

        Assert.False(assignment.IsAvailableAt(Now.AddMinutes(2)));
    }

    // ----------------------------------------------------------------- changes

    [Fact]
    public void ChangeAvailability_updates_the_window()
    {
        var assignment = NewAssignment();
        var newFrom = Now.AddDays(1);
        var newUntil = Now.AddDays(2);

        Assert.False(assignment.ChangeAvailability(newFrom, newUntil).TryGetError(out _));

        Assert.Equal(newFrom, assignment.AvailableFrom);
        Assert.Equal(newUntil, assignment.AvailableUntil);
    }

    [Fact]
    public void ChangeAvailability_rejects_an_inverted_window()
    {
        var assignment = NewAssignment();

        var result = assignment.ChangeAvailability(Now.AddDays(2), Now.AddDays(1));

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("assignment.window", error.Code);
    }

    [Fact]
    public void ChangeAttemptLimit_rejects_zero_and_negative_values()
    {
        var assignment = NewAssignment(attemptLimit: 3);

        Assert.True(assignment.ChangeAttemptLimit(0).TryGetError(out var zeroError));
        Assert.Equal("assignment.attempt_limit", zeroError.Code);
        Assert.True(assignment.ChangeAttemptLimit(-1).TryGetError(out var negativeError));
        Assert.Equal("assignment.attempt_limit", negativeError.Code);
        Assert.Equal(3, assignment.AttemptLimit);
    }

    [Fact]
    public void ChangeAttemptLimit_accepts_null_to_lift_the_limit()
    {
        var assignment = NewAssignment(attemptLimit: 2);

        Assert.False(assignment.ChangeAttemptLimit(null).TryGetError(out _));

        Assert.Null(assignment.AttemptLimit);
    }

    [Fact]
    public void A_cancelled_assignment_rejects_window_and_limit_changes()
    {
        var assignment = NewAssignment();
        Assert.False(assignment.Cancel(Admin, Now, null).TryGetError(out _));

        var window = assignment.ChangeAvailability(Now, Now.AddDays(1));
        var limit = assignment.ChangeAttemptLimit(5);

        Assert.True(window.TryGetError(out var windowError));
        Assert.Equal("assignment.cancelled", windowError.Code);
        Assert.True(limit.TryGetError(out var limitError));
        Assert.Equal("assignment.cancelled", limitError.Code);
    }

    // ------------------------------------------------------------ cancellation

    [Fact]
    public void Cancel_records_the_actor_time_and_trimmed_reason()
    {
        var assignment = NewAssignment();
        var cancelledAt = Now.AddHours(1);

        Assert.False(assignment.Cancel(Admin, cancelledAt, "  duplicate assignment  ").TryGetError(out _));

        Assert.Equal(AssignmentStatus.Cancelled, assignment.Status);
        Assert.Equal(Admin, assignment.CancelledBy);
        Assert.Equal(cancelledAt, assignment.CancelledAt);
        Assert.Equal("duplicate assignment", assignment.CancelReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_normalizes_a_blank_reason_to_null(string? reason)
    {
        var assignment = NewAssignment();

        Assert.False(assignment.Cancel(Admin, Now, reason).TryGetError(out _));

        Assert.Null(assignment.CancelReason);
    }

    [Fact]
    public void Cancel_rejects_a_reason_longer_than_the_persistence_limit()
    {
        var assignment = NewAssignment();

        var result = assignment.Cancel(
            Admin,
            Now,
            new string('x', TestAssignmentLimits.CancelReasonMaxLength + 1));

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("assignment.cancel_reason", error.Code);
        Assert.Equal(AssignmentStatus.Active, assignment.Status);
    }

    [Fact]
    public void Cancel_rejects_a_cancellation_time_before_the_assignment_time()
    {
        var assignment = NewAssignment();

        var result = assignment.Cancel(Admin, Now.AddMinutes(-1), null);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("assignment.cancelled_at", error.Code);
    }

    [Fact]
    public void Cancelling_twice_is_a_conflict()
    {
        var assignment = NewAssignment();
        Assert.False(assignment.Cancel(Admin, Now, null).TryGetError(out _));

        var result = assignment.Cancel(Admin, Now, null);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("assignment.cancelled", error.Code);
    }
}
