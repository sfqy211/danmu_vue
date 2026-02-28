using Danmu.Server.Data;
using Danmu.Server.Filters;
using Danmu.Server.Models;
using Danmu.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Danmu.Server.Controllers;

[ApiController]
[Route("api/admin")]
[TypeFilter(typeof(AdminAuthAttribute))]
public class AdminController : ControllerBase
{
    private readonly DanmuContext _db;
    private readonly ProcessManager _pm;
    private readonly ILogger<AdminController> _logger;
    private readonly BilibiliService _bilibili;
    private readonly string _danmakuDir;

    public AdminController(DanmuContext db, ProcessManager pm, BilibiliService bilibili, ILogger<AdminController> logger)
    {
        _db = db;
        _pm = pm;
        _bilibili = bilibili;
        _logger = logger;
        _danmakuDir = Environment.GetEnvironmentVariable("DANMAKU_DIR")
                      ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../server/data/danmaku"));
    }

    [HttpGet("rooms")]
    public async Task<IActionResult> GetRooms()
    {
        var rooms = await _db.Rooms.ToListAsync();
        var processes = _pm.GetProcesses();

        var result = rooms.Select(room =>
        {
            var procName = string.IsNullOrEmpty(room.Name) ? $"danmu-{room.RoomId}" : $"danmu-{room.Name}";
            var proc = processes.FirstOrDefault(p => p.Name == procName);

            return new
            {
                id = room.Id,
                room_id = room.RoomId,
                name = room.Name,
                uid = room.Uid,
                is_active = room.IsActive,
                auto_record = room.AutoRecord,
                process_status = proc?.Status ?? "stopped",
                process_uptime = proc?.Uptime ?? "0s",
                live_status = 0, // Removed redundant live status fetch to avoid rate limit
                live_start_time = room.LastLiveTime,
                pid = proc?.Pid,
                remark = room.Remark
            };
        });

        return Ok(result);
    }

    [HttpPost("rooms")]
    public async Task<IActionResult> AddRoom([FromBody] RoomDto dto)
    {
        _logger.LogInformation($"AddRoom called: Uid={dto.Uid}, Remark={dto.Remark}");
        try
        {
            long roomId = 0;
            string name = "";
            string uidStr = dto.Uid ?? string.Empty;
            long uid = 0;

            if (string.IsNullOrEmpty(uidStr) || !long.TryParse(uidStr, out uid) || uid <= 0)
            {
                return BadRequest(new { error = "必须提供有效的 UID" });
            }

            var (fetchedRoomId, fetchedName) = await _bilibili.GetRoomInfoByUidAsync(uid);
            if (fetchedRoomId > 0)
            {
                roomId = fetchedRoomId;
                name = fetchedName;
                _logger.LogInformation($"Resolved room from UID {uid}: RoomId={roomId}, Name={name}");
            }
            else
            {
                return BadRequest(new { error = $"无法找到 UID {uid} 对应的直播间" });
            }

            if (string.IsNullOrEmpty(name)) name = "Unknown";

            // Resolve real room ID if it's a short ID (double check)
            var realRoomId = await _bilibili.GetRealRoomIdAsync(roomId);
            if (realRoomId <= 0) realRoomId = roomId;

            var existing = await _db.Rooms.FirstOrDefaultAsync(r => r.RoomId == realRoomId);
            if (existing != null)
            {
                return BadRequest(new { error = "主播已存在 (真实房间号: " + realRoomId + ")" });
            }

            var room = new Room
            {
                RoomId = realRoomId,
                Name = name,
                Uid = uid.ToString(),
                Remark = dto.Remark,
                IsActive = 1,
                AutoRecord = 1
            };

            _db.Rooms.Add(room);
            await _db.SaveChangesAsync();

            // Start Process
            await _pm.StartRecorder(room.RoomId, room.Name);

            return Ok(new { success = true, realRoomId = realRoomId, name = name });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add room or start recorder");
            return StatusCode(500, new { error = ex.Message, details = ex.ToString() });
        }
    }

