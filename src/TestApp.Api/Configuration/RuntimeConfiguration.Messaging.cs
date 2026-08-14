namespace TestApp.Api;

public static partial class RuntimeConfiguration
{
    public static RabbitMqRuntimeOptions LoadRabbitMq(IConfiguration configuration)
    {
        var enabled = configuration.GetValue<bool>("RabbitMq:Enabled");
        var connectionString = configuration["RabbitMq:ConnectionString"]?.Trim() ?? string.Empty;
        var exchange = configuration["RabbitMq:Exchange"]?.Trim() ?? "testapp.events";
        var routingKeyPrefix = configuration["RabbitMq:RoutingKeyPrefix"]?.Trim() ?? "testapp";
        var clientProvidedName = configuration["RabbitMq:ClientProvidedName"]?.Trim() ?? "TestApp.Outbox";
        var batchSize = configuration.GetValue<int?>("Outbox:BatchSize") ?? 100;
        var maxAttempts = configuration.GetValue<int?>("Outbox:MaxAttempts") ?? 10;
        var pollIntervalSeconds = configuration.GetValue<int?>("Outbox:PollIntervalSeconds") ?? 5;
        var baseRetryDelaySeconds = configuration.GetValue<int?>("Outbox:BaseRetryDelaySeconds") ?? 5;
        var maxRetryDelaySeconds = configuration.GetValue<int?>("Outbox:MaxRetryDelaySeconds") ?? 900;
        var advisoryLockTimeoutSeconds = configuration.GetValue<int?>("Outbox:AdvisoryLockTimeoutSeconds") ?? 5;

        if (enabled)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("RabbitMq:ConnectionString is required when RabbitMq:Enabled=true.");
            if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "amqp" && uri.Scheme != "amqps"))
                throw new InvalidOperationException("RabbitMq:ConnectionString must be an absolute amqp:// or amqps:// URI.");
            if (string.IsNullOrWhiteSpace(exchange))
                throw new InvalidOperationException("RabbitMq:Exchange cannot be empty when RabbitMQ delivery is enabled.");
            if (string.IsNullOrWhiteSpace(routingKeyPrefix))
                throw new InvalidOperationException("RabbitMq:RoutingKeyPrefix cannot be empty when RabbitMQ delivery is enabled.");
        }

        if (batchSize is < 1 or > 1000)
            throw new InvalidOperationException("Outbox:BatchSize must be between 1 and 1000.");
        if (maxAttempts < 1)
            throw new InvalidOperationException("Outbox:MaxAttempts must be greater than zero.");
        if (pollIntervalSeconds < 1)
            throw new InvalidOperationException("Outbox:PollIntervalSeconds must be greater than zero.");
        if (baseRetryDelaySeconds < 1)
            throw new InvalidOperationException("Outbox:BaseRetryDelaySeconds must be greater than zero.");
        if (maxRetryDelaySeconds < baseRetryDelaySeconds)
            throw new InvalidOperationException("Outbox:MaxRetryDelaySeconds cannot be smaller than Outbox:BaseRetryDelaySeconds.");
        if (advisoryLockTimeoutSeconds < 0)
            throw new InvalidOperationException("Outbox:AdvisoryLockTimeoutSeconds cannot be negative.");

        return new RabbitMqRuntimeOptions(
            enabled,
            connectionString,
            exchange,
            routingKeyPrefix,
            clientProvidedName,
            batchSize,
            maxAttempts,
            pollIntervalSeconds,
            baseRetryDelaySeconds,
            maxRetryDelaySeconds,
            advisoryLockTimeoutSeconds);
    }

    public static AttemptExpirationRuntimeOptions LoadAttemptExpiration(IConfiguration configuration)
    {
        var batchSize = configuration.GetValue<int?>("AttemptExpiration:BatchSize") ?? 100;
        var pollIntervalSeconds = configuration.GetValue<int?>("AttemptExpiration:PollIntervalSeconds") ?? 30;
        if (batchSize is < 1 or > 1000)
            throw new InvalidOperationException("AttemptExpiration:BatchSize must be between 1 and 1000.");
        if (pollIntervalSeconds < 1)
            throw new InvalidOperationException("AttemptExpiration:PollIntervalSeconds must be greater than zero.");
        return new AttemptExpirationRuntimeOptions(batchSize, pollIntervalSeconds);
    }
}
