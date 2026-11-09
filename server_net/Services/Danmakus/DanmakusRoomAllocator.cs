using System.Collections.Concurrent;
using Danmu.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Services.Danmakus;

/// <summary>
/// 把"该录谁"分配给采集节点。
///
/// 目标：中心后台配的所有自动录制主播（<c>rooms.auto_record = 1</c>）都有人在录，
/// 但同一房间在同一时刻**只分给一个节点**，避免重复采集（重复数据虽然能靠 message_id 去重，
/// 但会白白浪费带宽和对方的连接数配额）。
///
/// 分配策略：
/// 1. 候选 = 正在直播的房间（LiveStatusService 缓存，未知状态的也纳入，宁可先连上）
/// 2. 保持粘性：节点已经持有的房间只要还在播、且节点还有槽位，就继续给它
/// 3. 补位：按"已持有房间数 / 容量"从少到多排，逐个把空闲房间分下去
/// 4. 回收：下播的房间立刻收回；节点掉线后它持有的房间进入可分配池
///
/// 这样任一台机器关掉，它那部分房间会在下一轮汇报时被其它节点接管。
/// </summary>
public class DanmakusRoomAllocator
{
    private readonly DanmakusRuntimeRegistry _registry;
    private readonly LiveStatusService _liveStatus;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DanmakusRoomAllocator> _logger;

    /// <summary>房间归属：roomId → clientKey。避免两个节点同时录同一个房间。</summary>
    private readonly ConcurrentDictionary<long, string> _roomOwners = new();

