using Microsoft.EntityFrameworkCore;
using TestApp.Domain.Identity;
using TestApp.Infrastructure.Outbox;
using TestApp.Infrastructure.Persistence;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class OutboxDeadLetterManagementTests
{
    [Fact]
    public async Task Manager_requeues_and_discards_dead_letters_with_atomic_audit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await MariaDbTestDatabase.CreateAsync(ct);
        var requeueId = Guid.NewGuid();
        var discardId = Guid.NewGuid();
        var actor = ExternalUserId.FromSubject("admin-dead-letter");

        await using (var seed = database.CreateContext())
        {
            await seed.Database.MigrateAsync(ct);
            await InsertDeadLetterAsync(seed, requeueId, "secret-payload-requeue", ct);
            await InsertDeadLetterAsync(seed, discardId, "secret-payload-discard", ct);
        }

        await using (var db = database.CreateContext())
        {
            var manager = new OutboxDeadLetterManager(db, TimeProvider.System);

            var requeued = await manager.RequeueAsync(
                requeueId,
                actor,
                "Dependency fixed; retry delivery.",
                "corr-requeue",
                ct);
            Assert.Equal(OutboxDeadLetterCommandStatus.Success, requeued.Status);
            Assert.NotNull(requeued.Detail);
            Assert.Null(requeued.Detail.DeadLetteredAt);
            Assert.Null(requeued.Detail.DiscardedAt);
            Assert.Equal(0, requeued.Detail.AttemptCount);

            var discarded = await manager.DiscardAsync(
                discardId,
                actor,
                "Message is obsolete after incident remediation.",
                "corr-discard",
                ct);
            Assert.Equal(OutboxDeadLetterCommandStatus.Success, discarded.Status);
            Assert.NotNull(discarded.Detail);
            Assert.NotNull(discarded.Detail.DeadLetteredAt);
            Assert.NotNull(discarded.Detail.DiscardedAt);

            var repeated = await manager.RequeueAsync(
                discardId,
                actor,
                "Should be rejected.",
                "corr-repeat",
                ct);
            Assert.Equal(OutboxDeadLetterCommandStatus.Conflict, repeated.Status);
            Assert.Equal("outbox.dead_letter.state", repeated.Code);
        }

        await using var verify = database.CreateContext();
        var requeuedMessage = await verify.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == requeueId, ct);
        Assert.Null(requeuedMessage.DeadLetteredAt);
        Assert.Null(requeuedMessage.DiscardedAt);
        Assert.NotNull(requeuedMessage.NextAttemptAt);
        Assert.True(requeuedMessage.IsDueAt(DateTimeOffset.UtcNow.AddMinutes(1)));

        var discardedMessage = await verify.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == discardId, ct);
        Assert.NotNull(discardedMessage.DeadLetteredAt);
        Assert.NotNull(discardedMessage.DiscardedAt);
        Assert.False(discardedMessage.IsDueAt(DateTimeOffset.UtcNow.AddYears(1)));

        var actions = await verify.OutboxDeadLetterActions.AsNoTracking()
            .OrderBy(x => x.OccurredAt)
            .ToArrayAsync(ct);
        Assert.Equal(2, actions.Length);
        Assert.Contains(actions, x =>
            x.EventId == requeueId &&
            x.Action == OutboxDeadLetterActionType.Requeued &&
            x.ActorId == actor.Value &&
            x.Reason == "Dependency fixed; retry delivery." &&
            x.CorrelationId == "corr-requeue");
        Assert.Contains(actions, x =>
            x.EventId == discardId &&
            x.Action == OutboxDeadLetterActionType.Discarded &&
            x.ActorId == actor.Value &&
            x.Reason == "Message is obsolete after incident remediation." &&
            x.CorrelationId == "corr-discard");

        var monitor = new OutboxMonitor(verify);
        var status = await monitor.GetStatusAsync(cancellationToken: ct);
        Assert.Equal(1, status.PendingCount);
        Assert.Equal(1, status.DeadLetterCount);
        Assert.Equal(1, status.DiscardedCount);
        Assert.Contains(status.RecentDeadLetters, x => x.EventId == discardId && x.Error == "dead-letter-probe");
    }

    private static Task InsertDeadLetterAsync(
        AppDbContext db,
        Guid id,
        string payload,
        CancellationToken ct)
    {
        var occurredAt = DateTimeOffset.UtcNow.AddHours(-2);
        var failedAt = DateTimeOffset.UtcNow.AddHours(-1);
        const string type = "Retention.SecretIntegrationEvent";
        const string error = "dead-letter-probe";
        const int attempts = 10;
        DateTimeOffset? processedAt = null;
        DateTimeOffset? nextAttemptAt = null;
        DateTimeOffset? discardedAt = null;

        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO outbox_messages
                (Id, OccurredAt, Type, Payload, ProcessedAt, Error, AttemptCount, LastAttemptAt, NextAttemptAt, DeadLetteredAt, DiscardedAt)
            VALUES
                ({id}, {occurredAt}, {type}, {payload}, {processedAt}, {error}, {attempts}, {failedAt}, {nextAttemptAt}, {failedAt}, {discardedAt});
            """, ct);
    }
}
