using Application;
using Domain;
using System.Collections.Concurrent;

namespace Infrastructure;

public sealed class IncidentRepository : IIncidentRepository
{
    private readonly ConcurrentDictionary<string, Incident> _cache = new();

    public Task<Incident?> GetOpenByCorrelationKeyAsync(string correlationKey, CancellationToken ct)
        => Task.FromResult(_cache.Values.FirstOrDefault(x => x.CorrelationKey == correlationKey && x.Status is IncidentStatus.Open or IncidentStatus.Investigating));

    public Task UpsertAsync(Incident incident, CancellationToken ct)
    {
        _cache[incident.Id] = incident;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Incident>> SearchAsync(string? status, string? env, string? source, CancellationToken ct)
    {
        var q = _cache.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<IncidentStatus>(status, true, out var statusValue)) q = q.Where(x => x.Status == statusValue);
        if (!string.IsNullOrWhiteSpace(env)) q = q.Where(x => x.Env.Equals(env, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(source)) q = q.Where(x => x.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult((IReadOnlyList<Incident>)q.OrderByDescending(x => x.LastSeenUtc).ToList());
    }

    public Task<Incident?> GetByIdAsync(string id, CancellationToken ct) => Task.FromResult(_cache.TryGetValue(id, out var found) ? found : null);

    public Task<IReadOnlyList<Incident>> GetStaleOpenIncidentsAsync(DateTime olderThanUtc, CancellationToken ct)
        => Task.FromResult((IReadOnlyList<Incident>)_cache.Values.Where(x => (x.Status is IncidentStatus.Open or IncidentStatus.Investigating) && x.LastSeenUtc <= olderThanUtc).ToList());
}
