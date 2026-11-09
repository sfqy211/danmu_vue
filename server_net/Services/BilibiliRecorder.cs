using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Danmu.Server.Models;
using Danmu.Server.Utils;
using Microsoft.Data.Sqlite;

namespace Danmu.Server.Services;

public class BilibiliRecorder : IDisposable
{
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan RecorderHeartbeatInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RecorderHeartbeatTtl = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RedisIncrementalDumpInterval = TimeSpan.FromMinutes(1);

    private readonly long _roomId;
    private readonly string _uid;
    private readonly string _name;
    private readonly ILogger _logger;
    private readonly RedisService _redis;
    private readonly BiliAccountService _accountService;
    private readonly SessionDbService _sessionDb;
    private BilibiliService? _bilibiliService;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private bool _isDisposed;
    private string? _token;
    private string? _host;
    private long _currentAccountUid;
    private string? _currentSessionKey;
    private Task? _recorderHeartbeatTask;
    private Task? _redisDumpTask;
    private long _dumpOffset;
    private long _sessionStartTimestamp;
    private SqliteConnection? _sessionDbConn;
    private string _title = "未知直播";
    private string _userName = "未知主播";
    private long _realRoomId;
    private volatile bool _stopRequestedAfterOfflineDetected;
    /// <summary>Room-specific emoticon map keyed by trigger text (e.g. "[桃几表情包_好听]"), fetched from GetEmoticons API.</summary>
    private Dictionary<string, EmoticonInfo>? _roomEmoticonMap;

    /// <summary>
    /// SC 去重窗口：同一笔 SC 会以 SUPER_CHAT_MESSAGE / SUPER_CHAT_MESSAGE_JPN 重复下发
    /// （data.id 相同），只存首条，避免双语 SC 重复入库。有界 FIFO，长时间直播不无限增长。
    /// </summary>
    private readonly HashSet<string> _seenSuperChatIds = new();
    private readonly Queue<string> _superChatIdOrder = new();
    private readonly object _superChatDedupLock = new();
    private const int SuperChatDedupCapacity = 512;

    // Delegate to check for active session key
    public Func<string, long, Task<string?>>? CheckActiveSession;
    // Delegate to update last live time and stats in DB
    public Func<string, long, long, int, int, int, Task>? UpdateVupStats;
    // Delegate to notify session started
    public event Func<string, long, string, string, long, string, Task>? OnSessionStarted;
    // Delegate to notify session ended
    public event Func<string, long, long, string, Task>? OnSessionEnded;
    public event Func<string, long, string, Task>? OnTitleChanged;
    public event Func<string, long, string, Task>? OnRecorderStopped;

    public string Status { get; private set; } = "stopped";
    /// <summary>
    /// Time when this recorder instance was registered into ProcessManager.
    /// </summary>
    public DateTime RegisteredAt { get; set; }
    public DateTime StartTime { get; private set; }
    public string Uptime => Status != "stopped" ? $"{(int)(DateTime.Now - StartTime).TotalMinutes}m" : "0s";
    public int Pid => _receiveTask?.Id ?? 0;
    public string Uid => _uid;
    public long RoomId => _roomId;
    public string DisplayName => _name;
    public string ProcessName => $"danmu-{_uid}";
    public string RecorderHeartbeatKey => $"recorder:heartbeat:{_uid}:{_roomId}";

    // Real live status from Bilibili API (updated by WaitForLiveAsync / HeartbeatLoopAsync)
    public int LiveStatus { get; private set; }
    public long? LiveStartTime { get; private set; }

    public BilibiliRecorder(long roomId, string uid, string? name, ILogger logger, RedisService redis, BiliAccountService accountService, SessionDbService sessionDb)
    {
        _roomId = roomId;
        _uid = string.IsNullOrWhiteSpace(uid) ? roomId.ToString() : uid;
        _name = name ?? roomId.ToString();
        _logger = logger;
        _redis = redis;
        _accountService = accountService;
        _sessionDb = sessionDb;
    }

    public async Task StartAsync(string token, string host, BilibiliService bilibiliService, long realRoomId)
    {
        if (Status == "online") return;

        _bilibiliService = bilibiliService;
        _realRoomId = realRoomId > 0 ? realRoomId : _roomId;

        try
        {
             await RefreshRoomInfoAsync(syncCurrentSession: false);
             // Issue #20: Use structured logging instead of string interpolation
             _logger.LogInformation("Room Info: {Title} (@{UserName})", _title, _userName);
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Failed to get room info on start");
        }

        // Fetch room-specific emoticon packages for merging into DANMU_MSG emots
        try
        {
            var cookie = await GetCookieAsync();
            if (!string.IsNullOrEmpty(cookie))
            {
                _roomEmoticonMap = await bilibiliService.GetRoomEmoticonsAsync(_realRoomId, cookie);
                if (_roomEmoticonMap != null)
                {
                    _logger.LogInformation("Fetched {Count} room emoticons for room {RoomId}", _roomEmoticonMap.Count, _realRoomId);
                }
            }
            else
            {
                _logger.LogWarning("No cookie available, skipping room emoticon fetch");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch room emoticons, will fall back to default emoticons only");
        }

        _token = token;
        _host = host;
        _stopRequestedAfterOfflineDetected = false;
        Status = "online";
        StartTime = DateTime.Now;
        _cts = new CancellationTokenSource();

        _recorderHeartbeatTask = Task.Run(async () => await RecorderHeartbeatLoopAsync(_cts.Token));
        _redisDumpTask = Task.Run(async () => await RedisIncrementalDumpLoopAsync(_cts.Token));
        _receiveTask = Task.Run(async () => await KeepAliveLoopAsync(_cts.Token));
    }

    private async Task KeepAliveLoopAsync(CancellationToken token)
    {
        int rapidFailureCount = 0;
        long? lastAccountUid = null;

        while (!token.IsCancellationRequested)
        {
            // Check if any account is available before attempting connection
            var cookie = await GetCookieAsync();
            if (string.IsNullOrEmpty(cookie))
            {
                _logger.LogWarning("All accounts exhausted for room {RoomId}, backing off for 5 minutes.", _roomId);
                Status = "offline";
                await Task.Delay(TimeSpan.FromMinutes(5), token);
                continue;
            }

            DateTime? connectTime = null;
            try
            {
                await WaitForLiveAsync(token);
                connectTime = DateTime.Now;
                await ConnectAsync();

                // Start heartbeat
                _heartbeatTask = HeartbeatLoopAsync(token);

                // Receive loop blocks until connection closes
                await ReceiveLoopAsync(token);
                if (_stopRequestedAfterOfflineDetected)
                {
                    _logger.LogInformation("Room {RoomId} went offline during heartbeat check; finalizing and stopping recorder.", _roomId);
                    await StopAsync();
                    await NotifyRecorderStoppedAsync("stream-ended");
                    return;
                }
            }
            catch (Exception ex)
            {
                if (IsAuthFailure(ex))
                {
                    _logger.LogError(ex, "Auth failure for {RoomId}, will retry with different account.", _roomId);
                    var currentAccountUid = GetCurrentAccountUid();
                    if (currentAccountUid.HasValue)
                    {
                        await _accountService.ReportAccountFailureAsync(currentAccountUid.Value);
                        _logger.LogWarning("Reported account {Uid} as failing for room {Room} due to auth failure.",
                            currentAccountUid.Value, _roomId);
                    }
                    rapidFailureCount = 0;
                    lastAccountUid = null;
                }
                else if (ex is EndOfStreamException)
                {
                    _logger.LogInformation("Room {RoomId} ended live stream; finalizing and stopping recorder.", _roomId);
                    LiveStatus = 0;
                    LiveStartTime = null;
                    await CheckAndCloseStaleSessionAsync();
                    await StopAsync();
                    await NotifyRecorderStoppedAsync("stream-ended");
                    return;
                }
                else if (!(ex is System.Net.WebSockets.WebSocketException ||
                           ex is System.OperationCanceledException ||
                           ex is System.IO.IOException))
                {
                    _logger.LogError(ex, "Recorder error for {RoomId}, retrying in 5s...", _roomId);
                    rapidFailureCount = 0;
                    lastAccountUid = null;
                }
                Status = "reconnecting";
                LiveStatus = 0;
                LiveStartTime = null;
            }
            finally
            {
                // Cleanup before retry
                try
                {
                    if (_ws != null)
                    {
                         _ws.Dispose();
                         _ws = null;
                    }
                } catch { }
            }

            // Unified rapid failure detection: works for both normal return and exception
            if (connectTime.HasValue && (DateTime.Now - connectTime.Value).TotalSeconds < 30)
            {
                var currentAccountUid = GetCurrentAccountUid();
                if (currentAccountUid.HasValue && currentAccountUid.Value == lastAccountUid)
                    rapidFailureCount++;
                else
                {
                    rapidFailureCount = 1;
                    lastAccountUid = currentAccountUid;
                }

                _logger.LogWarning("Rapid disconnect #{Count} for room {RoomId} (lasted {Seconds:F1}s), account={Uid}",
                    rapidFailureCount, _roomId, (DateTime.Now - connectTime.Value).TotalSeconds, currentAccountUid);

                if (rapidFailureCount >= 2 && currentAccountUid.HasValue)
                {
                    await _accountService.ReportAccountFailureAsync(currentAccountUid.Value);
                    _logger.LogWarning("Reported account {Uid} as failing due to rapid disconnections.",
                        currentAccountUid.Value);
                    rapidFailureCount = 0;
                    lastAccountUid = null;
                    // Skip short delay, retry immediately with new account
                    continue;
                }
            }
            else
            {
                // Sustained connection or no connection attempt — reset tracking
                rapidFailureCount = 0;
                lastAccountUid = null;
            }

            if (!token.IsCancellationRequested)
            {
                await Task.Delay(5000, token);
            }
        }

        await NotifyRecorderStoppedAsync("cancelled");
        Status = "stopped";
    }

    private async Task NotifyRecorderStoppedAsync(string reason)
    {
        if (OnRecorderStopped == null) return;

        try
        {
            await OnRecorderStopped.Invoke(_uid, _roomId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify recorder stopped for uid {Uid}, room {RoomId}", _uid, _roomId);
        }
    }

    internal static bool IsAuthFailure(Exception ex)
    {
        // HttpRequestException with 403/401 status code
        if (ex is System.Net.Http.HttpRequestException httpEx && httpEx.StatusCode.HasValue)
        {
            var code = (int)httpEx.StatusCode.Value;
            return code == 403 || code == 401;
        }
        // Fallback: check message for auth-related keywords
        var msg = ex.Message ?? "";
        return msg.Contains("403") || msg.Contains("401") || msg.Contains("Unauthorized")
            || msg.Contains("认证") || msg.Contains("鉴权") || msg.Contains("登录");
    }

    private async Task WaitForLiveAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (_bilibiliService == null) return;
            try
            {
                var (liveStatus, _, _, _, _) = await RefreshRoomInfoAsync(syncCurrentSession: false);
                if (liveStatus == 1)
                {
                    return;
                }
                Status = "offline";
                
                await CheckAndCloseStaleSessionAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to check live status for {_roomId}");
                Status = "offline";
            }

            await Task.Delay(TimeSpan.FromSeconds(60), token);
        }
    }

