using Application;
using Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Infrastructure;

public sealed class ElasticClient : IElasticClient
{
    private readonly HttpClient _http;
    private readonly ElasticOptions _options;
    private readonly ILogger<ElasticClient> _logger;
    private readonly AsyncRetryPolicy _retry;

    public ElasticClient(HttpClient http, IOptions<ElasticOptions> options, ILogger<ElasticClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _retry = Policy.Handle<Exception>().WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(150 * attempt));
    }

    public async Task BootstrapAsync(CancellationToken ct)
    {
        _http.BaseAddress = new Uri(_options.Endpoint);

        var logsTemplate = new
        {
            index_patterns = new[] { "logs-*" },
            template = new
            {
                mappings = new
                {
                    properties = new
                    {
                        source = new { type = "keyword" },
                        env = new { type = "keyword" },
                        correlationKey = new { type = "keyword" },
                        message = new { type = "text" },
                        timestampUtc = new { type = "date" }
                    }
                }
            }
        };
        await PutJsonAsync("/_index_template/logs-template", logsTemplate, ct);

        var incidentsMapping = new
        {
            mappings = new
            {
                properties = new
                {
                    correlationKey = new { type = "keyword" },
                    source = new { type = "keyword" },
                    env = new { type = "keyword" },
                    severity = new { type = "keyword" },
                    status = new { type = "keyword" },
                    anomalyScore = new { type = "double" },
                    firstSeenUtc = new { type = "date" },
                    lastSeenUtc = new { type = "date" },
                    explanation = new { type = "text" }
                }
            }
        };
        await PutJsonAsync("/incidents", incidentsMapping, ct, allowFailure: true);

        var eventsTemplate = new
        {
            index_patterns = new[] { "incident-events-*" },
            template = new { mappings = new { properties = new { incidentId = new { type = "keyword" }, correlationKey = new { type = "keyword" }, occurredAtUtc = new { type = "date" }, eventType = new { type = "keyword" } } } }
        };
        await PutJsonAsync("/_index_template/incident-events-template", eventsTemplate, ct);
    }

    public Task IndexLogAsync(LogEvent log, CancellationToken ct)
    {
        using var activity = PlatformTelemetry.ActivitySource.StartActivity("elastic.index");
        var idx = $"logs-{log.TimestampUtc:yyyy.MM.dd}";
        return PostJsonAsync($"/{idx}/_doc/{log.Id}", log, ct);
    }

    public Task UpsertIncidentAsync(Incident incident, CancellationToken ct)
    {
        using var activity = PlatformTelemetry.ActivitySource.StartActivity("elastic.index");
        return PostJsonAsync($"/incidents/_doc/{incident.Id}", incident, ct);
    }

    public Task IndexIncidentEventAsync(IncidentLifecycleEvent @event, CancellationToken ct)
    {
        var idx = $"incident-events-{@event.OccurredAtUtc:yyyy.MM.dd}";
        return PostJsonAsync($"/{idx}/_doc", @event, ct);
    }

    public Task IndexDeadLetterAsync(DeadLetterMessage deadLetter, CancellationToken ct)
    {
        PlatformTelemetry.DlqTotal.Add(1);
        return PostJsonAsync($"/dead-letters/_doc/{deadLetter.Id}", deadLetter, ct);
    }

    public async Task<IReadOnlyList<IncidentLifecycleEvent>> GetIncidentEventsAsync(string incidentId, CancellationToken ct)
    {
        var query = new { size = 100, query = new { term = new { incidentId } }, sort = new object[] { new { occurredAtUtc = new { order = "asc" } } } };
        var hits = await SearchAsync("incident-events-*", query, ct);
        return hits.Select(x => JsonSerializer.Deserialize<IncidentLifecycleEvent>(x.GetRawText())!).ToList();
    }

    public async Task<IReadOnlyList<DeadLetterMessage>> GetRecentDeadLettersAsync(int size, CancellationToken ct)
    {
        var query = new { size, sort = new object[] { new { failedAtUtc = new { order = "desc" } } } };
        var hits = await SearchAsync("dead-letters", query, ct);
        return hits.Select(x => JsonSerializer.Deserialize<DeadLetterMessage>(x.GetRawText())!).ToList();
    }

    private async Task PutJsonAsync(string path, object payload, CancellationToken ct, bool allowFailure = false)
    {
        await _retry.ExecuteAsync(async () =>
        {
            var res = await _http.PutAsJsonAsync(path, payload, ct);
            if (!res.IsSuccessStatusCode && !allowFailure)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"Elastic PUT failed {path}: {body}");
            }
        });
    }

    private async Task PostJsonAsync(string path, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        await _retry.ExecuteAsync(async () =>
        {
            var res = await _http.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"), ct);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"Elastic POST failed {path}: {body}");
            }
        });
    }

    private async Task<IReadOnlyList<JsonElement>> SearchAsync(string index, object query, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(query);
        var response = await _http.PostAsync($"/{index}/_search", new StringContent(json, Encoding.UTF8, "application/json"), ct);
        if (!response.IsSuccessStatusCode) return Array.Empty<JsonElement>();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("hits", out var hitsNode)) return Array.Empty<JsonElement>();
        if (!hitsNode.TryGetProperty("hits", out var innerHits)) return Array.Empty<JsonElement>();
        var list = new List<JsonElement>();
        foreach (var hit in innerHits.EnumerateArray())
        {
            if (hit.TryGetProperty("_source", out var src)) list.Add(src.Clone());
        }
        return list;
    }
}
