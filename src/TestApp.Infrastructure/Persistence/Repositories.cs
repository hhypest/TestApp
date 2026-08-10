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
    public Task<Test?> Get(TestId id, CancellationToken ct) => db.Tests.SingleOrDefaultAsync(x => x.Id == id, ct);
    public void Add(Test test) => db.Tests.Add(test);
}

public sealed class RevisionRepository(AppDbContext db) : IRevisionRepository
{
    public Task<PublishedTestRevision?> Get(PublishedTestRevisionId id, CancellationToken ct) => db.Revisions.SingleOrDefaultAsync(x => x.Id == id, ct);
    public void Add(PublishedTestRevision revision) => db.Revisions.Add(revision);
}

public sealed class AssignmentRepository(AppDbContext db) : IAssignmentRepository
{
    public Task<TestAssignment?> Get(TestAssignmentId id, CancellationToken ct) => db.Assignments.SingleOrDefaultAsync(x => x.Id == id, ct);
    public void Add(TestAssignment assignment) => db.Assignments.Add(assignment);
}

public sealed class AttemptRepository(AppDbContext db) : IAttemptRepository
{
    public Task<TestAttempt?> Get(TestAttemptId id, CancellationToken ct) => db.Attempts.SingleOrDefaultAsync(x => x.Id == id, ct);
    public void Add(TestAttempt attempt) => db.Attempts.Add(attempt);

    public Task<int> CountForAssignment(TestAssignmentId assignmentId, ExternalUserId userId, CancellationToken ct) =>
        db.Attempts.CountAsync(x => x.AssignmentId == assignmentId && x.UserId == userId, ct);
}