    [HttpDelete("rooms/{id}")]
    public async Task<IActionResult> DeleteRoom(int id)
    {
        var room = await _db.Rooms.FindAsync(id);
        if (room == null) return NotFound(new { error = "Room not found" });

        // Stop process first
        var procName = string.IsNullOrEmpty(room.Name) ? $"danmu-{room.RoomId}" : $"danmu-{room.Name}";
        await _pm.StopRecorder(procName);

        _db.Rooms.Remove(room);
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    [HttpPut("rooms/{id}")]
    public async Task<IActionResult> UpdateRoom(int id, [FromBody] RoomDto dto)
    {
        var room = await _db.Rooms.FindAsync(id);
        if (room == null) return NotFound(new { error = "Room not found" });

        if (!string.IsNullOrEmpty(dto.Remark))
        {
            room.Remark = dto.Remark;
        }

        // If Name or Uid update is needed, handle it here.
        // For now, only Remark is explicitly requested.

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("process/start")]
    public async Task<IActionResult> StartProcess([FromBody] JsonElement body)
    {
        if (body.TryGetProperty("roomId", out var idProp))
        {
            long roomId = 0;
            if (idProp.ValueKind == JsonValueKind.Number) roomId = idProp.GetInt64();
            else if (idProp.ValueKind == JsonValueKind.String) long.TryParse(idProp.GetString(), out roomId);
            
            var room = await _db.Rooms.FirstOrDefaultAsync(r => r.RoomId == roomId);
            if (room == null) return NotFound(new { error = "Room not found" });

            await _pm.StartRecorder(room.RoomId, room.Name);
            return Ok(new { success = true });
        }
        return BadRequest(new { error = "Missing roomId" });
    }

    [HttpPost("process/stop")]
    public async Task<IActionResult> StopProcess([FromBody] JsonElement body)
    {
        if (body.TryGetProperty("roomId", out var idProp))
        {
            long roomId = 0;
            if (idProp.ValueKind == JsonValueKind.Number) roomId = idProp.GetInt64();
            else if (idProp.ValueKind == JsonValueKind.String) long.TryParse(idProp.GetString(), out roomId);

            var room = await _db.Rooms.FirstOrDefaultAsync(r => r.RoomId == roomId);
            if (room == null) return NotFound(new { error = "Room not found" });

            var procName = string.IsNullOrEmpty(room.Name) ? $"danmu-{room.RoomId}" : $"danmu-{room.Name}";
            await _pm.StopRecorder(procName);
            return Ok(new { success = true });
        }
        return BadRequest(new { error = "Missing roomId" });
    }

    [HttpPost("rooms/{id}/restart")]
    public async Task<IActionResult> RestartRoom(int id)
    {
        var room = await _db.Rooms.FindAsync(id);
        if (room == null) return NotFound(new { error = "Room not found" });

        var procName = string.IsNullOrEmpty(room.Name) ? $"danmu-{room.RoomId}" : $"danmu-{room.Name}";
        
        // Stop
        await _pm.StopRecorder(procName);
        
        // Wait a bit
        await Task.Delay(1000); 
        
        // Start
        try
        {
            await _pm.StartRecorder(room.RoomId, room.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart recorder");
            return StatusCode(500, new { error = ex.Message });
        }
        
        return Ok(new { success = true });
    }

    [HttpPost("rooms/{id}/toggle-monitor")]
    public async Task<IActionResult> ToggleMonitor(int id, [FromBody] JsonElement body)
    {
        var room = await _db.Rooms.FindAsync(id);
        if (room == null) return NotFound(new { error = "Room not found" });

        if (body.TryGetProperty("autoRecord", out var autoRecordProp))
        {
            room.AutoRecord = autoRecordProp.ValueKind == JsonValueKind.True ? 1 : 0;
            // Also keep isActive in sync if it's meant to be the master switch for PM2 control
            room.IsActive = room.AutoRecord;
            
            await _db.SaveChangesAsync();

            // If monitoring disabled, stop recorder
            if (room.AutoRecord == 0)
            {
                var procName = string.IsNullOrEmpty(room.Name) ? $"danmu-{room.RoomId}" : $"danmu-{room.Name}";
                await _pm.StopRecorder(procName);
            }
            else
            {
                // If monitoring enabled, try starting the recorder
                await _pm.StartRecorder(room.RoomId, room.Name);
            }

            return Ok(new { success = true, autoRecord = room.AutoRecord });
        }
        return BadRequest(new { error = "Missing autoRecord" });
    }

    [HttpPost("init-db")]
    public async Task<IActionResult> InitDb()
    {
        await _db.Database.EnsureCreatedAsync();
        return Ok(new { message = "数据库初始化成功" });
    }

    [HttpPost("scan")]
    public IActionResult Scan()
    {
        return Ok(new { message = "扫描任务已在后台启动 (Watcher active)" });
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = "", [FromQuery] string? userName = null, [FromQuery] string? roomId = null)
    {
        var query = _db.Sessions.AsQueryable();

        if (!string.IsNullOrEmpty(userName))
        {
            var lower = userName.ToLower();
            query = query.Where(s => s.UserName != null && s.UserName.ToLower().Contains(lower));
        }

        if (!string.IsNullOrEmpty(roomId))
        {
            query = query.Where(s => s.RoomId == roomId);
        }

        if (!string.IsNullOrEmpty(search))
        {
            var lower = search.ToLower();
            query = query.Where(s =>
                (s.Title != null && s.Title.ToLower().Contains(lower)) ||
                (s.UserName != null && s.UserName.ToLower().Contains(lower)) ||
                (s.RoomId != null && s.RoomId.ToLower().Contains(lower)));
        }

        var total = await query.CountAsync();

        var list = await query
            .OrderByDescending(s => s.StartTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                s.Id,
                s.RoomId,
                s.Title,
                s.UserName,
                s.StartTime,
                s.EndTime,
                s.FilePath
            })
            .ToListAsync();

        return Ok(new
        {
            list,
            total,
            page,
            pageSize
        });
    }

    [HttpPost("sessions")]
    public async Task<IActionResult> AddSession([FromBody] SessionDto dto)
    {
        if (dto == null) return BadRequest(new { error = "Invalid payload" });

        var session = new Session
        {
            RoomId = dto.RoomId,
            Title = dto.Title,
            UserName = dto.UserName,
            StartTime = dto.StartTime ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            EndTime = dto.EndTime,
            FilePath = dto.FilePath,
            SummaryJson = dto.SummaryJson,
            GiftSummaryJson = dto.GiftSummaryJson,
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        };

        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        return Ok(new { success = true, id = session.Id });
    }

    [HttpPut("sessions/{id}")]
    public async Task<IActionResult> UpdateSession(int id, [FromBody] SessionDto dto)
    {
        var session = await _db.Sessions.FindAsync(id);
        if (session == null) return NotFound(new { error = "Session not found" });

        session.RoomId = dto.RoomId ?? session.RoomId;
        session.Title = dto.Title ?? session.Title;
        session.UserName = dto.UserName ?? session.UserName;
        if (dto.StartTime.HasValue) session.StartTime = dto.StartTime;
        if (dto.EndTime.HasValue) session.EndTime = dto.EndTime;
        if (dto.FilePath != null) session.FilePath = dto.FilePath;
        if (dto.SummaryJson != null) session.SummaryJson = dto.SummaryJson;
        if (dto.GiftSummaryJson != null) session.GiftSummaryJson = dto.GiftSummaryJson;

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpDelete("sessions/{id}")]
    public async Task<IActionResult> DeleteSession(int id)
    {
        var session = await _db.Sessions.FindAsync(id);
        if (session == null) return NotFound(new { error = "Session not found" });

        var fullPath = ResolveSessionFilePath(session);

        var requests = _db.SongRequests.Where(r => r.SessionId == id);
        _db.SongRequests.RemoveRange(requests);
        _db.Sessions.Remove(session);
        await _db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(fullPath))
        {
            try
            {
                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to delete danmaku file: {fullPath}");
            }
        }

        return Ok(new { success = true });
    }

