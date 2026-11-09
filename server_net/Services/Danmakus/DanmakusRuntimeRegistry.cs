using System.Collections.Concurrent;
using System.Text.Json;
using Danmu.Server.Data;
using Danmu.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Services.Danmakus;

/// <summary>
/// 采集节点的在线会话登记表。
///
/// 一个 danmakus 客户端用 clientId 标识自己（同一账号可以在多台机器各跑一个），
/// 它通过 WS 长连接上报"我当前持有哪些房间 / 连上了哪些 / 还剩几个槽位"，
/// 中心据此做**房间分配**：把在播的房间分给它、把已下播的收回来。
///
/// 房间候选来自中心已有的 <see cref="Room"/> 表（AutoRecord=1），
/// 在播状态用 <see cref="LiveStatusService"/> 的缓存 —— 这样多台采集机和中心本机录制
/// 看到的是同一份"应该录谁"的答案，不会各录各的。
/// </summary>
public class DanmakusRuntimeRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, NodeSession> _sessions = new();
    private readonly ILogger<DanmakusRuntimeRegistry> _logger;

    /// <summary>节点最后一次状态汇报超过这个时长就认为掉线。</summary>
    private static readonly TimeSpan PresenceTimeout = TimeSpan.FromSeconds(60);

    public DanmakusRuntimeRegistry(ILogger<DanmakusRuntimeRegistry> logger)
    {
        _logger = logger;
    }

    public sealed class NodeSession
    {
        public string NodeId { get; init; } = "";
        public string ClientId { get; init; } = "";
        public string ClientVersion { get; init; } = "";
        public DateTime ConnectedAtUtc { get; init; } = DateTime.UtcNow;
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>中心分配给它的房间（客户端必须严格遵守，不能自己挑）。</summary>
        public List<long> HoldingRooms { get; set; } = new();

        /// <summary>客户端自报当前已连上的房间。</summary>
        public List<long> ConnectedRooms { get; set; } = new();

        /// <summary>本节点连接上限（capacityOverride 优先）。</summary>
        public int Capacity { get; set; }

        public string? LastError { get; set; }
        public long TotalEvents { get; set; }

        /// <summary>每个房间当前正在进行的上报会话。</summary>
        public ConcurrentDictionary<long, ActiveUpload> ActiveUploads { get; } = new();
    }

    public sealed class ActiveUpload
    {
        public int SessionId { get; init; }
        public long StreamerUid { get; init; }
        public long RoomId { get; init; }
        public long LiveStartTime { get; init; }
        public long LastSeenUtcTicks { get; set; } = DateTime.UtcNow.Ticks;
    }

    public NodeSession Register(string nodeId, string clientId, string clientVersion)
    {
        var session = new NodeSession
        {
            NodeId = nodeId,
            ClientId = clientId,
            ClientVersion = clientVersion
        };
        _sessions[SessionKey(nodeId, clientId)] = session;
        _logger.LogInformation("Danmakus node {NodeId} client {ClientId} connected (version={Version})",
            nodeId, clientId, clientVersion);
        return session;
    }

    public NodeSession? Find(string nodeId, string clientId)
        => _sessions.TryGetValue(SessionKey(nodeId, clientId), out var session) ? session : null;

    public void Remove(string nodeId, string clientId)
    {
        if (_sessions.TryRemove(SessionKey(nodeId, clientId), out _))
        {
            _logger.LogInformation("Danmakus node {NodeId} client {ClientId} disconnected", nodeId, clientId);
        }
    }

    public IReadOnlyList<object> Snapshot()
        => _sessions.Values.Select(s => (object)new
        {
            nodeId = s.NodeId,
            clientId = s.ClientId,
            clientVersion = s.ClientVersion,
            capacity = s.Capacity,
            holdingRooms = s.HoldingRooms,
            connectedRooms = s.ConnectedRooms,
            totalEvents = s.TotalEvents,
            activeUploads = s.ActiveUploads.Values.Select(u => new
            {
                roomId = u.RoomId,
                sessionId = u.SessionId,
                streamerUid = u.StreamerUid,
                liveStartTime = u.LiveStartTime
            }).ToList(),
            lastSeenSecondsAgo = (int)(DateTime.UtcNow - s.LastSeenUtc).TotalSeconds
        }).ToList();

    /// <summary>清理长时间没汇报的节点（进程崩溃时 WS 可能来不及走正常关闭流程）。</summary>
    public void PruneStale()
    {
        var now = DateTime.UtcNow;
        foreach (var (key, session) in _sessions)
        {
            if (now - session.LastSeenUtc > PresenceTimeout)
            {
                _sessions.TryRemove(key, out _);
                _logger.LogWarning("Danmakus node {NodeId} client {ClientId} timed out, removed from registry",
                    session.NodeId, session.ClientId);
            }
        }
    }

    public static string SessionKey(string nodeId, string clientId) => $"{nodeId}:{clientId}";
}
