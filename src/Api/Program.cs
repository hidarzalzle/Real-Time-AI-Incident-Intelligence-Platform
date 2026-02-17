using Application;
using Domain;
using Infrastructure;
using Microsoft.AspNetCore.SignalR;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.AddPlatformObservability();
builder.Services.AddPlatformInfrastructure(builder.Configuration);
builder.Services.AddSignalR();
builder.Services.AddHostedService<ElasticBootstrapService>();
builder.Services.AddHostedService<IncidentUpdateForwarder>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/ready", () => Results.Ok(new { status = "ready" }));

app.MapGet("/api/incidents", async (string? status, string? env, string? source, IIncidentRepository repo, CancellationToken ct)
    => Results.Ok(await repo.SearchAsync(status, env, source, ct)));

app.MapGet("/api/incidents/{id}", async (string id, IIncidentRepository repo, CancellationToken ct)
    => await repo.GetByIdAsync(id, ct) is { } incident ? Results.Ok(incident) : Results.NotFound());

app.MapGet("/api/incidents/{id}/events", async (string id, IElasticClient elastic, CancellationToken ct)
    => Results.Ok(await elastic.GetIncidentEventsAsync(id, ct)));

app.MapGet("/api/analytics/top-sources", async (IIncidentRepository repo, CancellationToken ct) =>
{
    var data = await repo.SearchAsync(null, null, null, ct);
    var top = data.GroupBy(x => x.Source).Select(g => new { source = g.Key, count = g.Count() }).OrderByDescending(x => x.count).Take(10);
    return Results.Ok(top);
});

app.MapGet("/api/deadletters", async (IElasticClient elastic, CancellationToken ct)
    => Results.Ok(await elastic.GetRecentDeadLettersAsync(50, ct)));

app.MapPost("/api/simulate/logs", async (string? scenario, IMessageBus bus, IHubContext<IncidentsHub> hub, IWebHostEnvironment env, CancellationToken ct) =>
{
    if (!env.IsDevelopment()) return Results.Forbid();
    var logs = SimulationScenarios.Create(scenario ?? "error-burst");
    foreach (var log in logs)
    {
        var envelope = new MessageEnvelope<LogEvent>(log.MessageId, DateTime.UtcNow, log.CorrelationKey, log.IdempotencyKey, log);
        await bus.PublishAsync("logs.exchange", "logs.ingest", envelope, ct);
    }

    await hub.Clients.All.SendAsync("statsUpdated", new { published = logs.Count, scenario = scenario ?? "error-burst" }, ct);
    return Results.Accepted();
});

app.MapHub<IncidentsHub>("/hubs/incidents");
app.MapGet("/dashboard", () => Results.File("wwwroot/dashboard.html", "text/html"));
app.MapFallbackToFile("dashboard.html");

await app.RunAsync();

public sealed class IncidentsHub : Hub { }

public static class SimulationScenarios
{
    public static List<LogEvent> Create(string scenario)
        => scenario.ToLowerInvariant() switch
        {
            "latency-spike" => Build("search-api", "prod", 20, i => $"search latency spike {500 + (i * 25)}ms"),
            "novel-fingerprint" => Build("checkout-api", "prod", 10, i => i == 8 ? "Unhandled index exception code XYZ-991" : "checkout error"),
            "multi-host-spread" => Build("checkout-api", "prod", 16, i => "Payment provider timeout", hostSpread: 8),
            _ => Build("checkout-api", "prod", 15, _ => "Payment provider timeout")
        };

    private static List<LogEvent> Build(string source, string env, int count, Func<int, string> messageFactory, int hostSpread = 4)
    {
        var list = new List<LogEvent>();
        for (var i = 0; i < count; i++)
        {
            var tags = new Dictionary<string, string> { ["tenant"] = "t1", ["region"] = "eu-west" };
            var message = messageFactory(i);
            var fp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message))).Substring(0, 10);
            var correlationKey = $"{source}:{env}:{fp}:t1:eu-west";
            list.Add(new LogEvent(Guid.NewGuid(), source, env, Domain.LogLevel.Error, message, DateTime.UtcNow.AddSeconds(-(count - i)), Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), $"node-{(i % hostSpread) + 1}", tags, fp, correlationKey, Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N")));
        }

        return list;
    }
}