    public DanmakusRoomAllocator(
        DanmakusRuntimeRegistry registry,
        LiveStatusService liveStatus,
        IServiceScopeFactory scopeFactory,
        ILogger<DanmakusRoomAllocator> logger)
    {
        _registry = registry;
        _liveStatus = liveStatus;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>一次分配结果，回给客户端用于推送房间。</summary>
    public sealed class AllocationResult
    {
        public List<long> HoldingRooms { get; init; } = new();
        public List<long> NewlyAssigned { get; init; } = new();
        public List<long> Dropped { get; init; } = new();
        public int EffectiveCapacity { get; init; }
        public object? Shortfall { get; init; }
    }

    /// <summary>
    /// 为某个节点重算它应持有的房间集合。
    /// </summary>
    public async Task<AllocationResult> AllocateAsync(
        DanmakusRuntimeRegistry.NodeSession session,
        CancellationToken cancellationToken = default)
    {
        var clientKey = DanmakusRuntimeRegistry.SessionKey(session.NodeId, session.ClientId);
        var capacity = Math.Max(0, session.Capacity);

        var candidates = await LoadCandidatesAsync(cancellationToken);

        var previous = session.HoldingRooms.ToList();
        var previousSet = previous.ToHashSet();

        // 可保留的：本轮仍是候选 且 自己还占着 且 不是在播判断为"已下播"
        var kept = new List<long>();
        foreach (var roomId in previous)
        {
            if (!candidates.Contains(roomId))
            {
                continue;
            }
            // 归属校验：期间被别的节点抢走的话不再保留
            if (_roomOwners.TryGetValue(roomId, out var owner) && owner != clientKey)
            {
                continue;
            }
            kept.Add(roomId);
        }

        // 候选里已经分给他人的房间排除
        var unavailable = _roomOwners
            .Where(kv => kv.Value != clientKey)
            .Select(kv => kv.Key)
            .ToHashSet();

        var free = candidates.Where(roomId => !kept.Contains(roomId) && !unavailable.Contains(roomId)).ToList();

        var target = capacity;
        var assigned = new List<long>(kept);

        // 补位：自由房间按房间号升序（稳定，重连后不会每次都换一批）
        foreach (var roomId in free.OrderBy(id => id))
        {
            if (assigned.Count >= target) break;
            assigned.Add(roomId);
        }

        // 超出容量的裁剪：优先保留本来就连着的（避免抖动），多余的在下一轮收回
        if (assigned.Count > target)
        {
            var connected = session.ConnectedRooms.ToHashSet();
            assigned = assigned
                .OrderByDescending(roomId => connected.Contains(roomId))
                .ThenBy(roomId => roomId)
                .Take(target)
                .ToList();
        }

        // 落库归属，并释放不再持有的
        foreach (var roomId in assigned)
        {
            _roomOwners[roomId] = clientKey;
        }
        foreach (var roomId in previous.Where(id => !assigned.Contains(id)))
        {
            if (_roomOwners.TryGetValue(roomId, out var owner) && owner == clientKey)
            {
                _roomOwners.TryRemove(roomId, out _);
            }
        }

        session.HoldingRooms = assigned;

        var newlyAssigned = assigned.Where(id => !previousSet.Contains(id)).ToList();
        var dropped = previous.Where(id => !assigned.Contains(id)).ToList();

        if (newlyAssigned.Count > 0 || dropped.Count > 0)
        {
            _logger.LogInformation(
                "Node {NodeId}/{ClientId} allocation: holding={Holding} (+{Added} / -{Dropped}) cap={Capacity}",
                session.NodeId, session.ClientId, assigned.Count, newlyAssigned.Count, dropped.Count, capacity);
        }

        // 槽位没占满时告诉客户端还差多少（客户端 UI 会显示"候选不足"）
        object? shortfall = null;
        if (assigned.Count < capacity)
        {
            shortfall = new
            {
                reason = "no_candidates",
                candidateCount = candidates.Count,
                assignableCandidateCount = free.Count,
                blockedBySameAccountCount = 0,
                blockedByOtherAccountsCount = unavailable.Count,
                missingCount = capacity - assigned.Count
            };
        }

        return new AllocationResult
        {
            HoldingRooms = assigned,
            NewlyAssigned = newlyAssigned,
            Dropped = dropped,
            EffectiveCapacity = capacity,
            Shortfall = shortfall
        };
    }

    /// <summary>
    /// 读取房间的实时状态（对外暴露，供 WS 推送主播状态与会话归并使用）。
    /// 走 LiveStatusService 缓存，缓存过期时它会自己回源。
    /// </summary>
    public async Task<LiveState?> ResolveLiveStateAsync(long roomId, CancellationToken cancellationToken = default)
    {
        try
        {
            var cached = await _liveStatus.GetCachedStatusAsync(roomId);
            if (cached != null) return cached;

            // 缓存没命中：直接取一次并回写缓存，避免整轮分配都在等 B 站
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
            var room = await db.Rooms.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RoomId == roomId, cancellationToken);
            if (room == null) return null;

            return await _liveStatus.RefreshStatusAsync(roomId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve live state for room {RoomId}", roomId);
            return null;
        }
    }

    /// <summary>节点断开时释放它名下的所有房间。</summary>
    public void ReleaseNode(DanmakusRuntimeRegistry.NodeSession session)
    {
        var clientKey = DanmakusRuntimeRegistry.SessionKey(session.NodeId, session.ClientId);
        var released = new List<long>();
        foreach (var (roomId, owner) in _roomOwners)
        {
            if (owner == clientKey && _roomOwners.TryRemove(roomId, out _))
            {
                released.Add(roomId);
            }
        }

        if (released.Count > 0)
        {
            _logger.LogInformation("Node {NodeId}/{ClientId} released {Count} rooms: {Rooms}",
                session.NodeId, session.ClientId, released.Count, string.Join(",", released));
        }
    }

    /// <summary>
    /// 候选房间：中心后台启用自动录制、且当前在播（或状态未知）的房间。
    /// 状态未知时也纳入，是因为宁可先连上多录一会儿，也不要漏掉一场直播的开头。
    /// </summary>
    private async Task<List<long>> LoadCandidatesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();

        var roomIds = await db.Rooms
            .Where(r => r.AutoRecord == 1)
            .Select(r => r.RoomId)
            .ToListAsync(cancellationToken);

        var candidates = new List<long>(roomIds.Count);
        foreach (var roomId in roomIds)
        {
            try
            {
                var state = await _liveStatus.GetCachedStatusAsync(roomId);
                // 明确下播的排除；在播或状态未知的都保留
                if (state != null && state.LiveStatus != 1)
                {
                    continue;
                }
                candidates.Add(roomId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to resolve live status for room {RoomId}, treating as candidate", roomId);
                candidates.Add(roomId);
            }
        }

        return candidates;
    }
}
