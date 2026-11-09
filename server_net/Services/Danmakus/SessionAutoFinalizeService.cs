using Danmu.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Services.Danmakus;

/// <summary>
/// 会话自动封盘。
///
/// 背景：`Session.EndTime == 0` 表示"这场还在录"。中心本机录制器由
/// <see cref="ProcessManager"/> 在录制结束时负责写 EndTime；但经由采集节点上报的会话
/// 没有等价的收敛路径 —— 节点断开时我们刻意不封盘（直播可能还在继续），
/// 于是必须有人负责判断"这场真的结束了"。
///
/// 判据（满足任一即封盘）：
/// 1. 直播已结束：主播下播（LiveStatusService 判定 LiveStatus != 1）
/// 2. 长时间无数据：超过 IdleTimeout 没有任何节点上报，且直播状态未知/已下播
///
/// 第 2 条是兜底：直播间可能页面显示下播但 B 站接口短暂异常，不能永远挂着。
/// </summary>
public class SessionAutoFinalizeService : BackgroundService
{
    /// <summary>多久检查一次。</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    /// <summary>无任何上报超过这个时长，且确认已下播，就封盘。</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SessionDbService _sessionDb;
    private readonly LiveStatusService _liveStatus;
    private readonly ILogger<SessionAutoFinalizeService> _logger;

    public SessionAutoFinalizeService(
        IServiceScopeFactory scopeFactory,
        SessionDbService sessionDb,
        LiveStatusService liveStatus,
        ILogger<SessionAutoFinalizeService> logger)
    {
        _scopeFactory = scopeFactory;
        _sessionDb = sessionDb;
        _liveStatus = liveStatus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动后等久一点，避开录制恢复的高峰
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FinalizeEndedSessionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Session auto-finalize failed");
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

    private async Task FinalizeEndedSessionsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();

        // 只处理"上报进来的、尚未封盘"的会话：本机录制器的会话由 ProcessManager 管
        var candidates = await db.Sessions
            .Where(s => s.EndTime == 0
                        && s.NodeId != null
                        && s.FilePath != null
                        && s.FilePath.StartsWith("sqlite:"))
            .OrderBy(s => s.Id)
            .Take(50)
            .ToListAsync(ct);

        if (candidates.Count == 0) return;

        foreach (var session in candidates)
        {
            ct.ThrowIfCancellationRequested();

            long roomId = 0;
            long.TryParse(session.RoomId, out roomId);
            if (roomId <= 0) continue;

            // 最近一条数据的写入时间：先从场次库里查（最准），查不到就跳过本轮
            var relative = session.FilePath!.Substring("sqlite:".Length);
            var lastTs = await TryGetLastTimestampAsync(relative);
            if (lastTs <= 0)
            {
                continue;
            }

            var idle = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastTs;
            var idleSpan = TimeSpan.FromMilliseconds(Math.Max(0, idle));
            if (idleSpan < IdleTimeout)
            {
                continue;
            }

            // 空闲够久：再确认一下直播状态，避免网络抖动把正在进行的场次关掉
            var state = await _liveStatus.GetCachedStatusAsync(roomId);
            var isLive = state?.LiveStatus == 1;
            if (isLive)
            {
                // 还在播但没数据 —— 可能是所有采集节点都掉线了，不该封盘
                continue;
            }

            session.EndTime = lastTs;
            await _sessionDb.CloseSessionAsync(session.Uid ?? "");
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Auto-finalized session {SessionId} (uid={Uid}, room={RoomId}), idle={Idle}, lastTs={LastTs}",
                session.Id, session.Uid, roomId, idleSpan, lastTs);
        }
    }

    private async Task<long> TryGetLastTimestampAsync(string relativePath)
    {
        try
        {
            var (messages, _) = await _sessionDb.GetPagedAsync(relativePath, 1, 1);
            return messages.Count > 0 ? messages[0].Timestamp : 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read last timestamp from {Path}", relativePath);
            return 0;
        }
    }
}
