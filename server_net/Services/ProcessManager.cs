using Danmu.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Services;

public class ProcessInfo
{
    public string Uid { get; set; } = "";
    public long RoomId { get; set; }
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Pid { get; set; }
    public string Status { get; set; } = "stopped";
    public string Uptime { get; set; } = "0s";
    public DateTime StartTime { get; set; }
    public int LiveStatus { get; set; }
    public long? LiveStartTime { get; set; }
    public long? AccountUid { get; set; }
}

public class ProcessManager
{
    private readonly Dictionary<string, BilibiliRecorder> _recorders = new(StringComparer.Ordinal);
    private readonly ILogger<ProcessManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RedisService _redis;
    private readonly BiliAccountService _accountService;

    public ProcessManager(ILogger<ProcessManager> logger, ILoggerFactory loggerFactory, IServiceScopeFactory scopeFactory, RedisService redis, BiliAccountService accountService)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _scopeFactory = scopeFactory;
        _redis = redis;
        _accountService = accountService;
    }

    public virtual List<ProcessInfo> GetProcesses()
    {
        var list = new List<ProcessInfo>();
        lock (_recorders)
        {
            foreach (var kvp in _recorders)
            {
                var recorder = kvp.Value;
                list.Add(new ProcessInfo
                {
                    Uid = kvp.Key,
                    RoomId = recorder.RoomId,
                    Name = recorder.ProcessName,
                    DisplayName = recorder.DisplayName,
                    Pid = recorder.Pid,
                    Status = recorder.Status,
                    Uptime = recorder.Uptime,
                    StartTime = recorder.StartTime,
                    LiveStatus = recorder.LiveStatus,
                    LiveStartTime = recorder.LiveStartTime,
                    AccountUid = _accountService.GetAssignedAccountUid(kvp.Key)
                });
            }
        }
        return list;
    }

    public virtual async Task StartRecorder(long roomId, string? name)
    {
        var identity = await ResolveRoomIdentityAsync(roomId, name);
        BilibiliRecorder? recorder;
        lock (_recorders)
        {
            if (_recorders.TryGetValue(identity.Uid, out var existing))
            {
                if (existing.Status == "online")
                {
                    _logger.LogInformation($"Recorder for uid {identity.Uid} is already running.");
                    return;
                }

                existing.Dispose();
                _recorders.Remove(identity.Uid);
            }

            var logger = _loggerFactory.CreateLogger<BilibiliRecorder>();
            recorder = CreateRecorder(identity.RoomId, identity.Uid, identity.Name, logger);
            
            // Delegate: Check for active session in DB
            recorder.CheckActiveSession = async (uid, rid) =>
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<DanmakuService>();
                var session = await svc.GetActiveSessionAsync(uid, rid);
                if (session != null && !string.IsNullOrEmpty(session.FilePath) && session.FilePath.StartsWith("redis:"))
                {
                    return session.FilePath.Substring(6); // Remove "redis:" prefix
                }
                return null;
            };

            recorder.UpdateVupStats = async (uid, rid, time, followers, guardNum, videoCount) =>
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
                var room = await db.Rooms.FirstOrDefaultAsync(r => r.Uid == uid)
                    ?? await db.Rooms.FirstOrDefaultAsync(r => r.RoomId == rid);
                if (room != null)
                {
                    bool changed = false;
                    if (rid > 0 && room.RoomId != rid)
                    {
                        room.RoomId = rid;
                        changed = true;
                    }
                    // Only update LastLiveTime if the new time is valid and significantly different or larger
                    if (time > 0 && Math.Abs(time - room.LastLiveTime) > 5000) 
                    { 
                        room.LastLiveTime = time; 
                        changed = true; 
                    }
                    if (followers > 0) { room.Followers = followers; changed = true; }
                    if (guardNum > 0) { room.GuardNum = guardNum; changed = true; }
                    if (videoCount > 0) { room.VideoCount = videoCount; changed = true; }
                    
                    if (changed)
                    {
                        room.UpdatedAt = DateTime.UtcNow.ToString("O");
                        await db.SaveChangesAsync();
                    }
                }
            };

            recorder.OnSessionStarted += async (uid, rid, title, uname, start, key) =>
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<DanmakuService>();
                await svc.CreateLiveSessionAsync(uid, rid, title, uname, start, key);
            };

            recorder.OnTitleChanged += async (uid, rid, title) =>
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<DanmakuService>();
                await svc.UpdateLiveSessionTitleAsync(uid, rid, title);
            };
            
            recorder.OnSessionEnded += async (uid, rid, endTime, finalPath) =>
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<DanmakuService>();
                await svc.CloseSessionAsync(uid, rid, endTime, finalPath);
            };

            recorder.OnRecorderStopped += async (uid, rid, reason) =>
            {
                await RemoveRecorderAsync(uid, rid, recorder, reason);
            };

            _recorders[identity.Uid] = recorder;
        }

        using (var scope = _scopeFactory.CreateScope())
        {
            var bilibili = scope.ServiceProvider.GetRequiredService<BilibiliService>();
            var (token, host, realRoomId) = await bilibili.GetDanmakuConfAsync(identity.RoomId);
            await recorder.StartAsync(token, host, bilibili, realRoomId);
        }
    }

    public virtual async Task StopRecorder(long roomId)
    {
        var uid = await ResolveUidAsync(roomId);
        if (string.IsNullOrWhiteSpace(uid))
        {
            return;
        }

        await StopRecorderByUidAsync(uid);
    }

    private async Task StopRecorderByUidAsync(string uid)
    {
        BilibiliRecorder? recorder = null;
        lock (_recorders)
        {
            if (_recorders.TryGetValue(uid, out var r))
            {
                recorder = r;
            }
        }

        if (recorder != null)
        {
            await recorder.StopAsync();
            await RemoveRecorderAsync(uid, recorder.RoomId, recorder, "manual-stop");
        }
    }

    private async Task RemoveRecorderAsync(string uid, long roomId, BilibiliRecorder recorder, string reason)
    {
        var removed = false;
        lock (_recorders)
        {
            if (_recorders.TryGetValue(uid, out var current) && ReferenceEquals(current, recorder))
            {
                _recorders.Remove(uid);
                removed = true;
            }
        }

        if (!removed) return;

        await _accountService.ReleaseRoomAssignmentAsync(uid);
        _logger.LogInformation("Stopped recorder for uid {Uid}, room {RoomId}. Reason: {Reason}", uid, roomId, reason);
        recorder.Dispose();
    }

    public virtual async Task RestoreRecordersAsync()
    {
        _logger.LogInformation("Restoring recorders...");
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        
        var rooms = await db.Rooms.Where(r => r.AutoRecord == 1).ToListAsync();
        foreach (var room in rooms)
        {
            try 
            {
                var biliService = scope.ServiceProvider.GetRequiredService<BilibiliService>();
                var danmakuService = scope.ServiceProvider.GetRequiredService<DanmakuService>();
                var liveState = await biliService.GetRoomStatusByRoomIdAsync(room.RoomId);
                var isLive = liveState?.LiveStatus == 1;
                await danmakuService.ReconcileTmpFilesAsync(room.Uid ?? room.RoomId.ToString(), room.RoomId, liveState?.LiveStartTime, isLive);

                if (!isLive)
                {
                    _logger.LogInformation("Skipping recorder restore for offline room {RoomName} (Uid: {Uid}, RoomId: {RoomId}) after tmp reconciliation.", room.Name, room.Uid, room.RoomId);
                    continue;
                }

                _logger.LogInformation($"Restoring recorder for {room.Name} (Uid: {room.Uid}, RoomId: {room.RoomId})...");
                await StartRecorder(room.RoomId, room.Name ?? room.RoomId.ToString());
                // Add a delay between starting each recorder to avoid Bilibili rate limit (412)
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to restore recorder for {room.Name}");
            }
        }
    }

    private async Task<(string Uid, long RoomId, string Name)> ResolveRoomIdentityAsync(long roomId, string? fallbackName)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.RoomId == roomId);

        var uid = room?.Uid;
        if (string.IsNullOrWhiteSpace(uid))
        {
            uid = roomId.ToString();
            _logger.LogWarning("Room {RoomId} has no uid, falling back to roomId as recorder identity.", roomId);
        }

        var name = room?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = fallbackName;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = roomId.ToString();
        }

        var resolvedRoomId = room?.RoomId ?? roomId;
        return (uid, resolvedRoomId, name);
    }

    protected virtual BilibiliRecorder CreateRecorder(long roomId, string uid, string name, ILogger logger)
    {
        return new BilibiliRecorder(roomId, uid, name, logger, _redis, _accountService);
    }

    private async Task<string?> ResolveUidAsync(long roomId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.RoomId == roomId);
        return room?.Uid;
    }
}
