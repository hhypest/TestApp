using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;

namespace TestApp.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface ICurrentActor
{
    ExternalUserId UserId { get; }
    IReadOnlySet<ExternalGroupId> Groups { get; }
    IReadOnlySet<string> Roles { get; }
}

public interface ITestRepository
{
    Task<Test?> GetAsync(TestId id, CancellationToken cancellationToken = default);
    Task AddAsync(Test test, CancellationToken cancellationToken = default);
}

public interface IPublishedTestRevisionRepository
{
    Task<PublishedTestRevision?> GetAsync(PublishedTestRevisionId id, CancellationToken cancellationToken = default);
    Task<int> GetNextVersionAsync(TestId testId, CancellationToken cancellationToken = default);
    Task AddAsync(PublishedTestRevision revision, CancellationToken cancellationToken = default);
}

public interface ITestAssignmentRepository
{
    Task<TestAssignment?> GetAsync(TestAssignmentId id, CancellationToken cancellationToken = default);
    Task AddAsync(TestAssignment assignment, CancellationToken cancellationToken = default);
}

public interface ITestAttemptRepository
{
    Task<TestAttempt?> GetAsync(TestAttemptId id, CancellationToken cancellationToken = default);
    Task<int> CountAttemptsAsync(TestAssignmentId assignmentId, ExternalUserId userId, CancellationToken cancellationToken = default);
    Task AddAsync(TestAttempt attempt, CancellationToken cancellationToken = default);
    Task<bool> TryAddWithinLimitAsync(TestAttempt attempt, int? attemptLimit, CancellationToken cancellationToken = default);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
