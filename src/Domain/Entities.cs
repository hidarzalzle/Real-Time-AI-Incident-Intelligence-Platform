namespace Domain;

public sealed record LogEvent(
    Guid Id,
    string Source,
    string Env,
    LogLevel Level,
    string Message,
    DateTime TimestampUtc,
    string? TraceId,
    string? SpanId,
    string? Host,
    IReadOnlyDictionary<string, string> Tags,
    string Fingerprint,
    string CorrelationKey,
    string MessageId,
    string IdempotencyKey);

public sealed record IncidentFinding(DateTime TimestampUtc, LogLevel Level, string Message, string? TraceId, string ContextJson);

public sealed record Incident(
    string Id,
    IncidentStatus Status,
    IncidentSeverity Severity,
    IncidentCategory Category,
    string Title,
    string Summary,
    string Explanation,
    IReadOnlyList<string> RecommendedActions,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc,
    string Source,
    string Env,
    string CorrelationKey,
    double AnomalyScore,
    IReadOnlyList<IncidentFinding> Evidence,
    int EventCount,
    double ErrorRate,
    double P95Latency,
    int UniqueHosts,
    string LlmProvider,
    string LlmModel,
    string PromptVersion,
    int PromptTokens,
    int CompletionTokens,
    string IdempotencyKey);

public sealed record DeadLetterMessage(Guid Id, string QueueName, string Payload, string Error, DateTime FailedAtUtc, string CorrelationKey);
public sealed record AnomalyScore(double Score, double ErrorRate, double BurstScore, double NoveltyScore, double HostSpread);

public sealed record IncidentLifecycleEvent(string EventType, string IncidentId, string CorrelationKey, DateTime OccurredAtUtc, object Payload);
