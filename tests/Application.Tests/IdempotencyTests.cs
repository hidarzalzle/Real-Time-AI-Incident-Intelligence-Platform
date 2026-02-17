using Application;
using Xunit;

public class IdempotencyTests
{
    [Fact]
    public async Task DuplicateKey_IsRejected()
    {
        var store = new InMemoryIdempotencyStore();
        var one = await store.TryAcquireAsync("k", TimeSpan.FromMinutes(1), default);
        var two = await store.TryAcquireAsync("k", TimeSpan.FromMinutes(1), default);
        Assert.True(one);
        Assert.False(two);
    }

    private sealed class InMemoryIdempotencyStore : IIdempotencyStore
    {
        private readonly HashSet<string> _keys = new();

        public Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
            => Task.FromResult(_keys.Add(key));
    }
}
