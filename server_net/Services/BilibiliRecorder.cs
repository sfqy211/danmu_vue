using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Danmu.Server.Models;

namespace Danmu.Server.Services;

public class BilibiliRecorder : IDisposable
{
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan RecorderHeartbeatInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RecorderHeartbeatTtl = TimeSpan.FromSeconds(20);

    private readonly long _roomId;
    private readonly string _uid;
    private readonly string _name;
    private readonly ILogger _logger;
    private readonly RedisService _redis;
    private readonly BiliAccountService _accountService;
    private BilibiliService? _bilibiliService;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private readonly string _danmakuDir;
    private bool _isDisposed;
    private string? _token;
    private string? _host;
    private string? _currentSessionKey;
    private Task? _recorderHeartbeatTask;
    private string _title = "未知直播";
    private string _userName = "未知主播";
    private long _realRoomId;
    
    // Delegate to check for active session key
    public Func<string, long, Task<string?>>? CheckActiveSession;
    // Delegate to update last live time and stats in DB
    public Func<string, long, long, int, int, int, Task>? UpdateVupStats;
    // Delegate to notify session started
    public event Func<string, long, string, string, long, string, Task>? OnSessionStarted;
    // Delegate to notify session ended
    public event Func<string, long, long, string, Task>? OnSessionEnded;
    public event Func<string, long, string, Task>? OnTitleChanged;

    public string Status { get; private set; } = "stopped";
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

    public BilibiliRecorder(long roomId, string uid, string? name, ILogger logger, RedisService redis, BiliAccountService accountService)
    {
        _roomId = roomId;
        _uid = string.IsNullOrWhiteSpace(uid) ? roomId.ToString() : uid;
        _name = name ?? roomId.ToString();
        _logger = logger;
        _redis = redis;
        _accountService = accountService;

        var root = Directory.GetCurrentDirectory();
        _danmakuDir = Environment.GetEnvironmentVariable("DANMAKU_DIR")
                       ?? Path.GetFullPath(Path.Combine(root, "../server/data/danmaku"));
    }

    public async Task StartAsync(string token, string host, BilibiliService bilibiliService, long realRoomId)
    {
        if (Status == "online") return;

        _bilibiliService = bilibiliService;
        _realRoomId = realRoomId > 0 ? realRoomId : _roomId;

        try 
        {
             await RefreshRoomInfoAsync(syncCurrentSession: false);
             _logger.LogInformation($"Room Info: {_title} (@{_userName})");
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Failed to get room info on start");
        }

        _token = token;
        _host = host;
        Status = "online";
        StartTime = DateTime.Now;
        _cts = new CancellationTokenSource();

        _recorderHeartbeatTask = Task.Run(async () => await RecorderHeartbeatLoopAsync(_cts.Token));
        _receiveTask = Task.Run(async () => await KeepAliveLoopAsync(_cts.Token));
    }

