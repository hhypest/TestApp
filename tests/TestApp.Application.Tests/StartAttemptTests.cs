using TestApp.Application.Abstractions;
using TestApp.Application.Attempts;
using TestApp.Application.Common;
using TestApp.Core.Monads;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using Xunit;

namespace TestApp.Application.Tests.Unit;

public sealed class StartAttemptTests
{
    [Fact]
    public async Task Group_assignment_allows_group_member()
    {
        var fixture = CreateFixture();
        var requestId = Guid.NewGuid();

        var result = await fixture.CreateHandler().Handle(
            new StartAttemptCommand(fixture.Assignment.Id, requestId),
            TestContext.Current.CancellationToken);

        Assert.True(result.Match(_ => true, _ => false));
        var attempt = Assert.Single(fixture.Attempts.Values);
        Assert.Equal(fixture.Revision.Id, attempt.RevisionId);
        Assert.Equal(requestId, attempt.StartRequestId);
        Assert.Single(attempt.Responses);
    }

    [Fact]
    public async Task Same_idempotency_key_returns_same_attempt()
    {
        var fixture = CreateFixture();
        var requestId = Guid.NewGuid();

        var handler = fixture.CreateHandler();
        var first = await handler.Handle(new StartAttemptCommand(fixture.Assignment.Id, requestId), TestContext.Current.CancellationToken);
        var second = await handler.Handle(new StartAttemptCommand(fixture.Assignment.Id, requestId), TestContext.Current.CancellationToken);

        Assert.Equal(Success(first), Success(second));
        Assert.Single(fixture.Attempts.Values);
    }

    [Fact]
    public async Task Same_idempotency_key_replays_after_assignment_is_cancelled()
    {
        var fixture = CreateFixture();
        var requestId = Guid.NewGuid();
        var handler = fixture.CreateHandler();
        var first = await handler.Handle(
            new StartAttemptCommand(fixture.Assignment.Id, requestId),
            TestContext.Current.CancellationToken);
        var cancelled = fixture.Assignment.Cancel(
            ExternalUserId.FromSubject("admin-1"),
            fixture.Now.AddMinutes(1),
            "Schedule changed");

        var replay = await handler.Handle(
            new StartAttemptCommand(fixture.Assignment.Id, requestId),
            TestContext.Current.CancellationToken);

        Assert.True(cancelled.Match(_ => true, _ => false));
        Assert.Equal(Success(first), Success(replay));
        Assert.Single(fixture.Attempts.Values);
    }

    [Fact]
    public async Task Same_idempotency_key_replays_after_assignment_expires()
    {
        var fixture = CreateFixture();
        var requestId = Guid.NewGuid();
        var first = await fixture.CreateHandler().Handle(
            new StartAttemptCommand(fixture.Assignment.Id, requestId),
            TestContext.Current.CancellationToken);

        var replay = await fixture.CreateHandler(now: fixture.Now.AddHours(2)).Handle(
            new StartAttemptCommand(fixture.Assignment.Id, requestId),
            TestContext.Current.CancellationToken);

        Assert.Equal(Success(first), Success(replay));
        Assert.Single(fixture.Attempts.Values);
    }

    [Fact]
    public async Task Same_idempotency_key_replays_after_group_membership_is_removed()
    {
        var fixture = CreateFixture();
        var requestId = Guid.NewGuid();
        var first = await fixture.CreateHandler().Handle(
            new StartAttemptCommand(fixture.Assignment.Id, requestId),
            TestContext.Current.CancellationToken);

        var replay = await fixture.CreateHandler(groups: new HashSet<ExternalGroupId>()).Handle(
            new StartAttemptCommand(fixture.Assignment.Id, requestId),
            TestContext.Current.CancellationToken);

        Assert.Equal(Success(first), Success(replay));
        Assert.Single(fixture.Attempts.Values);
    }

    [Fact]
    public async Task New_idempotency_key_still_rejects_cancelled_assignment()
    {
        var fixture = CreateFixture();
        var cancelled = fixture.Assignment.Cancel(
            ExternalUserId.FromSubject("admin-1"),
            fixture.Now,
            "Schedule changed");

        var result = await fixture.CreateHandler().Handle(
            new StartAttemptCommand(fixture.Assignment.Id, Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        Assert.True(cancelled.Match(_ => true, _ => false));
        Assert.Equal("assignment.unavailable", Failure(result).Code);
        Assert.Empty(fixture.Attempts.Values);
    }

    [Fact]
    public async Task New_idempotency_key_still_rejects_removed_group_member()
    {
        var fixture = CreateFixture();

        var result = await fixture.CreateHandler(groups: new HashSet<ExternalGroupId>()).Handle(
            new StartAttemptCommand(fixture.Assignment.Id, Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        Assert.Equal("assignment.forbidden", Failure(result).Code);
        Assert.Empty(fixture.Attempts.Values);
    }

    [Fact]
    public async Task Same_idempotency_key_does_not_reveal_attempt_to_another_actor()
    {
        var fixture = CreateFixture();
        var requestId = Guid.NewGuid();
        var first = await fixture.CreateHandler().Handle(
            new StartAttemptCommand(fixture.Assignment.Id, requestId),
            TestContext.Current.CancellationToken);
        var otherUser = ExternalUserId.FromSubject("user-2");

        var second = await fixture.CreateHandler(
            userId: otherUser,
            groups: new HashSet<ExternalGroupId>()).Handle(
            new StartAttemptCommand(fixture.Assignment.Id, requestId),
            TestContext.Current.CancellationToken);

        _ = Success(first);
        Assert.Equal("assignment.forbidden", Failure(second).Code);
        Assert.Single(fixture.Attempts.Values);
    }

    private static Fixture CreateFixture()
    {
        var now = DateTimeOffset.Parse("2026-08-10T12:00:00Z");
        var group = ExternalGroupId.FromExternalId("students");
        var userId = ExternalUserId.FromSubject("user-1");
        var test = Test.Create("DDD", ExternalUserId.FromSubject("author-1"))
            .Match(t => t, error => throw new Xunit.Sdk.XunitException(error.Message));
        var question = test.AddQuestion("What is an aggregate?", QuestionType.SingleChoice, 1, 1)
            .Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));
        test.AddAnswerOption(question, "Consistency boundary", true, 1);
        test.AddAnswerOption(question, "Database table", false, 2);
        test.Publish(now);
        var revision = PublishedTestRevision.From(test, PublishedTestRevisionId.New(), 1, now);
        var assignment = TestAssignment.Create(
            TestAssignmentId.New(), revision.Id, new AssignmentTarget.Group(group), userId,
            now, now.AddHours(-1), now.AddHours(1), 1)
            .Match(a => a, error => throw new Xunit.Sdk.XunitException(error.Message));
        return new Fixture(
            assignment,
            revision,
            new AssignmentRepo(assignment),
            new AttemptRepo(),
            new RevisionRepo(revision),
            userId,
            group,
            now);
    }

    private static TestAttemptId Success(Result<TestAttemptId, Error> result) =>
        result.Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));

