using Microsoft.EntityFrameworkCore;
using TestApp.Application.Common;
using TestApp.Domain.Assignments;
using TestApp.Domain.Attempts;
using TestApp.Domain.Identity;
using TestApp.Domain.Revisions;
using TestApp.Domain.Tests;
using TestApp.Infrastructure.Persistence;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class PersistenceBehaviorTests
{
    [Fact]
    public async Task Migrations_apply_to_empty_database()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var db = database.CreateContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var pending = await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task Test_owner_round_trips_through_PostgreSQL()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        var owner = ExternalUserId.FromSubject("author-owner");
        var test = Test.Create("Owned test", owner);

        await using (var write = database.CreateContext())
        {
            await write.Database.MigrateAsync(ct);
            write.Tests.Add(test);
            await write.SaveChangesAsync(ct);
        }

        await using var read = database.CreateContext();
        var persisted = await read.Tests.AsNoTracking().SingleAsync(x => x.Id == test.Id, ct);
        Assert.Equal(owner, persisted.OwnerId);
    }

    [Fact]
    public async Task Assignment_can_be_persisted_on_PostgreSQL()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var db = database.CreateContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var now = DateTimeOffset.Parse("2026-08-13T16:00:00+02:00");
        var assignment = TestAssignment.Create(
            TestAssignmentId.New(),
            PublishedTestRevisionId.New(),
            new AssignmentTarget.User(ExternalUserId.FromSubject("student-1")),
            ExternalUserId.FromSubject("admin-1"),
            now,
            now.AddMinutes(-1),
            now.AddHours(1),
            1);

        db.Assignments.Add(assignment);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var persisted = await db.Assignments.AsNoTracking().SingleAsync(x => x.Id == assignment.Id, TestContext.Current.CancellationToken);
        Assert.Equal(AssignmentTargetType.User, persisted.TargetType);
        Assert.Equal("student-1", persisted.TargetId);
        Assert.Equal(TimeSpan.Zero, persisted.AssignedAt.Offset);
        Assert.Equal(TimeSpan.Zero, persisted.AvailableFrom.Offset);
        Assert.Equal(TimeSpan.Zero, persisted.AvailableUntil!.Value.Offset);
        Assert.Equal(now.ToUniversalTime(), persisted.AssignedAt);
    }

    [Fact]
    public async Task Concurrent_aggregate_update_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.Tests.Add(Test.Create("Original", ExternalUserId.FromSubject("author-1")));
            await setup.SaveChangesAsync(ct);
        }

        TestId id;
        await using (var lookup = database.CreateContext())
            id = await lookup.Tests.Select(x => x.Id).SingleAsync(ct);

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstCopy = await first.Tests.SingleAsync(x => x.Id == id, ct);
        var secondCopy = await second.Tests.SingleAsync(x => x.Id == id, ct);

        firstCopy.Rename("First");
        secondCopy.Rename("Second");
        await first.SaveChangesAsync(ct);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => second.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Same_start_request_returns_same_attempt_id()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var db = database.CreateContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var repository = new TestAttemptRepository(db);
        var assignmentId = TestAssignmentId.New();
        var revisionId = PublishedTestRevisionId.New();
        var userId = ExternalUserId.FromSubject("user-1");
        var requestId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-08-12T10:00:00Z");

        var first = TestAttempt.Start(TestAttemptId.New(), assignmentId, revisionId, userId, requestId, now, null, []);
        var second = TestAttempt.Start(TestAttemptId.New(), assignmentId, revisionId, userId, requestId, now, null, []);

        var firstId = await repository.TryAddWithinLimitAsync(first, 2, TestContext.Current.CancellationToken);
        var secondId = await repository.TryAddWithinLimitAsync(second, 2, TestContext.Current.CancellationToken);
        var replayedId = await repository.FindIdByStartRequestAsync(assignmentId, userId, requestId, TestContext.Current.CancellationToken);
        var foreignId = await repository.FindIdByStartRequestAsync(
            assignmentId,
            ExternalUserId.FromSubject("user-2"),
            requestId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(firstId);
        Assert.Equal(firstId, secondId);
        Assert.Equal(firstId, replayedId);
        Assert.Null(foreignId);
        Assert.Equal(1, await db.Attempts.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Concurrent_starts_do_not_exceed_attempt_limit_on_PostgreSQL()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using (var migration = database.CreateContext())
            await migration.Database.MigrateAsync(ct);

        var assignmentId = TestAssignmentId.New();
        var revisionId = PublishedTestRevisionId.New();
        var userId = ExternalUserId.FromSubject("concurrent-user");
        var now = DateTimeOffset.UtcNow;

        await using var firstDb = database.CreateContext();
        await using var secondDb = database.CreateContext();
        var first = TestAttempt.Start(
            TestAttemptId.New(), assignmentId, revisionId, userId, Guid.NewGuid(), now, null, []);
        var second = TestAttempt.Start(
            TestAttemptId.New(), assignmentId, revisionId, userId, Guid.NewGuid(), now, null, []);

        var results = await Task.WhenAll(
            new TestAttemptRepository(firstDb).TryAddWithinLimitAsync(first, 1, ct),
            new TestAttemptRepository(secondDb).TryAddWithinLimitAsync(second, 1, ct));

        Assert.Single(results, x => x is not null);
        await using var verify = database.CreateContext();
        Assert.Equal(1, await verify.Attempts.CountAsync(ct));
    }

    [Fact]
    public async Task Idempotency_lease_serializes_competing_PostgreSQL_contexts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using (var migration = database.CreateContext())
            await migration.Database.MigrateAsync(ct);

        await using var firstDb = database.CreateContext();
        await using var secondDb = database.CreateContext();
        var firstStore = new IdempotencyStore(firstDb);
        var secondStore = new IdempotencyStore(secondDb);
        var actor = ExternalUserId.FromSubject("admin-1");
        var requestId = Guid.NewGuid();
        const string operation = "tests.publish";
        var fingerprint = IdempotencyFingerprint.Create("same-request");
        var expected = PublishedTestRevisionId.New();
        Task<IAsyncDisposable> competingAcquire;

        await using (var firstLease = await firstStore.AcquireAsync(operation, actor, requestId, ct))
        {
            competingAcquire = secondStore.AcquireAsync(operation, actor, requestId, ct);
            await Task.Delay(150, ct);
            Assert.False(competingAcquire.IsCompleted);

            await firstStore.AddResultAsync(operation, actor, requestId, fingerprint, expected, DateTimeOffset.UtcNow, ct);
            await firstDb.SaveChangesAsync(ct);
        }

        await using var secondLease = await competingAcquire;
        var observed = await secondStore.GetResultAsync<PublishedTestRevisionId>(operation, actor, requestId, fingerprint, ct);
        Assert.Equal(expected, observed);
    }

    [Fact]
    public async Task Reusing_idempotency_key_with_different_fingerprint_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(ct);
        await using var db = database.CreateContext();
        await db.Database.MigrateAsync(ct);

        var store = new IdempotencyStore(db);
        var actor = ExternalUserId.FromSubject("admin-1");
        var requestId = Guid.NewGuid();
        const string operation = "assignments.create";
        var originalFingerprint = IdempotencyFingerprint.Create("request-a");
        var differentFingerprint = IdempotencyFingerprint.Create("request-b");
        var result = TestAssignmentId.New();

        await store.AddResultAsync(operation, actor, requestId, originalFingerprint, result, DateTimeOffset.UtcNow, ct);
        await db.SaveChangesAsync(ct);

        await Assert.ThrowsAsync<IdempotencyKeyReuseException>(() =>
            store.GetResultAsync<TestAssignmentId>(operation, actor, requestId, differentFingerprint, ct));
    }
}
