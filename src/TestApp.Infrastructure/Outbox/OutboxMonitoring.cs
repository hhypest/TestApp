using Microsoft.EntityFrameworkCore;
using TestApp.Infrastructure.Persistence;

namespace TestApp.Infrastructure.Outbox;

public sealed record OutboxDeadLetterItem(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    int AttemptCount,
    DateTimeOffset DeadLetteredAt,
    string? Error);

public sealed record OutboxOperationalStatus(
    int PendingCount,
    int RetryScheduledCount,
    int DeadLetterCount,
    int DiscardedCount,
    DateTimeOffset? OldestPendingOccurredAt,
    IReadOnlyList<OutboxDeadLetterItem> RecentDeadLetters);

public sealed class OutboxMonitor(AppDbContext db)
{
    public async Task<OutboxOperationalStatus> GetStatusAsync(int deadLetterLimit = 20, CancellationToken ct = default)
    {
        deadLetterLimit = Math.Clamp(deadLetterLimit, 1, 100);

        var pending = db.OutboxMessages.AsNoTracking().Where(x =>
            x.ProcessedAt == null &&
            x.DeadLetteredAt == null &&
            x.DiscardedAt == null);
        var pendingCount = await pending.CountAsync(ct);
        var retryScheduledCount = await pending.CountAsync(x => x.NextAttemptAt != null, ct);
        var oldestPending = await pending.OrderBy(x => x.OccurredAt).Select(x => (DateTimeOffset?)x.OccurredAt).FirstOrDefaultAsync(ct);

        var deadLetterCount = await db.OutboxMessages.AsNoTracking()
            .CountAsync(x => x.DeadLetteredAt != null && x.DiscardedAt == null, ct);
        var discardedCount = await db.OutboxMessages.AsNoTracking()
            .CountAsync(x => x.DiscardedAt != null, ct);
        var recentDeadLetters = await db.OutboxMessages.AsNoTracking()
            .Where(x => x.DeadLetteredAt != null && x.DiscardedAt == null)
            .OrderByDescending(x => x.DeadLetteredAt)
            .Take(deadLetterLimit)
            .Select(x => new OutboxDeadLetterItem(
                x.Id,
                x.Type,
                x.OccurredAt,
                x.AttemptCount,
                x.DeadLetteredAt!.Value,
                x.Error))
            .ToArrayAsync(ct);

        return new OutboxOperationalStatus(
            pendingCount,
            retryScheduledCount,
            deadLetterCount,
            discardedCount,
            oldestPending,
            recentDeadLetters);
    }
}
