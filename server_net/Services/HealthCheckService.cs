using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace Danmu.Server.Services;

public class HealthCheckService : BackgroundService
{
    private readonly ILogger<HealthCheckService> _logger;
    private readonly ProcessManager _processManager;
    private readonly RedisService _redis;
    private readonly AlertService _alertService;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _heartbeatTolerance = TimeSpan.FromSeconds(25);
    private volatile HealthCheckReport _latestReport = HealthCheckReport.Empty;
    private string? _lastAlertSignature;

    public HealthCheckService(
        ILogger<HealthCheckService> logger,
        ProcessManager processManager,
        RedisService redis,
        AlertService alertService,
        IConfiguration configuration)
    {
        _logger = logger;
        _processManager = processManager;
        _redis = redis;
        _alertService = alertService;

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
            _latestReport = new HealthCheckReport
            {
                CheckedAt = DateTimeOffset.UtcNow,
                RecorderCount = 0,
                HealthyCount = 0,
                StaleHeartbeats = new List<HealthCheckIssue>(),
                DriftIssues = new List<HealthCheckIssue>()
            };
            _logger.LogDebug("Health check: no active recorders.");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var staleHeartbeats = new List<HealthCheckIssue>();
        var driftIssues = new List<HealthCheckIssue>();

        foreach (var process in processes)
        {
            try
            {
                var heartbeatKey = $"recorder:heartbeat:{process.Uid}:{process.RoomId}";
                var heartbeatRaw = await _redis.GetStringAsync(heartbeatKey);
                if (string.IsNullOrWhiteSpace(heartbeatRaw))
                {
                    staleHeartbeats.Add(new HealthCheckIssue(process.Uid, process.RoomId, "missing", null));
                    continue;
                }

                if (!long.TryParse(heartbeatRaw, out var timestampMs))
                {
                    staleHeartbeats.Add(new HealthCheckIssue(process.Uid, process.RoomId, "invalid", null));
                    continue;
                }

                var age = now - DateTimeOffset.FromUnixTimeMilliseconds(timestampMs);
                if (age > _heartbeatTolerance)
                {
                    staleHeartbeats.Add(new HealthCheckIssue(process.Uid, process.RoomId, "stale", age.TotalSeconds));
                }

                var liveStatus = await _redis.GetHashAsync($"live:status:{process.RoomId}");
                if (liveStatus.Count > 0
                    && liveStatus.TryGetValue("live_status", out var liveStatusText)
                    && int.TryParse(liveStatusText, out var cachedLiveStatus)
                    && cachedLiveStatus == 0
                    && process.Status == "online")
                {
                    driftIssues.Add(new HealthCheckIssue(process.Uid, process.RoomId, "live_status_drift", null));
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

        _latestReport = new HealthCheckReport
        {
            CheckedAt = now,
            RecorderCount = processes.Count,
            HealthyCount = processes.Count - staleHeartbeats.Count,
            StaleHeartbeats = staleHeartbeats,
            DriftIssues = driftIssues
        };

        if (staleHeartbeats.Count == 0)
        {
            _logger.LogInformation("Health check OK: {Count} recorder(s) healthy.", processes.Count);
            return;
        }

        _logger.LogWarning(
            "Health check found {Count} stale recorder heartbeat(s): {Details}",
            staleHeartbeats.Count,
            string.Join("; ", staleHeartbeats.Select(x => $"uid={x.Uid},room={x.RoomId},reason={x.Reason}{(x.AgeSeconds.HasValue ? $",age={x.AgeSeconds:F1}s" : string.Empty)}")));

        await SendAlertIfChangedAsync(_latestReport);
    }

    private async Task SendAlertIfChangedAsync(HealthCheckReport report)
    {
        if (!_alertService.IsEnabled)
        {
            return;
        }

        var signature = JsonSerializer.Serialize(new
        {
            stale = report.StaleHeartbeats.Select(x => new { x.Uid, x.RoomId, x.Reason }).OrderBy(x => x.Uid).ThenBy(x => x.RoomId).ThenBy(x => x.Reason),
            drift = report.DriftIssues.Select(x => new { x.Uid, x.RoomId, x.Reason }).OrderBy(x => x.Uid).ThenBy(x => x.RoomId).ThenBy(x => x.Reason)
        });

        if (signature == _lastAlertSignature)
        {
            return;
        }

        _lastAlertSignature = signature;
        await _alertService.SendHealthCheckAlertAsync(report);
    }

    public HealthCheckReport GetLatestReport() => _latestReport;
}

public record HealthCheckIssue(string Uid, long RoomId, string Reason, double? AgeSeconds);

public class HealthCheckReport
{
    public static readonly HealthCheckReport Empty = new()
    {
        CheckedAt = DateTimeOffset.MinValue,
        RecorderCount = 0,
        HealthyCount = 0,
        StaleHeartbeats = new List<HealthCheckIssue>(),
        DriftIssues = new List<HealthCheckIssue>()
    };

    public DateTimeOffset CheckedAt { get; set; }
    public int RecorderCount { get; set; }
    public int HealthyCount { get; set; }
    public List<HealthCheckIssue> StaleHeartbeats { get; set; } = new();
    public List<HealthCheckIssue> DriftIssues { get; set; } = new();
}
