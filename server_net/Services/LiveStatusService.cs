using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Services;

public class LiveStatusService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<LiveStatusService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProcessManager _pm;

    // Cache: room_id → (liveStatus, liveStartTime, updatedAt)
    private readonly ConcurrentDictionary<long, LiveState> _cache = new();
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(2);

    public LiveStatusService(IServiceProvider services, ILogger<LiveStatusService> logger,
        IHttpClientFactory httpClientFactory, ProcessManager pm)
    {
        _services = services;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _pm = pm;
    }

    public LiveState? GetCachedStatus(long roomId)
    {
        if (_cache.TryGetValue(roomId, out var state))
        {
            // Cache valid for 3 minutes
            if (DateTime.UtcNow - state.UpdatedAt < TimeSpan.FromMinutes(3))
                return state;
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

        // Batch parallel requests (max 10 concurrent)
        var semaphore = new SemaphoreSlim(10);
        var tasks = roomsToCheck.Select(async room =>
        {
            await semaphore.WaitAsync(token);
            try
            {
                var state = await FetchRoomStatusAsync(room.RoomId, token);
                if (state != null)
                    _cache[room.RoomId] = state;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to fetch live status for room {RoomId}", room.RoomId);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);
    }

    private async Task<LiveState?> FetchRoomStatusAsync(long roomId, CancellationToken token)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        // Use room_init as a lightweight endpoint (no cookie needed)
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.live.bilibili.com/room/v1/Room/room_init?id={roomId}");
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        request.Headers.TryAddWithoutValidation("Referer", $"https://live.bilibili.com/{roomId}");

        var response = await client.SendAsync(request, token);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(token);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("code", out var code) || code.GetInt32() != 0)
            return null;

        if (!root.TryGetProperty("data", out var data))
            return null;

        int liveStatus = 0;
        if (data.TryGetProperty("live_status", out var ls))
            liveStatus = ls.GetInt32();

        long? liveStartTime = null;
        if (data.TryGetProperty("live_time", out var lt))
        {
            if (lt.ValueKind == JsonValueKind.Number)
                liveStartTime = lt.GetInt64();
            else if (lt.ValueKind == JsonValueKind.String && long.TryParse(lt.GetString(), out var parsed))
                liveStartTime = parsed;
        }

        if (liveStartTime.HasValue && liveStartTime.Value > 0 && liveStartTime.Value < 1_000_000_000_000)
            liveStartTime = liveStartTime.Value * 1000;

        return new LiveState
        {
            RoomId = roomId,
            LiveStatus = liveStatus,
            LiveStartTime = liveStartTime,
            UpdatedAt = DateTime.UtcNow
        };
    }
}

public class LiveState
{
    public long RoomId { get; set; }
    public int LiveStatus { get; set; }
    public long? LiveStartTime { get; set; }
    public DateTime UpdatedAt { get; set; }
}
