using StackExchange.Redis;
using System.Text.Json;
using Danmu.Server.Models;

namespace Danmu.Server.Services;

public class RedisService
{
    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisService> _logger;

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

    public async Task PushMessageAsync(string key, string message)
    {
        await _db.ListRightPushAsync(key, message);
    }

    public async Task SetMetadataAsync(string key, Dictionary<string, string> metadata)
    {
        var entries = metadata.Select(kv => new HashEntry(kv.Key, kv.Value)).ToArray();
        await _db.HashSetAsync(key, entries);
    }

    public async Task<List<string>> GetMessagesAsync(string key)
    {
        var values = await _db.ListRangeAsync(key);
        return values.Select(v => v.ToString()).ToList();
    }
    
    public async Task<Dictionary<string, string>> GetMetadataAsync(string key)
    {
        var entries = await _db.HashGetAllAsync(key);
        return entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
    }

    public async Task DeleteKeyAsync(string key)
    {
        await _db.KeyDeleteAsync(key);
    }
    
    public async Task SetLiveSessionKeyAsync(long roomId, string sessionKey)
    {
        await _db.StringSetAsync($"danmu:live:{roomId}", sessionKey);
    }
    
    public async Task<string?> GetLiveSessionKeyAsync(long roomId)
    {
        return await _db.StringGetAsync($"danmu:live:{roomId}");
    }
    
    public async Task ClearLiveSessionKeyAsync(long roomId)
    {
        await _db.KeyDeleteAsync($"danmu:live:{roomId}");
    }
}
