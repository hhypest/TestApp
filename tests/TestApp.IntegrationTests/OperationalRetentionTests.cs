using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TestApp.Domain.Identity;
using TestApp.Infrastructure.Persistence;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class OperationalRetentionTests
{
    [Fact]
    public async Task Cleaner_removes_only_expired_safe_operational_records()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await MariaDbTestDatabase.CreateAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var oldAuditId = Guid.CreateVersion7();
        var recentAuditId = Guid.CreateVersion7();
        var oldIdempotencyRequest = Guid.NewGuid();
        var recentIdempotencyRequest = Guid.NewGuid();
        var oldProcessedOutbox = Guid.NewGuid();
        var recentProcessedOutbox = Guid.NewGuid();
        var oldPendingOutbox = Guid.NewGuid();
        var oldDeadLetterOutbox = Guid.NewGuid();

        await using (var seed = database.CreateContext())
        {
            await seed.Database.MigrateAsync(ct);

            seed.Set<AuditEntry>().AddRange(
                AuditEntry.Create(now.AddDays(-120), "old-user", "POST", "/old", 200, oldAuditId.ToString("N"), oldAuditId.ToString("N"), 1m),
                AuditEntry.Create(now.AddDays(-1), "recent-user", "POST", "/recent", 200, recentAuditId.ToString("N"), recentAuditId.ToString("N"), 1m));

            seed.Set<IdempotencyRecord>().AddRange(
                IdempotencyRecord.Create("retention.old", ExternalUserId.FromSubject("user"), oldIdempotencyRequest, new string('a', 64), 1, now.AddDays(-30)),
                IdempotencyRecord.Create("retention.recent", ExternalUserId.FromSubject("user"), recentIdempotencyRequest, new string('b', 64), 2, now.AddDays(-1)));

            await seed.SaveChangesAsync(ct);

            await InsertOutboxAsync(seed, oldProcessedOutbox, now.AddDays(-30), now.AddDays(-30), null, ct);
            await InsertOutboxAsync(seed, recentProcessedOutbox, now.AddDays(-1), now.AddDays(-1), null, ct);
            await InsertOutboxAsync(seed, oldPendingOutbox, now.AddDays(-30), null, null, ct);
            await InsertOutboxAsync(seed, oldDeadLetterOutbox, now.AddDays(-30), null, now.AddDays(-30), ct);
        }

        var options = Options.Create(new OperationalRetentionOptions
        {
            Enabled = true,
            AuditRetentionDays = 90,
            IdempotencyRetentionDays = 14,
            ProcessedOutboxRetentionDays = 14,
            BatchSize = 1,
            MaxBatchesPerRun = 10,
            PollInterval = TimeSpan.FromHours(6)
        });

        OperationalRetentionResult result;
        await using (var cleanupDb = database.CreateContext())
        {
            var cleaner = new OperationalRetentionCleaner(
                cleanupDb,
                TimeProvider.System,
                options,
                NullLogger<OperationalRetentionCleaner>.Instance);
            result = await cleaner.RunOnceAsync(ct);
        }

        Assert.True(result.LockAcquired);
        Assert.Equal(1, result.AuditDeleted);
        Assert.Equal(1, result.IdempotencyDeleted);
        Assert.Equal(1, result.ProcessedOutboxDeleted);

        await using var verify = database.CreateContext();
        Assert.DoesNotContain(await verify.Set<AuditEntry>().AsNoTracking().ToArrayAsync(ct), x => x.ActorId == "old-user");
        Assert.Contains(await verify.Set<AuditEntry>().AsNoTracking().ToArrayAsync(ct), x => x.ActorId == "recent-user");

        Assert.DoesNotContain(await verify.Set<IdempotencyRecord>().AsNoTracking().ToArrayAsync(ct), x => x.RequestId == oldIdempotencyRequest);
        Assert.Contains(await verify.Set<IdempotencyRecord>().AsNoTracking().ToArrayAsync(ct), x => x.RequestId == recentIdempotencyRequest);

        var outboxIds = await verify.OutboxMessages.AsNoTracking().Select(x => x.Id).ToArrayAsync(ct);
        Assert.DoesNotContain(oldProcessedOutbox, outboxIds);
        Assert.Contains(recentProcessedOutbox, outboxIds);
        Assert.Contains(oldPendingOutbox, outboxIds);
        Assert.Contains(oldDeadLetterOutbox, outboxIds);
    }

    private static Task InsertOutboxAsync(
        AppDbContext db,
        Guid id,
        DateTimeOffset occurredAt,
        DateTimeOffset? processedAt,
        DateTimeOffset? deadLetteredAt,
        CancellationToken ct)
    {
        var attemptCount = deadLetteredAt is null ? 0 : 10;
        var error = deadLetteredAt is null ? null : "dead-letter-probe";
        var lastAttemptAt = processedAt ?? deadLetteredAt;
        DateTimeOffset? nextAttemptAt = null;
        const string type = "Retention.Probe";
        const string payload = "{}";

        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO outbox_messages
                (Id, OccurredAt, Type, Payload, ProcessedAt, Error, AttemptCount, LastAttemptAt, NextAttemptAt, DeadLetteredAt)
            VALUES
                ({id}, {occurredAt}, {type}, {payload}, {processedAt}, {error}, {attemptCount}, {lastAttemptAt}, {nextAttemptAt}, {deadLetteredAt});
            """, ct);
    }
}
