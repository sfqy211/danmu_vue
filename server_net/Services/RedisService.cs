using StackExchange.Redis;
using System.Text.Json;
using Danmu.Server.Models;

namespace Danmu.Server.Services;

public class RedisService
{
    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisService> _logger;

    /// <summary>
    /// Parameterless constructor for mocking/inheritance. Does not connect to Redis.
    /// </summary>
    protected RedisService()
    {
        _logger = null!;
        _redis = null!;
        _db = null!;
    }

    public RedisService(ILogger<RedisService> logger)
    {
        _logger = logger;
        var connectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";
        // Ensure abortConnect=false is set to prevent startup crash if Redis is not ready
        if (!connectionString.Contains("abortConnect", StringComparison.OrdinalIgnoreCase))
        {
            connectionString += ",abortConnect=false";
        }
        _redis = ConnectionMultiplexer.Connect(connectionString);
        _db = _redis.GetDatabase();
    }

    public virtual async Task PushMessageAsync(string key, string message)
    {
        await _db.ListRightPushAsync(key, message);
    }

    public virtual async Task SetMetadataAsync(string key, Dictionary<string, string> metadata)
    {
        var entries = metadata.Select(kv => new HashEntry(kv.Key, kv.Value)).ToArray();
        await _db.HashSetAsync(key, entries);
    }

    public virtual async Task SetMetadataFieldAsync(string key, string field, string value)
    {
        await _db.HashSetAsync(key, new HashEntry[] { new HashEntry(field, value) });
    }

    public virtual async Task SetHashFieldAsync(string key, string field, string value)
    {
        await _db.HashSetAsync(key, field, value);
    }

    public virtual async Task<Dictionary<string, string>> GetHashAsync(string key)
    {
        var entries = await _db.HashGetAllAsync(key);
        return entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
    }

    public virtual async Task DeleteHashFieldAsync(string key, string field)
    {
        await _db.HashDeleteAsync(key, field);
    }

    public virtual async Task<List<string>> GetMessagesAsync(string key)
    {
        var values = await _db.ListRangeAsync(key);
        return values.Select(v => v.ToString()).ToList();
    }

    public virtual async Task<long> GetListLengthAsync(string key)
    {
        return await _db.ListLengthAsync(key);
    }

    public virtual async Task<List<string>> GetListRangeAsync(string key, long start, long stop)
    {
        var values = await _db.ListRangeAsync(key, start, stop);
        return values.Select(v => v.ToString()).ToList();
    }
    
    public virtual async Task<Dictionary<string, string>> GetMetadataAsync(string key)
    {
        var entries = await _db.HashGetAllAsync(key);
        return entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
    }

    public virtual async Task DeleteKeyAsync(string key)
    {
        await _db.KeyDeleteAsync(key);
    }
    
    public virtual async Task SetLiveSessionKeyAsync(string uid, string sessionKey)
    {
        await _db.StringSetAsync($"danmu:live:{uid}", sessionKey);
    }

    public virtual async Task SetStringWithExpiryAsync(string key, string value, TimeSpan expiry)
    {
        await _db.StringSetAsync(key, value, expiry);
    }
    
    public virtual async Task<string?> GetLiveSessionKeyAsync(string uid)
    {
        return await _db.StringGetAsync($"danmu:live:{uid}");
    }
    
    public virtual async Task ClearLiveSessionKeyAsync(string uid)
    {
        await _db.KeyDeleteAsync($"danmu:live:{uid}");
    }
}
