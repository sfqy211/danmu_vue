using Danmu.Server.Data;
using Danmu.Server.Models;
using Danmu.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Danmu.Server.Controllers;

[ApiController]
[Route("api")]
public class DanmakuController : ControllerBase
{
    private readonly DanmuContext _db;
    private readonly DanmakuService _service;
    private readonly ProcessManager _pm;
    private readonly ILogger<DanmakuController> _logger;

    public DanmakuController(DanmuContext db, DanmakuService service, ProcessManager pm, ILogger<DanmakuController> logger)
    {
        _db = db;
        _service = service;
        _pm = pm;
        _logger = logger;
    }

    [HttpGet("pm2-status")]
    public IActionResult GetProcessStatus()
    {
        var processes = _pm.GetProcesses();
        var hasError = processes.Any(p => p.Status == "errored");

        return Ok(new
        {
            status = hasError ? "error" : "success",
            processes = processes.Select(p => new
            {
                name = p.Name,
                status = p.Status,
                id = p.Pid
            })
        });
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions([FromQuery] string? userName, [FromQuery] string? roomId, [FromQuery] long? startTime, [FromQuery] long? endTime)
    {
        // Cache Control
        Response.Headers["Cache-Control"] = "public, max-age=60, s-maxage=60";

        var query = _db.Sessions.AsQueryable();

        if (!string.IsNullOrEmpty(userName)) query = query.Where(s => s.UserName == userName);
        if (!string.IsNullOrEmpty(roomId)) query = query.Where(s => s.RoomId == roomId);
        if (startTime.HasValue) query = query.Where(s => s.StartTime >= startTime);
        if (endTime.HasValue) query = query.Where(s => s.EndTime <= endTime);

        var sessions = await query.OrderByDescending(s => s.StartTime)
            .Select(s => new { s.Id, s.RoomId, s.Title, s.UserName, s.StartTime, s.EndTime })
            .ToListAsync();

        return Ok(sessions);
    }

    [HttpGet("sessions/total")]
    public async Task<IActionResult> GetSessionsTotal([FromQuery] string? userName, [FromQuery] string? roomId)
    {
        var query = _db.Sessions.AsQueryable();
        if (!string.IsNullOrEmpty(userName)) query = query.Where(s => s.UserName == userName);
        if (!string.IsNullOrEmpty(roomId)) query = query.Where(s => s.RoomId == roomId);
        
        var count = await query.CountAsync();
        return Ok(new { total = count });
    }

    [HttpGet("streamers")]
    public async Task<IActionResult> GetStreamers()
    {
        var streamers = await _db.Sessions
            .Where(s => !string.IsNullOrEmpty(s.UserName))
            .GroupBy(s => s.UserName)
            .Select(g => new { user_name = g.Key, room_id = g.Max(s => s.RoomId) })
            .OrderBy(x => x.user_name)
            .ToListAsync();

        return Ok(streamers);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] int id)
    {
        var session = await _db.Sessions.FindAsync(id);
        if (session == null) return NotFound(new { error = "未找到该录制分析" });
        return Ok(session);
    }

    [HttpGet("danmaku")]
    public async Task<IActionResult> GetDanmaku([FromQuery] int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 200)
    {
        try
        {
            var result = await _service.GetDanmakuPagedAsync(id, page, pageSize);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting danmaku");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("song-requests")]
    public async Task<IActionResult> GetSongRequests([FromQuery] string? roomId, [FromQuery] int? id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = "")
    {
        var query = _db.SongRequests.AsQueryable();

        if (!string.IsNullOrEmpty(roomId))
        {
            query = query.Where(r => r.RoomId == roomId);
        }
        else if (id.HasValue)
        {
            query = query.Where(r => r.SessionId == id);
        }
        else
        {
            return BadRequest(new { error = "无效的 ID 或 Room ID" });
        }

        if (!string.IsNullOrEmpty(search))
        {
            search = search.ToLower();
            query = query.Where(r => 
                (r.SongName != null && r.SongName.ToLower().Contains(search)) || 
                (r.UserName != null && r.UserName.ToLower().Contains(search)) ||
                (r.Singer != null && r.Singer.ToLower().Contains(search)));
        }

        var total = await query.CountAsync();
        
        // Include session info if querying by room_id
        // But EF Core join is complex with dynamic result.
        // For simplicity, I'll fetch SongRequests and if needed I could fetch Session info.
        // The Node version does a LEFT JOIN for room_id query.
        
        object list;

        if (!string.IsNullOrEmpty(roomId))
        {
             var rawList = await query.OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Join(_db.Sessions, 
                      sr => sr.SessionId, 
                      s => s.Id, 
                      (sr, s) => new { 
                          sr.Id, sr.UserName, sr.Uid, sr.SongName, sr.Singer, sr.CreatedAt, 
                          session_title = s.Title, 
                          session_start_time = s.StartTime 
                      })
                .ToListAsync();
             list = rawList;
        }
        else
        {
             var rawList = await query.OrderBy(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(sr => new { sr.Id, sr.UserName, sr.Uid, sr.SongName, sr.Singer, sr.CreatedAt })
                .ToListAsync();
             list = rawList;
        }

        return Ok(new
        {
            list,
            total,
            page,
            pageSize
        });
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] JsonElement body)
    {
        if (!body.TryGetProperty("id", out var idProp)) return BadRequest(new { error = "Missing session ID" });
        
        // Mock
        await Task.Delay(2000);

        var mockAnalysis = @"
# 弹幕情感分析报告 (模拟数据)

## 核心观点
本次直播观众情绪高涨，主要集中在以下几个话题：
1. 对主播操作的赞赏 (60%)
2. 玩梗互动 (30%)
3. 其他讨论 (10%)

## 热门关键词
- 666
- 哈哈哈哈
- 强啊
- 这种事情见多了

*注：此功能为接口测试，尚未接入真实 AI 模型。*
";
        return Ok(new { analysis = mockAnalysis });
    }
}
