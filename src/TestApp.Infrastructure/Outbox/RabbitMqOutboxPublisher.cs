using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace TestApp.Infrastructure.Outbox;

public sealed class RabbitMqOutboxOptions
{
    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = string.Empty;
    public string Exchange { get; set; } = "testapp.events";
    public string RoutingKeyPrefix { get; set; } = "testapp";
    public string ClientProvidedName { get; set; } = "TestApp.Outbox";

    public void Validate()
    {
        if (!Enabled)
            return;
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new InvalidOperationException("RabbitMq:ConnectionString is required when RabbitMQ Outbox delivery is enabled.");
        if (!Uri.TryCreate(ConnectionString, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "amqp" && uri.Scheme != "amqps"))
            throw new InvalidOperationException("RabbitMq:ConnectionString must be a valid amqp:// or amqps:// URI.");
        if (string.IsNullOrWhiteSpace(Exchange))
            throw new InvalidOperationException("RabbitMq:Exchange is required.");
        if (string.IsNullOrWhiteSpace(RoutingKeyPrefix))
            throw new InvalidOperationException("RabbitMq:RoutingKeyPrefix is required.");
    }
}

public sealed class RabbitMqOutboxPublisher : IOutboxPublisher, IAsyncDisposable
{
    private readonly RabbitMqOutboxOptions _options;
    private readonly ILogger<RabbitMqOutboxPublisher> _logger;
    private readonly ConnectionFactory _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqOutboxPublisher(
        IOptions<RabbitMqOutboxOptions> options,
        ILogger<RabbitMqOutboxPublisher> logger)
    {
        _options = options.Value;
        _options.Validate();
        _logger = logger;
        _factory = new ConnectionFactory
        {
            Uri = new Uri(_options.ConnectionString),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            ClientProvidedName = _options.ClientProvidedName
        };
    }

    public async Task Publish(Guid eventId, string eventType, string payload, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(payload);

        await _gate.WaitAsync(ct);
        try
        {
            await EnsureChannelAsync(ct);

            var properties = new BasicProperties
            {
                MessageId = eventId.ToString("D"),
                Type = eventType,
                ContentType = "application/json",
                Persistent = true,
                AppId = "TestApp"
            };

            var routingKey = BuildRoutingKey(eventType);
            var body = Encoding.UTF8.GetBytes(payload);

            await _channel!.BasicPublishAsync(
                exchange: _options.Exchange,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: ct);

            _logger.LogDebug(
                "Published integration event {EventId} {EventType} to RabbitMQ exchange {Exchange} routing key {RoutingKey}",
                eventId,
                eventType,
                _options.Exchange,
                routingKey);
        }
        catch
        {
            if (_channel is { IsOpen: false })
            {
                await _channel.DisposeAsync();
                _channel = null;
            }
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true } && _connection is { IsOpen: true })
            return;

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        if (_connection is null || !_connection.IsOpen)
        {
            if (_connection is not null)
                await _connection.DisposeAsync();
            _connection = await _factory.CreateConnectionAsync(ct);
        }

        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);

        _channel = await _connection.CreateChannelAsync(channelOptions, ct);
        await _channel.ExchangeDeclareAsync(
            exchange: _options.Exchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);
    }

    private string BuildRoutingKey(string eventType)
    {
        var normalizedType = new string(eventType
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '.')
            .ToArray())
            .Trim('.')
            .Replace("..", ".", StringComparison.Ordinal);

        return $"{_options.RoutingKeyPrefix}.{normalizedType}";
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_channel is not null)
            {
                await _channel.DisposeAsync();
                _channel = null;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
