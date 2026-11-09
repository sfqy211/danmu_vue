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

    /// <summary>
    /// 主动回源查一次直播状态并写回缓存。
    ///
    /// 背景：<see cref="GetCachedStatusAsync"/> 只在内存/Redis 有值时返回。
    /// 采集节点刚连上就上报弹幕时缓存往往还是空的，那时必须同步取一次真实状态，
    /// 否则开播时间拿不到，多节点就会各自用"事件时间对齐"的兜底键，
    /// 同一场直播会被拆成多个 Session。
    /// </summary>
    public async Task<LiveState?> RefreshStatusAsync(long roomId, CancellationToken token = default)
    {
        var state = await FetchRoomStatusWithDedupAsync(roomId, token);
        if (state == null) return null;

        _cache[roomId] = state;
        await PersistRoomStateAsync(state, token);
        return state;
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
        if (_pm.IsRestoring)
        {
            return;
        }

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

        _logger.LogDebug("LiveStatusService checking {Count} rooms without active recorder", roomsToCheck.Count);

        var tasks = roomsToCheck.Select(async room =>
        {
            try
            {
                var state = await FetchRoomStatusWithDedupAsync(room.RoomId, token);
                if (state != null)
                {
                    _cache[room.RoomId] = state;
                    await PersistRoomStateAsync(state, token);

                    if (state.LiveStatus == 1)
                    {
                        // 已被采集节点认领的房间交给节点录，本机不再重复开录制。
                        // 用延迟解析避免构造期循环依赖。
                        var allocator = _services.GetService<Services.Danmakus.DanmakusRoomAllocator>();
                        if (allocator != null && allocator.IsRoomClaimedByNode(room.RoomId))
                        {
                            _logger.LogDebug(
                                "Room {RoomId} is claimed by an ingest node, skipping local recorder",
                                room.RoomId);
                            return;
                        }

                        _logger.LogInformation("Auto-restarting recorder for live room {RoomId}", room.RoomId);
                        try
                        {
                            if (!_pm.HasRecorder(room.RoomId))
                            {
                                await _pm.StartRecorder(room.RoomId, null);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to auto-restart recorder for room {RoomId}", room.RoomId);
                        }
                    }
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