    private async Task KeepAliveLoopAsync(CancellationToken token)
    {
        int rapidFailureCount = 0;
        long? lastAccountUid = null;

        while (!token.IsCancellationRequested)
        {
            // Check if any account is available before attempting connection
            var cookie = GetCookie();
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
            }
            catch (Exception ex)
            {
                if (IsAuthFailure(ex))
                {
                    _logger.LogError(ex, "Auth failure for {RoomId}, will retry with different account.", _roomId);
                    var currentAccountUid = GetCurrentAccountUid();
                    if (currentAccountUid.HasValue)
                    {
                        _accountService.ReportAccountFailure(currentAccountUid.Value);
                        _logger.LogWarning("Reported account {Uid} as failing for room {Room} due to auth failure.",
                            currentAccountUid.Value, _roomId);
                    }
                    rapidFailureCount = 0;
                    lastAccountUid = null;
                }
                else if (!(ex is EndOfStreamException ||
                           ex is System.Net.WebSockets.WebSocketException ||
                           ex is System.OperationCanceledException ||
                           ex is System.IO.IOException))
                {
                    _logger.LogError(ex, "Recorder error for {RoomId}, retrying in 5s...", _roomId);
                    rapidFailureCount = 0;
                    lastAccountUid = null;
                }
                Status = "reconnecting";
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
                    _accountService.ReportAccountFailure(currentAccountUid.Value);
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

        Status = "stopped";
    }

    private static bool IsAuthFailure(Exception ex)
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
        _cts?.Cancel();
        
        if (_ws != null)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stop", CancellationToken.None);
            }
            catch { }
            _ws.Dispose();
            _ws = null;
        }

        await ClearRecorderHeartbeatAsync();

        // Only on explicit Stop do we finalize the session
        await EndRedisSessionAsync(isFinal: true);
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
        
        // Add headers if needed
        _ws.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        
        long uid = 0;
        var cookie = GetCookie();
        if (!string.IsNullOrEmpty(cookie))
        {
            var match = Regex.Match(cookie, @"DedeUserID=([^;]+)");
            if (match.Success && long.TryParse(match.Groups[1].Value, out var parsedUid))
            {
                uid = parsedUid;
            }
        }
        
        var uriString = string.IsNullOrWhiteSpace(_host) ? "wss://broadcastlv.chat.bilibili.com/sub" : _host;
        var uri = new Uri(uriString);
        await _ws.ConnectAsync(uri, _cts!.Token);
        
        var authBody = JsonSerializer.Serialize(new
        {
            uid = uid,
            roomid = _realRoomId > 0 ? _realRoomId : _roomId,
            protover = 2, // 2 = Zlib
            platform = "web",
            type = 2,
            key = _token
        });
        
        var bodyBytes = Encoding.UTF8.GetBytes(authBody);
        await SendPacketAsync(7, bodyBytes);
        _logger.LogInformation($"Connected to room {_roomId}");
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

    private string? GetCookie()
    {
        return _accountService.GetCookieForRoom(_uid);
    }

    private long? GetCurrentAccountUid()
    {
        return _accountService.GetAssignedAccountUid(_uid);
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
                        _logger.LogInformation($"Detected offline via API check for {_roomId}");
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
                    _logger.LogWarning($"Failed to check live status in heartbeat: {ex.Message}");
                }
            }

            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                try {
                    await SendPacketAsync(2, Array.Empty<byte>());
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
                    return; // Server closed connection normally
                }

                var data = new byte[result.Count];
                Array.Copy(buffer, data, result.Count);
                
                ProcessPacket(data);
            }
            catch (System.Net.WebSockets.WebSocketException)
            {
                return; // Connection closed
            }
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
                _logger.LogInformation($"Room {_roomId} auth success");
                _ = StartRedisSessionAsync();
            }
            else if (op == 3) // HEARTBEAT_REPLY
            {
                if (body.Length >= 4)
                {
                    var popularity = BinaryPrimitives.ReadUInt32BigEndian(body);
                }
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
                            _ = UpdateTitleAsync(newTitle);
                        }
                    }
                }
                else
                {
                    if (cmd == "LOG_IN_NOTICE")
                    {
                        _logger.LogWarning("Received LOG_IN_NOTICE for uid {Uid}. Current cookie/session may be degraded or expired.", _uid);
                    }

                    var recordedEvent = CreateRecordedEvent(root, cmd);
                    if (recordedEvent != null)
                    {
                        _ = WriteEventToRedisAsync(recordedEvent);
                    }
                    else if (!string.IsNullOrWhiteSpace(cmd))
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

    internal static RecordedDanmakuEvent? CreateRecordedEvent(JsonElement root, string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd))
        {
            return null;
        }

        if (cmd.StartsWith("DANMU_MSG", StringComparison.Ordinal))
        {
            var info = root.GetProperty("info");
            var timestamp = TryGetInt64(info[0], 4) ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var medalInfo = GetArrayElement(info, 3);
            var userLevelInfo = GetArrayElement(info, 4);
            var guardLevel = TryGetInt32(info, 7);
            var wealthInfo = GetArrayElement(info, 16);

            return new RecordedDanmakuEvent
            {
                Type = "comment",
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
                RawCommand = cmd
            };
        }

        if (cmd == "SEND_GIFT")
        {
            var data = root.GetProperty("data");
            var count = TryGetInt32(data, "num") ?? 1;
            var priceRaw = TryGetDouble(data, "price") ?? 0;
            return new RecordedDanmakuEvent
            {
                Type = "gift",
                Timestamp = (TryGetInt64(data, "timestamp") ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000) * 1000,
                Name = TryGetString(data, "giftName") ?? TryGetString(data, "gift_name"),
                Count = count > 0 ? count : 1,
                Price = NormalizeMoney(priceRaw),
                IsPriceTotal = false,
                CoinType = TryGetString(data, "coin_type"),
                GuardLevel = TryGetInt32(data, "guard_level"),
                User = TryGetString(data, "uname") ?? "",
                Uid = TryGetString(data, "uid") ?? "",
                RawCommand = cmd
            };
        }

        if (cmd.StartsWith("SUPER_CHAT_MESSAGE", StringComparison.Ordinal))
        {
            var data = root.GetProperty("data");
            var userInfo = data.TryGetProperty("user_info", out var u) ? u : default;
            var medalInfo = data.TryGetProperty("medal_info", out var m) ? m : default;
            return new RecordedDanmakuEvent
            {
                Type = "super_chat",
                Timestamp = (TryGetInt64(data, "ts") ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000) * 1000,
                Price = TryGetDouble(data, "price"),
                IsPriceTotal = true,
                Text = TryGetString(data, "message") ?? "",
                MessageJpn = TryGetString(data, "message_jpn"),
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
                RawCommand = cmd
            };
        }

        if (cmd == "GUARD_BUY")
        {
            var data = root.GetProperty("data");
            return new RecordedDanmakuEvent
            {
                Type = "guard",
                Timestamp = (TryGetInt64(data, "start_time") ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000) * 1000,
                Name = TryGetString(data, "gift_name") ?? "guard",
                Count = Math.Max(1, TryGetInt32(data, "num") ?? 1),
                Price = NormalizeMoney(TryGetDouble(data, "price") ?? 0) * Math.Max(1, TryGetInt32(data, "num") ?? 1),
                IsPriceTotal = true,
                GuardLevel = TryGetInt32(data, "guard_level"),
                User = TryGetString(data, "username") ?? "",
                Uid = TryGetString(data, "uid") ?? "",
                RawCommand = cmd
            };
        }

        if (cmd == "COMBO_SEND")
        {
            var data = root.GetProperty("data");
            var comboNum = TryGetInt32(data, "combo_num") ?? TryGetInt32(data, "total_num") ?? TryGetInt32(data, "gift_num") ?? 1;
            var comboTotalCoin = TryGetDouble(data, "combo_total_coin");
            var coinType = TryGetString(data, "coin_type");
            var normalizedPrice = comboTotalCoin.HasValue && string.Equals(coinType, "gold", StringComparison.OrdinalIgnoreCase)
                ? NormalizeMoney(comboTotalCoin.Value)
                : 0;

            return new RecordedDanmakuEvent
            {
                Type = "gift_combo",
                Timestamp = (TryGetInt64(data, "send_master") ?? TryGetInt64(data, "timestamp") ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000) * 1000,
                Name = TryGetString(data, "gift_name") ?? TryGetString(data, "giftName") ?? "combo_gift",
                Count = Math.Max(1, comboNum),
                Price = normalizedPrice,
                IsPriceTotal = true,
                CoinType = coinType,
                GuardLevel = TryGetInt32(data, "guard_level"),
                User = TryGetString(data, "uname") ?? "",
                Uid = TryGetString(data, "uid") ?? TryGetString(data, "ruid") ?? "",
                RawCommand = cmd
            };
        }

        if (cmd == "INTERACT_WORD")
        {
            var data = root.GetProperty("data");
            var msgType = TryGetInt32(data, "msg_type");
            var eventType = msgType switch
            {
                1 => "enter",
                2 => "follow",
                3 => "share",
                _ => "interact"
            };

            var textSuffix = msgType switch
            {
                2 => "followed the room",
                3 => "shared the room",
                _ => "entered room"
            };

            return new RecordedDanmakuEvent
            {
                Type = eventType,
                Timestamp = (TryGetInt64(data, "timestamp") ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000) * 1000,
                Text = TryGetString(data, "uname") is { Length: > 0 } uname ? $"{uname} {textSuffix}" : $"user {textSuffix}",
                User = TryGetString(data, "uname") ?? "",
                Uid = TryGetString(data, "uid") ?? "",
                GuardLevel = TryGetInt32(data, "guard_level"),
                RawCommand = cmd
            };
        }

        return null;
    }

    private async Task WriteEventToRedisAsync(RecordedDanmakuEvent recordedEvent)
    {
        var content = JsonSerializer.Serialize(recordedEvent, EventJsonOptions);
        await WriteToRedisAsync(content);
    }

    internal static double NormalizeMoney(double rawPrice)
    {
        if (rawPrice <= 0) return 0;
        return rawPrice >= 1000 ? rawPrice / 1000.0 : rawPrice;
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

    private static string? TryGetString(JsonElement element, string propertyName)
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

    private static int? TryGetInt32(JsonElement element, string propertyName)
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
                    await SyncCurrentSessionMetadataAsync(updateFilename: true);
                    if (OnTitleChanged != null)
                    {
                        await OnTitleChanged.Invoke(_uid, _roomId, _title);
                    }
                    _logger.LogInformation($"Resuming existing Redis session: {_currentSessionKey}");
                    return;
                }
            }

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _currentSessionKey = $"danmaku:session:{_uid}:{timestamp}";
            var filename = BuildSessionFilename(timestamp);
            
            var meta = new Dictionary<string, string>
            {
                { "uid", _uid },
                { "room_id", _roomId.ToString() },
                { "real_room_id", _realRoomId.ToString() },
                { "room_title", _title },
                { "user_name", _userName },
                { "video_start_time", timestamp.ToString() },
                { "filename", filename }
            };
            
            await _redis.SetMetadataAsync(_currentSessionKey + ":meta", meta);
            await _redis.SetLiveSessionKeyAsync(_uid, _currentSessionKey);
            
            if (OnSessionStarted != null)
            {
                await OnSessionStarted.Invoke(_uid, _roomId, _title, _userName, timestamp, _currentSessionKey);
            }
            
            _logger.LogInformation($"Started recording to Redis: {_currentSessionKey}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Redis session");
        }
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

        await SyncCurrentSessionMetadataAsync(updateFilename: true);

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
            await SyncCurrentSessionMetadataAsync(updateFilename: titleChanged);
            if (titleChanged && OnTitleChanged != null)
            {
                await OnTitleChanged.Invoke(_uid, _roomId, _title);
            }
        }

        if (UpdateVupStats != null)
        {
            _ = UpdateVupStats(_uid, _roomId, liveStartTime ?? 0, followers, guardNum, videoCount);
        }

        LiveStatus = liveStatus;
        LiveStartTime = liveStartTime;

        return (liveStatus, liveStartTime, followers, guardNum, videoCount);
    }

    private async Task SyncCurrentSessionMetadataAsync(bool updateFilename)
    {
        if (_currentSessionKey == null) return;

        var metaKey = _currentSessionKey + ":meta";
        await _redis.SetMetadataFieldAsync(metaKey, "room_title", _title);
        await _redis.SetMetadataFieldAsync(metaKey, "user_name", _userName);

        if (!updateFilename) return;

        var meta = await _redis.GetMetadataAsync(metaKey);
        if (long.TryParse(meta.GetValueOrDefault("video_start_time"), out var startTimestamp))
        {
            await _redis.SetMetadataFieldAsync(metaKey, "filename", BuildSessionFilename(startTimestamp));
        }
    }

    private string BuildSessionFilename(long startTimestamp)
    {
        var dateStr = DateTimeOffset.FromUnixTimeMilliseconds(startTimestamp).LocalDateTime.ToString("yyyy-MM-dd HH-mm-ss");
        return $"{dateStr} {SanitizeFileName(_title)}.jsonl";
    }

    private static string SanitizeFileName(string title)
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
             _logger.LogInformation($"Closing active session {_currentSessionKey} because room is offline.");
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
                    await EndRedisSessionAsync(isFinal: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking/closing stale session");
            }
        }
    }
    
    private async Task EndRedisSessionAsync(bool isFinal)
    {
        if (_currentSessionKey == null) return;
        if (!isFinal) return; // We only dump on final stop now
        
        try
        {
            _logger.LogInformation($"Finalizing session {_currentSessionKey}...");
            
            var messages = await _redis.GetMessagesAsync(_currentSessionKey + ":list");
            var meta = await _redis.GetMetadataAsync(_currentSessionKey + ":meta");

            var filename = meta.ContainsKey("filename") ? meta["filename"] : $"{DateTime.Now:yyyy-MM-dd HH-mm-ss} {_uid}.jsonl";
            var roomDir = Path.Combine(_danmakuDir, _uid);
            if (!Directory.Exists(roomDir)) Directory.CreateDirectory(roomDir);
            var filePath = Path.Combine(roomDir, filename);
            var tempPath = filePath + ".tmp";

            var lines = new List<string>
            {
                JsonSerializer.Serialize(new
                {
                    kind = "meta",
                    version = "danmu-jsonl-v1",
                    uid = meta.GetValueOrDefault("uid", _uid),
                    roomId = meta.GetValueOrDefault("room_id", _roomId.ToString()),
                    realRoomId = meta.GetValueOrDefault("real_room_id", _realRoomId.ToString()),
                    title = meta.GetValueOrDefault("room_title", _title),
                    userName = meta.GetValueOrDefault("user_name", _userName),
                    startTime = meta.GetValueOrDefault("video_start_time", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString())
                }, EventJsonOptions)
            };
            lines.AddRange(messages);

            await File.WriteAllLinesAsync(tempPath, lines, Encoding.UTF8);
            
            if (File.Exists(filePath)) File.Delete(filePath);
            File.Move(tempPath, filePath);
            
            _logger.LogInformation($"Dumped Redis to {filePath}");
            
            var endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            if (OnSessionEnded != null)
            {
                await OnSessionEnded.Invoke(_uid, _roomId, endTime, filePath);
            }

            await _redis.DeleteKeyAsync(_currentSessionKey + ":list");
            await _redis.DeleteKeyAsync(_currentSessionKey + ":meta");
            await _redis.ClearLiveSessionKeyAsync(_uid);
            _currentSessionKey = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dump Redis to JSONL");
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopAsync().Wait();
        _cts?.Dispose();
    }
}
