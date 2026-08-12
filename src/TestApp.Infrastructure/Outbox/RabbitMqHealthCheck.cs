using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace TestApp.Infrastructure.Outbox;

public sealed class RabbitMqHealthCheck(IOptions<RabbitMqOutboxOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var configuration = options.Value;

        try
        {
            configuration.Validate();
            if (!configuration.Enabled)
                return HealthCheckResult.Healthy("RabbitMQ Outbox delivery is disabled.");

            var factory = new ConnectionFactory
            {
                Uri = new Uri(configuration.ConnectionString),
                AutomaticRecoveryEnabled = false,
                TopologyRecoveryEnabled = false,
                ClientProvidedName = $"{configuration.ClientProvidedName}.Health"
            };

            await using var connection = await factory.CreateConnectionAsync(cancellationToken);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await channel.ExchangeDeclareAsync(
                exchange: configuration.Exchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);

            return connection.IsOpen && channel.IsOpen
                ? HealthCheckResult.Healthy("RabbitMQ is reachable and the Outbox exchange is available.")
                : HealthCheckResult.Unhealthy("RabbitMQ connection or channel is not open.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ readiness check failed.", exception);
        }
    }
}
