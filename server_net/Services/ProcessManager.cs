using Danmu.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Services;

public class ProcessInfo
{
    public string Name { get; set; } = "";
    public int Pid { get; set; }
    public string Status { get; set; } = "stopped";
    public string Uptime { get; set; } = "0s";
    public DateTime StartTime { get; set; }
}

public class ProcessManager
{
    private readonly Dictionary<string, BilibiliRecorder> _recorders = new();
    private readonly ILogger<ProcessManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RedisService _redis;

    public ProcessManager(ILogger<ProcessManager> logger, ILoggerFactory loggerFactory, IServiceScopeFactory scopeFactory, RedisService redis)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _scopeFactory = scopeFactory;
        _redis = redis;
    }

    public List<ProcessInfo> GetProcesses()
    {
        var list = new List<ProcessInfo>();
        lock (_recorders)
        {
            foreach (var kvp in _recorders)
            {
                var recorder = kvp.Value;
                list.Add(new ProcessInfo
                {
                    Name = kvp.Key,
                    Pid = recorder.Pid,
                    Status = recorder.Status,
                    Uptime = recorder.Uptime,
                    StartTime = recorder.StartTime
                });
            }
        }
        return list;
    }

    public async Task StartRecorder(long roomId, string? name)
    {
        string processName = $"danmu-{name}";
        if (string.IsNullOrEmpty(name)) processName = $"danmu-{roomId}";

        BilibiliRecorder? recorder;
        lock (_recorders)
        {
            if (_recorders.TryGetValue(processName, out var existing))
            {
                if (existing.Status == "online")
                {
                    _logger.LogInformation($"Recorder {processName} is already running.");
                    return;
                }
                // If stopped/errored, dispose and remove
                existing.Dispose();
                _recorders.Remove(processName);
            }

            var logger = _loggerFactory.CreateLogger<BilibiliRecorder>();
            recorder = new BilibiliRecorder(roomId, name, logger, _redis);
            
            // Delegate: Check for active session in DB
            recorder.CheckActiveSession = async (rid) =>
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<DanmakuService>();
                var session = await svc.GetActiveSessionAsync(rid);
                if (session != null && !string.IsNullOrEmpty(session.FilePath) && session.FilePath.StartsWith("redis:"))
                {
                    return session.FilePath.Substring(6); // Remove "redis:" prefix
                }
                return null;
            };

            recorder.OnSessionStarted += async (rid, title, uname, start, key) =>
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<DanmakuService>();
                await svc.CreateLiveSessionAsync(rid, title, uname, start, key);
            };
            
            recorder.OnSessionEnded += async (rid, endTime, finalPath) =>
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<DanmakuService>();
                await svc.CloseSessionAsync(rid, endTime, finalPath);
            };

            _recorders[processName] = recorder;
        }

        using (var scope = _scopeFactory.CreateScope())
        {
            var bilibili = scope.ServiceProvider.GetRequiredService<BilibiliService>();
            var (token, host, realRoomId) = await bilibili.GetDanmakuConfAsync(roomId);
            await recorder.StartAsync(token, host, bilibili, realRoomId);
        }
    }

    public async Task StopRecorder(string processName)
    {
        BilibiliRecorder? recorder = null;
        lock (_recorders)
        {
            if (_recorders.TryGetValue(processName, out var r))
            {
                recorder = r;
            }
        }

        if (recorder != null)
        {
            await recorder.StopAsync();
            lock (_recorders)
            {
                _recorders.Remove(processName);
            }
            recorder.Dispose();
        }
    }

    public async Task RestoreRecordersAsync()
    {
        _logger.LogInformation("Restoring recorders...");
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        
        var rooms = await db.Rooms.Where(r => r.AutoRecord == 1).ToListAsync();
        foreach (var room in rooms)
        {
            try 
            {
                await StartRecorder(room.RoomId, room.Name ?? room.RoomId.ToString());
                // Add a larger delay between starting each recorder to avoid Bilibili rate limit (412)
                await Task.Delay(TimeSpan.FromSeconds(30));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,($"Failed to restore recorder for {room.Name}"));
            }
        }
    }
}
