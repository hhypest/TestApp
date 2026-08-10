using TestApp.Application.Abstractions;
using TestApp.Application.Attempts;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;

namespace TestApp.Application.Tests.Unit;

public sealed class StartAttemptTests
{
    [Fact]
    public async Task Group_assignment_allows_group_member()
    {
        var now = DateTimeOffset.Parse("2026-08-10T12:00:00Z");
        var group = ExternalGroupId.FromExternalId("students");
        var assignment = TestAssignment.Create(TestAssignmentId.New(), PublishedTestRevisionId.New(), new AssignmentTarget.Group(group), now.AddHours(-1), now.AddHours(1), 1, now);
        var assignments = new AssignmentRepo(assignment);
        var attempts = new AttemptRepo();
        var handler = new StartAttemptCommandHandler(assignments, attempts, new Actor(ExternalUserId.FromSubject("user-1"), [group]), new Clock(now), new Uow());

        var result = await handler.Handle(new StartAttemptCommand(assignment.Id), default);
        Assert.True(result.Match(_ => true, _ => false));
        Assert.NotNull(attempts.Value);
    }

    private sealed class AssignmentRepo(TestAssignment value) : ITestAssignmentRepository
    {
        public Task<TestAssignment?> GetAsync(TestAssignmentId id, CancellationToken ct = default) => Task.FromResult<TestAssignment?>(value);
        public Task AddAsync(TestAssignment assignment, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> CountAttemptsAsync(TestAssignmentId assignmentId, ExternalUserId userId, CancellationToken ct = default) => Task.FromResult(0);
    }
    private sealed class AttemptRepo : ITestAttemptRepository { public TestAttempt? Value; public Task AddAsync(TestAttempt attempt, CancellationToken ct = default) { Value = attempt; return Task.CompletedTask; } }
    private sealed record Actor(ExternalUserId UserId, IReadOnlySet<ExternalGroupId> Groups) : ICurrentActor { public IReadOnlySet<string> Roles => new HashSet<string>(); }
    private sealed record Clock(DateTimeOffset UtcNow) : IClock;
    private sealed class Uow : IUnitOfWork { public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask; }
}
