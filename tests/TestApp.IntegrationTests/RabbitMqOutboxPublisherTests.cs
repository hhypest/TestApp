using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TestApp.Infrastructure.Outbox;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class RabbitMqOutboxPublisherTests
{
    [Fact]
    public async Task Publisher_delivers_confirmed_persistent_event_with_stable_event_id()
    {
        var connectionString = Environment.GetEnvironmentVariable("TESTAPP_RABBITMQ");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var ct = TestContext.Current.CancellationToken;
        const string exchange = "testapp.events.integration";
        const string eventType = "TestApp.IntegrationTests.SampleIntegrationEvent";
        const string payload = "{\"value\":42}";
        var eventId = Guid.CreateVersion7();

        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

        var queue = await channel.QueueDeclareAsync(
            queue: string.Empty,
            durable: false,
            exclusive: true,
            autoDelete: true,
            cancellationToken: ct);

        await channel.ExchangeDeclareAsync(
            exchange: exchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        await channel.QueueBindAsync(
            queue: queue.QueueName,
            exchange: exchange,
            routingKey: "#",
            cancellationToken: ct);

        await using var publisher = new RabbitMqOutboxPublisher(
            Options.Create(new RabbitMqOutboxOptions
            {
                Enabled = true,
                ConnectionString = connectionString,
                Exchange = exchange,
                RoutingKeyPrefix = "testapp.integration",
                ClientProvidedName = "TestApp.IntegrationTests"
            }),
            NullLogger<RabbitMqOutboxPublisher>.Instance);

        await publisher.Publish(eventId, eventType, payload, ct);

        BasicGetResult? delivered = null;
        for (var attempt = 0; attempt < 20 && delivered is null; attempt++)
        {
            delivered = await channel.BasicGetAsync(queue.QueueName, autoAck: true, cancellationToken: ct);
            if (delivered is null)
                await Task.Delay(50, ct);
        }

        Assert.NotNull(delivered);
        Assert.Equal(payload, Encoding.UTF8.GetString(delivered.Body.ToArray()));
        Assert.Equal(eventId.ToString("D"), delivered.BasicProperties.MessageId);
        Assert.Equal(eventType, delivered.BasicProperties.Type);
        Assert.Equal("application/json", delivered.BasicProperties.ContentType);
        Assert.True(delivered.BasicProperties.Persistent);

        await channel.ExchangeDeleteAsync(exchange, ifUnused: false, cancellationToken: ct);
    }
}
