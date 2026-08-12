using TestApp.Application.Abstractions;
using TestApp.Application.Attempts;
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

        var result = await fixture.Handler.Handle(
            new StartAttemptCommand(fixture.Assignment.Id, requestId),
            TestContext.Current.CancellationToken);

        Assert.True(result.Match(_ => true, _ => false));
        Assert.NotNull(fixture.Attempts.Value);
        Assert.Equal(fixture.Revision.Id, fixture.Attempts.Value!.RevisionId);
        Assert.Equal(requestId, fixture.Attempts.Value.StartRequestId);
        Assert.Single(fixture.Attempts.Value.Responses);
    }

    [Fact]
    public async Task Same_idempotency_key_returns_same_attempt()
    {
        var fixture = CreateFixture();
        var requestId = Guid.NewGuid();

        var first = await fixture.Handler.Handle(new StartAttemptCommand(fixture.Assignment.Id, requestId), TestContext.Current.CancellationToken);
        var second = await fixture.Handler.Handle(new StartAttemptCommand(fixture.Assignment.Id, requestId), TestContext.Current.CancellationToken);

        var firstId = first.Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));
        var secondId = second.Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));
        Assert.Equal(firstId, secondId);
    }

    private static Fixture CreateFixture()
    {
        var now = DateTimeOffset.Parse("2026-08-10T12:00:00Z");
        var group = ExternalGroupId.FromExternalId("students");
        var userId = ExternalUserId.FromSubject("user-1");
        var test = Test.Create("DDD", ExternalUserId.FromSubject("author-1"));
        var question = test.AddQuestion("What is an aggregate?", QuestionType.SingleChoice, 1, 1)
            .Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));
        test.AddAnswerOption(question, "Consistency boundary", true, 1);
        test.AddAnswerOption(question, "Database table", false, 2);
        test.Publish(now);
        var revision = PublishedTestRevision.From(test, PublishedTestRevisionId.New(), 1, now);
        var assignment = TestAssignment.Create(
            TestAssignmentId.New(), revision.Id, new AssignmentTarget.Group(group), userId,
            now, now.AddHours(-1), now.AddHours(1), 1);
        var attempts = new AttemptRepo();
        var handler = new StartAttemptCommandHandler(
            new AssignmentRepo(assignment), attempts, new RevisionRepo(revision),
            new Actor(userId, new HashSet<ExternalGroupId> { group }), new Clock(now));
        return new Fixture(assignment, revision, attempts, handler);
    }

    private sealed record Fixture(TestAssignment Assignment, PublishedTestRevision Revision, AttemptRepo Attempts, StartAttemptCommandHandler Handler);

    private sealed class AssignmentRepo(TestAssignment value) : ITestAssignmentRepository
    {
        public Task<TestAssignment?> GetAsync(TestAssignmentId id, CancellationToken ct = default) => Task.FromResult<TestAssignment?>(value);
        public Task AddAsync(TestAssignment assignment, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class AttemptRepo : ITestAttemptRepository
    {
        public TestAttempt? Value;
        public Task<TestAttempt?> GetAsync(TestAttemptId id, CancellationToken ct = default) => Task.FromResult(Value?.Id == id ? Value : null);
        public Task<IReadOnlyCollection<TestAttemptId>> GetExpiredInProgressIdsAsync(DateTimeOffset now, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<TestAttemptId>>(Array.Empty<TestAttemptId>());
        public Task<int> CountAttemptsAsync(TestAssignmentId assignmentId, ExternalUserId userId, CancellationToken ct = default) => Task.FromResult(Value is null ? 0 : 1);
        public Task AddAsync(TestAttempt attempt, CancellationToken ct = default) { Value = attempt; return Task.CompletedTask; }
        public Task<TestAttemptId?> TryAddWithinLimitAsync(TestAttempt attempt, int? attemptLimit, CancellationToken ct = default)
        {
            if (Value is not null && Value.AssignmentId == attempt.AssignmentId && Value.UserId == attempt.UserId && Value.StartRequestId == attempt.StartRequestId)
                return Task.FromResult<TestAttemptId?>(Value.Id);
            if (attemptLimit is { } limit && Value is not null && limit <= 1)
                return Task.FromResult<TestAttemptId?>(null);
            Value = attempt;
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
