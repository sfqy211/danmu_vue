using System.Text.Json;
using Danmu.Server.Data;
using Danmu.Server.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Controllers;

/// <summary>
/// danmakus-client 的账号中心兼容层。
///
/// 客户端启动流程里有三处必打这里（见 danmakus-core/src/core/AccountApiClient.ts）：
/// <list type="number">
/// <item><c>GET /api/v2/account/core-config</c> —— 取核心配置（连接上限、运行时地址、状态检查间隔）</item>
/// <item><c>GET /api/v2/account/info</c> —— 取账号信息（用来在 UI 里显示"以谁的身份采集"）</item>
/// <item><c>GET /api/v2/account/recording</c> —— 取录制列表（决定"我要录谁"）</item>
/// </list>
///
/// 我们把这三处都映射到本中心已有的「采集节点」+「房间」模型上：
/// 一个节点 Token 就是一个"账号"，录制列表 = 后台启用自动录制的房间。
///
/// 注意：客户端只会把这些数据用于**显示**；真正"录谁"由 WS 下发的 holdingRooms 决定
/// （见 <see cref="Services.Danmakus.DanmakusRoomAllocator"/>）。所以这里返回的录制列表
/// 与 WS 分配结果保持一致即可，不会出现两套真相。
/// </summary>
[ApiController]
[Route("api/v2/account")]
[TypeFilter(typeof(IngestAuthAttribute))]
public class DanmakusAccountController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DanmuContext _db;
    private readonly ILogger<DanmakusAccountController> _logger;

    public DanmakusAccountController(DanmuContext db, ILogger<DanmakusAccountController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private string CurrentNodeId => HttpContext.Items[IngestAuthAttribute.NodeIdItemKey] as string ?? "";

    /// <summary>
    /// 核心配置。客户端用 <c>runtimeUrl</c> 决定后续 WS/状态接口打到哪；
    /// 我们直接返回本次请求的 origin，客户端就能自动跟随当前域名（蓝绿切换也不受影响）。
    /// </summary>
    [HttpGet("core-config")]
    public async Task<IActionResult> GetCoreConfig()
    {
        var node = await _db.IngestNodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.NodeId == CurrentNodeId);

        var origin = $"{Request.Scheme}://{Request.Host}";

        // capacity：本节点默认并发连接数。放在节点表里没有对应字段，
        // 用环境变量 DANMAKUS_NODE_CAPACITY 统一配置（默认 10，够一台机器录 10 个直播间）。
        var capacityText = Environment.GetEnvironmentVariable("DANMAKUS_NODE_CAPACITY");
        var maxConnections = int.TryParse(capacityText, out var cap) ? Math.Clamp(cap, 1, 100) : 10;

        Response.Headers["X-Core-Config-Tag"] = ComputeTag(node?.NodeId, maxConnections, origin);

        return Ok(new
        {
            maxConnections,
            runtimeUrl = $"{origin}/api/v2/core-runtime",
            autoReconnect = true,
            reconnectInterval = 5000,
            statusCheckInterval = 30,
            streamers = Array.Empty<object>(),
            requestServerRooms = true,
            allowedAreas = Array.Empty<string>(),
            allowedParentAreas = Array.Empty<string>(),
            excludedServerRoomUserIds = Array.Empty<long>()
        });
    }

    /// <summary>
    /// 账号信息。客户端只用它做展示，字段最小化即可。
    /// </summary>
    [HttpGet("info")]
    public async Task<IActionResult> GetUserInfo()
    {
        var node = await _db.IngestNodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.NodeId == CurrentNodeId);

        Response.Headers["X-Core-Config-Tag"] = ComputeTag(node?.NodeId, null, null);

        return Ok(new
        {
            id = node?.Id ?? 0,
            name = node?.Name ?? node?.NodeId ?? CurrentNodeId,
            bindedOAuth = Array.Empty<string>(),
            recievedDanmakusCount = node?.TotalEvents ?? 0
        });
    }

    /// <summary>
    /// 录制列表。返回后台启用自动录制的所有房间，与 WS 分配口径一致。
    /// </summary>
    [HttpGet("recording")]
    public async Task<IActionResult> GetRecordingList()
    {
        var rooms = await _db.Rooms.AsNoTracking()
            .Where(r => r.AutoRecord == 1)
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Id)
            .ToListAsync();

        Response.Headers["X-Core-Recording-Tag"] = ComputeTag(
            "recording", rooms.Count, string.Join(",", rooms.Select(r => r.RoomId)));

        var list = rooms.Select(room => new
        {
            channel = new
            {
                uId = ParseLong(room.Uid),
                uName = room.Name ?? "",
                roomId = room.RoomId,
                faceUrl = "",
                isLiving = false,
                livingInfo = (object?)null
            },
            setting = new { isPublic = true },
            todayDanmakusCount = 0,
            providedDanmakuDataCount = 0,
            providedMessageCount = 0
        });

        return Ok(list);
    }

    /// <summary>
    /// 新增录制（客户端 UI 的"加主播"）。映射为把该房间标记为自动录制。
    /// </summary>
    [HttpGet("add-record")]
    public async Task<IActionResult> AddRecord([FromQuery] long uId)
    {
        var room = await FindRoomByUidAsync(uId);
        if (room is null)
        {
            return NotFound(new { message = $"未找到 uid={uId} 的房间" });
        }

        room.AutoRecord = 1;
        await _db.SaveChangesAsync();
        return Ok(new { roomId = room.RoomId, uId });
    }

    /// <summary>删除录制：取消该房间的自动录制标记。</summary>
    [HttpGet("del-record")]
    public async Task<IActionResult> RemoveRecord([FromQuery] long uId)
    {
        var room = await FindRoomByUidAsync(uId);
        if (room is null)
        {
            return NotFound(new { message = $"未找到 uid={uId} 的房间" });
        }

        room.AutoRecord = 0;
        await _db.SaveChangesAsync();
        return Ok(new { roomId = room.RoomId, uId });
    }

    /// <summary>更新录制设置（是否公开）。中心侧目前没有"未公开场次"的概念，直接接受。</summary>
    [HttpPost("update-recording-setting")]
    public IActionResult UpdateRecordingSetting([FromBody] JsonElement payload)
        => Ok(Array.Empty<long>());

    private async Task<Models.Room?> FindRoomByUidAsync(long uid)
    {
        var text = uid.ToString();
        return await _db.Rooms.FirstOrDefaultAsync(r => r.Uid == text);
    }

    private static long ParseLong(string? value)
        => long.TryParse(value, out var parsed) ? parsed : 0;

    /// <summary>生成 ETag 风格的 tag，客户端据此判断配置是否变化。</summary>
    private static string ComputeTag(string? nodeId, int? capacity, string? extra)
    {
        var raw = $"{nodeId}|{capacity}|{extra}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }
}
