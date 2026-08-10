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
        var now = DateTimeOffset.Parse("2026-08-10T12:00:00Z");
        var group = ExternalGroupId.FromExternalId("students");
        var userId = ExternalUserId.FromSubject("user-1");
        var test = Test.Create("DDD");
        var question = test.AddQuestion("What is an aggregate?", QuestionType.SingleChoice, 1, 1)
            .Match(id => id, error => throw new Xunit.Sdk.XunitException(error.Message));
        test.AddAnswerOption(question, "Consistency boundary", true, 1);
        test.AddAnswerOption(question, "Database table", false, 2);
        test.Publish(now);
        var revision = PublishedTestRevision.From(test, PublishedTestRevisionId.New(), 1, now);
        var assignment = TestAssignment.Create(
            TestAssignmentId.New(),
            revision.Id,
            new AssignmentTarget.Group(group),
            userId,
            now,
            now.AddHours(-1),
            now.AddHours(1),
            1);
        var assignments = new AssignmentRepo(assignment);
        var attempts = new AttemptRepo();
        var handler = new StartAttemptCommandHandler(
            assignments,
            attempts,
            new RevisionRepo(revision),
            new Actor(userId, new HashSet<ExternalGroupId> { group }),
            new Clock(now));

        var result = await handler.Handle(new StartAttemptCommand(assignment.Id), TestContext.Current.CancellationToken);

        Assert.True(result.Match(_ => true, _ => false));
        Assert.NotNull(attempts.Value);
        Assert.Equal(revision.Id, attempts.Value!.RevisionId);
        Assert.Single(attempts.Value.Responses);
    }

    private sealed class AssignmentRepo(TestAssignment value) : ITestAssignmentRepository
    {
        public Task<TestAssignment?> GetAsync(TestAssignmentId id, CancellationToken ct = default) => Task.FromResult<TestAssignment?>(value);
        public Task AddAsync(TestAssignment assignment, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class AttemptRepo : ITestAttemptRepository
    {
        public TestAttempt? Value;
        public Task<TestAttempt?> GetAsync(TestAttemptId id, CancellationToken ct = default) => Task.FromResult(Value?.Id == id ? Value : null);
        public Task<int> CountAttemptsAsync(TestAssignmentId assignmentId, ExternalUserId userId, CancellationToken ct = default) => Task.FromResult(Value is null ? 0 : 1);
        public Task AddAsync(TestAttempt attempt, CancellationToken ct = default)
        {
            Value = attempt;
            return Task.CompletedTask;
        }
        public Task<bool> TryAddWithinLimitAsync(TestAttempt attempt, int? attemptLimit, CancellationToken ct = default)
        {
            if (attemptLimit is { } limit && Value is not null && limit <= 1)
                return Task.FromResult(false);
            Value = attempt;
            return Task.FromResult(true);
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
