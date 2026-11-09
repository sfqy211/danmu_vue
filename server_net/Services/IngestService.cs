using System.Collections.Concurrent;
using System.Text.Json;
using Danmu.Server.Data;
using Danmu.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Danmu.Server.Services;

/// <summary>
/// 采集端上报的处理中心。
///
/// 设计要点：
/// - 多台机器可能录同一场直播的不同时段。会话归并键是 <c>(Uid, LiveStartTime)</c>，
///   其中 LiveStartTime 取自 B 站下发的直播开始时间，因此任何节点算出的一致。
/// - 事件按 B 站真实 message_id 去重（SQLite 唯一索引 + INSERT OR IGNORE），
///   多节点重复上报同一条弹幕会被自动丢弃，无需跨机锁。
/// - 命中已有会话时复用同一个 Session 行，前端看到的就是一条连续时间线。
/// </summary>
public class IngestService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IngestService> _logger;
    private readonly SessionDbService _sessionDb;

    /// <summary>活跃上报会话：key = "{nodeId}:{sessionId}"，value = 该节点的上报状态。</summary>
    private readonly ConcurrentDictionary<string, NodeUploadState> _uploads = new();

    /// <summary>上报会话心跳超时，超时后自动封盘，避免会话永远停在 redis/ingest 状态。</summary>
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(90);

    public IngestService(IServiceScopeFactory scopeFactory, ILogger<IngestService> logger, SessionDbService sessionDb)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _sessionDb = sessionDb;
    }

    private sealed class NodeUploadState
    {
        public string NodeId { get; init; } = "";
        public int SessionId { get; init; }
        public string Uid { get; init; } = "";
        public long LiveStartTime { get; init; }
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
        public long TotalEvents { get; set; }
        public long MinTimestamp { get; set; }
        public long MaxTimestamp { get; set; }
    }

    // ─── 会话开始 ───────────────────────────────────────────────

    /// <summary>
    /// 采集端开播时调用。按 (Uid, LiveStartTime) 复用已有会话，否则新建。
    /// </summary>
    public async Task<int> StartSessionAsync(IngestSessionStartRequest req, string nodeId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();

        var uid = req.Uid ?? "";
        var existing = await db.Sessions
            .Where(s => s.Uid == uid && s.LiveStartTime == req.LiveStartTime)
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync();

        int sessionId;
        if (existing != null)
        {
            sessionId = existing.Id;
            _logger.LogInformation("Ingest node {NodeId} joined existing session {SessionId} for uid {Uid}",
                nodeId, sessionId, uid);
        }
        else
        {
            var session = new Session
            {
                Uid = uid,
                RoomId = req.RoomId?.ToString(),
                Title = req.Title,
                UserName = req.UserName,
                StartTime = req.LiveStartTime ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                LiveStartTime = req.LiveStartTime,
                EndTime = 0,
                // 上报中的会话用 ingest: 前缀，封盘后改为 sqlite:
                FilePath = $"ingest:{nodeId}:{req.LiveStartTime}",
                SummaryJson = "{}",
                GiftSummaryJson = "{}",
                NodeId = nodeId,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };
            db.Sessions.Add(session);
            await db.SaveChangesAsync();
            sessionId = session.Id;
            _logger.LogInformation("Ingest node {NodeId} created session {SessionId} for uid {Uid}",
                nodeId, sessionId, uid);

            // 提前建好场次库文件，后续批量写入直接复用
            var startTs = session.StartTime ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _sessionDb.OpenSessionAsync(uid, startTs);
        }

        var stateKey = $"{nodeId}:{sessionId}";
        _uploads[stateKey] = new NodeUploadState
        {
            NodeId = nodeId,
            SessionId = sessionId,
            Uid = uid,
            LiveStartTime = req.LiveStartTime ?? 0
        };

        return sessionId;
    }

    // ─── 事件上报 ───────────────────────────────────────────────

    /// <summary>
    /// 批量写入事件。返回实际入库条数（重复的 message_id 会被忽略）。
    /// </summary>
    public async Task<IngestAppendResult> AppendEventsAsync(int sessionId, string nodeId, List<RecordedDanmakuEvent> events)
    {
        if (events.Count == 0) return new IngestAppendResult(0, 0);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var session = await db.Sessions.FindAsync(sessionId);
        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        var startTs = session.StartTime ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var conn = await _sessionDb.OpenSessionAsync(session.Uid ?? "", startTs);

        var before = await GetRowCountAsync(conn);
        await _sessionDb.WriteBatchAsync(conn, events);
        var after = await GetRowCountAsync(conn);
        var accepted = (int)(after - before);

        var stateKey = $"{nodeId}:{sessionId}";
        if (_uploads.TryGetValue(stateKey, out var state))
        {
            state.LastSeenUtc = DateTime.UtcNow;
            state.TotalEvents += events.Count;
            var ts = events.Select(e => e.Timestamp).ToList();
            var min = ts.Min();
            var max = ts.Max();
            if (state.MinTimestamp == 0 || min < state.MinTimestamp) state.MinTimestamp = min;
            if (max > state.MaxTimestamp) state.MaxTimestamp = max;

            // 记录该节点覆盖的时段，便于前端展示"这场由哪些节点拼接"
            await UpdateProviderNodesAsync(db, session, state);
        }

        var skipped = events.Count - accepted;
        if (skipped > 0)
        {
            _logger.LogDebug("Ingest session {SessionId} from {NodeId}: {Accepted} accepted, {Skipped} duplicates",
                sessionId, nodeId, accepted, skipped);
        }

        return new IngestAppendResult(events.Count, accepted);
    }

    private static async Task<long> GetRowCountAsync(Microsoft.Data.Sqlite.SqliteConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM danmaku_messages;";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    private async Task UpdateProviderNodesAsync(DanmuContext db, Session session, NodeUploadState state)
    {
        var list = ParseProviderNodes(session.ProviderNodes);
        var entry = list.FirstOrDefault(p => p.NodeId == state.NodeId);
        if (entry == null)
        {
            entry = new ProviderNodeInfo { NodeId = state.NodeId };
            list.Add(entry);
        }

        entry.From = state.MinTimestamp;
        entry.To = state.MaxTimestamp;
        entry.Count = state.TotalEvents;
        session.ProviderNodes = JsonSerializer.Serialize(list, JsonOptions);
        await db.SaveChangesAsync();
    }

    private static List<ProviderNodeInfo> ParseProviderNodes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<ProviderNodeInfo>();
        try
        {
            return JsonSerializer.Deserialize<List<ProviderNodeInfo>>(json, JsonOptions) ?? new List<ProviderNodeInfo>();
        }
        catch
        {
            return new List<ProviderNodeInfo>();
        }
    }

    // ─── 心跳 / 结束 ────────────────────────────────────────────

    public async Task HeartbeatAsync(int sessionId, string nodeId, long? totalEvents)
    {
        var stateKey = $"{nodeId}:{sessionId}";
        if (_uploads.TryGetValue(stateKey, out var state))
        {
            state.LastSeenUtc = DateTime.UtcNow;
            if (totalEvents.HasValue) state.TotalEvents = totalEvents.Value;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var node = await db.IngestNodes.FirstOrDefaultAsync(n => n.NodeId == nodeId);
        if (node != null)
        {
            node.LastSeenAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// 采集端结束上报。所有节点都结束后把会话封盘为 sqlite: 路径。
    /// </summary>
    public async Task EndSessionAsync(int sessionId, string nodeId, long? endTime)
    {
        var stateKey = $"{nodeId}:{sessionId}";
        _uploads.TryRemove(stateKey, out _);

        // 还有其它节点在上报同一会话时，不封盘
        var stillActive = _uploads.Values.Any(s => s.SessionId == sessionId);
        if (stillActive)
        {
            _logger.LogInformation("Session {SessionId}: node {NodeId} finished, other nodes still uploading", sessionId, nodeId);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var session = await db.Sessions.FindAsync(sessionId);
        if (session == null) return;

        var startTs = session.StartTime ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var relative = _sessionDb.GetRelativeDbPath(session.Uid ?? "", startTs);
        await _sessionDb.CloseSessionAsync(session.Uid ?? "");

        session.FilePath = "sqlite:" + relative;
        session.EndTime = endTime ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await db.SaveChangesAsync();

        _logger.LogInformation("Session {SessionId} finalized from ingest, file={FilePath}", sessionId, session.FilePath);
    }

    // ─── 节点信息 ───────────────────────────────────────────────

    public IReadOnlyList<object> GetActiveUploads()
    {
        return _uploads.Values
            .Select(s => (object)new
            {
                nodeId = s.NodeId,
                sessionId = s.SessionId,
                uid = s.Uid,
                liveStartTime = s.LiveStartTime,
                totalEvents = s.TotalEvents,
                from = s.MinTimestamp,
                to = s.MaxTimestamp,
                lastSeenSecondsAgo = (int)(DateTime.UtcNow - s.LastSeenUtc).TotalSeconds
            })
            .ToList();
    }

    /// <summary>
    /// 清理长时间无心跳的上报会话（由 HealthCheck 周期调用）。
    /// </summary>
    public async Task PruneStaleUploadsAsync()
    {
        var now = DateTime.UtcNow;
        var stale = _uploads.Where(kv => now - kv.Value.LastSeenUtc > HeartbeatTimeout).ToList();
        foreach (var (key, state) in stale)
        {
            _logger.LogWarning("Ingest node {NodeId} stopped heartbeating for session {SessionId}, finalizing",
                state.NodeId, state.SessionId);
            try
            {
                await EndSessionAsync(state.SessionId, state.NodeId, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to finalize stale ingest session {SessionId}", state.SessionId);
            }
        }
    }
}

public class IngestSessionStartRequest
{
    public string? Uid { get; set; }
    public long? RoomId { get; set; }
    public string? Title { get; set; }
    public string? UserName { get; set; }
    /// <summary>B 站下发的直播开始时间（毫秒），用于多节点归并同一场直播。</summary>
    public long? LiveStartTime { get; set; }
}

public record IngestAppendResult(int Received, int Accepted);

public class ProviderNodeInfo
{
    public string NodeId { get; set; } = "";
    public long From { get; set; }
    public long To { get; set; }
    public long Count { get; set; }
}
