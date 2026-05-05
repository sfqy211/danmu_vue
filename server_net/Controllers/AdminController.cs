using Danmu.Server.Data;
using Danmu.Server.Filters;
using Danmu.Server.Models;
using Danmu.Server.Models.Dtos;
using Danmu.Server.Services;
using Danmu.Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    private readonly DanmakuService _danmakuService;
    private readonly BiliAccountService _accountService;
    private readonly LiveStatusService _liveStatusService;
    private readonly HealthCheckService _healthCheckService;
    private readonly ChangelogService _changelogService;
    private readonly LogService _logService;
    private readonly string _danmakuDir;

    public AdminController(DanmuContext db, ProcessManager pm, BilibiliService bilibili, DanmakuService danmakuService, BiliAccountService accountService, LiveStatusService liveStatusService, HealthCheckService healthCheckService, ChangelogService changelogService, LogService logService, ILogger<AdminController> logger)
    {
        _db = db;
        _pm = pm;
        _bilibili = bilibili;
        _danmakuService = danmakuService;
        _accountService = accountService;
        _liveStatusService = liveStatusService;
        _healthCheckService = healthCheckService;
        _changelogService = changelogService;
        _logService = logService;
        _logger = logger;
        _danmakuDir = Environment.GetEnvironmentVariable("DANMAKU_DIR")
                      ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../server/data/danmaku"));
    }

    [HttpGet("health-check")]
    public IActionResult GetHealthCheckReport()
    {
        return Ok(_healthCheckService.GetLatestReport());
    }

    [HttpGet("rooms")]
    public async Task<IActionResult> GetRooms()
    {
        var rooms = await _db.Rooms.ToListAsync();
        var processes = _pm.GetProcesses();
        var results = new List<AdminRoomListItemDto>();

        foreach (var room in rooms)
        {
            var proc = processes.FirstOrDefault(p => p.Uid == (room.Uid ?? ""));

            long? liveStartTime = room.LastLiveTime > 0 ? room.LastLiveTime : null;

            // Layer 1: only trust recorder live status when recorder is actually online
            // Layer 2: fallback to cached live status from LiveStatusService otherwise
            var realLiveStatus = 0;
            long? realLiveStartTime = liveStartTime;

            if (proc != null && proc.Status == "online")
            {
                realLiveStatus = proc.LiveStatus;
                realLiveStartTime = proc.LiveStartTime ?? liveStartTime;
            }
            else
            {
                var cached = await _liveStatusService.GetCachedStatusAsync(room.RoomId);
                if (cached != null)
                {
                    realLiveStatus = cached.LiveStatus;
                    if (cached.LiveStartTime.HasValue)
                        realLiveStartTime = cached.LiveStartTime.Value;
                }
            }

            results.Add(new AdminRoomListItemDto
            {
                Id = room.Id,
                RoomId = room.RoomId,
                Name = room.Name,
                Uid = room.Uid,
                AutoRecord = room.AutoRecord,
                ProcessStatus = proc?.Status ?? "stopped",
                ProcessUptime = proc?.Uptime ?? "0s",
                ProcessStartTime = TimeUtils.ToUnixMilliseconds(proc?.StartTime),
                LiveStatus = realLiveStatus,
                LiveStartTime = realLiveStartTime,
                Pid = proc?.Pid,
                AccountUid = proc?.AccountUid,
                Remark = room.Remark,
                PlaylistUrl = room.PlaylistUrl
            });
        }

        return Ok(results);
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

            var existing = await _db.Rooms.FirstOrDefaultAsync(r => r.Uid == uid.ToString());
            if (existing != null)
            {
                return BadRequest(new { error = "主播已存在 (UID: " + uid + ")" });
            }

            var room = new Room
            {
                RoomId = realRoomId,
                Name = name,
                Uid = uid.ToString(),
                Remark = dto.Remark,
                PlaylistUrl = string.IsNullOrWhiteSpace(dto.PlaylistUrl) ? null : dto.PlaylistUrl.Trim(),
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
        await _pm.StopRecorder(room.RoomId);

        _db.Rooms.Remove(room);
        await _db.SaveChangesAsync();

        return Ok(new { success = true });
    }

    [HttpPut("rooms/{id}")]
    public async Task<IActionResult> UpdateRoom(int id, [FromBody] RoomDto dto)
    {
        var room = await _db.Rooms.FindAsync(id);
        if (room == null) return NotFound(new { error = "Room not found" });

        if (dto.Remark != null)
        {
            room.Remark = string.IsNullOrWhiteSpace(dto.Remark) ? null : dto.Remark;
        }

        if (dto.PlaylistUrl != null)
        {
            room.PlaylistUrl = string.IsNullOrWhiteSpace(dto.PlaylistUrl) ? null : dto.PlaylistUrl.Trim();
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

            await _pm.StopRecorder(room.RoomId);
            return Ok(new { success = true });
        }
        return BadRequest(new { error = "Missing roomId" });
    }

    [HttpPost("rooms/{id}/restart")]
    public async Task<IActionResult> RestartRoom(int id)
    {
        var room = await _db.Rooms.FindAsync(id);
        if (room == null) return NotFound(new { error = "Room not found" });

        // Stop
        await _pm.StopRecorder(room.RoomId);
        
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
            // Robust parsing of boolean or number
            bool isEnabled = false;
            if (autoRecordProp.ValueKind == JsonValueKind.True) isEnabled = true;
            else if (autoRecordProp.ValueKind == JsonValueKind.False) isEnabled = false;
            else if (autoRecordProp.ValueKind == JsonValueKind.Number) isEnabled = autoRecordProp.GetInt32() != 0;

            room.AutoRecord = isEnabled ? 1 : 0;
            
            _logger.LogInformation($"Toggling monitor for {room.Name}: {isEnabled} (ID: {id})");
            
            await _db.SaveChangesAsync();

            // Perform action based on the NEW state in DB
            if (room.AutoRecord == 0)
            {
                await _pm.StopRecorder(room.RoomId);
                _logger.LogInformation($"Stopped recorder for {room.Name}");
            }
            else
            {
                await _pm.StartRecorder(room.RoomId, room.Name);
                _logger.LogInformation($"Started recorder for {room.Name}");
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
                s.Uid,
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
            Uid = dto.Uid,
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

    [HttpPost("sessions/upload-xml")]
    [RequestSizeLimit(512 * 1024 * 1024)]
    public async Task<IActionResult> UploadSessionXml([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "请选择要上传的 XML 文件" });
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".xml", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "仅支持上传 .xml 文件" });
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "danmu-session-imports");
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.xml");

        try
        {
            await using (var stream = System.IO.File.Create(tempPath))
            {
                await file.CopyToAsync(stream);
            }

            var session = await _danmakuService.ImportLegacyXmlAsJsonlAsync(tempPath);
            if (session == null)
            {
                return BadRequest(new { error = "XML 解析或转换失败" });
            }

            return Ok(new
            {
                success = true,
                id = session.Id,
                session = new
                {
                    session.Id,
                    session.Uid,
                    session.RoomId,
                    session.Title,
                    session.UserName,
                    session.StartTime,
                    session.EndTime,
                    session.FilePath
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload and import session XML file {FileName}", file.FileName);
            return StatusCode(500, new { error = "上传导入失败: " + ex.Message });
        }
        finally
        {
            try
            {
                if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temp upload file {TempPath}", tempPath);
            }
        }
    }

    [HttpPost("sessions/{id}/replace-xml")]
    [RequestSizeLimit(512 * 1024 * 1024)]
    public async Task<IActionResult> ReplaceSessionXml(int id, [FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "请选择要上传的 XML 文件" });
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".xml", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "仅支持上传 .xml 文件" });
        }

        var session = await _db.Sessions.FindAsync(id);
        if (session == null)
        {
            return NotFound(new { error = "Session not found" });
        }

        if (string.IsNullOrWhiteSpace(session.FilePath) || session.FilePath.StartsWith("redis:", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "当前直播回放不是本地文件，无法替换" });
        }

        var targetPath = ResolveSessionFilePath(session);
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return BadRequest(new { error = "无法定位原始 jsonl 文件" });
        }

        var backupPath = Path.Combine(Path.GetTempPath(), "danmu-session-backups", $"{Guid.NewGuid():N}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);

        var hasBackup = false;
        try
        {
            if (System.IO.File.Exists(targetPath))
            {
                System.IO.File.Copy(targetPath, backupPath, true);
                hasBackup = true;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "danmu-session-imports");
            Directory.CreateDirectory(tempDir);
            var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.xml");

            try
            {
                await using (var stream = System.IO.File.Create(tempPath))
                {
                    await file.CopyToAsync(stream);
                }

                var imported = await _danmakuService.ImportLegacyXmlAsJsonlAsync(tempPath, targetPath);
                if (imported == null)
                {
                    throw new InvalidOperationException("XML 解析或转换失败");
                }

                return Ok(new
                {
                    success = true,
                    id = imported.Id,
                    session = new
                    {
                        imported.Id,
                        imported.Uid,
                        imported.RoomId,
                        imported.Title,
                        imported.UserName,
                        imported.StartTime,
                        imported.EndTime,
                        imported.FilePath
                    }
                });
            }
            finally
            {
                try
                {
                    if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temp upload file {TempPath}", tempPath);
                }
            }
        }
        catch (Exception ex)
        {
            if (hasBackup)
            {
                try
                {
                    System.IO.File.Copy(backupPath, targetPath, true);
                }
                catch (Exception restoreEx)
                {
                    _logger.LogError(restoreEx, "Failed to restore backup jsonl file {TargetPath} after XML replace failed", targetPath);
                }
            }

            _logger.LogError(ex, "Failed to replace session XML for session {SessionId}", id);
            return StatusCode(500, new { error = "替换导入失败: " + ex.Message });
        }
        finally
        {
            try
            {
                if (System.IO.File.Exists(backupPath)) System.IO.File.Delete(backupPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete backup jsonl file {BackupPath}", backupPath);
            }
        }
    }

    [HttpPost("sessions/recalculate")]
    public async Task<IActionResult> RecalculateSessions([FromBody] RecalculateSessionsDto dto)
    {
        if (dto == null || dto.SessionIds == null || dto.SessionIds.Count == 0)
        {
            return BadRequest(new { error = "Missing sessionIds" });
        }

        var targetIds = dto.SessionIds.Where(id => id > 0).Distinct().ToList();
        if (targetIds.Count == 0) return BadRequest(new { error = "Invalid sessionIds" });

        var sessions = await _db.Sessions.Where(s => targetIds.Contains(s.Id)).ToListAsync();
        var sessionMap = sessions.ToDictionary(s => s.Id, s => s);

        var successIds = new List<int>();
        var skippedIds = new List<int>();
        var failedIds = new List<int>();

        foreach (var id in targetIds)
        {
            if (!sessionMap.TryGetValue(id, out var session) || session == null)
            {
                failedIds.Add(id);
                continue;
            }

            if (string.IsNullOrEmpty(session.FilePath) || session.FilePath.StartsWith("redis:"))
            {
                skippedIds.Add(id);
                continue;
            }

            var fullPath = ResolveSessionFilePath(session);
            if (string.IsNullOrEmpty(fullPath) || !System.IO.File.Exists(fullPath))
            {
                failedIds.Add(id);
                continue;
            }

            try
            {
                var result = await _danmakuService.ProcessFileAsync(fullPath);
                if (result == null)
                {
                    failedIds.Add(id);
                }
                else
                {
                    successIds.Add(id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to recalculate session {id}");
                failedIds.Add(id);
            }
        }

        return Ok(new
        {
            successCount = successIds.Count,
            skippedCount = skippedIds.Count,
            failedCount = failedIds.Count,
            successIds,
            skippedIds,
            failedIds
        });
    }

    [HttpPut("sessions/{id}")]
    public async Task<IActionResult> UpdateSession(int id, [FromBody] SessionDto dto)
    {
        var session = await _db.Sessions.FindAsync(id);
        if (session == null) return NotFound(new { error = "Session not found" });

        session.RoomId = dto.RoomId ?? session.RoomId;
        session.Uid = dto.Uid ?? session.Uid;
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
        if (!string.IsNullOrEmpty(session.Uid))
        {
            var uidPath = Path.Combine(_danmakuDir, session.Uid, basename);
            if (System.IO.File.Exists(uidPath)) return uidPath;
        }

        if (!string.IsNullOrEmpty(session.RoomId))
        {
            var roomPath = Path.Combine(_danmakuDir, session.RoomId, basename);
            if (System.IO.File.Exists(roomPath)) return roomPath;
        }

        var rootPath = Path.Combine(_danmakuDir, basename);
        if (System.IO.File.Exists(rootPath)) return rootPath;

        return fullPath;
    }

    // ─── Changelog Endpoints ──────────────────────────────────────────

    [HttpGet("changelog")]
    public async Task<IActionResult> GetAdminChangelog()
    {
        var entries = await _changelogService.GetAllAsync();
        return Ok(entries);
    }

    [HttpPost("changelog")]
    public async Task<IActionResult> AddChangelog([FromBody] ChangelogDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Version) || string.IsNullOrWhiteSpace(dto.Content))
            return BadRequest(new { message = "Version and content are required" });

        var entry = await _changelogService.AddAsync(dto.Version, dto.Date, dto.Content);
        return Ok(entry);
    }

    [HttpPut("changelog/{id}")]
    public async Task<IActionResult> UpdateChangelog(int id, [FromBody] ChangelogDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Version) || string.IsNullOrWhiteSpace(dto.Content))
            return BadRequest(new { message = "Version and content are required" });

        var entry = await _changelogService.UpdateAsync(id, dto.Version, dto.Date, dto.Content);
        if (entry == null) return NotFound(new { message = "Entry not found" });
        return Ok(entry);
    }

    [HttpDelete("changelog/{id}")]
    public async Task<IActionResult> DeleteChangelog(int id)
    {
        var deleted = await _changelogService.DeleteAsync(id);
        if (!deleted) return NotFound(new { message = "Entry not found" });
        return Ok(new { message = "Deleted" });
    }

    // ─── Log File Endpoints ─────────────────────────────────────────────

    [HttpGet("logs/files")]
    public IActionResult GetLogFiles()
    {
        var files = _logService.GetLogFiles();
        return Ok(files);
    }

    [HttpGet("logs/content")]
    public IActionResult GetLogContent([FromQuery] string? file, [FromQuery] int tail = 500)
    {
        var content = _logService.GetLogContent(file, tail);
        if (content == null) return NotFound(new { message = "Log file not found" });
        return Ok(new { content, file = content.FileName, size = content.Size, lines = content.Lines });
    }

    [HttpGet("logs/download")]
    public IActionResult DownloadLogFile([FromQuery] string file)
    {
        var filePath = _logService.GetLogFilePath(file);
        if (filePath == null) return NotFound(new { message = "Log file not found" });
        return PhysicalFile(filePath, "text/plain", file);
    }

    // ─── BiliAccount Endpoints ──────────────────────────────────────

    [HttpGet("bili-accounts")]
    public async Task<IActionResult> GetBiliAccounts()
    {
        var list = await _accountService.GetAllAsync();
        var results = list.Select(a => new BiliAccountListItem
        {
            Uid = a.Uid,
            Name = a.Name,
            ExpiresAt = a.ExpiresAt.HasValue ? new DateTimeOffset(a.ExpiresAt.Value).ToUnixTimeMilliseconds() : null,
            IsActive = a.IsActive,
            CreatedAt = new DateTimeOffset(a.CreatedAt).ToUnixTimeMilliseconds()
        }).ToList();
        return Ok(results);
    }

    [HttpPost("bili-accounts/import-cookie")]
    public async Task<IActionResult> ImportCookie([FromBody] ImportCookieDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Cookie))
            return BadRequest(new { message = "Cookie is required" });
        try
        {
            await _accountService.ImportFromCookieStringAsync(dto.Uid, dto.Cookie);
            return Ok(new { message = "Imported" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import cookie failed");
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost("bili-accounts/qrcode")]
    public async Task<IActionResult> StartQrLogin()
    {
        try
        {
            var (url, id) = await _accountService.StartTvLoginAsync();
            return Ok(new QrCodeLoginResponse { Url = url, Id = id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Start QR login failed");
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpGet("bili-accounts/qrcode/poll")]
    public IActionResult PollQrLogin([FromQuery] string id)
    {
        var state = _accountService.GetLoginState(id);
        if (state == null)
            return BadRequest(new { message = "Login session not found" });
        return Ok(new QrCodePollResponse
        {
            Status = state.Status,
            FailReason = state.FailReason,
            Uid = state.Uid
        });
    }

    [HttpPost("bili-accounts/qrcode/cancel")]
    public IActionResult CancelQrLogin([FromBody] Dictionary<string, string> body)
    {
        if (!body.TryGetValue("id", out var id))
            return BadRequest(new { message = "id required" });
        _accountService.CancelLogin(id);
        return Ok(new { message = "Cancelled" });
    }

    [HttpPost("bili-accounts/{uid:long}/activate")]
    public async Task<IActionResult> ActivateAccount(long uid)
    {
        await _accountService.SetActiveAsync(uid);
        return Ok(new { message = "Activated" });
    }

    [HttpPost("bili-accounts/{uid:long}/refresh-info")]
    public async Task<IActionResult> RefreshAccountInfo(long uid)
    {
        try
        {
            await _accountService.UpdateUserInfoAsync(uid);
            return Ok(new { message = "Refreshed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh info failed");
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost("bili-accounts/{uid:long}/refresh-auth")]
    public async Task<IActionResult> RefreshAccountAuth(long uid)
    {
        try
        {
            await _accountService.RefreshAuthAsync(uid);
            return Ok(new { message = "Auth refreshed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh auth failed");
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpDelete("bili-accounts/{uid:long}")]
    public async Task<IActionResult> DeleteAccount(long uid)
    {
        await _accountService.DeleteAsync(uid);
        return Ok(new { message = "Deleted" });
    }

    [HttpGet("bili-accounts/assignments")]
    public IActionResult GetAssignments()
    {
        var assignments = _accountService.GetRoomAssignments();
        var processes = _pm.GetProcesses();
        var activeUids = processes
            .Where(p => p.Status is "online" or "reconnecting")
            .Select(p => p.Uid)
            .ToHashSet();
        var rooms = _db.Rooms.AsNoTracking().ToList();

        var result = assignments
            .Where(kv => activeUids.Contains(kv.Key))
            .Select(kv =>
        {
            var roomUid = kv.Key;
            var accountUid = kv.Value;
            var proc = processes.FirstOrDefault(p => p.Uid == roomUid);
            var room = rooms.FirstOrDefault(r => r.Uid == roomUid);
            return new AssignmentItem
            {
                RoomUid = roomUid,
                RoomId = room?.RoomId ?? proc?.RoomId ?? 0,
                RoomName = room?.Remark ?? room?.Name ?? proc?.DisplayName ?? $"UID:{roomUid}",
                AccountUid = accountUid,
                IsRecording = proc != null && proc.Status is "online" or "reconnecting"
            };
        }).ToList();

        return Ok(result);
    }

    [HttpPost("bili-accounts/{targetUid:long}/reassign/{roomUid}")]
    public async Task<IActionResult> ReassignRoom(long targetUid, string roomUid)
    {
        var ok = await _accountService.ReassignRoomAsync(roomUid, targetUid);
        if (!ok)
            return BadRequest(new { message = "目标账户不存在或没有有效 Cookie" });
        return Ok(new { message = "已重新分配" });
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
    public string? PlaylistUrl { get; set; }
}

public class SessionDto
{
    public string? RoomId { get; set; }
    public string? Uid { get; set; }
    public string? Title { get; set; }
    public string? UserName { get; set; }
    public long? StartTime { get; set; }
    public long? EndTime { get; set; }
    public string? FilePath { get; set; }
    public string? SummaryJson { get; set; }
    public string? GiftSummaryJson { get; set; }
}

public class RecalculateSessionsDto
{
    public List<int> SessionIds { get; set; } = new();
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

// ─── BiliAccount DTOs ───────────────────────────────────────────────

public class BiliAccountListItem
{
    public long Uid { get; set; }
    public string? Name { get; set; }
    public long? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public long CreatedAt { get; set; }
}

public class ImportCookieDto
{
    public long Uid { get; set; }
    public string Cookie { get; set; } = "";
}

public class QrCodePollResponse
{
    public string Status { get; set; } = "scan";
    public string? FailReason { get; set; }
    public long? Uid { get; set; }
}

public class QrCodeLoginResponse
{
    public string Url { get; set; } = "";
    public string Id { get; set; } = "";
}

public class AssignmentItem
{
    [JsonPropertyName("room_uid")]
    public string RoomUid { get; set; } = "";

    [JsonPropertyName("room_id")]
    public long RoomId { get; set; }

    [JsonPropertyName("room_name")]
    public string? RoomName { get; set; }

    [JsonPropertyName("account_uid")]
    public long AccountUid { get; set; }

    [JsonPropertyName("is_recording")]
    public bool IsRecording { get; set; }
}

public class ChangelogDto
{
    public string Version { get; set; } = "";
    public DateTime Date { get; set; }
    public string Content { get; set; } = "";
}
