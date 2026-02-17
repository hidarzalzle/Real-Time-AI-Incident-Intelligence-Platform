using Domain;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Application;

public sealed class IncidentProcessor
{
    public const double Threshold = 0.6;

    private readonly IAnomalyEngine _engine;
    private readonly IIncidentRepository _repo;
    private readonly IElasticClient _elastic;
    private readonly IIdempotencyStore _idempotency;
    private readonly ILockManager _locks;
    private readonly ILLMClient _llm;
    private readonly IMessageBus _bus;
    private readonly IClock _clock;

    public IncidentProcessor(IAnomalyEngine engine, IIncidentRepository repo, IElasticClient elastic, IIdempotencyStore idempotency, ILockManager locks, ILLMClient llm, IMessageBus bus, IClock clock)
    { _engine = engine; _repo = repo; _elastic = elastic; _idempotency = idempotency; _locks = locks; _llm = llm; _bus = bus; _clock = clock; }

    public async Task ProcessLogAsync(LogEvent log, WindowState state, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var activity = PlatformTelemetry.ActivitySource.StartActivity("log.process", ActivityKind.Consumer);
        activity?.SetTag("correlationKey", log.CorrelationKey);
        activity?.SetTag("messageId", log.MessageId);

        if (!await _idempotency.TryAcquireAsync($"log:{log.IdempotencyKey}", TimeSpan.FromHours(6), ct)) return;

        await _elastic.IndexLogAsync(log, ct);
        var score = _engine.Evaluate(log, state);
        PlatformTelemetry.LogsProcessed.Add(1);

        if (score.Score < Threshold)
        {
            PlatformTelemetry.ProcessingDurationMs.Record(sw.Elapsed.TotalMilliseconds);
            return;
        }

        await using var lck = await _locks.TryAcquireAsync($"lock:{log.CorrelationKey}", TimeSpan.FromSeconds(30), ct);
        if (lck is null) return;

        var existing = await _repo.GetOpenByCorrelationKeyAsync(log.CorrelationKey, ct);

        using var llmActivity = PlatformTelemetry.ActivitySource.StartActivity("llm.analyze");
        llmActivity?.SetTag("correlationKey", log.CorrelationKey);
        var llm = await _llm.AnalyzeAsync(new LlmIncidentRequest(
            $"{log.Source} anomaly in {log.Env}", log.Source, log.Env, score.Score,
            new Dictionary<string, object>{{"errorRate", score.ErrorRate},{"burst",score.BurstScore}, {"novelty", score.NoveltyScore}},
            new[] { log.Message }), ct);
        PlatformTelemetry.LlmCalls.Add(1);

        var incident = existing is null ? CreateIncident(log, score, llm) : UpdateIncident(existing, log, score, llm);

        using var upsertActivity = PlatformTelemetry.ActivitySource.StartActivity("incident.upsert");
        upsertActivity?.SetTag("incidentId", incident.Id);
        upsertActivity?.SetTag("correlationKey", incident.CorrelationKey);

        await _repo.UpsertAsync(incident, ct);
        await _elastic.UpsertIncidentAsync(incident, ct);

        var eventType = existing is null ? "incidentOpened" : "incidentUpdated";
        var lifecycle = new IncidentLifecycleEvent(eventType, incident.Id, incident.CorrelationKey, _clock.UtcNow, new { incident.Id, incident.Status, incident.Severity, incident.Title, incident.AnomalyScore });
        await _elastic.IndexIncidentEventAsync(lifecycle, ct);

        if (existing is null) PlatformTelemetry.IncidentsOpened.Add(1);

        await _bus.PublishAsync("incidents.exchange", "incidents.update", new MessageEnvelope<object>(
            Guid.NewGuid().ToString("N"), _clock.UtcNow, log.CorrelationKey, incident.IdempotencyKey, lifecycle), ct);

        PlatformTelemetry.ProcessingDurationMs.Record(sw.Elapsed.TotalMilliseconds);
    }

    public async Task ResolveIncidentAsync(Incident incident, CancellationToken ct)
    {
        var resolved = incident with { Status = IncidentStatus.Resolved, LastSeenUtc = _clock.UtcNow };
        await _repo.UpsertAsync(resolved, ct);
        await _elastic.UpsertIncidentAsync(resolved, ct);
        var lifecycle = new IncidentLifecycleEvent("incidentResolved", resolved.Id, resolved.CorrelationKey, _clock.UtcNow, resolved);
        await _elastic.IndexIncidentEventAsync(lifecycle, ct);
        await _bus.PublishAsync("incidents.exchange", "incidents.update", new MessageEnvelope<object>(Guid.NewGuid().ToString("N"), _clock.UtcNow, resolved.CorrelationKey, resolved.IdempotencyKey, lifecycle), ct);
        PlatformTelemetry.IncidentsResolved.Add(1);
    }

    private Incident CreateIncident(LogEvent log, AnomalyScore score, LlmIncidentAnalysis llm)
    {
        var incidentId = DeterministicId(log.CorrelationKey);
        return new Incident(incidentId, IncidentStatus.Open, ParseSeverity(llm.Severity), ParseCategory(llm.Category),
            $"{log.Source} anomaly", llm.Summary, llm.Explanation, llm.RecommendedActions,
            log.TimestampUtc, log.TimestampUtc, log.Source, log.Env, log.CorrelationKey, score.Score,
            new[] { new IncidentFinding(log.TimestampUtc, log.Level, log.Message, log.TraceId, "{}") }, 1, score.ErrorRate, ParseLatency(log.Message), 1,
            llm.Provider, llm.Model, "v1", llm.PromptTokens ?? 0, llm.CompletionTokens ?? 0, log.IdempotencyKey);
    }

    private Incident UpdateIncident(Incident current, LogEvent log, AnomalyScore score, LlmIncidentAnalysis llm)
    {
        var findings = current.Evidence.Take(9).ToList();
        findings.Insert(0, new IncidentFinding(log.TimestampUtc, log.Level, log.Message, log.TraceId, "{}"));
        return current with
        {
            LastSeenUtc = log.TimestampUtc,
            Status = IncidentStatus.Investigating,
            Severity = ParseSeverity(llm.Severity),
            Category = ParseCategory(llm.Category),
            Summary = llm.Summary,
            Explanation = llm.Explanation,
            RecommendedActions = llm.RecommendedActions,
            AnomalyScore = Math.Max(current.AnomalyScore, score.Score),
            EventCount = current.EventCount + 1,
            ErrorRate = score.ErrorRate,
            Evidence = findings,
            UniqueHosts = Math.Max(current.UniqueHosts, current.Evidence.Select(x => x.ContextJson).Distinct().Count()),
            IdempotencyKey = log.IdempotencyKey
        };
    }

    private static double ParseLatency(string message)
    {
        var marker = "ms";
        var idx = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx <= 0) return 0;
        var chunk = new string(message[..idx].Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return double.TryParse(chunk, out var v) ? v : 0;
    }

    private static string DeterministicId(string correlationKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(correlationKey));
        return Convert.ToHexString(bytes[..16]).ToLowerInvariant();
    }

    private static IncidentSeverity ParseSeverity(string severity) => Enum.TryParse<IncidentSeverity>(severity, true, out var v) ? v : IncidentSeverity.S2;
    private static IncidentCategory ParseCategory(string category) => Enum.TryParse<IncidentCategory>(category, true, out var v) ? v : IncidentCategory.Unknown;
}
