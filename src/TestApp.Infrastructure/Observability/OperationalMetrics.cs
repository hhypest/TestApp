using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TestApp.Domain.Attempts;
using TestApp.Infrastructure.Persistence;

namespace TestApp.Infrastructure.Observability;

public sealed class OperationalMetrics : IDisposable
{
    public const string MeterName = "TestApp.Operations";

    private readonly Meter _meter = new(MeterName, "1.0.0");
    private readonly Counter<long> _outboxPublish;
    private readonly Histogram<double> _outboxDeliveryLag;
    private readonly Counter<long> _deadLetterActions;
    private readonly Counter<long> _attemptExpiration;
    private readonly Counter<long> _retentionDeleted;

    private long _outboxPending;
    private long _outboxDeadLetters;
    private long _outboxOldestPendingAgeBits;
    private long _overdueAttempts;
    private long _oldestOverdueLagBits;

    public OperationalMetrics()
    {
        _outboxPublish = _meter.CreateCounter<long>(
            "testapp.outbox.publish",
            unit: "{message}",
            description: "Outbox publish outcomes.");
        _outboxDeliveryLag = _meter.CreateHistogram<double>(
            "testapp.outbox.delivery_lag",
            unit: "s",
            description: "Seconds between integration event occurrence and successful Outbox delivery.");
        _deadLetterActions = _meter.CreateCounter<long>(
            "testapp.outbox.dead_letter_action",
            unit: "{action}",
            description: "Explicit administrator dead-letter actions.");
        _attemptExpiration = _meter.CreateCounter<long>(
            "testapp.attempt.expiration",
            unit: "{attempt}",
            description: "Background attempt expiration outcomes.");
        _retentionDeleted = _meter.CreateCounter<long>(
            "testapp.retention.deleted",
            unit: "{record}",
            description: "Operational records removed by retention policy.");

        _meter.CreateObservableGauge("testapp.outbox.pending", () => Interlocked.Read(ref _outboxPending), "{message}");
        _meter.CreateObservableGauge("testapp.outbox.dead_letters", () => Interlocked.Read(ref _outboxDeadLetters), "{message}");
        _meter.CreateObservableGauge("testapp.outbox.oldest_pending_age", () => ReadDouble(ref _outboxOldestPendingAgeBits), "s");
        _meter.CreateObservableGauge("testapp.attempt.overdue", () => Interlocked.Read(ref _overdueAttempts), "{attempt}");
        _meter.CreateObservableGauge("testapp.attempt.oldest_overdue_lag", () => ReadDouble(ref _oldestOverdueLagBits), "s");
    }

    public void RecordOutboxPublish(bool success, bool deadLettered, TimeSpan? deliveryLag = null)
    {
        var outcome = success ? "success" : deadLettered ? "dead_letter" : "failure";
        _outboxPublish.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        if (success && deliveryLag is { } lag)
            _outboxDeliveryLag.Record(Math.Max(0d, lag.TotalSeconds));
    }

    public void RecordDeadLetterAction(string action) =>
        _deadLetterActions.Add(1, new KeyValuePair<string, object?>("action", action));

    public void RecordAttemptExpiration(bool success) =>
        _attemptExpiration.Add(1, new KeyValuePair<string, object?>("outcome", success ? "success" : "failure"));

    public void RecordRetentionDeleted(string kind, int count)
    {
        if (count > 0)
            _retentionDeleted.Add(count, new KeyValuePair<string, object?>("kind", kind));
    }

    internal void UpdateSnapshot(
        long outboxPending,
        long outboxDeadLetters,
        double oldestPendingAgeSeconds,
        long overdueAttempts,
        double oldestOverdueLagSeconds)
    {
        Interlocked.Exchange(ref _outboxPending, outboxPending);
        Interlocked.Exchange(ref _outboxDeadLetters, outboxDeadLetters);
        WriteDouble(ref _outboxOldestPendingAgeBits, oldestPendingAgeSeconds);
        Interlocked.Exchange(ref _overdueAttempts, overdueAttempts);
        WriteDouble(ref _oldestOverdueLagBits, oldestOverdueLagSeconds);
    }

    public void Dispose() => _meter.Dispose();

    private static double ReadDouble(ref long storage) =>
        BitConverter.Int64BitsToDouble(Interlocked.Read(ref storage));

    private static void WriteDouble(ref long storage, double value) =>
        Interlocked.Exchange(ref storage, BitConverter.DoubleToInt64Bits(value));
}

public sealed class OperationalMetricsSampler(
    IServiceScopeFactory scopes,
    TimeProvider time,
    OperationalMetrics metrics,
    ILogger<OperationalMetricsSampler> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SampleAsync(stoppingToken);
        using var timer = new PeriodicTimer(Interval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await SampleAsync(stoppingToken);
    }

    private async Task SampleAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = time.GetUtcNow();

            var pendingQuery = db.OutboxMessages.AsNoTracking().Where(x =>
                x.ProcessedAt == null &&
                x.DeadLetteredAt == null &&
                x.DiscardedAt == null);
            var pendingCount = await pendingQuery.LongCountAsync(ct);
            var oldestPending = await pendingQuery
                .OrderBy(x => x.OccurredAt)
                .Select(x => (DateTimeOffset?)x.OccurredAt)
                .FirstOrDefaultAsync(ct);
            var deadLetters = await db.OutboxMessages.AsNoTracking()
                .LongCountAsync(x => x.DeadLetteredAt != null && x.DiscardedAt == null, ct);

            var overdueQuery = db.Attempts.AsNoTracking().Where(x =>
                x.Status == AttemptStatus.InProgress &&
                x.DeadlineAt != null &&
                x.DeadlineAt < now);
            var overdueCount = await overdueQuery.LongCountAsync(ct);
            var oldestDeadline = await overdueQuery
                .OrderBy(x => x.DeadlineAt)
                .Select(x => x.DeadlineAt)
                .FirstOrDefaultAsync(ct);

            metrics.UpdateSnapshot(
                pendingCount,
                deadLetters,
                oldestPending is { } pendingAt ? Math.Max(0d, (now - pendingAt).TotalSeconds) : 0d,
                overdueCount,
                oldestDeadline is { } deadline ? Math.Max(0d, (now - deadline).TotalSeconds) : 0d);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to sample TestApp operational metrics.");
        }
    }
}
