using Microsoft.Extensions.Configuration;

namespace Danmu.Server.Services;

public class HealthCheckService : BackgroundService
{
    private readonly ILogger<HealthCheckService> _logger;
    private readonly ProcessManager _processManager;
    private readonly RedisService _redis;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _heartbeatTolerance = TimeSpan.FromSeconds(25);

    public HealthCheckService(
        ILogger<HealthCheckService> logger,
        ProcessManager processManager,
        RedisService redis,
        IConfiguration configuration)
    {
        _logger = logger;
        _processManager = processManager;
        _redis = redis;

        var intervalSeconds = configuration.GetValue<int?>("HealthCheck:IntervalSeconds") ?? 30;
        _interval = TimeSpan.FromSeconds(Math.Max(10, intervalSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunHealthCheckAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check sweep failed");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task RunHealthCheckAsync()
    {
        var processes = _processManager.GetProcesses();
        if (processes.Count == 0)
        {
            _logger.LogDebug("Health check: no active recorders.");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var staleHeartbeats = new List<string>();

        foreach (var process in processes)
        {
            try
            {
                var heartbeatKey = $"recorder:heartbeat:{process.Uid}:{process.RoomId}";
                var heartbeatRaw = await _redis.GetStringAsync(heartbeatKey);
                if (string.IsNullOrWhiteSpace(heartbeatRaw))
                {
                    staleHeartbeats.Add($"uid={process.Uid},room={process.RoomId},reason=missing");
                    continue;
                }

                if (!long.TryParse(heartbeatRaw, out var timestampMs))
                {
                    staleHeartbeats.Add($"uid={process.Uid},room={process.RoomId},reason=invalid");
                    continue;
                }

                var age = now - DateTimeOffset.FromUnixTimeMilliseconds(timestampMs);
                if (age > _heartbeatTolerance)
                {
                    staleHeartbeats.Add($"uid={process.Uid},room={process.RoomId},age={age.TotalSeconds:F1}s");
                }

                var liveStatus = await _redis.GetHashAsync($"live:status:{process.RoomId}");
                if (liveStatus.Count > 0
                    && liveStatus.TryGetValue("live_status", out var liveStatusText)
                    && int.TryParse(liveStatusText, out var cachedLiveStatus)
                    && cachedLiveStatus == 0
                    && process.Status == "online")
                {
                    _logger.LogWarning(
                        "Health check detected recorder/live-status drift: recorder online but cached live status is offline. uid={Uid}, room={RoomId}",
                        process.Uid,
                        process.RoomId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Health check failed for uid={Uid}, room={RoomId}", process.Uid, process.RoomId);
            }
        }

        if (staleHeartbeats.Count == 0)
        {
            _logger.LogInformation("Health check OK: {Count} recorder(s) healthy.", processes.Count);
            return;
        }

        _logger.LogWarning(
            "Health check found {Count} stale recorder heartbeat(s): {Details}",
            staleHeartbeats.Count,
            string.Join("; ", staleHeartbeats));
    }
}
