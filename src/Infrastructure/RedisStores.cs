using Application;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace Infrastructure;

public sealed class RedisConnectionFactory : IAsyncDisposable
{
    private readonly Lazy<Task<ConnectionMultiplexer>> _mux;
    public RedisConnectionFactory(IOptions<RedisOptions> options)
    {
        _mux = new(() => ConnectionMultiplexer.ConnectAsync(options.Value.ConnectionString));
    }

    public async Task<IDatabase> GetDatabaseAsync() => (await _mux.Value).GetDatabase();

    public async ValueTask DisposeAsync()
    {
        if (_mux.IsValueCreated) (await _mux.Value).Dispose();
    }
}

public sealed class RedisIdempotencyStore(RedisConnectionFactory factory) : IIdempotencyStore
{
    public async Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        var db = await factory.GetDatabaseAsync();
        return await db.StringSetAsync($"idempotency:{key}", "1", ttl, When.NotExists);
    }
}

public sealed class RedisWindowStateStore(RedisConnectionFactory factory) : IWindowStateStore
{
    public async Task<WindowState> GetAsync(string correlationKey, CancellationToken ct)
    {
        var db = await factory.GetDatabaseAsync();
        var json = await db.StringGetAsync($"window:{correlationKey}");
        return json.HasValue ? JsonSerializer.Deserialize<WindowState>(json!) ?? WindowState.Empty : WindowState.Empty;
    }

    public async Task SetAsync(string correlationKey, WindowState state, CancellationToken ct)
    {
        var db = await factory.GetDatabaseAsync();
        await db.StringSetAsync($"window:{correlationKey}", JsonSerializer.Serialize(state), TimeSpan.FromMinutes(30));
    }
}

public sealed class RedisLockManager(RedisConnectionFactory factory) : ILockManager
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        var db = await factory.GetDatabaseAsync();
        var lockKey = $"lock:{key}";
        var value = Guid.NewGuid().ToString("N");
        var ok = await db.StringSetAsync(lockKey, value, ttl, When.NotExists);
        if (!ok) return null;
        return new RedisLockHandle(db, lockKey, value);
    }

    private sealed class RedisLockHandle(IDatabase db, string key, string value) : IAsyncDisposable
    {
        private const string Lua = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";
        public async ValueTask DisposeAsync()
        {
            _ = await db.ScriptEvaluateAsync(Lua, new RedisKey[] { key }, new RedisValue[] { value });
        }
    }
}
