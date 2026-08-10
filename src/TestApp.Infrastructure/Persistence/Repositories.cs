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
    public async Task<int> GetNextVersionAsync(TestId testId, CancellationToken ct = default) =>
        (await db.Revisions.Where(x => x.TestId == testId).MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1;
    public async Task AddAsync(PublishedTestRevision revision, CancellationToken ct = default) => await db.Revisions.AddAsync(revision, ct);
}

public sealed class TestAssignmentRepository(AppDbContext db) : ITestAssignmentRepository
{
    public Task<TestAssignment?> GetAsync(TestAssignmentId id, CancellationToken ct = default) => db.Assignments.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task AddAsync(TestAssignment assignment, CancellationToken ct = default) => await db.Assignments.AddAsync(assignment, ct);
    public Task<int> CountAttemptsAsync(TestAssignmentId assignmentId, ExternalUserId userId, CancellationToken ct = default) =>
        db.Attempts.CountAsync(x => x.AssignmentId == assignmentId && x.UserId == userId, ct);
}

public sealed class TestAttemptRepository(AppDbContext db) : ITestAttemptRepository
{
    public async Task AddAsync(TestAttempt attempt, CancellationToken ct = default) => await db.Attempts.AddAsync(attempt, ct);
}
