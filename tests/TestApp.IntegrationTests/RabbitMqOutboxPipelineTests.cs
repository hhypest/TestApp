using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TestApp.Domain.Events;
using TestApp.Infrastructure.Outbox;
using TestApp.Infrastructure.Persistence;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class RabbitMqOutboxPipelineTests
{
    [Fact]
    public async Task Processor_marks_message_processed_only_after_RabbitMQ_delivery()
    {
        var rabbitMq = Environment.GetEnvironmentVariable("TESTAPP_RABBITMQ");
        if (string.IsNullOrWhiteSpace(rabbitMq))
            return;

        var ct = TestContext.Current.CancellationToken;
        await using var database = await MariaDbTestDatabase.CreateAsync(ct);
        var exchange = $"testapp.events.pipeline.{Guid.CreateVersion7():N}";
        var integrationEvent = new PipelineIntegrationEvent(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "pipeline-payload");

        await using (var setup = database.CreateContext())
        {
            await setup.Database.MigrateAsync(ct);
            setup.OutboxMessages.Add(OutboxMessage.From(integrationEvent));
            await setup.SaveChangesAsync(ct);
        }

        var factory = new ConnectionFactory { Uri = new Uri(rabbitMq) };
        await using var brokerConnection = await factory.CreateConnectionAsync(ct);
        await using var brokerChannel = await brokerConnection.CreateChannelAsync(cancellationToken: ct);
        await brokerChannel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        var queue = await brokerChannel.QueueDeclareAsync(string.Empty, durable: false, exclusive: true, autoDelete: true, cancellationToken: ct);
        await brokerChannel.QueueBindAsync(queue.QueueName, exchange, "#", cancellationToken: ct);

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseMySql(
            database.ConnectionString,
            new MariaDbServerVersion(new Version(11, 4, 0))));
        services.Configure<RabbitMqOutboxOptions>(options =>
        {
            options.Enabled = true;
            options.ConnectionString = rabbitMq;
            options.Exchange = exchange;
            options.RoutingKeyPrefix = "testapp.pipeline";
            options.ClientProvidedName = "TestApp.PipelineTests";
        });
        services.AddSingleton<RabbitMqOutboxPublisher>();
        services.AddSingleton<IOutboxPublisher>(sp => sp.GetRequiredService<RabbitMqOutboxPublisher>());

        await using var provider = services.BuildServiceProvider();
        var processor = new OutboxProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            Options.Create(new OutboxDeliveryOptions()),
            NullLogger<OutboxProcessor>.Instance);

        var processedCount = await processor.ProcessBatchAsync(ct);

        Assert.Equal(1, processedCount);
        var delivered = await brokerChannel.BasicGetAsync(queue.QueueName, autoAck: true, cancellationToken: ct);
        Assert.NotNull(delivered);
        Assert.Equal(integrationEvent.EventId.ToString("D"), delivered.BasicProperties.MessageId);
        Assert.Contains("pipeline-payload", Encoding.UTF8.GetString(delivered.Body.ToArray()), StringComparison.Ordinal);

        await using var verification = database.CreateContext();
        var message = await verification.OutboxMessages.AsNoTracking().SingleAsync(x => x.Id == integrationEvent.EventId, ct);
        Assert.NotNull(message.ProcessedAt);
        Assert.Null(message.Error);
        Assert.Null(message.DeadLetteredAt);

        await brokerChannel.ExchangeDeleteAsync(exchange, ifUnused: false, cancellationToken: ct);
    }

    private sealed record PipelineIntegrationEvent(Guid EventId, DateTimeOffset OccurredAt, string Value) : IIntegrationEvent;
}
