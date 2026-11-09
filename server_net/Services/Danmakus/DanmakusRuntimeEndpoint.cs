using System.Net.WebSockets;
using System.Text.Json;
using MessagePack;
using Danmu.Server.Data;
using Danmu.Server.Filters;
using Danmu.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Services.Danmakus;

/// <summary>
/// danmakus-client 的 Core Runtime WebSocket 端点。
///
/// 这是让现有 danmakus 客户端直接连上本中心的**唯一入口**，协议完全对齐
/// danmakus-core/src/core/RuntimeWsConnection.ts：
///
/// <list type="table">
/// <item><term>0x01 CLIENT_AUTH</term><description>连接后第一帧。msgpack{token, clientId, version}。回 0x81 {success, serverTime, extractionRules}</description></item>
/// <item><term>0x02 CLIENT_UPLOAD_DANMAKU</term><description>上报批次。回 0x82 {acceptedCount, rejected[]}</description></item>
/// <item><term>0x03 CLIENT_STATE_REPORT</term><description>15s 心跳 + 请求房间分配。回 0x83 holdingRooms 推送</description></item>
/// <item><term>0x04 CLIENT_REQUEST_ROOMS</term><description>主动请求分配（客户端预留，等同 0x03）</description></item>
/// </list>
///
/// 鉴权复用现有节点 Token 体系（<c>ingest_nodes.token_hash</c>，SHA256 比对），
/// 因此后台「采集节点」里创建的凭据可以直接填进 danmakus 客户端的 <c>--token</c>。
/// </summary>
public class DanmakusRuntimeEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly DanmakusRuntimeRegistry _registry;
    private readonly DanmakusRoomAllocator _allocator;
    private readonly IngestService _ingest;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DanmakusRuntimeEndpoint> _logger;

    public DanmakusRuntimeEndpoint(
        DanmakusRuntimeRegistry registry,
        DanmakusRoomAllocator allocator,
        IngestService ingest,
        IServiceScopeFactory scopeFactory,
        ILogger<DanmakusRuntimeEndpoint> logger)
    {
        _registry = registry;
        _allocator = allocator;
        _ingest = ingest;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket required");
            return;
        }

        using var ws = await context.WebSockets.AcceptWebSocketAsync();
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        _logger.LogInformation("Danmakus core-runtime WS connected from {RemoteIp}", remoteIp);

        DanmakusRuntimeRegistry.NodeSession? session = null;

        try
        {
            session = await AuthenticateAsync(ws, context.RequestAborted);
            if (session is null)
            {
                return;
            }

            await ReceiveLoopAsync(ws, session, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // 服务端/客户端正常断开
        }
        catch (WebSocketException ex)
        {
            _logger.LogDebug(ex, "Danmakus WS closed abnormally");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Danmakus WS handler failed for node {NodeId}", session?.NodeId ?? "?");
        }
        finally
        {
            if (session is not null)
            {
                // 断开时把该节点名下的上报会话都收尾：
                // 不结束的话 Session.EndTime 会一直为 0，前端永远显示"正在录制"，
                // 而且 IngestService 的活跃表里会残留一条永不超时的记录。
                await FinalizeNodeSessionsAsync(session);
                _allocator.ReleaseNode(session);
                _registry.Remove(session.NodeId, session.ClientId);
            }
        }
    }

    /// <summary>
    /// 节点断开时结束它名下所有上报会话。
    ///
    /// 注意这段**不封盘**：WS 断开通常只是采集机掉线/换网，直播往往还在继续。
    /// 封盘会让前端把这场标成"已结束"，且其它节点接管时无法再往同一场追加数据。
    /// </summary>
    private async Task FinalizeNodeSessionsAsync(DanmakusRuntimeRegistry.NodeSession session)
    {
        foreach (var (roomId, upload) in session.ActiveUploads)
        {
            try
            {
                await _ingest.EndSessionAsync(upload.SessionId, session.NodeId, null, finalize: false);
                _logger.LogInformation(
                    "Danmakus node {NodeId} detached session {SessionId} for room {RoomId}",
                    session.NodeId, upload.SessionId, roomId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to finalize session {SessionId} for node {NodeId}", upload.SessionId, session.NodeId);
            }
        }
        session.ActiveUploads.Clear();
    }

    // ─── 鉴权 ───────────────────────────────────────────────────

    /// <summary>等第一帧必须是 CLIENT_AUTH，失败则回 auth_result{success:false} 并关闭。</summary>
    private async Task<DanmakusRuntimeRegistry.NodeSession?> AuthenticateAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var first = await ReceiveFrameAsync(ws, buffer, ct);
        if (first is null)
        {
            return null;
        }

        var (frame, payload) = first.Value;
        if (frame.OpCode != CoreWsProtocol.ClientAuth)
        {
            await SendAuthResultAsync(ws, frame.Seq, false, "首帧必须是 CLIENT_AUTH", null, ct);
            await CloseAsync(ws, "unauthorized", ct);
            return null;
        }

        IReadOnlyDictionary<string, object?> auth;
        try
        {
            if (!DanmakusMsgPack.TryAsMap(MessagePackSerializer.Deserialize<object>(payload), out auth))
            {
                throw new InvalidDataException("鉴权帧不是 map");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Danmakus auth frame is not valid msgpack");
            await SendAuthResultAsync(ws, frame.Seq, false, "鉴权帧解码失败", null, ct);
            await CloseAsync(ws, "bad-auth", ct);
            return null;
        }

        var token = DanmakusEventMapper.GetString(auth, "token");
        var clientId = DanmakusEventMapper.GetString(auth, "clientId") ?? "";
        var version = DanmakusEventMapper.GetString(auth, "version") ?? "";

        if (string.IsNullOrWhiteSpace(token))
        {
            await SendAuthResultAsync(ws, frame.Seq, false, "缺少 token", null, ct);
            await CloseAsync(ws, "bad-auth", ct);
            return null;
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            // clientId 是分配房间的稳定身份，缺失就无法做归属，直接拒
            await SendAuthResultAsync(ws, frame.Seq, false, "缺少 clientId", null, ct);
            await CloseAsync(ws, "bad-auth", ct);
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var node = await ValidateNodeTokenAsync(db, token, ct);
        if (node is null)
        {
            await SendAuthResultAsync(ws, frame.Seq, false, "节点 Token 无效或已停用", null, ct);
            await CloseAsync(ws, "bad-auth", ct);
            return null;
        }

        node.LastSeenAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await db.SaveChangesAsync(ct);

        var session = _registry.Register(node.NodeId, clientId, version);

        // extractionRules：客户端据此在本地提取字段，key 与我们的事件模型同名
        var rulesJson = DanmakusExtractionRules.BuildJson(JsonOptions);
        await SendAuthResultAsync(ws, frame.Seq, true, null, rulesJson, ct);

        _logger.LogInformation("Danmakus node {NodeId} authenticated (client={ClientId}, version={Version})",
            node.NodeId, clientId, version);

        return session;
    }

    /// <summary>与 IngestAuthAttribute 相同的校验逻辑：SHA256(token) 与库中 hash 比对。</summary>
    private static async Task<IngestNode?> ValidateNodeTokenAsync(DanmuContext db, string token, CancellationToken ct)
    {
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)))
            .ToLowerInvariant();

        var node = await db.IngestNodes.FirstOrDefaultAsync(n => n.TokenHash == hash, ct);
        if (node is null || !node.IsEnabled)
        {
            return null;
        }
        return node;
    }

    // ─── 主接收循环 ─────────────────────────────────────────────

    private async Task ReceiveLoopAsync(
        WebSocket ws,
        DanmakusRuntimeRegistry.NodeSession session,
        CancellationToken ct)
    {
        var buffer = new byte[512 * 1024];

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var received = await ReceiveFrameAsync(ws, buffer, ct);
            if (received is null)
            {
                break;
            }

            var (frame, payload) = received.Value;
            session.LastSeenUtc = DateTime.UtcNow;

            try
            {
                switch (frame.OpCode)
                {
                    case CoreWsProtocol.ClientUploadDanmaku:
                        await HandleUploadAsync(ws, session, frame.Seq, payload, ct);
                        break;

                    case CoreWsProtocol.ClientStateReport:
                        await HandleStateReportAsync(ws, session, payload, ct);
                        break;

                    case CoreWsProtocol.ClientRequestRooms:
                        await HandleStateReportAsync(ws, session, payload, ct);
                        break;

                    default:
                        _logger.LogDebug("Danmakus node {NodeId} sent unknown opcode 0x{Op:X2}",
                            session.NodeId, frame.OpCode);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Danmakus node {NodeId} failed handling opcode 0x{Op:X2}",
                    session.NodeId, frame.OpCode);
                await SendErrorAsync(ws, "HANDLER_FAILED", ex.Message, ct);
            }
        }
    }

    // ─── 弹幕上报 ───────────────────────────────────────────────

    /// <summary>
    /// 处理 CLIENT_UPLOAD_DANMAKU：
    /// 解信封（compression/payload）→ 解 msgpack 批次 → 逐条映射成中心事件 → 按 session 落库。
    ///
    /// 关键点：采集端不告诉我们 sessionId，它只按 roomId 上报。
    /// 会话由中心按 <c>(主播uid, 直播开始时间)</c> 自行归并 —— 这样多节点录同一场会自动汇到一场，
    /// 而且客户端重启/换机器都不影响。
    /// </summary>
    private async Task HandleUploadAsync(
        WebSocket ws,
        DanmakusRuntimeRegistry.NodeSession session,
        uint seq,
        byte[] payload,
        CancellationToken ct)
    {
        var accepted = 0;
        var rejected = new List<Dictionary<string, object?>>();

        IReadOnlyDictionary<string, object?> batch;
        try
        {
            var inner = DanmakusUploadEnvelope.Unwrap(payload);
            if (!DanmakusMsgPack.TryAsMap(MessagePackSerializer.Deserialize<object>(inner), out batch))
            {
                throw new InvalidDataException("上传批次不是 map");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Danmakus node {NodeId} upload batch undecodable", session.NodeId);
            await SendUploadAckAsync(ws, seq, 0, new List<Dictionary<string, object?>>(), "DECODE_FAILED", ex.Message, ct);
            return;
        }

        if (!batch.TryGetValue("events", out var eventsValue) ||
            !DanmakusMsgPack.TryAsList(eventsValue, out var events))
        {
            await SendUploadAckAsync(ws, seq, 0, rejected, "BAD_REQUEST", "缺少 events", ct);
            return;
        }

        // 按 roomId 分组：每个房间归到自己的会话
        var grouped = new Dictionary<long, List<(long LocalId, long StreamerUid, long EventTsMs, IReadOnlyDictionary<string, object?> Danmaku)>>();

        foreach (var raw in events)
        {
            // 注意：MessagePack 反序列化出的嵌套 map 实际类型是 Dictionary<object, object>，
            // 而不是 Dictionary<string, object?>，必须用宽松转换，否则所有事件都会被静默丢弃。
            if (!DanmakusMsgPack.TryAsMap(raw, out var evt))
            {
                continue;
            }

            var localId = DanmakusEventMapper.GetLong(evt, "localId") ?? 0;
            var roomId = DanmakusEventMapper.GetLong(evt, "roomId") ?? 0;
            var streamerUid = DanmakusEventMapper.GetLong(evt, "streamerUid") ?? 0;
            var eventTsMs = DanmakusEventMapper.GetLong(evt, "eventTsMs") ?? 0;

            if (roomId <= 0)
            {
                if (localId > 0) rejected.Add(new Dictionary<string, object?> { ["localId"] = localId, ["code"] = "BAD_ROOM", ["message"] = "缺少 roomId" });
                continue;
            }

            if (!DanmakusMsgPack.TryAsMap(evt.GetValueOrDefault("danmaku"), out var danmaku))
            {
                // runtimeDelta / lifecycleSignal 类事件本期不入库，直接 ACK 掉，避免客户端无限重试
                if (localId > 0) accepted++;
                continue;
            }

            if (!grouped.TryGetValue(roomId, out var list))
            {
                list = new List<(long, long, long, IReadOnlyDictionary<string, object?>)>();
                grouped[roomId] = list;
            }
            list.Add((localId, streamerUid, eventTsMs, danmaku));
        }

        foreach (var (roomId, items) in grouped)
        {
            var mapped = new List<RecordedDanmakuEvent>(items.Count);
            var localIds = new List<long>(items.Count);

            foreach (var item in items)
            {
                var evt = DanmakusEventMapper.Map(item.Danmaku, item.EventTsMs);
                if (evt is null)
                {
                    if (item.LocalId > 0)
                    {
                        rejected.Add(new Dictionary<string, object?> { ["localId"] = item.LocalId, ["code"] = "BAD_EVENT", ["message"] = "事件字段不完整" });
                    }
                    continue;
                }
                mapped.Add(evt);
                localIds.Add(item.LocalId);
            }

            if (mapped.Count == 0) continue;

            try
            {
                var sessionId = await EnsureSessionAsync(session, roomId, items[0].StreamerUid, mapped, ct);
                var result = await _ingest.AppendEventsAsync(sessionId, session.NodeId, mapped);
                // 用 Accepted（实际新增）而不是 Received（本批条数）：
                // 客户端按 acceptedCount 判断"这批到底有没有落地"，
                // 用 Received 会把重复上报也说成成功，掩盖去重。
                accepted += result.Accepted;
                session.TotalEvents += result.Accepted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Danmakus node {NodeId} failed to persist {Count} events for room {RoomId}",
                    session.NodeId, mapped.Count, roomId);
                // 整批退回，客户端会重试 —— 宁可能力范围内重传，也不能静默丢数据
                foreach (var localId in localIds.Where(id => id > 0))
                {
                    rejected.Add(new Dictionary<string, object?> { ["localId"] = localId, ["code"] = "PERSIST_FAILED", ["message"] = ex.Message });
                }
            }
        }

        await SendUploadAckAsync(ws, seq, accepted, rejected, null, null, ct);
    }

    /// <summary>
    /// 确保房间有对应的上报会话。
    ///
    /// 会话键用 (streamerUid, liveStartTime)：
    /// - streamerUid 客户端已经解析好（它从录制列表/状态里取），是主播 uid
    /// - liveStartTime 从中心 LiveStatusService 缓存取；取不到则用本批最早事件时间对齐到分钟，
    ///   保证同一节点重连后还能算回同一场（不会每批都新建会话）
    /// </summary>
    private async Task<int> EnsureSessionAsync(
        DanmakusRuntimeRegistry.NodeSession nodeSession,
        long roomId,
        long streamerUid,
        List<RecordedDanmakuEvent> events,
        CancellationToken ct)
    {
        if (nodeSession.ActiveUploads.TryGetValue(roomId, out var existing))
        {
            existing.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
            return existing.SessionId;
        }

        var liveStartTime = await ResolveLiveStartTimeAsync(roomId, events, ct);
        var uid = streamerUid > 0 ? streamerUid.ToString() : roomId.ToString();

        // 主播名/房间名从中心房间表回填：客户端只报 uid/roomId，
        // 不补的话场次列表里全是空白主播名。
        var room = await LoadRoomAsync(uid, roomId, ct);

        var sessionId = await _ingest.StartSessionAsync(new IngestSessionStartRequest
        {
            Uid = uid,
            RoomId = roomId,
            Title = room?.Name,
            UserName = room?.Name,
            LiveStartTime = liveStartTime
        }, nodeSession.NodeId);

        nodeSession.ActiveUploads[roomId] = new DanmakusRuntimeRegistry.ActiveUpload
        {
            SessionId = sessionId,
            StreamerUid = streamerUid,
            RoomId = roomId,
            LiveStartTime = liveStartTime ?? 0
        };

        return sessionId;
    }

    /// <summary>
    /// 取房间展示信息（主播名）。客户端不上报这些，中心自己有，直接补。
    /// </summary>
    private async Task<Danmu.Server.Models.Room?> LoadRoomAsync(string uid, long roomId, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();

            var room = await db.Rooms.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Uid == uid, ct);
            if (room != null) return room;

            return await db.Rooms.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RoomId == roomId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load room info for uid={Uid} room={RoomId}", uid, roomId);
            return null;
        }
    }

    /// <summary>
    /// 直播开始时间（毫秒）。优先用中心缓存的开播时间 —— 这是唯一能让
    /// "A 机器录前半段、B 机器录后半段"落到同一个 Session 的键。
    /// </summary>
    private async Task<long?> ResolveLiveStartTimeAsync(
        long roomId,
        List<RecordedDanmakuEvent> events,
        CancellationToken ct)
    {
        try
        {
            var state = await _allocator.ResolveLiveStateAsync(roomId, ct);
            if (state?.LiveStartTime is long start && start > 0)
            {
                return start;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve live start time for room {RoomId}", roomId);
        }

        // 兜底：按本批最早事件时间对齐到 10 分钟粒度。
        // 目的是让同一场直播的多批数据落到同一个桶里，而不是每批新建会话。
        var minTs = events.Count > 0 ? events.Min(e => e.Timestamp) : 0;
        if (minTs <= 0) return null;
        const long bucket = 10 * 60 * 1000L;
        return minTs / bucket * bucket;
    }

    // ─── 状态汇报 / 房间分配 ────────────────────────────────────

    /// <summary>
    /// 处理 CLIENT_STATE_REPORT：更新节点在线信息，并回推房间分配结果。
    /// </summary>
    private async Task HandleStateReportAsync(
        WebSocket ws,
        DanmakusRuntimeRegistry.NodeSession session,
        byte[] payload,
        CancellationToken ct)
    {
        // 15s 心跳帧可能不带 payload（客户端在没注册 getStateReportPayload 时发空帧），
        // 这种情况下只需要回一次分配即可。
        IReadOnlyDictionary<string, object?>? state = null;
        if (payload.Length > 0)
        {
            try
            {
                if (DanmakusMsgPack.TryAsMap(MessagePackSerializer.Deserialize<object>(payload), out var parsed))
                {
                    state = parsed;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Danmakus node {NodeId} state report undecodable", session.NodeId);
            }
        }

        if (state is not null)
        {
            if (DanmakusEventMapper.GetLong(state, "capacity") is long capacity)
            {
                session.Capacity = (int)Math.Clamp(capacity, 0, 100);
            }

            if (DanmakusMsgPack.TryAsList(state.GetValueOrDefault("connectedRooms"), out var connected))
            {
                session.ConnectedRooms = connected
                    .Select(DanmakusEventMapper.ConvertToLong)
                    .Where(id => id is > 0)
                    .Select(id => id!.Value)
                    .ToList();
            }

            if (DanmakusEventMapper.GetString(state, "reason") is string reason)
            {
                _logger.LogDebug("Danmakus node {NodeId} state report: {Reason}", session.NodeId, reason);
            }
        }

        var allocation = await _allocator.AllocateAsync(session, ct);

        await SendRoomAssignAsync(ws, allocation, ct);

        // 顺便把这一轮的主播开播状态推给客户端，省掉它自己去轮询 B 站
        await SendStreamerStatusAsync(ws, allocation.HoldingRooms, ct);
    }

    // ─── 帧收发 ─────────────────────────────────────────────────

    /// <summary>
    /// 读一个完整帧。WebSocket 消息可能被分片，这里按 EndOfMessage 拼装。
    /// 返回 null 表示对端已关闭。
    /// </summary>
    private static async Task<(CoreWsProtocol.Frame Frame, byte[] Payload)?> ReceiveFrameAsync(
        WebSocket ws,
        byte[] buffer,
        CancellationToken ct)
    {
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;

        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }
            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        if (ms.Length < CoreWsProtocol.FrameHeaderSize)
        {
            return null;
        }

        var raw = ms.ToArray();
        if (!CoreWsProtocol.TryDecode(raw, out var frame))
        {
            return null;
        }

        return (frame, frame.Payload.ToArray());
    }

    private static Task SendAuthResultAsync(
        WebSocket ws, uint seq, bool success, string? error, string? extractionRulesJson, CancellationToken ct)
    {
        var response = new Dictionary<string, object?>
        {
            ["success"] = success,
            ["serverTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        if (!string.IsNullOrEmpty(error)) response["error"] = error;

        if (!string.IsNullOrEmpty(extractionRulesJson))
        {
            // 规则必须是 msgpack 里可直接序列化的结构。JsonSerializer 反序列化出来的
            // JsonElement 是 MessagePack 的未注册类型，序列化会抛 FormatterNotRegisteredException，
            // 所以这里转成 Dictionary/List/标量组成的纯 BCL 结构。
            response["extractionRules"] = ToPlainObject(
                JsonSerializer.Deserialize<JsonElement>(extractionRulesJson));
        }

        var payload = MessagePackSerializer.Serialize(response);
        var frame = CoreWsProtocol.Encode(CoreWsProtocol.ServerAuthResult, seq, payload);
        return SendAsync(ws, frame, ct);
    }

    /// <summary>
    /// JsonElement → Dictionary/List/string/long/double/bool/null。
    /// MessagePack 的 StandardResolver 对这几个 BCL 类型有内置 formatter。
    /// </summary>
    private static object? ToPlainObject(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>();
                foreach (var prop in element.EnumerateObject())
                {
                    dict[prop.Name] = ToPlainObject(prop.Value);
                }
                return dict;

            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(ToPlainObject(item));
                }
                return list;

            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                // 整数值回 long（客户端按 int/long 解析），小数回 double
                if (element.TryGetInt64(out var l)) return l;
                return element.GetDouble();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            default:
                return null;
        }
    }

    private static Task SendUploadAckAsync(
        WebSocket ws, uint seq, int acceptedCount, List<Dictionary<string, object?>> rejected,
        string? code, string? error, CancellationToken ct)
    {
        var response = new Dictionary<string, object?>
        {
            ["acceptedCount"] = acceptedCount
        };
        if (rejected.Count > 0) response["rejected"] = rejected;
        if (!string.IsNullOrEmpty(code)) response["code"] = code;
        if (!string.IsNullOrEmpty(error)) response["error"] = error;

        var payload = MessagePackSerializer.Serialize(response);
        var frame = CoreWsProtocol.Encode(CoreWsProtocol.ServerUploadAck, seq, payload);
        return SendAsync(ws, frame, ct);
    }

    private static Task SendRoomAssignAsync(
        WebSocket ws, DanmakusRoomAllocator.AllocationResult result, CancellationToken ct)
    {
        var response = new Dictionary<string, object?>
        {
            ["holdingRooms"] = result.HoldingRooms,
            ["newlyAssigned"] = result.NewlyAssigned,
            ["dropped"] = result.Dropped,
            ["effectiveCapacity"] = result.EffectiveCapacity
        };
        if (result.Shortfall is not null)
        {
            // Shortfall 是匿名对象，msgpack 序列化不了，统一转成 Dictionary
            response["shortfall"] = ToPlainObject(
                JsonSerializer.SerializeToElement(result.Shortfall, JsonOptions));
        }

        var payload = MessagePackSerializer.Serialize(response);
        var frame = CoreWsProtocol.Encode(CoreWsProtocol.ServerPushRoomAssign, 0, payload);
        return SendAsync(ws, frame, ct);
    }

    /// <summary>
    /// 推送主播开播状态。客户端据此决定"持有房间"里哪些真的要去连。
    /// 走中心缓存而不是让客户端直接打 B 站，一来省配额，二来多节点看到的状态一致。
    /// </summary>
    private async Task SendStreamerStatusAsync(WebSocket ws, List<long> roomIds, CancellationToken ct)
    {
        if (roomIds.Count == 0) return;

        var updates = new List<object?>();
        foreach (var roomId in roomIds)
        {
            try
            {
                var state = await _allocator.ResolveLiveStateAsync(roomId, ct);
                if (state is null) continue;
                // 用 Dictionary 而不是匿名类型：msgpack 的 StandardResolver 不认识匿名类
                updates.Add(new Dictionary<string, object?>
                {
                    ["roomId"] = roomId,
                    ["isLive"] = state.LiveStatus == 1,
                    ["liveStartTime"] = state.LiveStartTime
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to push streamer status for room {RoomId}", roomId);
            }
        }

        if (updates.Count == 0) return;

        var response = new Dictionary<string, object?> { ["updates"] = updates };
        var payload = MessagePackSerializer.Serialize(response);
        var frame = CoreWsProtocol.Encode(CoreWsProtocol.ServerPushStreamerStatus, 0, payload);
        await SendAsync(ws, frame, ct);
    }

    private static Task SendErrorAsync(WebSocket ws, string code, string message, CancellationToken ct)
    {
        var payload = MessagePackSerializer.Serialize(new Dictionary<string, object?>
        {
            ["code"] = code,
            ["message"] = message
        });
        var frame = CoreWsProtocol.Encode(CoreWsProtocol.ServerErrorNotify, 0, payload);
        return SendAsync(ws, frame, ct);
    }

    private static async Task SendAsync(WebSocket ws, byte[] frame, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;
        await ws.SendAsync(frame, WebSocketMessageType.Binary, true, ct);
    }

    private static async Task CloseAsync(WebSocket ws, string reason, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;
        try
        {
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, reason, ct);
        }
        catch
        {
            // 对端已断开时忽略
        }
    }
}
