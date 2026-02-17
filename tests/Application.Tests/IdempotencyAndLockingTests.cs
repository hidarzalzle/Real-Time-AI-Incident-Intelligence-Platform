using Application;
using Domain;
using Xunit;

public class IdempotencyAndLockingTests
{
    [Fact]
    public async Task Processor_DuplicateIdempotency_DoesNotCreateSecondUpdate()
    {
        var repo = new FakeIncidentRepo();
        var elastic = new FakeElastic();
        var bus = new FakeBus();
        var processor = new IncidentProcessor(new AnomalyEngine(), repo, elastic, new FakeIdempotency(falseOnSecond: true), new FakeLockManager(), new FakeLlm(), bus, new FakeClock());

        var log = TestLog();
        await processor.ProcessLogAsync(log, WindowState.Empty, default);
        await processor.ProcessLogAsync(log, WindowState.Empty, default);

        Assert.Single(repo.Items);
    }

    [Fact]
    public async Task LockManager_BlocksSecondAcquireUntilRelease()
    {
        var locks = new FakeLockManager();
        var first = await locks.TryAcquireAsync("k", TimeSpan.FromSeconds(30), default);
        var second = await locks.TryAcquireAsync("k", TimeSpan.FromSeconds(30), default);
        Assert.NotNull(first);
        Assert.Null(second);
        await first!.DisposeAsync();
        var third = await locks.TryAcquireAsync("k", TimeSpan.FromSeconds(30), default);
        Assert.NotNull(third);
    }

    private static LogEvent TestLog() => new(Guid.NewGuid(), "checkout-api", "prod", LogLevel.Error, "Payment provider timeout", DateTime.UtcNow, null, null, "node-1", new Dictionary<string, string>(), "fp1", "corr1", "m1", "id1");

    private sealed class FakeClock : IClock { public DateTime UtcNow => DateTime.UtcNow; }
    private sealed class FakeLlm : ILLMClient
    {
        public Task<LlmIncidentAnalysis> AnalyzeAsync(LlmIncidentRequest req, CancellationToken ct) => Task.FromResult(new LlmIncidentAnalysis("Errors", "S2", "sum", "exp", new[] { "a" }, 0.9, "mock", "m", 1, 1));
    }
    private sealed class FakeElastic : IElasticClient
    {
        public Task BootstrapAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<DeadLetterMessage>> GetRecentDeadLettersAsync(int size, CancellationToken ct) => Task.FromResult((IReadOnlyList<DeadLetterMessage>)Array.Empty<DeadLetterMessage>());
        public Task<IReadOnlyList<IncidentLifecycleEvent>> GetIncidentEventsAsync(string incidentId, CancellationToken ct) => Task.FromResult((IReadOnlyList<IncidentLifecycleEvent>)Array.Empty<IncidentLifecycleEvent>());
        public Task IndexDeadLetterAsync(DeadLetterMessage deadLetter, CancellationToken ct) => Task.CompletedTask;
        public Task IndexIncidentEventAsync(IncidentLifecycleEvent @event, CancellationToken ct) => Task.CompletedTask;
        public Task IndexLogAsync(LogEvent log, CancellationToken ct) => Task.CompletedTask;
        public Task UpsertIncidentAsync(Incident incident, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class FakeIncidentRepo : IIncidentRepository
    {
        public List<Incident> Items { get; } = new();
        public Task<Incident?> GetByIdAsync(string id, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(x => x.Id == id));
        public Task<IReadOnlyList<Incident>> GetStaleOpenIncidentsAsync(DateTime olderThanUtc, CancellationToken ct) => Task.FromResult((IReadOnlyList<Incident>)Items);
        public Task<Incident?> GetOpenByCorrelationKeyAsync(string correlationKey, CancellationToken ct) => Task.FromResult(Items.FirstOrDefault(x => x.CorrelationKey == correlationKey));
        public Task<IReadOnlyList<Incident>> SearchAsync(string? status, string? env, string? source, CancellationToken ct) => Task.FromResult((IReadOnlyList<Incident>)Items);
        public Task UpsertAsync(Incident incident, CancellationToken ct) { var i = Items.FindIndex(x => x.Id == incident.Id); if (i >= 0) Items[i] = incident; else Items.Add(incident); return Task.CompletedTask; }
    }
    private sealed class FakeIdempotency(bool falseOnSecond) : IIdempotencyStore
    {
        private int _count;
        public Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct) => Task.FromResult(++_count == 1 || !falseOnSecond);
    }
    private sealed class FakeLockManager : ILockManager
    {
        private readonly HashSet<string> _held = new();
        public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
        {
            if (!_held.Add(key)) return Task.FromResult<IAsyncDisposable?>(null);
            return Task.FromResult<IAsyncDisposable?>(new Handle(_held, key));
        }
        private sealed class Handle(HashSet<string> held, string key) : IAsyncDisposable { public ValueTask DisposeAsync() { held.Remove(key); return ValueTask.CompletedTask; } }
    }
    private sealed class FakeBus : IMessageBus
    {
        public Task PublishAsync<T>(string exchange, string routingKey, MessageEnvelope<T> message, CancellationToken ct) => Task.CompletedTask;
        public Task SubscribeAsync<T>(string queue, Func<MessageEnvelope<T>, CancellationToken, Task> handler, CancellationToken ct) => Task.CompletedTask;
    }
}
