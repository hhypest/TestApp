using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace TestApp.Infrastructure.Persistence;

public sealed class OperationalRetentionOptions
{
    public bool Enabled { get; set; } = true;
    public int AuditRetentionDays { get; set; } = 90;
    public int IdempotencyRetentionDays { get; set; } = 14;
    public int ProcessedOutboxRetentionDays { get; set; } = 14;
    public int BatchSize { get; set; } = 500;
    public int MaxBatchesPerRun { get; set; } = 20;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromHours(6);

    internal void Validate()
    {
        if (AuditRetentionDays < 1) throw new InvalidOperationException("OperationalRetention AuditRetentionDays must be greater than zero.");
        if (IdempotencyRetentionDays < 1) throw new InvalidOperationException("OperationalRetention IdempotencyRetentionDays must be greater than zero.");
        if (ProcessedOutboxRetentionDays < 1) throw new InvalidOperationException("OperationalRetention ProcessedOutboxRetentionDays must be greater than zero.");
        if (BatchSize is < 1 or > 5000) throw new InvalidOperationException("OperationalRetention BatchSize must be between 1 and 5000.");
        if (MaxBatchesPerRun is < 1 or > 1000) throw new InvalidOperationException("OperationalRetention MaxBatchesPerRun must be between 1 and 1000.");
        if (PollInterval < TimeSpan.FromMinutes(1)) throw new InvalidOperationException("OperationalRetention PollInterval must be at least one minute.");
    }
}

public sealed record OperationalRetentionResult(
    bool LockAcquired,
    int AuditDeleted,
    int IdempotencyDeleted,
    int ProcessedOutboxDeleted)
{
    public int TotalDeleted => AuditDeleted + IdempotencyDeleted + ProcessedOutboxDeleted;
}

public sealed class OperationalRetentionCleaner(
    AppDbContext db,
    TimeProvider time,
    IOptions<OperationalRetentionOptions> options,
    ILogger<OperationalRetentionCleaner> logger)
{
    private const string LockName = "testapp:retention";
    private readonly OperationalRetentionOptions _options = options.Value;

    public async Task<OperationalRetentionResult> RunOnceAsync(CancellationToken ct = default)
    {
        _options.Validate();
        if (!_options.Enabled)
            return new OperationalRetentionResult(false, 0, 0, 0);

        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Database connection string is not configured.");
        await using var lockConnection = new MySqlConnection(connectionString);
        await lockConnection.OpenAsync(ct);

        if (!await TryAcquireLockAsync(lockConnection, ct))
            return new OperationalRetentionResult(false, 0, 0, 0);

        try
        {
            var now = time.GetUtcNow();
            var auditDeleted = await DeleteAuditAsync(now.AddDays(-_options.AuditRetentionDays), ct);
            var idempotencyDeleted = await DeleteIdempotencyAsync(now.AddDays(-_options.IdempotencyRetentionDays), ct);
            var outboxDeleted = await DeleteProcessedOutboxAsync(now.AddDays(-_options.ProcessedOutboxRetentionDays), ct);

            var result = new OperationalRetentionResult(true, auditDeleted, idempotencyDeleted, outboxDeleted);
            if (result.TotalDeleted > 0)
            {
                logger.LogInformation(
                    "Operational retention deleted {TotalDeleted} records (audit={AuditDeleted}, idempotency={IdempotencyDeleted}, processedOutbox={ProcessedOutboxDeleted})",
                    result.TotalDeleted,
                    result.AuditDeleted,
                    result.IdempotencyDeleted,
                    result.ProcessedOutboxDeleted);
            }

            return result;
        }
        finally
        {
            await ReleaseLockAsync(lockConnection);
        }
    }

    private Task<int> DeleteAuditAsync(DateTimeOffset cutoff, CancellationToken ct) =>
        DeleteInBatchesAsync(
            () => db.Set<AuditEntry>()
                .AsNoTracking()
                .Where(x => x.OccurredAt < cutoff)
                .OrderBy(x => x.OccurredAt)
                .Select(x => x.Id)
                .Take(_options.BatchSize)
                .ToArrayAsync(ct),
            ids => db.Set<AuditEntry>().Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(ct),
            ct);

    private Task<int> DeleteIdempotencyAsync(DateTimeOffset cutoff, CancellationToken ct) =>
        DeleteInBatchesAsync(
            () => db.Set<IdempotencyRecord>()
                .AsNoTracking()
                .Where(x => x.CreatedAt < cutoff)
                .OrderBy(x => x.CreatedAt)
                .Select(x => x.Id)
                .Take(_options.BatchSize)
                .ToArrayAsync(ct),
            ids => db.Set<IdempotencyRecord>().Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(ct),
            ct);

    private Task<int> DeleteProcessedOutboxAsync(DateTimeOffset cutoff, CancellationToken ct) =>
        DeleteInBatchesAsync(
            () => db.OutboxMessages
                .AsNoTracking()
                .Where(x => x.ProcessedAt != null && x.ProcessedAt < cutoff && x.DeadLetteredAt == null)
                .OrderBy(x => x.ProcessedAt)
                .Select(x => x.Id)
                .Take(_options.BatchSize)
                .ToArrayAsync(ct),
            ids => db.OutboxMessages.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(ct),
            ct);

    private async Task<int> DeleteInBatchesAsync(
        Func<Task<Guid[]>> selectIds,
        Func<Guid[], Task<int>> deleteIds,
        CancellationToken ct)
    {
        var deleted = 0;
        for (var batch = 0; batch < _options.MaxBatchesPerRun; batch++)
        {
            ct.ThrowIfCancellationRequested();
            var ids = await selectIds();
            if (ids.Length == 0)
                break;

            deleted += await deleteIds(ids);
            if (ids.Length < _options.BatchSize)
                break;
        }

        return deleted;
    }

    private static async Task<bool> TryAcquireLockAsync(MySqlConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT GET_LOCK(@name, 0);";
        command.Parameters.AddWithValue("@name", LockName);
        var value = await command.ExecuteScalarAsync(ct);
        return value is not null && value is not DBNull && Convert.ToInt32(value) == 1;
    }

    private static async Task ReleaseLockAsync(MySqlConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open)
            return;

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RELEASE_LOCK(@name);";
        command.Parameters.AddWithValue("@name", LockName);
        await command.ExecuteScalarAsync();
    }
}

public sealed class OperationalRetentionWorker(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IOptions<OperationalRetentionOptions> options,
    ILogger<OperationalRetentionWorker> logger) : BackgroundService
{
    private readonly OperationalRetentionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _options.Validate();
        if (!_options.Enabled)
            return;

        await RunCycleAsync(stoppingToken);

        using var timer = new PeriodicTimer(_options.PollInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunCycleAsync(stoppingToken);
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var cleaner = scope.ServiceProvider.GetRequiredService<OperationalRetentionCleaner>();
            await cleaner.RunOnceAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Operational retention cycle failed; records were left for a future retry.");
        }
    }
}