    private string? ResolveSessionFilePath(Session session)
    {
        if (session == null || string.IsNullOrEmpty(session.FilePath)) return null;

        var fullPath = Path.Combine(_danmakuDir, session.FilePath);
        if (System.IO.File.Exists(fullPath)) return fullPath;

        var basename = Path.GetFileName(session.FilePath);
        if (!string.IsNullOrEmpty(session.RoomId))
        {
            var roomPath = Path.Combine(_danmakuDir, session.RoomId, basename);
            if (System.IO.File.Exists(roomPath)) return roomPath;
        }

        var rootPath = Path.Combine(_danmakuDir, basename);
        if (System.IO.File.Exists(rootPath)) return rootPath;

        return fullPath;
    }

    [HttpGet("song-requests")]
    public async Task<IActionResult> GetSongRequests([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = "", [FromQuery] int? sessionId = null, [FromQuery] string? roomId = null, [FromQuery] string? userName = null)
    {
        var query = _db.SongRequests.AsQueryable();

        if (sessionId.HasValue) query = query.Where(r => r.SessionId == sessionId);
        if (!string.IsNullOrEmpty(roomId)) query = query.Where(r => r.RoomId == roomId);
        if (!string.IsNullOrEmpty(userName)) 
        {
            var lower = userName.ToLower();
            query = query.Where(r => r.UserName != null && r.UserName.ToLower().Contains(lower));
        }

        if (!string.IsNullOrEmpty(search))
        {
            var lower = search.ToLower();
            query = query.Where(r =>
                (r.SongName != null && r.SongName.ToLower().Contains(lower)) ||
                (r.UserName != null && r.UserName.ToLower().Contains(lower)) ||
                (r.Singer != null && r.Singer.ToLower().Contains(lower)));
        }

        var total = await query.CountAsync();

        var list = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                r.Id,
                r.SessionId,
                r.RoomId,
                r.UserName,
                r.Uid,
                r.SongName,
                r.Singer,
                r.CreatedAt
            })
            .ToListAsync();

