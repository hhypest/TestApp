using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TestApp.Domain.Events;
using TestApp.Infrastructure.Outbox;
using TestApp.Infrastructure.Persistence;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class OutboxDeliveryTests
{
    [Fact]
    public async Task Successful_delivery_marks_message_processed_and_passes_event_id()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await MariaDbTestDatabase.CreateAsync(ct);
        var integrationEvent = new TestIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, "payload");

        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.OutboxMessages.Add(OutboxMessage.From(integrationEvent));
            await setup.SaveChangesAsync(ct);
        }

        var publisher = new RecordingPublisher();
        await using var provider = BuildServices(database.ConnectionString, publisher);
        var processor = CreateProcessor(provider, new OutboxDeliveryOptions());

        var count = await processor.ProcessBatchAsync(ct);

        Assert.Equal(1, count);
        Assert.Equal(integrationEvent.EventId, publisher.LastEventId);

        await using var verification = database.CreateContext();
        var message = await verification.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == integrationEvent.EventId, ct);
        Assert.NotNull(message.ProcessedAt);
        Assert.Null(message.DeadLetteredAt);
        Assert.Null(message.Error);
    }

    [Fact]
    public async Task Failed_delivery_is_dead_lettered_after_configured_attempt_limit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await MariaDbTestDatabase.CreateAsync(ct);
        var integrationEvent = new TestIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, "payload");

        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.OutboxMessages.Add(OutboxMessage.From(integrationEvent));
            await setup.SaveChangesAsync(ct);
        }

        await using var provider = BuildServices(database.ConnectionString, new FailingPublisher());
        var processor = CreateProcessor(provider, new OutboxDeliveryOptions
        {
            MaxAttempts = 1,
            BaseRetryDelay = TimeSpan.FromMilliseconds(10),
            MaxRetryDelay = TimeSpan.FromMilliseconds(10)
        });

        var count = await processor.ProcessBatchAsync(ct);

        Assert.Equal(0, count);
        await using var verification = database.CreateContext();
        var message = await verification.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == integrationEvent.EventId, ct);
        Assert.Null(message.ProcessedAt);
        Assert.Equal(1, message.AttemptCount);
        Assert.NotNull(message.LastAttemptAt);
        Assert.NotNull(message.DeadLetteredAt);
        Assert.Null(message.NextAttemptAt);
        Assert.Contains("transport unavailable", message.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceProvider BuildServices(string connectionString, IOutboxPublisher publisher)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseMySql(connectionString, new MariaDbServerVersion(new Version(11, 4, 0))));
        services.AddScoped<IOutboxPublisher>(_ => publisher);
        return services.BuildServiceProvider();
    }

    private static OutboxProcessor CreateProcessor(ServiceProvider provider, OutboxDeliveryOptions options) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            Options.Create(options),
            NullLogger<OutboxProcessor>.Instance);

    private sealed record TestIntegrationEvent(Guid EventId, DateTimeOffset OccurredAt, string Value) : IIntegrationEvent;

    private sealed class RecordingPublisher : IOutboxPublisher
    {
        public Guid? LastEventId { get; private set; }

        public Task Publish(Guid eventId, string eventType, string payload, CancellationToken ct)
        {
            LastEventId = eventId;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingPublisher : IOutboxPublisher
    {
        public Task Publish(Guid eventId, string eventType, string payload, CancellationToken ct) =>
            Task.FromException(new InvalidOperationException("transport unavailable"));
    }
}
