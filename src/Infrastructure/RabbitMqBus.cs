using Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace Infrastructure;

public sealed class RabbitMqBus : IMessageBus, IAsyncDisposable
{
    private readonly RabbitOptions _options;
    private readonly ILogger<RabbitMqBus> _logger;
    private readonly AsyncRetryPolicy _retry;
    private IConnection? _connection;
    private IModel? _channel;

    public RabbitMqBus(IOptions<RabbitOptions> options, ILogger<RabbitMqBus> logger)
    {
        _options = options.Value;
        _logger = logger;
        _retry = Policy.Handle<Exception>().WaitAndRetryAsync(_options.RetryCount, i => TimeSpan.FromMilliseconds(200 * i));
    }

    public async Task PublishAsync<T>(string exchange, string routingKey, MessageEnvelope<T> message, CancellationToken ct)
    {
        await EnsureTopologyAsync(ct);
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        await _retry.ExecuteAsync(() =>
        {
            var props = _channel!.CreateBasicProperties();
            props.MessageId = message.MessageId;
            props.CorrelationId = message.CorrelationId;
            props.Timestamp = new AmqpTimestamp(((DateTimeOffset)message.OccurredAtUtc).ToUnixTimeSeconds());
            props.Persistent = true;
            _channel.BasicPublish(exchange, routingKey, props, payload);
            return Task.CompletedTask;
        });
    }

    public async Task SubscribeAsync<T>(string queue, Func<MessageEnvelope<T>, CancellationToken, Task> handler, CancellationToken ct)
    {
        await EnsureTopologyAsync(ct);
        var consumer = new AsyncEventingBasicConsumer(_channel!);
        consumer.Received += async (_, ea) =>
        {
            var raw = Encoding.UTF8.GetString(ea.Body.Span);
            try
            {
                var envelope = JsonSerializer.Deserialize<MessageEnvelope<T>>(raw);
                if (envelope is null) throw new InvalidOperationException("Envelope null");
                await handler(envelope, ct);
                _channel!.BasicAck(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Message processing failed, NACK to DLQ queue={Queue}", queue);
                _channel!.BasicNack(ea.DeliveryTag, false, false);
            }
        };

        _channel!.BasicQos(0, 10, false);
        _channel.BasicConsume(queue, false, consumer);
    }

    private Task EnsureTopologyAsync(CancellationToken ct)
    {
        if (_channel is not null) return Task.CompletedTask;
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.Username,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        DeclareWithDlq("logs.exchange", "logs.ingest", "logs.ingest.q", "logs.ingest.dlq");
        DeclareWithDlq("incidents.exchange", "incidents.update", "incidents.update.q", "incidents.update.dlq");
        return Task.CompletedTask;
    }

    private void DeclareWithDlq(string exchange, string routingKey, string queue, string dlq)
    {
        _channel!.ExchangeDeclare(exchange, ExchangeType.Topic, durable: true);
        _channel.ExchangeDeclare($"{exchange}.dlq", ExchangeType.Topic, durable: true);

        _channel.QueueDeclare(dlq, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(dlq, $"{exchange}.dlq", routingKey);

        var args = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = $"{exchange}.dlq",
            ["x-dead-letter-routing-key"] = routingKey
        };
        _channel.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false, arguments: args);
        _channel.QueueBind(queue, exchange, routingKey);
    }

    public ValueTask DisposeAsync()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        return ValueTask.CompletedTask;
    }
}
