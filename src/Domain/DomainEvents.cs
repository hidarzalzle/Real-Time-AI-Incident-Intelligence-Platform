namespace Domain;

public interface IDomainEvent { DateTime OccurredAtUtc { get; } }
public sealed record IncidentOpened(Guid IncidentId, string CorrelationKey, DateTime OccurredAtUtc) : IDomainEvent;
public sealed record IncidentUpdated(Guid IncidentId, string CorrelationKey, DateTime OccurredAtUtc) : IDomainEvent;
public sealed record IncidentResolved(Guid IncidentId, string CorrelationKey, DateTime OccurredAtUtc) : IDomainEvent;
public sealed record IncidentDeadLettered(Guid IncidentId, string CorrelationKey, DateTime OccurredAtUtc) : IDomainEvent;
