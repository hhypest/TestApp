using System.Data;
using Microsoft.EntityFrameworkCore;
using TestApp.Application.Abstractions;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;

namespace TestApp.Infrastructure.Persistence;

public sealed class TestRepository(AppDbContext db) : ITestRepository
{
    public Task<Test?> GetAsync(TestId id, CancellationToken ct = default) => db.Tests.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task AddAsync(Test test, CancellationToken ct = default) => await db.Tests.AddAsync(test, ct);
}

public sealed class PublishedTestRevisionRepository(AppDbContext db) : IPublishedTestRevisionRepository
{
    public Task<PublishedTestRevision?> GetAsync(PublishedTestRevisionId id, CancellationToken ct = default) => db.Revisions.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<int> GetNextVersionAsync(TestId testId, CancellationToken ct = default) => (await db.Revisions.Where(x => x.TestId == testId).MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1;
    public async Task AddAsync(PublishedTestRevision revision, CancellationToken ct = default) => await db.Revisions.AddAsync(revision, ct);
}

public sealed class TestAssignmentRepository(AppDbContext db) : ITestAssignmentRepository
{
    public Task<TestAssignment?> GetAsync(TestAssignmentId id, CancellationToken ct = default) => db.Assignments.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task AddAsync(TestAssignment assignment, CancellationToken ct = default) => await db.Assignments.AddAsync(assignment, ct);
}

public sealed class TestAttemptRepository(AppDbContext db) : ITestAttemptRepository
{
    public Task<TestAttempt?> GetAsync(TestAttemptId id, CancellationToken ct = default) => db.Attempts.SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyCollection<TestAttemptId>> GetExpiredInProgressIdsAsync(DateTimeOffset now, int limit, CancellationToken ct = default)
    {
        if (limit < 1) return Array.Empty<TestAttemptId>();

        return await db.Attempts
            .AsNoTracking()
            .Where(x => x.Status == AttemptStatus.InProgress && x.DeadlineAt != null && x.DeadlineAt <= now)
            .OrderBy(x => x.DeadlineAt)
            .Select(x => x.Id)
            .Take(limit)
            .ToArrayAsync(ct);
    }

    public Task<int> CountAttemptsAsync(TestAssignmentId assignmentId, ExternalUserId userId, CancellationToken ct = default) => db.Attempts.CountAsync(x => x.AssignmentId == assignmentId && x.UserId == userId, ct);
    public async Task AddAsync(TestAttempt attempt, CancellationToken ct = default) => await db.Attempts.AddAsync(attempt, ct);

    public async Task<TestAttemptId?> TryAddWithinLimitAsync(TestAttempt attempt, int? attemptLimit, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var existingId = await db.Attempts
            .Where(x => x.AssignmentId == attempt.AssignmentId && x.UserId == attempt.UserId && x.StartRequestId == attempt.StartRequestId)
            .Select(x => (TestAttemptId?)x.Id)
            .SingleOrDefaultAsync(ct);

        if (existingId is not null)
        {
            await transaction.CommitAsync(ct);
            return existingId;
        }

        if (attemptLimit is { } limit)
        {
            var count = await db.Attempts.CountAsync(x => x.AssignmentId == attempt.AssignmentId && x.UserId == attempt.UserId, ct);
            if (count >= limit)
            {
                await transaction.RollbackAsync(ct);
                return null;
            }
        }

        await db.Attempts.AddAsync(attempt, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return attempt.Id;
    }
}
