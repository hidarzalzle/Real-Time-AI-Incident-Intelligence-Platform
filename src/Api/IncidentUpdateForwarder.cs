using Application;
using Domain;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

public sealed class IncidentUpdateForwarder(IMessageBus bus, IHubContext<IncidentsHub> hub, ILogger<IncidentUpdateForwarder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await bus.SubscribeAsync<JsonElement>("incidents.update.q", Handle, stoppingToken);
        logger.LogInformation("Subscribed to incidents.update.q");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task Handle(MessageEnvelope<JsonElement> envelope, CancellationToken ct)
    {
        var lifecycle = envelope.Payload.Deserialize<IncidentLifecycleEvent>();
        var eventName = lifecycle?.EventType ?? "incidentUpdated";
        await hub.Clients.All.SendAsync(eventName, lifecycle?.Payload ?? envelope.Payload, ct);
        await hub.Clients.All.SendAsync("statsUpdated", new { occurredAtUtc = envelope.OccurredAtUtc, correlationId = envelope.CorrelationId, eventType = eventName }, ct);
    }
}
