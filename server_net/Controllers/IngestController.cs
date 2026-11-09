using Danmu.Server.Data;
using Danmu.Server.Filters;
using Danmu.Server.Models;
using Danmu.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Controllers;

/// <summary>
/// 采集端上报接口。仅接受持有有效节点 Token 的客户端，
/// 与后台管理接口（/api/admin）完全分离。
/// </summary>
[ApiController]
[Route("api/ingest")]
[TypeFilter(typeof(IngestAuthAttribute))]
public class IngestController : ControllerBase
{
    private readonly IngestService _ingest;
    private readonly DanmuContext _db;
    private readonly ILogger<IngestController> _logger;

    public IngestController(IngestService ingest, DanmuContext db, ILogger<IngestController> logger)
    {
        _ingest = ingest;
        _db = db;
        _logger = logger;
    }

    private string CurrentNodeId => HttpContext.Items[IngestAuthAttribute.NodeIdItemKey] as string ?? "";

    /// <summary>采集端开播时注册会话，返回中心侧统一的 sessionId。</summary>
    [HttpPost("session/start")]
    public async Task<IActionResult> StartSession([FromBody] IngestSessionStartRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Uid))
        {
            return BadRequest(new { error = "uid is required" });
        }

        var sessionId = await _ingest.StartSessionAsync(req, CurrentNodeId);
        return Ok(new { sessionId, serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
    }

    /// <summary>批量上报事件。重复 message_id 会被静默丢弃（返回 accepted 表示实际新增条数）。</summary>
    [HttpPost("session/events")]
    public async Task<IActionResult> AppendEvents([FromBody] IngestEventsRequest req)
    {
        if (req.SessionId <= 0 || req.Events == null || req.Events.Count == 0)
        {
            return BadRequest(new { error = "sessionId and events are required" });
        }

        if (req.Events.Count > 2000)
        {
            return BadRequest(new { error = "batch too large (max 2000)" });
        }

        try
        {
            var result = await _ingest.AppendEventsAsync(req.SessionId, CurrentNodeId, req.Events);
            return Ok(new { received = result.Received, accepted = result.Accepted });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Ingest append failed for session {SessionId}", req.SessionId);
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>心跳，同时刷新节点在线状态。</summary>
    [HttpPost("session/heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] IngestHeartbeatRequest req)
    {
        if (req.SessionId <= 0) return BadRequest(new { error = "sessionId is required" });
        await _ingest.HeartbeatAsync(req.SessionId, CurrentNodeId, req.TotalEvents);
        return Ok(new { ok = true });
    }

    /// <summary>采集端结束上报。所有节点都结束后会话封盘。</summary>
    [HttpPost("session/end")]
    public async Task<IActionResult> EndSession([FromBody] IngestEndRequest req)
    {
        if (req.SessionId <= 0) return BadRequest(new { error = "sessionId is required" });
        await _ingest.EndSessionAsync(req.SessionId, CurrentNodeId, req.EndTime);
        return Ok(new { ok = true });
    }

    /// <summary>当前活跃上报会话（管理端可用来观察各节点状态）。</summary>
    [HttpGet("uploads")]
    public IActionResult GetUploads() => Ok(_ingest.GetActiveUploads());

    /// <summary>本节点自检：确认 Token 有效并返回节点信息。</summary>
    [HttpGet("whoami")]
    public async Task<IActionResult> WhoAmI()
    {
        var nodeId = CurrentNodeId;
        var node = await _db.IngestNodes.AsNoTracking().FirstOrDefaultAsync(n => n.NodeId == nodeId);
        return Ok(new
        {
            nodeId,
            name = node?.Name,
            totalEvents = node?.TotalEvents ?? 0,
            serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }
}

public class IngestEventsRequest
{
    public int SessionId { get; set; }
    public List<RecordedDanmakuEvent>? Events { get; set; }
}

public class IngestHeartbeatRequest
{
    public int SessionId { get; set; }
    public long? TotalEvents { get; set; }
}

public class IngestEndRequest
{
    public int SessionId { get; set; }
    public long? EndTime { get; set; }
}
