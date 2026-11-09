using Danmu.Server.Filters;
using Danmu.Server.Services;
using Danmu.Server.Services.Danmakus;
using Microsoft.AspNetCore.Mvc;

namespace Danmu.Server.Controllers;

/// <summary>
/// danmakus-client 的主播状态接口兼容层。
///
/// 客户端每 30s POST 一次 <c>{roomIds:[...]}</c>（见 StreamerStatusManager.ts），
/// 拿回每个房间是否在播、开播时间。它自己也有一个"直接打 B 站 API"的兜底路径，
/// 但那会让每台采集机都各自消耗 B 站风控配额，而且不同机器看到的状态可能不一致。
///
/// 这里统一走中心的 <see cref="LiveStatusService"/> 缓存 —— 中心本来就有一个每 2 分钟
/// 轮询所有自动录制房间的后台服务，天然就是这个信息的唯一真源。
/// </summary>
[ApiController]
[Route("api/v2/streamer-status")]
[TypeFilter(typeof(IngestAuthAttribute))]
public class DanmakusStreamerStatusController : ControllerBase
{
    private readonly LiveStatusService _liveStatus;
    private readonly DanmakusRoomAllocator _allocator;
    private readonly ILogger<DanmakusStreamerStatusController> _logger;

    public DanmakusStreamerStatusController(
        LiveStatusService liveStatus,
        DanmakusRoomAllocator allocator,
        ILogger<DanmakusStreamerStatusController> logger)
    {
        _liveStatus = liveStatus;
        _allocator = allocator;
        _logger = logger;
    }

    public class StreamerStatusRequest
    {
        public List<long>? RoomIds { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] StreamerStatusRequest request, CancellationToken ct)
    {
        var roomIds = (request.RoomIds ?? new List<long>())
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToList();

        if (roomIds.Count == 0)
        {
            return Ok(Array.Empty<object>());
        }

        var results = new List<object>(roomIds.Count);
        foreach (var roomId in roomIds)
        {
            try
            {
                var state = await _allocator.ResolveLiveStateAsync(roomId, ct);
                if (state is null)
                {
                    // 查不到就返回"未知"（isLive=false）而不是报错，
                    // 客户端会保留上一轮缓存，不会误判成下播
                    results.Add(new { roomId, isLive = false });
                    continue;
                }

                results.Add(new
                {
                    roomId,
                    isLive = state.LiveStatus == 1,
                    liveStartTime = state.LiveStartTime
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to resolve streamer status for room {RoomId}", roomId);
                results.Add(new { roomId, isLive = false });
            }
        }

        return Ok(results);
    }
}