    private static Error Failure(Result<TestAttemptId, Error> result) =>
        result.Match(_ => throw new Xunit.Sdk.XunitException("Expected the command to fail."), error => error);

    private sealed record Fixture(
        TestAssignment Assignment,
        PublishedTestRevision Revision,
        AssignmentRepo Assignments,
        AttemptRepo Attempts,
        RevisionRepo Revisions,
        ExternalUserId UserId,
        ExternalGroupId Group,
        DateTimeOffset Now)
    {
        public StartAttemptCommandHandler CreateHandler(
            ExternalUserId? userId = null,
            IReadOnlySet<ExternalGroupId>? groups = null,
            DateTimeOffset? now = null) =>
            new(
                Assignments,
                Attempts,
                Revisions,
                new Actor(userId ?? UserId, groups ?? new HashSet<ExternalGroupId> { Group }),
                new Clock(now ?? Now));
    }

    private sealed class AssignmentRepo(TestAssignment value) : ITestAssignmentRepository
    {
        public Task<TestAssignment?> GetAsync(TestAssignmentId id, CancellationToken ct = default) => Task.FromResult<TestAssignment?>(value);
        public Task AddAsync(TestAssignment assignment, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class AttemptRepo : ITestAttemptRepository
    {
        public List<TestAttempt> Values { get; } = [];

        public Task<TestAttempt?> GetAsync(TestAttemptId id, CancellationToken ct = default) =>
            Task.FromResult(Values.SingleOrDefault(value => value.Id == id));

        public Task<TestAttemptId?> FindIdByStartRequestAsync(
            TestAssignmentId assignmentId,
            ExternalUserId userId,
            Guid startRequestId,
            CancellationToken ct = default) =>
            Task.FromResult<TestAttemptId?>(Values
                .Where(value => value.AssignmentId == assignmentId && value.UserId == userId && value.StartRequestId == startRequestId)
                .Select(value => (TestAttemptId?)value.Id)
                .SingleOrDefault());

        public Task<IReadOnlyCollection<TestAttemptId>> GetExpiredInProgressIdsAsync(DateTimeOffset now, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<TestAttemptId>>(Array.Empty<TestAttemptId>());

        public Task<int> CountAttemptsAsync(TestAssignmentId assignmentId, ExternalUserId userId, CancellationToken ct = default) =>
            Task.FromResult(Values.Count(value => value.AssignmentId == assignmentId && value.UserId == userId));

        public Task AddAsync(TestAttempt attempt, CancellationToken ct = default)
        {
            Values.Add(attempt);
            return Task.CompletedTask;
        }

        public Task<TestAttemptId?> TryAddWithinLimitAsync(TestAttempt attempt, int? attemptLimit, CancellationToken ct = default)
        {
            var existing = Values.SingleOrDefault(value =>
                value.AssignmentId == attempt.AssignmentId &&
                value.UserId == attempt.UserId &&
                value.StartRequestId == attempt.StartRequestId);
            if (existing is not null)
                return Task.FromResult<TestAttemptId?>(existing.Id);
            if (attemptLimit is { } limit && Values.Count(value =>
                    value.AssignmentId == attempt.AssignmentId && value.UserId == attempt.UserId) >= limit)
                return Task.FromResult<TestAttemptId?>(null);
            Values.Add(attempt);
            return Task.FromResult<TestAttemptId?>(attempt.Id);
        }
    }

    private sealed class RevisionRepo(PublishedTestRevision revision) : IPublishedTestRevisionRepository
    {
        public Task<PublishedTestRevision?> GetAsync(PublishedTestRevisionId id, CancellationToken ct = default) => Task.FromResult<PublishedTestRevision?>(revision.Id == id ? revision : null);
        public Task<int> GetNextVersionAsync(TestId testId, CancellationToken ct = default) => Task.FromResult(2);
        public Task AddAsync(PublishedTestRevision value, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed record Actor(ExternalUserId UserId, IReadOnlySet<ExternalGroupId> Groups) : ICurrentActor
    {
        public IReadOnlySet<string> Roles => new HashSet<string>();
    }

    private sealed record Clock(DateTimeOffset UtcNow) : IClock;
}