    public async Task StopAsync()
    {
        if (Status == "stopped") return;

        Status = "stopped";
        LiveStatus = 0;
        LiveStartTime = null;
        _cts?.Cancel();

        if (_redisDumpTask != null)
        {
            try
            {
                await _redisDumpTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
         
        var ws = _ws;
        _ws = null;
        if (ws != null)
        {
            try
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stop", CancellationToken.None);
            }
            catch { }
            ws.Dispose();
        }

        await ClearRecorderHeartbeatAsync();

        // Only on explicit Stop do we finalize the session
        await EndRedisSessionAsync(isFinal: true);
    }

    private async Task RedisIncrementalDumpLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RedisIncrementalDumpInterval, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (token.IsCancellationRequested)
            {
                break;
            }

            await FlushRedisIncrementallyAsync();
        }
    }

    private async Task FlushRedisIncrementallyAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentSessionKey)) return;
        // Issue #3: Capture connection to local variable to avoid TOCTOU race
        var conn = _sessionDbConn;
        if (conn == null) return;

        // 连接被外部关掉时（历史上 CloseSessionAsync 按 uid 关共享连接所致），
        // 这里自动重开一次再继续，避免每分钟刷一条同样的错误、数据一直积在 Redis 里。
        if (conn.State != System.Data.ConnectionState.Open)
        {
            try
            {
                _logger.LogWarning(
                    "Session SQLite connection for uid {Uid} was closed externally, reopening", _uid);
                conn = await _sessionDb.OpenSessionAsync(_uid, _sessionStartTimestamp);
                _sessionDbConn = conn;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reopen session SQLite connection for uid {Uid}", _uid);
                return;
            }
        }

        try
        {
            var listKey = _currentSessionKey + ":list";
            var metaKey = _currentSessionKey + ":meta";
            var totalCount = await _redis.GetListLengthAsync(listKey);
            if (totalCount <= _dumpOffset)
            {
                return;
            }

            var start = _dumpOffset;
            var stop = totalCount - 1;
            var messages = await _redis.GetListRangeAsync(listKey, start, stop);
            if (messages.Count == 0)
            {
                _dumpOffset = totalCount;
                await _redis.SetMetadataFieldAsync(metaKey, "dump_offset", _dumpOffset.ToString());
                return;
            }

            var events = DeserializeEvents(messages);
            if (events.Count > 0)
            {
                await _sessionDb.WriteBatchAsync(conn, events);
            }

            _dumpOffset = totalCount;
            await _redis.SetMetadataFieldAsync(metaKey, "dump_offset", _dumpOffset.ToString());
            _logger.LogDebug("Incrementally dumped {Count} messages to SQLite (offset={Offset})", events.Count, _dumpOffset);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to incrementally dump Redis session to SQLite for uid {Uid}, room {RoomId}", _uid, _roomId);
        }
    }

    private List<RecordedDanmakuEvent> DeserializeEvents(List<string> jsonMessages)
    {
        var events = new List<RecordedDanmakuEvent>(jsonMessages.Count);
        foreach (var json in jsonMessages)
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                var evt = JsonSerializer.Deserialize<RecordedDanmakuEvent>(json, EventJsonOptions);
                if (evt != null) events.Add(evt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize danmaku event for SQLite write");
            }
        }
        return events;
    }

    private static async Task<long> GetSqliteRowCountAsync(SqliteConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM danmaku_messages";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    private async Task RecorderHeartbeatLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await _redis.SetStringWithExpiryAsync(
                    RecorderHeartbeatKey,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
                    RecorderHeartbeatTtl);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to write recorder heartbeat for uid {Uid}, room {RoomId}", _uid, _roomId);
            }

            try
            {
                await Task.Delay(RecorderHeartbeatInterval, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ClearRecorderHeartbeatAsync()
    {
        try
        {
            await _redis.DeleteKeyAsync(RecorderHeartbeatKey);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to clear recorder heartbeat for uid {Uid}, room {RoomId}", _uid, _roomId);
        }
    }

    private async Task ConnectAsync()
    {
        _ws = new ClientWebSocket();

        // 注意：不要在这里加 User-Agent/Cookie/Origin 等头。B 站弹幕 WS 网关只接受
        // 极简升级请求（与 bilidanmu/tungstenite 默认行为一致），带浏览器头反而会被断开。
        // Atomically get cookie AND account UID from the same account
        var (cookie, accountUid) = await _accountService.GetCookieWithAccountForRoomAsync(_uid);
        
        if (string.IsNullOrEmpty(cookie) || !accountUid.HasValue)
            throw new InvalidOperationException($"No available account for room {_roomId}");

        _currentAccountUid = accountUid.Value;

        // 鉴权模式：登录态（真实 uid）能拿到完整用户名与全部公开事件；匿名（uid=0）时
        // B 站会把用户名打码成 "xxx***"，且看不到部分需要登录态的事件。
        // 优先登录态，cookie/token 失效时自动降级为匿名，保证录制不中断。
        var useLoginAuth = true;
        var authUid = _currentAccountUid;

        // Refresh token using the SAME cookie that will be used for the auth packet.
        // This ensures token and UID come from the same account, fixing the token/UID mismatch bug.
        if (_bilibiliService != null)
        {
            try
            {
                // conf 用账号 cookie 签发登录态 token，鉴权时配真实 uid（二者必须配对，否则被风控秒断）。
                var (token, host, realRoomId) = await _bilibiliService.GetDanmakuConfAsync(_roomId, cookie);
                _token = token;
                if (!string.IsNullOrWhiteSpace(host)) _host = host;
                if (realRoomId > 0) _realRoomId = realRoomId;

                if (string.IsNullOrWhiteSpace(token))
                {
                    throw new InvalidOperationException("Empty danmaku token");
                }

                _logger.LogInformation("Danmaku conf ok for room {RoomId}: account={AccountUid}, cookieLen={CookieLen}, tokenLen={TokenLen}",
                    _roomId, _currentAccountUid, cookie.Length, token.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login danmaku conf failed for room {RoomId} (account={AccountUid}, cookieLen={CookieLen}), falling back to anonymous auth",
                    _roomId, _currentAccountUid, cookie.Length);
                useLoginAuth = false;
                authUid = 0;

                // 降级时必须重新用匿名 conf 取 token（登录 token 配 uid=0 会配对失败被秒断）
                try
                {
                    var (anonToken, anonHost, anonRoomId) = await _bilibiliService.GetDanmakuConfAsync(_roomId, "");
                    if (!string.IsNullOrWhiteSpace(anonToken))
                    {
                        _token = anonToken;
                        if (!string.IsNullOrWhiteSpace(anonHost)) _host = anonHost;
                        if (anonRoomId > 0) _realRoomId = anonRoomId;
                        _logger.LogInformation("Using anonymous danmaku token for room {RoomId}", _roomId);
                    }
                }
                catch (Exception anonEx)
                {
                    _logger.LogWarning(anonEx, "Anonymous danmaku conf also failed for room {RoomId}, using cached token", _roomId);
                }
            }
        }
        
        var uriString = string.IsNullOrWhiteSpace(_host) ? "wss://broadcastlv.chat.bilibili.com/sub" : _host;

        // B 站风控按 TLS 指纹拦截非浏览器客户端的 wss 连接：.NET SChannel 的 wss 握手会被
        // 接受后秒断（Node/rustls 可正常连接，实测 2026-11）。改走非 TLS 的 2244 端口
        // （tiny-bilibili-ws 的 WEBSOCKET_PORT），明文连接没有 TLS 指纹可查，实测可正常鉴权。
        // 边缘节点偶发握手挂起，加 15s 超时让重连循环接管。
        var uri = BuildPlainDanmakuUri(uriString);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts!.Token);
        connectCts.CancelAfter(TimeSpan.FromSeconds(15));
        await _ws.ConnectAsync(uri, connectCts.Token);

        var authBody = JsonSerializer.Serialize(new
        {
            // 鉴权 uid 必须与 conf 签发的 token 类型配对：登录态 token 配真实 uid，
            // 匿名 token 配 0；不配对会被风控秒断。
            uid = useLoginAuth ? authUid : 0,
            roomid = _realRoomId > 0 ? _realRoomId : _roomId,
            protover = 3, // 3 = Brotli（ProcessPacket 已支持）
            platform = "web",
            type = 2,
            buvid = GetBuvid3(cookie),
            key = _token
        });

        var bodyBytes = Encoding.UTF8.GetBytes(authBody);
        await SendPacketAsync(7, bodyBytes);
        _logger.LogInformation("Connected to room {RoomId} (ws plain 2244, {AuthMode} auth), account={AccountUid}",
            _roomId, useLoginAuth ? "login" : "anonymous", _currentAccountUid);
    }

    /// <summary>
    /// 把 GetDanmakuConf 返回的 wss 地址转换为非 TLS 弹幕地址 ws://{host}:2244/sub。
    /// </summary>
    internal static Uri BuildPlainDanmakuUri(string confUri)
    {
        var host = confUri;
        if (Uri.TryCreate(confUri, UriKind.Absolute, out var parsed) && !string.IsNullOrEmpty(parsed.Host))
        {
            host = parsed.Host;
        }
        else
        {
            var noScheme = confUri.Replace("wss://", "").Replace("ws://", "");
            host = noScheme.Split('/')[0].Split(':')[0];
        }
        return new Uri($"ws://{host}:2244/sub");
    }

    /// <summary>
    /// 从 cookie 中提取 buvid3；TV 登录 cookie 通常没有，回退为占位值（与 bilidanmu 一致）。
    /// </summary>
    internal static string GetBuvid3(string? cookie)
    {
        if (string.IsNullOrWhiteSpace(cookie)) return "buvid-missing";
        foreach (var part in cookie.Split(';'))
        {
            var kv = part.Trim();
            if (kv.StartsWith("buvid3=", StringComparison.OrdinalIgnoreCase) && kv.Length > "buvid3=".Length)
            {
                return kv["buvid3=".Length..];
            }
        }
        return "buvid-missing";
    }

    private async Task SendPacketAsync(int operation, byte[] body)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) return;

        var header = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)(16 + body.Length));
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4, 2), 16);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6, 2), 1);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8, 4), (uint)operation);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12, 4), 1);

        var buffer = new byte[16 + body.Length];
        Array.Copy(header, 0, buffer, 0, 16);
        Array.Copy(body, 0, buffer, 16, body.Length);

        await _ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, true, _cts!.Token);
    }

    /// <summary>鉴权成功后立即发送一次心跳；失败不影响主流程。</summary>
    private async Task SendHeartbeatSafeAsync()
    {
        try
        {
            await SendPacketAsync(2, Encoding.UTF8.GetBytes("[object Object]"));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Immediate heartbeat after auth failed for room {RoomId}", _roomId);
        }
    }

    private async Task<string?> GetCookieAsync()
    {
        var (cookie, _) = await _accountService.GetCookieWithAccountForRoomAsync(_uid);
        return cookie;
    }

    private long? GetCurrentAccountUid()
    {
        return _currentAccountUid > 0 ? _currentAccountUid : null;
    }

    private async Task HeartbeatLoopAsync(CancellationToken token)
    {
        int heartbeatCount = 0;
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(30000, token);

            // Fail-safe: Check live status via API every 60s
            heartbeatCount++;
            if (heartbeatCount % 2 == 0 && _bilibiliService != null)
            {
                try
                {
                    var (liveStatus, _, _, _, _) = await RefreshRoomInfoAsync(syncCurrentSession: true);
                    if (liveStatus != 1)
                    {
                        // Issue #20: Use structured logging
                        _logger.LogInformation("Detected offline via API check for {RoomId}", _roomId);
                        LiveStatus = 0;
                        LiveStartTime = null;
                        await CheckAndCloseStaleSessionAsync();
                        _stopRequestedAfterOfflineDetected = true;
                        if (_ws != null) 
                        {
                            try 
                            {
                                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Offline", token);
                            } 
                            catch {}
                        }
                        break;
                    }
                }
                catch (Exception ex)
                {
                    // Issue #20: Use structured logging
                    _logger.LogWarning(ex, "Failed to check live status in heartbeat for room {RoomId}", _roomId);
                }
            }

            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                try {
                    // 心跳包体与参考项目一致（"[object Object]"），服务端以此回复人气值
                    await SendPacketAsync(2, Encoding.UTF8.GetBytes("[object Object]"));
                } catch { break; }
            }
            else 
            {
                 break;
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        var buffer = new byte[65536];
        // B 站会推送超过 64KB 的压缩弹幕批次，被拆成多次 ReceiveAsync 返回；
        // 必须按 EndOfMessage 拼装完整消息后再处理，否则大包被当残包丢弃且字节流错位，
        // 导致后续礼物/SC 等消息批量解析失败。
        var pending = new MemoryStream();
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (_ws == null || _ws.State != WebSocketState.Open)
                {
                    return; // Connection was intentionally closed
                }

                try
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("WebSocket closed for room {RoomId}: status={Status}, description={Desc}",
                            _roomId, _ws?.CloseStatus, _ws?.CloseStatusDescription);
                        return;
                    }

                    if (result.Count > 0)
                    {
                        pending.Write(buffer, 0, result.Count);
                    }

                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    if (pending.Length == 0)
                    {
                        continue;
                    }

                    var data = pending.ToArray();
                    pending.SetLength(0);
                    ProcessPacket(data);
                }
                catch (System.Net.WebSockets.WebSocketException ex)
                {
                    // 连接被服务端重置时记录原因，便于排查风控/网络问题
                    _logger.LogInformation("WS receive aborted for room {RoomId}: {Msg}, state={State}", _roomId, ex.Message, _ws?.State);
                    return; // Connection closed
                }
            }
        }
        finally
        {
            pending.Dispose();
        }
    }

    private void ProcessPacket(byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            if (buffer.Length - offset < 16) break;

            var packetLen = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset, 4));
            if (packetLen < 16) break; 
            if (buffer.Length - offset < packetLen) break;

            var headerLen = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(offset + 4, 2));
            var protoVer = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(offset + 6, 2));
            var op = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset + 8, 4));
            var seq = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset + 12, 4));

            var bodyLen = (int)packetLen - headerLen;
            var body = buffer.AsSpan(offset + headerLen, bodyLen).ToArray();

            if (op == 5) // MESSAGE
            {
                if (protoVer == 0) // JSON
                {
                    HandleMessage(body);
                }
                else if (protoVer == 2) // Zlib
                {
                    try
                    {
                        using var ms = new MemoryStream(body);
                        using var zs = new ZLibStream(ms, CompressionMode.Decompress);
                        using var outMs = new MemoryStream();
                        zs.CopyTo(outMs);
                        ProcessPacket(outMs.ToArray());
                    }
                    catch (Exception ex) 
                    { 
                        if (ex is EndOfStreamException) throw;
                        _logger.LogError(ex, "Zlib decompression failed"); 
                    }
                }
                else if (protoVer == 3) // Brotli
                {
                    try
                    {
                        using var ms = new MemoryStream(body);
                        using var bs = new BrotliStream(ms, CompressionMode.Decompress);
                        using var outMs = new MemoryStream();
                        bs.CopyTo(outMs);
                        ProcessPacket(outMs.ToArray());
                    }
                    catch (Exception ex) 
                    { 
                        if (ex is EndOfStreamException) throw;
                        _logger.LogError(ex, "Brotli decompression failed"); 
                    }
                }
            }
            else if (op == 8) // CONNECT_SUCCESS
            {
                _logger.LogInformation("Room {RoomId} auth success", _roomId);
                // 鉴权成功后立即发送首个心跳（tiny-bilibili-ws/bilidanmu 同款行为），
                // 等待 30s 周期心跳会被服务端断开
                _ = FireAndForgetAsync(SendHeartbeatSafeAsync(), "FirstHeartbeat");
                // Issue #11: Fire-and-forget with error handling
                _ = FireAndForgetAsync(StartRedisSessionAsync(), "StartRedisSession");
            }
            else if (op == 3) // HEARTBEAT_REPLY
            {
                // Popularity payload currently not persisted.
            }

            offset += (int)packetLen;
        }
    }

    private void HandleMessage(byte[] body)
    {
        try
        {
            var json = Encoding.UTF8.GetString(body);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("cmd", out var cmdProp))
            {
                var cmd = cmdProp.GetString();

                if (cmd == "PREPARING")
                {
                    throw new EndOfStreamException("Stream ended (PREPARING)");
                }

                if (cmd == "ROOM_CHANGE")
                {
                    if (root.TryGetProperty("data", out var data) && data.TryGetProperty("title", out var titleProp))
                    {
                        var newTitle = titleProp.GetString();
                        if (!string.IsNullOrWhiteSpace(newTitle))
                        {
                            _ = FireAndForgetAsync(UpdateTitleAsync(newTitle), "UpdateTitle");
                        }
                    }
                }

                if (cmd == "LOG_IN_NOTICE")
                {
                    _logger.LogWarning("Received LOG_IN_NOTICE for uid {Uid}. Current cookie/session may be degraded or expired.", _uid);
                }

                // 同一笔 SC 会以 SUPER_CHAT_MESSAGE / SUPER_CHAT_MESSAGE_JPN 重复下发（data.id 相同），
                // 先按 id 去重，只保留首条
                if (cmd == "SUPER_CHAT_MESSAGE" || cmd == "SUPER_CHAT_MESSAGE_JPN")
                {
                    if (!TryAcceptSuperChat(root, cmd))
                    {
                        _logger.LogDebug("Dropped duplicate super chat push for uid {Uid}", _uid);
                        return;
                    }
                }

                // SEND_GIFT_V2 一条消息可携带多件礼物，逐件产出
                var recordedEvents = CreateRecordedEvents(root, cmd);
                foreach (var recordedEvent in recordedEvents)
                {
                    // Merge room-specific emoticons into emots when text contains matching triggers
                    MergeRoomEmoticons(recordedEvent);

                    _ = FireAndForgetAsync(WriteEventToRedisAsync(recordedEvent), "WriteEventToRedis");
                }

                if (recordedEvents.Count == 0 && !string.IsNullOrWhiteSpace(cmd))
                {
                    if (cmd == "SEND_GIFT_V2")
                    {
                        // 礼物已迁移为 protobuf 载荷，解析失败需要显式暴露，否则表现为"礼物收不到"
                        _logger.LogWarning("Failed to parse SEND_GIFT_V2 payload for uid {Uid} (data.pb missing or invalid)", _uid);
                    }
                    else
                    {
                        _logger.LogDebug("Ignoring unsupported danmaku command {Command} for uid {Uid}", cmd, _uid);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (ex is EndOfStreamException) throw;
            _logger.LogWarning(ex, "Failed to handle websocket message for uid {Uid}", _uid);
        }
    }

    /// <summary>
    /// For dmType=1 (emoticon-only) messages, look up the emoticon URL from the room emoticon map
    /// if it wasn't provided by the WebSocket (info[0][15].extra.emots / info[0][13]).
    /// Does NOT merge for dmType=0 (text) messages to avoid misidentifying typed text as emoticons.
    /// </summary>
    private void MergeRoomEmoticons(RecordedDanmakuEvent e)
    {
        if (_roomEmoticonMap == null || string.IsNullOrEmpty(e.Text)) return;
        if (e.DmType != 1) return;
        if (e.Emots != null && e.Emots.Count > 0) return;

        // Try to find the emoticon by matching the whole message text as trigger key
        if (_roomEmoticonMap.TryGetValue(e.Text, out var emot))
        {
            e.Emots = new Dictionary<string, EmoticonInfo> { [e.Text] = emot };
        }
    }

    /// <summary>
    /// 只读空列表，用于"命令不支持/不记录"的返回值，避免每条消息都分配新列表。
    /// </summary>
    private static readonly List<RecordedDanmakuEvent> EmptyEvents = new();

    internal static List<RecordedDanmakuEvent> CreateRecordedEvents(JsonElement root, string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd))
        {
            return EmptyEvents;
        }

        if (cmd.StartsWith("DANMU_MSG", StringComparison.Ordinal))
        {
            var info = root.GetProperty("info");
            var timestamp = TryGetInt64(info[0], 4) ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var medalInfo = GetArrayElement(info, 3);
            var userLevelInfo = GetArrayElement(info, 4);
            var guardLevel = TryGetInt32(info, 7);
            var wealthInfo = GetArrayElement(info, 16);

            var modeInfo = GetArrayElement(info[0], 15);
            var faceUrl = TryGetNestedFace(modeInfo);
            var dmType = TryGetInt32(info[0], 12) ?? 0;
            var emots = TryExtractEmots(modeInfo);
            // B 站弹幕唯一 ID（info[0][15].extra.id_str），用于多节点去重。
            // 取不到时用 uid+时间戳+文本兜底，保证同一条弹幕在任何节点生成同样的键。
            var messageId = TryExtractMessageId(modeInfo)
                ?? BuildFallbackMessageId(GetString(info[2], 0), timestamp, GetString(info, 1));
            // For dmType=1 (emoticon-only), also try info[0][13] which has url/height/width
            if (dmType == 1 && emots == null)
            {
                emots = TryExtractEmoticonFromOptions(info[0], 13, GetString(info, 1) ?? "");
            }

            return new List<RecordedDanmakuEvent>
            {
                new RecordedDanmakuEvent
                {
                    Type = "comment",
                    MessageId = messageId,
                    Timestamp = timestamp,
                    Text = GetString(info, 1) ?? "",
                    User = GetString(info[2], 1) ?? "",
                    Uid = GetString(info[2], 0) ?? "",
                    GuardLevel = guardLevel,
                    MedalLevel = TryGetInt32(medalInfo, 0),
                    MedalName = GetString(medalInfo, 1),
                    MedalAnchor = GetString(medalInfo, 2),
                    MedalRoomId = TryGetInt32(medalInfo, 3),
                    MedalGuardLevel = TryGetInt32(medalInfo, 10),
                    MedalIsLight = TryGetInt32(medalInfo, 11) is int isLight ? isLight == 1 : null,
                    MedalAnchorUid = TryGetInt64(medalInfo, 12),
                    UlLevel = TryGetInt32(userLevelInfo, 0),
                    WealthLevel = TryGetInt32(wealthInfo, 0),
                    Face = NormalizeFaceUrl(faceUrl),
                    DmType = dmType,
                    Emots = emots,
                    RawCommand = cmd
                }
            };
        }

        // SEND_GIFT_V2：B 站迁移后的礼物消息，data.pb 为 base64 protobuf（不再是明文 JSON），
        // 一条消息可携带多件礼物。缺少此分支时新协议房间完全收不到礼物。
        if (cmd == "SEND_GIFT_V2")
        {
            return ParseSendGiftV2(root, cmd);
        }

        if (cmd == "SEND_GIFT")
        {
            var data = root.GetProperty("data");
            var count = TryGetInt32(data, "num") ?? 1;
            var priceRaw = TryGetDouble(data, "price") ?? 0;
            var totalCoinRaw = TryGetDouble(data, "total_coin");
            var discountPriceRaw = TryGetDouble(data, "discount_price");
            var coinType = TryGetString(data, "coin_type");
            return new List<RecordedDanmakuEvent>
            {
                new RecordedDanmakuEvent
                {
                    Type = "gift",
                    // B 站礼物消息唯一 ID：tid 与 rnd 语义相同（时间戳+随机数），优先 tid
                    MessageId = TryGetString(data, "tid") ?? TryGetString(data, "rnd"),
                    Timestamp = (TryGetInt64(data, "timestamp") ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000) * 1000,
                    Name = TryGetString(data, "giftName") ?? TryGetString(data, "gift_name"),
                    Count = count > 0 ? count : 1,
                    Price = NormalizeGoldSeeds(priceRaw),
                    TotalCoin = totalCoinRaw.HasValue ? NormalizeGoldSeeds(totalCoinRaw.Value) : null,
                    DiscountPrice = discountPriceRaw.HasValue ? NormalizeGoldSeeds(discountPriceRaw.Value) : null,
                    IsPriceTotal = false,
                    CoinType = coinType,
                    GuardLevel = TryGetInt32(data, "guard_level"),
                    User = TryGetString(data, "uname") ?? "",
                    Uid = TryGetString(data, "uid") ?? "",
                    Face = NormalizeFaceUrl(TryGetString(data, "face")),
                    RawCommand = cmd
                }
            };
        }

        // SUPER_CHAT_MESSAGE_JPN 与 SUPER_CHAT_MESSAGE 是同一笔 SC 的两种下发（data.id 相同），
        // 已由 HandleMessage 中的 id 去重窗口保证只处理首条，两种命令共用同一解析分支。
        // （参考 bilidanmu：不做 cmd 名过滤，按 id 去重更健壮——无论 B 站以何种形态重复下发都只存一条。）
        if (cmd == "SUPER_CHAT_MESSAGE" || cmd == "SUPER_CHAT_MESSAGE_JPN")
        {
            var data = root.GetProperty("data");
            var userInfo = data.TryGetProperty("user_info", out var u) ? u : default;
            var medalInfo = data.TryGetProperty("medal_info", out var m) ? m : default;
            return new List<RecordedDanmakuEvent>
            {
                new RecordedDanmakuEvent
                {
                    Type = "super_chat",
                    // SC 的 data.id 在 SUPER_CHAT_MESSAGE 与 _JPN 两种下发中相同，
                    // 既是本地去重窗口的键，也是跨节点去重的键
                    MessageId = TryGetString(data, "id"),
                    Timestamp = (TryGetInt64(data, "ts") ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000) * 1000,
                    Price = TryGetDouble(data, "price"),
                    IsPriceTotal = true,
                    Text = TryGetString(data, "message") ?? "",
                    MessageJpn = TryGetString(data, "message_jpn"),
                    MessageTrans = TryGetString(data, "message_trans"),
                    Duration = TryGetInt32(data, "time"),
                    MedalLevel = medalInfo.ValueKind != JsonValueKind.Undefined ? TryGetInt32(medalInfo, "medal_level") : null,
                    MedalName = medalInfo.ValueKind != JsonValueKind.Undefined ? TryGetString(medalInfo, "medal_name") : null,
                    MedalAnchor = medalInfo.ValueKind != JsonValueKind.Undefined ? TryGetString(medalInfo, "anchor_uname") : null,
                    MedalRoomId = medalInfo.ValueKind != JsonValueKind.Undefined ? TryGetInt32(medalInfo, "anchor_roomid") : null,
                    MedalGuardLevel = medalInfo.ValueKind != JsonValueKind.Undefined ? TryGetInt32(medalInfo, "guard_level") : null,
                    MedalIsLight = medalInfo.ValueKind != JsonValueKind.Undefined ? (TryGetInt32(medalInfo, "is_lighted") is int isLight ? isLight == 1 : null) : null,
                    MedalAnchorUid = medalInfo.ValueKind != JsonValueKind.Undefined ? TryGetInt64(medalInfo, "target_id") : null,
                    User = userInfo.ValueKind != JsonValueKind.Undefined ? (TryGetString(userInfo, "uname") ?? "") : "",
                    Uid = userInfo.ValueKind != JsonValueKind.Undefined ? (TryGetString(userInfo, "uid") ?? "") : "",
                    Face = NormalizeFaceUrl(userInfo.ValueKind != JsonValueKind.Undefined ? TryGetString(userInfo, "face") : null),
                    RawCommand = cmd
                }
            };
        }

        if (cmd == "GUARD_BUY")
        {
            var data = root.GetProperty("data");
            return new List<RecordedDanmakuEvent>
            {
                new RecordedDanmakuEvent
                {
                    Type = "guard",
                    // 舰长无独立消息 ID，用 uid+开通时间+等级构造稳定键（多节点一致）
                    MessageId = $"guard-{TryGetString(data, "uid")}-{TryGetInt64(data, "start_time")}-{TryGetInt32(data, "guard_level")}",
                    Timestamp = (TryGetInt64(data, "start_time") ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000) * 1000,
                    Name = TryGetString(data, "gift_name") ?? "guard",
                    Count = Math.Max(1, TryGetInt32(data, "num") ?? 1),
                    Price = NormalizeGoldSeeds(TryGetDouble(data, "price") ?? 0),
                    IsPriceTotal = true,
                    GuardLevel = TryGetInt32(data, "guard_level"),
                    User = TryGetString(data, "username") ?? "",
                    Uid = TryGetString(data, "uid") ?? "",
                    Face = NormalizeFaceUrl(TryGetString(data, "face")),
                    RawCommand = cmd
                }
            };
        }

        if (cmd == "USER_TOAST_MSG")
        {
            var data = root.GetProperty("data");
            return new List<RecordedDanmakuEvent>
            {
                new RecordedDanmakuEvent
                {
                    Type = "guard",
                    // USER_TOAST_MSG 与 GUARD_BUY 描述同一笔购买，前缀区分以免互相吞掉
                    MessageId = $"toast-{TryGetString(data, "uid")}-{TryGetInt64(data, "start_time")}-{TryGetInt32(data, "guard_level")}",
                    Timestamp = (TryGetInt64(data, "start_time") ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000) * 1000,
                    Name = TryGetString(data, "role_name") ?? "guard",
                    Count = Math.Max(1, TryGetInt32(data, "num") ?? 1),
                    Price = NormalizeGoldSeeds(TryGetDouble(data, "price") ?? 0),
                    IsPriceTotal = true,
                    GuardLevel = TryGetInt32(data, "guard_level"),
                    User = TryGetString(data, "username") ?? "",
                    Uid = TryGetString(data, "uid") ?? "",
                    Text = TryGetString(data, "toast_msg"),
                    RawCommand = cmd
                }
            };
        }

        // COMBO_SEND 是连击聚合通知：连击的每笔购买已通过 SEND_GIFT / SEND_GIFT_V2 单独下发，
        // 其 combo_total_coin 是累计金额。参考 biliLive-tools / bilidanmu 的做法完全不记录，
        // 否则连击礼物会被重复存储、重复计数（bug：礼物重复接收）。
        if (cmd == "COMBO_SEND")
        {
            return EmptyEvents;
        }

        return EmptyEvents;
    }

    /// <summary>
    /// 解析 SEND_GIFT_V2 的 data.pb（base64 protobuf）。字段定义来自社区逆向
    /// （参考 bilidanmu / JLiverTool / sjh8130/bili_danmaku 的 SEND_GIFT_V2.proto）：
    /// root{1:uid, 2:uname, 3:face, 5:guard_level, 8:medal_info, 10:repeated gift_item}，
    /// gift_item{1:gift_id, 2:gift_name, 3:num, 5:price, 9:tid, 10:timestamp, 18:action}。
    /// 一条消息可携带多件礼物，逐件产出事件（与 biliLive-tools / bilidanmu 一致）。
    /// </summary>
    private static List<RecordedDanmakuEvent> ParseSendGiftV2(JsonElement root, string cmd)
    {
        var events = new List<RecordedDanmakuEvent>();

        if (!root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("pb", out var pbProp) ||
            pbProp.ValueKind != JsonValueKind.String)
        {
            return events;
        }

        byte[] pbBytes;
        try
        {
            pbBytes = Convert.FromBase64String(pbProp.GetString() ?? "");
        }
        catch (FormatException)
        {
            return events;
        }

        BiliProtoMessage pb;
        try
        {
            pb = BiliProtoMessage.Decode(pbBytes);
        }
        catch (Exception)
        {
            return events;
        }

        var uid = pb.TryGetVarint(1, out var pbUid) ? pbUid.ToString() : "";
        var uname = pb.GetString(2) ?? "";
        var face = NormalizeFaceUrl(pb.GetString(3));

        // guard_level：root 字段 5 优先（biliLive-tools beta proto），回退 medal_info 字段 12（bilidanmu）
        var guardLevel = 0;
        if (pb.TryGetVarint(5, out var rootGuard) && rootGuard > 0)
        {
            guardLevel = (int)rootGuard;
        }
        else
        {
            var medal = pb.GetMessages(8).FirstOrDefault();
            if (medal != null && medal.TryGetVarint(12, out var medalGuard) && medalGuard > 0)
            {
                guardLevel = (int)medalGuard;
            }
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (var item in pb.GetMessages(10))
        {
            var name = item.GetString(2);
            var count = item.TryGetVarint(3, out var num) && num > 0 ? (int)num : 1;
            var priceRaw = item.TryGetVarint(5, out var price) ? (double)price : 0;

            long tsMs;
            if (!item.TryGetVarint(10, out var rawTs) || rawTs <= 0)
            {
                tsMs = nowMs;
            }
            else
            {
                // 时间戳为秒级；异常大值（>1e12）按已是毫秒处理
                tsMs = rawTs > 1_000_000_000_000 ? (long)rawTs : (long)rawTs * 1000;
            }

            events.Add(new RecordedDanmakuEvent
            {
                Type = "gift",
                // V2 礼物唯一 ID：gift_item.tid（protobuf 字段 9）。社区协议定义里是 string，
                // 但个别版本可能编码为 varint，两种形态都兼容。
                MessageId = item.GetString(9)
                    ?? (item.TryGetVarint(9, out var tidVarint) ? tidVarint.ToString() : null),
                Timestamp = tsMs,
                Name = string.IsNullOrWhiteSpace(name) ? "未知礼物" : name,
                Count = count,
                Price = NormalizeGoldSeeds(priceRaw),
                IsPriceTotal = false,
                GuardLevel = guardLevel > 0 ? guardLevel : null,
                User = uname,
                Uid = uid,
                Face = face,
                RawCommand = cmd
            });
        }

        return events;
    }

    /// <summary>
    /// SC 去重：同一笔 SC 会以 SUPER_CHAT_MESSAGE 与 SUPER_CHAT_MESSAGE_JPN 两种命令各下发一次
    /// （data.id 相同，但 id 可能一边是数字一边是字符串），不去重会导致双语 SC 重复入库。
    /// 按归一化 id 只放行首条；窗口有界（512 条，FIFO 淘汰），长时间直播不会无限增长。
    /// 拿不到 id 时退回旧行为：只放行主推送 SUPER_CHAT_MESSAGE。
    /// </summary>
    private bool TryAcceptSuperChat(JsonElement root, string? cmd)
    {
        string? scId = null;
        if (root.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var idProp))
        {
            scId = idProp.ValueKind switch
            {
                JsonValueKind.String => idProp.GetString(),
                JsonValueKind.Number => idProp.ToString(),
                _ => null
            };
        }

        if (string.IsNullOrWhiteSpace(scId))
        {
            return !string.Equals(cmd, "SUPER_CHAT_MESSAGE_JPN", StringComparison.Ordinal);
        }

        lock (_superChatDedupLock)
        {
            if (_seenSuperChatIds.Contains(scId))
            {
                return false;
            }

            _seenSuperChatIds.Add(scId);
            _superChatIdOrder.Enqueue(scId);
            while (_superChatIdOrder.Count > SuperChatDedupCapacity)
            {
                var evicted = _superChatIdOrder.Dequeue();
                _seenSuperChatIds.Remove(evicted);
            }

            return true;
        }
    }

    private async Task WriteEventToRedisAsync(RecordedDanmakuEvent recordedEvent)
    {
        var content = JsonSerializer.Serialize(recordedEvent, EventJsonOptions);
        await WriteToRedisAsync(content);
    }

    internal static double NormalizeGoldSeeds(double rawPrice)
    {
        if (rawPrice <= 0) return 0;
        return rawPrice / 1000.0;
    }

    internal static string? GetString(JsonElement element, int index)
    {
        if (element.ValueKind != JsonValueKind.Array || index < 0 || element.GetArrayLength() <= index)
        {
            return null;
        }

        return element[index].ValueKind switch
        {
            JsonValueKind.String => element[index].GetString(),
            JsonValueKind.Number => element[index].ToString(),
            _ => element[index].ToString()
        };
    }

    internal static JsonElement GetArrayElement(JsonElement element, int index)
    {
        if (element.ValueKind != JsonValueKind.Array || index < 0 || element.GetArrayLength() <= index)
        {
            return default;
        }

        return element[index];
    }

    internal static long? TryGetInt64(JsonElement element, int index)
    {
        if (element.ValueKind != JsonValueKind.Array || index < 0 || element.GetArrayLength() <= index)
        {
            return null;
        }

        return element[index].ValueKind switch
        {
            JsonValueKind.Number when element[index].TryGetInt64(out var value) => value,
            JsonValueKind.String when long.TryParse(element[index].GetString(), out var value) => value,
            _ => null
        };
    }

    internal static int? TryGetInt32(JsonElement element, int index)
    {
        if (element.ValueKind != JsonValueKind.Array || index < 0 || element.GetArrayLength() <= index)
        {
            return null;
        }

        return element[index].ValueKind switch
        {
            JsonValueKind.Number when element[index].TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(element[index].GetString(), out var value) => value,
            _ => null
        };
    }

    internal static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => value.ToString()
        };
    }

    /// <summary>
    /// Normalize Bilibili face URL: ensure it starts with https://, handle protocol-relative URLs.
    /// </summary>
    private static string? NormalizeFaceUrl(string? faceUrl)
    {
        if (string.IsNullOrWhiteSpace(faceUrl)) return null;
        // Ensure protocol prefix
        if (faceUrl.StartsWith("//")) faceUrl = "https:" + faceUrl;
        if (!faceUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
        return faceUrl;
    }

    /// <summary>
    /// Extract face URL from nested user.base.face structure in DANMU_MSG info[0][15].
    /// </summary>
    private static string? TryGetNestedFace(JsonElement modeInfo)
    {
        if (modeInfo.ValueKind != JsonValueKind.Object) return null;
        if (!modeInfo.TryGetProperty("user", out var user)) return null;
        if (user.ValueKind != JsonValueKind.Object) return null;
        if (!user.TryGetProperty("base", out var baseObj)) return null;
        if (baseObj.ValueKind != JsonValueKind.Object) return null;
        return TryGetString(baseObj, "face");
    }

    /// <summary>
    /// Extract emoticon map from info[0][15].extra JSON string.
    /// <summary>
    /// 从 info[0][15].extra 中取弹幕唯一 ID（id_str，34 位十六进制）。
    /// 该值由 B 站下发，同一弹幕在任何客户端收到时都相同，因此可作为跨节点去重键。
    /// </summary>
    private static string? TryExtractMessageId(JsonElement modeInfo)
    {
        if (modeInfo.ValueKind != JsonValueKind.Object) return null;
        if (!modeInfo.TryGetProperty("extra", out var extraElement)) return null;

        string? extraJson = extraElement.ValueKind switch
        {
            JsonValueKind.String => extraElement.GetString(),
            _ => extraElement.ToString()
        };
        if (string.IsNullOrWhiteSpace(extraJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(extraJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            return TryGetString(doc.RootElement, "id_str");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 消息缺少 B 站唯一 ID 时的兜底键：uid + 服务器时间戳 + 文本哈希。
    /// 只要各节点收到的是同一条消息，拼出的键就一致，仍可跨节点去重。
    /// </summary>
    internal static string BuildFallbackMessageId(string? uid, long timestamp, string? text)
    {
        var payload = $"{uid}|{timestamp}|{text}";
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return "fp-" + Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    }

    /// <summary>
    /// The extra field contains an "emots" object keyed by trigger text (e.g. "[热]").
    /// </summary>
    private static Dictionary<string, EmoticonInfo>? TryExtractEmots(JsonElement modeInfo)
    {
        if (modeInfo.ValueKind != JsonValueKind.Object) return null;
        if (!modeInfo.TryGetProperty("extra", out var extraElement)) return null;

        string? extraJson = extraElement.ValueKind switch
        {
            JsonValueKind.String => extraElement.GetString(),
            _ => extraElement.ToString()
        };

        if (string.IsNullOrWhiteSpace(extraJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(extraJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("emots", out var emotsElement)) return null;
            if (emotsElement.ValueKind != JsonValueKind.Object) return null;

            var result = new Dictionary<string, EmoticonInfo>();
            foreach (var prop in emotsElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;

                var emoticon = new EmoticonInfo
                {
                    Url = TryGetString(prop.Value, "url"),
                    EmoticonId = TryGetInt32(prop.Value, "emoticon_id"),
                    EmoticonUnique = TryGetString(prop.Value, "emoticon_unique"),
                    Height = TryGetInt32(prop.Value, "height"),
                    Width = TryGetInt32(prop.Value, "width")
                };

                // Normalize URL
                if (!string.IsNullOrEmpty(emoticon.Url))
                {
                    emoticon.Url = NormalizeFaceUrl(emoticon.Url);
                }

                result[prop.Name] = emoticon;
            }

            return result.Count > 0 ? result : null;
        }
        catch
        {
            // Failed to parse extra JSON, ignore emots
            return null;
        }
    }

    /// <summary>
    /// Extract emoticon info from info[0][13] (emoticon_options) for dmType=1 messages.
    /// The field is a JSON string like {"url": "...", "height": 60, "width": 183, "emoticon_unique": "room_xxx"}.
    /// </summary>
    private static Dictionary<string, EmoticonInfo>? TryExtractEmoticonFromOptions(JsonElement info0, int index, string triggerText)
    {
        var optionsElement = GetArrayElement(info0, index);
        string? optionsJson = null;

        if (optionsElement.ValueKind == JsonValueKind.String)
        {
            optionsJson = optionsElement.GetString();
        }
        else if (optionsElement.ValueKind == JsonValueKind.Object)
        {
            optionsJson = optionsElement.ToString();
        }

        if (string.IsNullOrWhiteSpace(optionsJson) || optionsJson == "{}") return null;

        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var url = TryGetString(root, "url");
            if (string.IsNullOrEmpty(url)) return null;

            var emoticon = new EmoticonInfo
            {
                Url = NormalizeFaceUrl(url),
                EmoticonUnique = TryGetString(root, "emoticon_unique"),
                Height = TryGetInt32(root, "height"),
                Width = TryGetInt32(root, "width")
            };

            return new Dictionary<string, EmoticonInfo> { [triggerText] = emoticon };
        }
        catch
        {
            return null;
        }
    }

    internal static int? TryGetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var result) => result,
            JsonValueKind.String when int.TryParse(value.GetString(), out var result) => result,
            _ => null
        };
    }

    private static long? TryGetInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var result) => result,
            JsonValueKind.String when long.TryParse(value.GetString(), out var result) => result,
            _ => null
        };
    }

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var result) => result,
            JsonValueKind.String when double.TryParse(value.GetString(), out var result) => result,
            _ => null
        };
    }

    private async Task StartRedisSessionAsync()
    {
        try
        {
            try
            {
                await RefreshRoomInfoAsync(syncCurrentSession: false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh room title before creating Redis session for uid {Uid}", _uid);
            }

            // Check for existing session via delegate
            if (CheckActiveSession != null)
            {
                var existingKey = await CheckActiveSession.Invoke(_uid, _roomId);
                if (!string.IsNullOrEmpty(existingKey))
                {
                    _currentSessionKey = existingKey;
                    await RecoverSessionStateAsync();
                    await SyncCurrentSessionMetadataAsync();
                    if (OnTitleChanged != null)
                    {
                        await OnTitleChanged.Invoke(_uid, _roomId, _title);
                    }
                    _logger.LogInformation("Resuming existing Redis session: {SessionKey}", _currentSessionKey);
                    return;
                }
            }

            var timestamp = LiveStartTime.GetValueOrDefault();
            var startTimeIsFallback = false;
            if (timestamp <= 0)
            {
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                startTimeIsFallback = true;
            }
            _sessionStartTimestamp = timestamp;
            _currentSessionKey = $"danmaku:session:{_uid}:{timestamp}";

            var meta = new Dictionary<string, string>
            {
                { "uid", _uid },
                { "room_id", _roomId.ToString() },
                { "real_room_id", _realRoomId.ToString() },
                { "room_title", _title },
                { "user_name", _userName },
                { "video_start_time", timestamp.ToString() },
                { "recording_start_time", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() },
                { "start_time_is_fallback", startTimeIsFallback ? "1" : "0" },
                { "dump_offset", "0" }
            };

            await _redis.SetMetadataAsync(_currentSessionKey + ":meta", meta);
            await _redis.SetLiveSessionKeyAsync(_uid, _currentSessionKey);

            // Open SQLite connection for the new session
            _sessionDbConn = await _sessionDb.OpenSessionAsync(_uid, _sessionStartTimestamp);

            if (OnSessionStarted != null)
            {
                await OnSessionStarted.Invoke(_uid, _roomId, _title, _userName, timestamp, _currentSessionKey);
            }

            _logger.LogInformation("Started recording to Redis: {SessionKey}", _currentSessionKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Redis session");
        }
    }

    /// <summary>
    /// 恢复 session 状态：打开 SQLite 连接，从 SQLite 行数恢复 dump_offset。
    /// 若 Redis 被清空（长度 < SQLite 行数），dump_offset 归零以重新写入新消息。
    /// </summary>
    private async Task RecoverSessionStateAsync()
    {
        var meta = await _redis.GetMetadataAsync(_currentSessionKey + ":meta");
        _sessionStartTimestamp = GetSessionStartTimestamp(meta);
        _sessionDbConn = await _sessionDb.OpenSessionAsync(_uid, _sessionStartTimestamp);

        var listKey = _currentSessionKey + ":list";
        var redisLen = await _redis.GetListLengthAsync(listKey);
        var sqliteCount = _sessionDbConn != null ? await GetSqliteRowCountAsync(_sessionDbConn) : 0;
        // SQLite 已有 sqliteCount 行 = 已从 Redis 前 sqliteCount 条写入过
        // 无论 Redis 是否被部分清除，dumpOffset 都应等于已写入的行数，
        // 避免重复写入已存在于 SQLite 的消息
        _dumpOffset = sqliteCount;
        await _redis.SetMetadataFieldAsync(_currentSessionKey + ":meta", "dump_offset", _dumpOffset.ToString());
        _logger.LogInformation("Recovered session state: SQLite rows={SqliteCount}, Redis len={RedisLen}, dump_offset={DumpOffset}",
            sqliteCount, redisLen, _dumpOffset);
    }

    private async Task WriteToRedisAsync(string content)
    {
        if (_currentSessionKey == null) return;
        try 
        {
             await _redis.PushMessageAsync(_currentSessionKey + ":list", content);
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Failed to write to Redis");
        }
    }

    private async Task UpdateTitleAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title == _title) return;
        _title = title;

        await SyncCurrentSessionMetadataAsync();

        if (OnTitleChanged != null)
        {
            await OnTitleChanged.Invoke(_uid, _roomId, _title);
        }
    }

    private async Task<(int LiveStatus, long? LiveStartTime, int Followers, int GuardNum, int VideoCount)> RefreshRoomInfoAsync(bool syncCurrentSession)
    {
        if (_bilibiliService == null)
        {
            return (0, null, 0, 0, 0);
        }

        var (title, userName, liveStatus, _, _, liveStartTime, followers, guardNum, videoCount) =
            await _bilibiliService.GetRoomInfoAsync(_realRoomId);

        var titleChanged = !string.IsNullOrWhiteSpace(title) && title != _title;
        if (titleChanged)
        {
            _title = title!;
        }

        var userNameChanged = !string.IsNullOrWhiteSpace(userName) && userName != _userName;
        if (userNameChanged)
        {
            _userName = userName!;
        }

        if (syncCurrentSession && _currentSessionKey != null && (titleChanged || userNameChanged))
        {
            await SyncCurrentSessionMetadataAsync();
            if (titleChanged && OnTitleChanged != null)
            {
                await OnTitleChanged.Invoke(_uid, _roomId, _title);
            }
        }

        if (UpdateVupStats != null)
        {
            // Issue #11: Fire-and-forget with error handling
            _ = FireAndForgetAsync(UpdateVupStats(_uid, _roomId, liveStartTime ?? 0, followers, guardNum, videoCount), "UpdateVupStats");
        }

        LiveStatus = liveStatus;
        LiveStartTime = liveStartTime;

        return (liveStatus, liveStartTime, followers, guardNum, videoCount);
    }

    private async Task SyncCurrentSessionMetadataAsync()
    {
        if (_currentSessionKey == null) return;

        var metaKey = _currentSessionKey + ":meta";
        await _redis.SetMetadataFieldAsync(metaKey, "room_title", _title);
        await _redis.SetMetadataFieldAsync(metaKey, "user_name", _userName);
    }

    internal static long GetSessionStartTimestamp(IReadOnlyDictionary<string, string> meta)
    {
        return long.TryParse(meta.GetValueOrDefault("video_start_time"), out var startTimestamp) && startTimestamp > 0
            ? startTimestamp
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    internal static string SanitizeFileName(string title)
    {
        var safeTitle = string.IsNullOrWhiteSpace(title) ? "未知直播" : title.Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            safeTitle = safeTitle.Replace(invalidChar, '_');
        }
        return string.IsNullOrWhiteSpace(safeTitle) ? "未知直播" : safeTitle;
    }


    
    private async Task CheckAndCloseStaleSessionAsync()
    {
        if (_currentSessionKey != null)
        {
             _logger.LogInformation("Closing active session {SessionKey} because room is offline.", _currentSessionKey);
             await EndRedisSessionAsync(isFinal: true);
             return;
        }

        if (CheckActiveSession != null)
        {
            try
            {
                var sessionKey = await CheckActiveSession(_uid, _roomId);
                if (!string.IsNullOrEmpty(sessionKey))
                {
                    _logger.LogInformation("Found stale session {SessionKey} for uid {Uid}. Closing it.", sessionKey, _uid);
                    _currentSessionKey = sessionKey;
                    await RecoverSessionStateAsync();
                    await EndRedisSessionAsync(isFinal: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking/closing stale session");
            }
        }
    }
    
    private int _isEndingSession;

    private async Task EndRedisSessionAsync(bool isFinal)
    {
        if (_currentSessionKey == null) return;
        if (!isFinal) return; // We only dump on final stop now

        // Prevent concurrent execution (e.g., StopAsync and receive loop both triggering)
        if (Interlocked.CompareExchange(ref _isEndingSession, 1, 0) != 0) return;

        try
        {
            _logger.LogInformation("Finalizing session {SessionKey}...", _currentSessionKey);

            var listKey = _currentSessionKey + ":list";
            var metaKey = _currentSessionKey + ":meta";
            var meta = await _redis.GetMetadataAsync(metaKey);

            // Flush remaining Redis tail to SQLite
            var totalCount = await _redis.GetListLengthAsync(listKey);
            var remainingCount = totalCount - _dumpOffset;
            // Issue #3: Capture connection to local variable
            var conn = _sessionDbConn;
            if (remainingCount > 0 && conn != null)
            {
                var remaining = await _redis.GetListRangeAsync(listKey, _dumpOffset, totalCount - 1);
                var events = DeserializeEvents(remaining);
                if (events.Count > 0)
                {
                    await _sessionDb.WriteBatchAsync(conn, events);
                }
                _dumpOffset = totalCount;
                _logger.LogInformation("Flushed final Redis tail ({Count} messages) to SQLite", events.Count);
            }

            // Close SQLite connection
            await _sessionDb.CloseSessionAsync(_uid);
            _sessionDbConn = null;

            var endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var startTimestamp = GetSessionStartTimestamp(meta);
            var sqliteRelativePath = "sqlite:" + _sessionDb.GetRelativeDbPath(_uid, startTimestamp);

            if (OnSessionEnded != null)
            {
                await OnSessionEnded.Invoke(_uid, _roomId, endTime, sqliteRelativePath);
            }

            await _redis.DeleteKeyAsync(_currentSessionKey + ":list");
            await _redis.DeleteKeyAsync(metaKey);
            await _redis.ClearLiveSessionKeyAsync(_uid);
            _currentSessionKey = null;
            _dumpOffset = 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to finalize Redis session to SQLite");
            // 保底：即使归档失败，也要关闭连接并把 session 标记为已结束
            try
            {
                await _sessionDb.CloseSessionAsync(_uid);
                _sessionDbConn = null;
                var endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (OnSessionEnded != null)
                {
                    var sqliteRelativePath = "sqlite:" + _sessionDb.GetRelativeDbPath(_uid, _sessionStartTimestamp);
                    await OnSessionEnded.Invoke(_uid, _roomId, endTime, sqliteRelativePath);
                }
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Failed to notify session end in fallback");
            }
        }
        finally
        {
            Volatile.Write(ref _isEndingSession, 0);
        }
    }

    /// <summary>
    /// Issue #11: Helper to observe exceptions from fire-and-forget async calls.
    /// </summary>
    private async Task FireAndForgetAsync(Task task, string operationName)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fire-and-forget operation {Operation} failed for uid {Uid}", operationName, _uid);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        // Issue #9: Use Task.Run to avoid deadlock from synchronous blocking on async method
        // In ASP.NET Core there's no SynchronizationContext, but this is safer for all contexts
        Task.Run(async () =>
        {
            try
            {
                await StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during StopAsync in Dispose for uid {Uid}", _uid);
            }
            finally
            {
                // 确保 SQLite 连接不泄漏：StopAsync 可能因异常未走到 CloseSessionAsync
                if (_sessionDbConn != null)
                {
                    try { await _sessionDbConn.CloseAsync(); } catch { /* best effort */ }
                    try { await _sessionDbConn.DisposeAsync(); } catch { /* best effort */ }
                    _sessionDbConn = null;
                }
                _cts?.Dispose();
            }
        }).Wait(TimeSpan.FromSeconds(10)); // Wait with timeout to prevent infinite hang
    }
}
