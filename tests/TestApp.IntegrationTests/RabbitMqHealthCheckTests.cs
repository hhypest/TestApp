using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TestApp.Infrastructure.Outbox;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class RabbitMqHealthCheckTests
{
    [Fact]
    public async Task Enabled_transport_is_ready_when_broker_and_exchange_are_available()
    {
        var rabbitMq = Environment.GetEnvironmentVariable("TESTAPP_RABBITMQ");
        if (string.IsNullOrWhiteSpace(rabbitMq))
            return;

        var ct = TestContext.Current.CancellationToken;
        var exchange = $"testapp.events.health.{Guid.CreateVersion7():N}";
        var check = new RabbitMqHealthCheck(Options.Create(new RabbitMqOutboxOptions
        {
            Enabled = true,
            ConnectionString = rabbitMq,
            Exchange = exchange,
            RoutingKeyPrefix = "testapp.health",
            ClientProvidedName = "TestApp.HealthTests"
        }));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), ct);

        Assert.Equal(HealthStatus.Healthy, result.Status);

        var factory = new ConnectionFactory { Uri = new Uri(rabbitMq) };
        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await channel.ExchangeDeleteAsync(exchange, ifUnused: false, cancellationToken: ct);
    }

    [Fact]
    public async Task Disabled_transport_does_not_require_broker()
    {
        var check = new RabbitMqHealthCheck(Options.Create(new RabbitMqOutboxOptions
        {
            Enabled = false
        }));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
