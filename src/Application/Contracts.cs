using Domain;

namespace Application;

public sealed record MessageEnvelope<T>(
    string MessageId,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string IdempotencyKey,
    T Payload);

public interface IMessageBus
{
    Task PublishAsync<T>(string exchange, string routingKey, MessageEnvelope<T> message, CancellationToken ct);
    Task SubscribeAsync<T>(string queue, Func<MessageEnvelope<T>, CancellationToken, Task> handler, CancellationToken ct);
}

public interface IIncidentRepository
{
    Task<Incident?> GetOpenByCorrelationKeyAsync(string correlationKey, CancellationToken ct);
    Task UpsertAsync(Incident incident, CancellationToken ct);
    Task<IReadOnlyList<Incident>> SearchAsync(string? status, string? env, string? source, CancellationToken ct);
    Task<Incident?> GetByIdAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<Incident>> GetStaleOpenIncidentsAsync(DateTime olderThanUtc, CancellationToken ct);
}

public interface IElasticClient
{
    Task BootstrapAsync(CancellationToken ct);
    Task IndexLogAsync(LogEvent log, CancellationToken ct);
    Task UpsertIncidentAsync(Incident incident, CancellationToken ct);
    Task IndexIncidentEventAsync(IncidentLifecycleEvent @event, CancellationToken ct);
    Task IndexDeadLetterAsync(DeadLetterMessage deadLetter, CancellationToken ct);
    Task<IReadOnlyList<IncidentLifecycleEvent>> GetIncidentEventsAsync(string incidentId, CancellationToken ct);
    Task<IReadOnlyList<DeadLetterMessage>> GetRecentDeadLettersAsync(int size, CancellationToken ct);
}

public interface IIdempotencyStore
{
    Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct);
}

public interface ILockManager
{
    Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct);
}

public interface IClock { DateTime UtcNow { get; } }

public interface ILLMClient
{
    Task<LlmIncidentAnalysis> AnalyzeAsync(LlmIncidentRequest req, CancellationToken ct);
}

public record LlmIncidentRequest(string Title,string Source,string Env,double AnomalyScore,IDictionary<string, object> Metrics,IReadOnlyList<string> EvidenceMessages);
public record LlmIncidentAnalysis(string Category,string Severity,string Summary,string Explanation,IReadOnlyList<string> RecommendedActions,double Confidence,string Provider,string Model,int? PromptTokens,int? CompletionTokens);

public interface IWindowStateStore
{
    Task<WindowState> GetAsync(string correlationKey, CancellationToken ct);
    Task SetAsync(string correlationKey, WindowState state, CancellationToken ct);
}
