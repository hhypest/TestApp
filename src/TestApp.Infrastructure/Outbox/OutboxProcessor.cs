using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;
using TestApp.Infrastructure.Persistence;

namespace TestApp.Infrastructure.Outbox;

public interface IOutboxPublisher
{
    Task Publish(Guid eventId, string eventType, string payload, CancellationToken ct);
}

public sealed class OutboxDeliveryOptions
{
    public int BatchSize { get; set; } = 100;
    public int MaxAttempts { get; set; } = 10;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(15);
    public int AdvisoryLockTimeoutSeconds { get; set; } = 5;

    internal void Validate()
    {
        if (BatchSize is < 1 or > 1000) throw new InvalidOperationException("Outbox BatchSize must be between 1 and 1000.");
        if (MaxAttempts < 1) throw new InvalidOperationException("Outbox MaxAttempts must be greater than zero.");
        if (PollInterval <= TimeSpan.Zero) throw new InvalidOperationException("Outbox PollInterval must be positive.");
        if (BaseRetryDelay <= TimeSpan.Zero) throw new InvalidOperationException("Outbox BaseRetryDelay must be positive.");
        if (MaxRetryDelay < BaseRetryDelay) throw new InvalidOperationException("Outbox MaxRetryDelay cannot be smaller than BaseRetryDelay.");
        if (AdvisoryLockTimeoutSeconds < 0) throw new InvalidOperationException("Outbox AdvisoryLockTimeoutSeconds cannot be negative.");
    }
}

public sealed class OutboxProcessor(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IOptions<OutboxDeliveryOptions> options,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private readonly OutboxDeliveryOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _options.Validate();
        await ProcessBatchAsync(stoppingToken);

        using var timer = new PeriodicTimer(_options.PollInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ProcessBatchAsync(stoppingToken);
    }

    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        Guid[] ids;

        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            ids = await db.OutboxMessages
                .AsNoTracking()
                .Where(x => x.ProcessedAt == null && x.DeadLetteredAt == null && (x.NextAttemptAt == null || x.NextAttemptAt <= now))
                .OrderBy(x => x.OccurredAt)
                .Select(x => x.Id)
                .Take(_options.BatchSize)
                .ToArrayAsync(ct);
        }

        var processed = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            if (await TryProcessMessageAsync(id, ct))
                processed++;
        }

        return processed;
    }

    private async Task<bool> TryProcessMessageAsync(Guid id, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Database connection string is not configured.");

        await using var lockConnection = new MySqlConnection(connectionString);
        await lockConnection.OpenAsync(ct);
        var lockName = $"testapp:outbox:{id:N}";

        if (!await TryAcquireLockAsync(lockConnection, lockName, ct))
            return false;

        try
        {
            var message = await db.OutboxMessages.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (message is null || !message.IsDueAt(time.GetUtcNow()))
                return false;

            try
            {
                await publisher.Publish(message.Id, message.Type, message.Payload, ct);
                message.MarkProcessed(time.GetUtcNow());
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var failedAt = time.GetUtcNow();
                var nextAttemptNumber = message.AttemptCount + 1;
                var delay = CalculateRetryDelay(nextAttemptNumber);
                message.MarkFailed(failedAt, ex.Message, failedAt.Add(delay), _options.MaxAttempts);
                await db.SaveChangesAsync(ct);

                if (message.DeadLetteredAt is not null)
                    logger.LogError(ex, "Outbox message {OutboxMessageId} moved to dead letter after {AttemptCount} failed attempts", message.Id, message.AttemptCount);
                else
                    logger.LogWarning(ex, "Failed to publish outbox message {OutboxMessageId}; attempt {AttemptCount}, next retry at {NextAttemptAt}", message.Id, message.AttemptCount, message.NextAttemptAt);

                return false;
            }
        }
        finally
        {
            await ReleaseLockAsync(lockConnection, lockName);
        }
    }

    private TimeSpan CalculateRetryDelay(int attemptNumber)
    {
        var exponent = Math.Min(Math.Max(0, attemptNumber - 1), 20);
        var multiplier = Math.Pow(2, exponent);
        var milliseconds = Math.Min(_options.BaseRetryDelay.TotalMilliseconds * multiplier, _options.MaxRetryDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private async Task<bool> TryAcquireLockAsync(MySqlConnection connection, string lockName, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT GET_LOCK(@name, @timeoutSeconds);";
        command.Parameters.AddWithValue("@name", lockName);
        command.Parameters.AddWithValue("@timeoutSeconds", _options.AdvisoryLockTimeoutSeconds);
        var result = await command.ExecuteScalarAsync(ct);
        return result is not null && result is not DBNull && Convert.ToInt32(result) == 1;
    }

    private static async Task ReleaseLockAsync(MySqlConnection connection, string lockName)
    {
        if (connection.State != System.Data.ConnectionState.Open)
            return;

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RELEASE_LOCK(@name);";
        command.Parameters.AddWithValue("@name", lockName);
        await command.ExecuteScalarAsync();
    }
}