        return Ok(new
        {
            list,
            total,
            page,
            pageSize
        });
    }

    [HttpPost("song-requests")]
    public async Task<IActionResult> AddSongRequest([FromBody] SongRequestDto dto)
    {
        if (dto == null) return BadRequest(new { error = "Invalid payload" });

        var request = new SongRequest
        {
            SessionId = dto.SessionId,
            RoomId = dto.RoomId,
            UserName = dto.UserName,
            Uid = dto.Uid,
            SongName = dto.SongName,
            Singer = dto.Singer,
            CreatedAt = dto.CreatedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        _db.SongRequests.Add(request);
        await _db.SaveChangesAsync();

        return Ok(new { success = true, id = request.Id });
    }

    [HttpPut("song-requests/{id}")]
    public async Task<IActionResult> UpdateSongRequest(int id, [FromBody] SongRequestDto dto)
    {
        var request = await _db.SongRequests.FindAsync(id);
        if (request == null) return NotFound(new { error = "Song request not found" });

        if (dto.SessionId.HasValue) request.SessionId = dto.SessionId;
        if (dto.RoomId != null) request.RoomId = dto.RoomId;
        if (dto.UserName != null) request.UserName = dto.UserName;
        if (dto.Uid != null) request.Uid = dto.Uid;
        if (dto.SongName != null) request.SongName = dto.SongName;
        if (dto.Singer != null) request.Singer = dto.Singer;
        if (dto.CreatedAt.HasValue) request.CreatedAt = dto.CreatedAt;

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpDelete("song-requests/{id}")]
    public async Task<IActionResult> DeleteSongRequest(int id)
    {
        var request = await _db.SongRequests.FindAsync(id);
        if (request == null) return NotFound(new { error = "Song request not found" });

        _db.SongRequests.Remove(request);
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }
}

public class RoomDto
{
    public long RoomId { get; set; }
    public required string Name { get; set; }
    public string? Uid { get; set; }
    public string? Remark { get; set; }
}

public class SessionDto
{
    public string? RoomId { get; set; }
    public string? Title { get; set; }
    public string? UserName { get; set; }
    public long? StartTime { get; set; }
    public long? EndTime { get; set; }
    public string? FilePath { get; set; }
    public string? SummaryJson { get; set; }
    public string? GiftSummaryJson { get; set; }
}

public class SongRequestDto
{
    public int? SessionId { get; set; }
    public string? RoomId { get; set; }
    public string? UserName { get; set; }
    public string? Uid { get; set; }
    public string? SongName { get; set; }
    public string? Singer { get; set; }
    public long? CreatedAt { get; set; }
}
