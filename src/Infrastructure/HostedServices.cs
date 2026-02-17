using Application;
using Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure;

public sealed class ElasticBootstrapService(IElasticClient elastic, ILogger<ElasticBootstrapService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await elastic.BootstrapAsync(cancellationToken);
        logger.LogInformation("Elasticsearch templates and indices bootstrapped.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class LogConsumerService(
    IMessageBus bus,
    IncidentProcessor processor,
    IWindowStateStore stateStore,
    IAnomalyEngine engine,
    IElasticClient elastic,
    ILogger<LogConsumerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await bus.SubscribeAsync<LogEvent>("logs.ingest.q", Handle, stoppingToken);
        logger.LogInformation("Subscribed to logs.ingest.q");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task Handle(MessageEnvelope<LogEvent> envelope, CancellationToken ct)
    {
        try
        {
            var state = await stateStore.GetAsync(envelope.Payload.CorrelationKey, ct);
            await processor.ProcessLogAsync(envelope.Payload, state, ct);
            await stateStore.SetAsync(envelope.Payload.CorrelationKey, engine.Next(state, envelope.Payload), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed processing log {MessageId}", envelope.MessageId);
            var dlq = new DeadLetterMessage(Guid.NewGuid(), "logs.ingest.q", System.Text.Json.JsonSerializer.Serialize(envelope.Payload), ex.Message, DateTime.UtcNow, envelope.CorrelationId);
            await elastic.IndexDeadLetterAsync(dlq, ct);
        }
    }
}

public sealed class AutoResolveService(
    IIncidentRepository repo,
    IncidentProcessor processor,
    IClock clock,
    IOptions<ProcessingOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var stale = await repo.GetStaleOpenIncidentsAsync(clock.UtcNow.AddMinutes(-options.Value.AutoResolveQuietMinutes), stoppingToken);
            foreach (var incident in stale)
            {
                await processor.ResolveIncidentAsync(incident, stoppingToken);
            }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
