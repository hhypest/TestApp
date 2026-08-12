using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TestApp.Application.Abstractions;
using TestApp.Application.Attempts;
using TestApp.Application.Common;

namespace TestApp.Infrastructure.Attempts;

public sealed class AttemptExpirationOptions
{
    public int BatchSize { get; set; } = 100;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (BatchSize is < 1 or > 1000)
            throw new InvalidOperationException("Attempt expiration BatchSize must be between 1 and 1000.");
        if (PollInterval <= TimeSpan.Zero)
            throw new InvalidOperationException("Attempt expiration PollInterval must be positive.");
    }
}

public sealed class OverdueAttemptProcessor(
    IServiceScopeFactory scopes,
    IClock clock,
    IOptions<AttemptExpirationOptions> options,
    ILogger<OverdueAttemptProcessor> logger) : BackgroundService
{
    private readonly AttemptExpirationOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _options.Validate();
        await ProcessBatchAsync(stoppingToken);

        using var timer = new PeriodicTimer(_options.PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ProcessBatchAsync(stoppingToken);
    }

    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        IReadOnlyCollection<TestApp.Domain.Attempts.TestAttemptId> ids;

        await using (var scope = scopes.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ITestAttemptRepository>();
            ids = await repository.GetExpiredInProgressIdsAsync(clock.UtcNow, _options.BatchSize, ct);
        }

        var expiredCount = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();

            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ExpireAttemptCommandHandler>();

            try
            {
                var result = await handler.Handle(new ExpireAttemptCommand(id), ct);
                var expired = result.Match(
                    _ => true,
                    error =>
                    {
                        logger.LogWarning(
                            "Could not expire overdue attempt {AttemptId}: {ErrorCode} {ErrorMessage}",
                            id.Value,
                            error.Code,
                            error.Message);
                        return false;
                    });

                if (expired)
                    expiredCount++;
            }
            catch (ConcurrencyConflictException)
            {
                logger.LogDebug(
                    "Attempt {AttemptId} changed concurrently while automatic expiration was running; current state will be used on the next scan",
                    id.Value);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to expire overdue attempt {AttemptId}", id.Value);
            }
        }

        if (expiredCount > 0)
            logger.LogInformation("Automatically expired {ExpiredAttemptCount} overdue attempts", expiredCount);

        return expiredCount;
    }
}
