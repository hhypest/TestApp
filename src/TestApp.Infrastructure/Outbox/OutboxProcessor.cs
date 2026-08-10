using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TestApp.Infrastructure.Persistence;

namespace TestApp.Infrastructure.Outbox;

public interface IOutboxPublisher
{
    Task Publish(string eventType, string payload, CancellationToken ct);
}

public sealed class NullOutboxPublisher : IOutboxPublisher
{
    public Task Publish(string eventType, string payload, CancellationToken ct) => Task.CompletedTask;
}

public sealed class OutboxProcessor(IServiceScopeFactory scopes, IOutboxPublisher publisher, TimeProvider time, ILogger<OutboxProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), time);
        while (await timer.WaitForNextTickAsync(stoppingToken)) await Process(stoppingToken);
    }

    private async Task Process(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var messages = await db.OutboxMessages.Where(x => x.ProcessedAt == null).OrderBy(x => x.OccurredAt).Take(100).ToListAsync(ct);
        foreach (var message in messages)
        {
            try
            {
                await publisher.Publish(message.Type, message.Payload, ct);
                message.MarkProcessed(time.GetUtcNow());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish outbox message {OutboxMessageId}", message.Id);
                message.MarkFailed(ex.Message);
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
