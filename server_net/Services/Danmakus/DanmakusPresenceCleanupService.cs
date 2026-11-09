namespace Danmu.Server.Services.Danmakus;

/// <summary>
/// 周期性清理掉线的采集节点。
///
/// WS 正常关闭时会走 <see cref="DanmakusRuntimeEndpoint"/> 的 finally 分支释放房间，
/// 但进程被 kill、断网、机器休眠时 WebSocket 可能长时间不触发关闭回调，
/// 它持有的房间就会一直挂在名下，导致别的节点分不到 —— 必须有超时兜底。
/// </summary>
public class DanmakusPresenceCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly DanmakusRuntimeRegistry _registry;
    private readonly ILogger<DanmakusPresenceCleanupService> _logger;

    public DanmakusPresenceCleanupService(
        DanmakusRuntimeRegistry registry,
        ILogger<DanmakusPresenceCleanupService> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动后先等一会儿，别和启动期的房间恢复抢资源
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _registry.PruneStale();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Danmakus presence cleanup failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
