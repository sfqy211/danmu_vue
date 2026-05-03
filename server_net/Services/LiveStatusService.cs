using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Services;

public class LiveStatusService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<LiveStatusService> _logger;
    private readonly BilibiliService _bilibiliService;
    private readonly RedisService _redis;
    private readonly ProcessManager _pm;

    // Cache: room_id → (liveStatus, liveStartTime, updatedAt)
    private readonly ConcurrentDictionary<long, LiveState> _cache = new();
    private readonly ConcurrentDictionary<long, Task<LiveState?>> _pendingRequests = new();
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(2);

    public LiveStatusService(IServiceProvider services, ILogger<LiveStatusService> logger,
        BilibiliService bilibiliService, RedisService redis, ProcessManager pm)
    {
        _services = services;
        _logger = logger;
        _bilibiliService = bilibiliService;
        _redis = redis;
        _pm = pm;
    }

    public async Task<LiveState?> GetCachedStatusAsync(long roomId)
    {
        if (_cache.TryGetValue(roomId, out var state))
        {
            // Cache valid for 3 minutes
            if (DateTime.UtcNow - state.UpdatedAt < TimeSpan.FromMinutes(3))
                return state;
        }

        try
        {
            var redisState = await _redis.GetHashAsync($"live:status:{roomId}");
            if (redisState.Count > 0 && TryParseLiveState(roomId, redisState, out var parsed))
            {
                _cache[roomId] = parsed;
                return parsed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read cached live status from Redis for room {RoomId}", roomId);
        }

        return null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay to let app start up
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckRoomsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LiveStatusService check failed");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task CheckRoomsAsync(CancellationToken token)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.DanmuContext>();
        var rooms = await db.Rooms
            .Where(r => r.AutoRecord == 1)
            .Select(r => new { r.RoomId, r.Uid })
            .ToListAsync(token);

        var processes = _pm.GetProcesses();
        var processRoomIds = processes.Select(p => p.RoomId).ToHashSet();

        // Only check rooms WITHOUT an active recorder (Layer 1 covers those)
        var roomsToCheck = rooms.Where(r => !processRoomIds.Contains(r.RoomId)).ToList();
        if (roomsToCheck.Count == 0) return;

        _logger.LogInformation("LiveStatusService checking {Count} rooms without active recorder", roomsToCheck.Count);

        var tasks = roomsToCheck.Select(async room =>
        {
            try
            {
                var state = await FetchRoomStatusWithDedupAsync(room.RoomId, token);
                if (state != null)
                {
                    _cache[room.RoomId] = state;
                    await PersistRoomStateAsync(state, token);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to fetch live status for room {RoomId}", room.RoomId);
            }
        }).ToList();

        await Task.WhenAll(tasks);
    }

    private Task<LiveState?> FetchRoomStatusWithDedupAsync(long roomId, CancellationToken token)
    {
        return _pendingRequests.GetOrAdd(roomId, _ => FetchRoomStatusInternalAsync(roomId, token));
    }

    private async Task<LiveState?> FetchRoomStatusInternalAsync(long roomId, CancellationToken token)
    {
        try
        {
            var jitterMs = Random.Shared.Next(-3000, 3001);
            if (jitterMs > 0)
            {
                await Task.Delay(jitterMs, token);
            }

            return await _bilibiliService.GetRoomInitAsync(roomId, token);
        }
        finally
        {
            _pendingRequests.TryRemove(roomId, out _);
        }
    }

    private async Task PersistRoomStateAsync(LiveState state, CancellationToken token)
    {
        try
        {
            await _redis.SetMetadataAsync($"live:status:{state.RoomId}", new Dictionary<string, string>
            {
                ["room_id"] = state.RoomId.ToString(),
                ["live_status"] = state.LiveStatus.ToString(),
                ["live_start_time"] = state.LiveStartTime?.ToString() ?? string.Empty,
                ["updated_at"] = state.UpdatedAt.ToString("O")
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist live status to Redis for room {RoomId}", state.RoomId);
        }
    }

    internal static bool TryParseLiveState(long roomId, Dictionary<string, string> data, out LiveState state)
    {
        state = null!;
        if (!data.TryGetValue("live_status", out var liveStatusText) || !int.TryParse(liveStatusText, out var liveStatus))
            return false;

        long? liveStartTime = null;
        if (data.TryGetValue("live_start_time", out var liveStartTimeText) && !string.IsNullOrWhiteSpace(liveStartTimeText))
        {
            if (long.TryParse(liveStartTimeText, out var parsed))
                liveStartTime = parsed;
        }

        var updatedAt = DateTime.UtcNow;
        if (data.TryGetValue("updated_at", out var updatedAtText) && DateTime.TryParse(updatedAtText, out var parsedUpdatedAt))
        {
            updatedAt = parsedUpdatedAt;
        }

        state = new LiveState
        {
            RoomId = roomId,
            LiveStatus = liveStatus,
            LiveStartTime = liveStartTime,
            UpdatedAt = updatedAt
        };

        return true;
    }
}

public class LiveState
{
    public long RoomId { get; set; }
    public int LiveStatus { get; set; }
    public long? LiveStartTime { get; set; }
    public DateTime UpdatedAt { get; set; }
}
