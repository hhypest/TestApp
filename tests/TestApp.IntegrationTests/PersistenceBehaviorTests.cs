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
        await using var database = await MariaDbTestDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using var db = database.CreateContext();

        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);

        var pending = await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task Concurrent_aggregate_update_is_rejected()
    {
        await using var database = await MariaDbTestDatabase.CreateAsync(TestContext.Current.CancellationToken);
        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(TestContext.Current.CancellationToken);
            setup.Tests.Add(Test.Create("Original"));
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        TestId id;
        await using (var lookup = database.CreateContext())
            id = await lookup.Tests.Select(x => x.Id).SingleAsync(TestContext.Current.CancellationToken);

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstCopy = await first.Tests.SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken);
        var secondCopy = await second.Tests.SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken);

        firstCopy.Rename("First");
        secondCopy.Rename("Second");
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => second.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Same_start_request_returns_same_attempt_id()
    {
        await using var database = await MariaDbTestDatabase.CreateAsync(TestContext.Current.CancellationToken);
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

        Assert.NotNull(firstId);
        Assert.Equal(firstId, secondId);
        Assert.Equal(1, await db.Attempts.CountAsync(TestContext.Current.CancellationToken));
    }
}
