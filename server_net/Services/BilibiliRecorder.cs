using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Danmu.Server.Services;

public class BilibiliRecorder : IDisposable
{
    private readonly long _roomId;
    private readonly string _name;
    private readonly ILogger _logger;
    private readonly RedisService _redis;
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
    private string _title = "未知直播";
    private string _userName = "未知主播";
    private long _realRoomId;
    
    // Delegate to check for active session key
    public Func<long, Task<string?>>? CheckActiveSession;
    // Delegate to notify session started
    public event Func<long, string, string, long, string, Task>? OnSessionStarted;
    // Delegate to notify session ended
    public event Func<long, long, string, Task>? OnSessionEnded;

    public string Status { get; private set; } = "stopped";
    public DateTime StartTime { get; private set; }
    public string Uptime => Status != "stopped" ? $"{(int)(DateTime.Now - StartTime).TotalMinutes}m" : "0s";
    public int Pid => _receiveTask?.Id ?? 0;

    public BilibiliRecorder(long roomId, string? name, ILogger logger, RedisService redis)
    {
        _roomId = roomId;
        _name = name ?? roomId.ToString();
        _logger = logger;
        _redis = redis;
        
        var root = Directory.GetCurrentDirectory();
        _danmakuDir = Environment.GetEnvironmentVariable("DANMAKU_DIR") 
                       ?? Path.GetFullPath(Path.Combine(root, "../server/data/danmaku"));
    }

    public async Task StartAsync(string token, string host, BilibiliService bilibiliService, long realRoomId)
    {
        if (Status == "online") return;

        _bilibiliService = bilibiliService;
        _realRoomId = realRoomId > 0 ? realRoomId : _roomId;

        // Fetch Room Info for Title
        try 
        {
             var (title, userName, liveStatus, _, _) = await bilibiliService.GetRoomInfoAsync(_realRoomId);
             if (!string.IsNullOrEmpty(title)) _title = title;
             if (!string.IsNullOrEmpty(userName)) _userName = userName;
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

        _receiveTask = Task.Run(async () => await KeepAliveLoopAsync(_cts.Token));
    }

    private async Task KeepAliveLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await WaitForLiveAsync(token);
                await ConnectAsync();
                
                // Start heartbeat
                _heartbeatTask = HeartbeatLoopAsync(token);
                
                // Receive loop blocks until connection closes
                await ReceiveLoopAsync(token);
            }
            catch (Exception ex)
            {
                if (ex is EndOfStreamException)
                {
                    _logger.LogInformation($"Stream ended for {_roomId}.");
                }
                else
                {
                    _logger.LogError(ex, $"Recorder error for {_roomId}, retrying in 5s...");
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
                
                // In reconnect loop, we don't end session unless explicit stop or error handling logic decides so.
                // But here we just want to keep session open.
            }

            if (!token.IsCancellationRequested)
            {
                await Task.Delay(5000, token);
            }
        }
        
        Status = "stopped";
    }

    private async Task WaitForLiveAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (_bilibiliService == null) return;
            try
            {
                var (_, _, liveStatus, _, _) = await _bilibiliService.GetRoomInfoAsync(_realRoomId);
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

        // Only on explicit Stop do we finalize the session
        await EndRedisSessionAsync(isFinal: true);
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

    private static string? NormalizeCookie(string? cookie)
    {
        if (string.IsNullOrWhiteSpace(cookie)) return null;
        var trimmed = cookie.Trim();
        if ((trimmed.StartsWith("\"") && trimmed.EndsWith("\"")) || (trimmed.StartsWith("'") && trimmed.EndsWith("'")))
        {
            trimmed = trimmed.Substring(1, trimmed.Length - 2);
        }
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? LoadCookieFromEnvFile()
    {
        var root = Directory.GetCurrentDirectory();
        var envPathLocal = Path.GetFullPath(Path.Combine(root, "../server/.env"));
        var envPathRoot = Path.GetFullPath(Path.Combine(root, "../.env"));
        var paths = new[] { envPathLocal, envPathRoot };
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                var parts = line.Split('=', 2);
                if (parts.Length != 2) continue;
                if (!string.Equals(parts[0].Trim(), "BILI_COOKIE", StringComparison.OrdinalIgnoreCase)) continue;
                return NormalizeCookie(parts[1]);
            }
        }
        return null;
    }

    private static string? GetCookie()
    {
        var fileCookie = LoadCookieFromEnvFile();
        if (!string.IsNullOrEmpty(fileCookie)) return fileCookie;
        return NormalizeCookie(Environment.GetEnvironmentVariable("BILI_COOKIE"));
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
                    var (_, _, liveStatus, _, _) = await _bilibiliService.GetRoomInfoAsync(_realRoomId);
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
                            // Close might not be enough if ReceiveLoop is stuck, but ReceiveAsync should return or throw.
                            // However, we should break here to stop sending heartbeats.
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
                throw new Exception("WebSocket closed or null");
            }

            var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            if (result.MessageType == WebSocketMessageType.Close) 
            {
                throw new Exception("WebSocket closed by server");
            }

            var data = new byte[result.Count];
            Array.Copy(buffer, data, result.Count);
            
            ProcessPacket(data);
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
                string xml = "";

                if (cmd == "PREPARING")
                {
                    throw new EndOfStreamException("Stream ended (PREPARING)");
                }

                if (cmd != null && cmd.StartsWith("DANMU_MSG"))
                {
                    var info = root.GetProperty("info");
                    var content = info[1].GetString();
                    var user = info[2][1].GetString();
                    var uid = info[2][0].ToString();
                    var timestamp = info[0][4].GetInt64();
                    xml = $"<d p=\"{timestamp},1,25,16777215,{timestamp},0,{uid},0\" user=\"{user}\" uid=\"{uid}\" timestamp=\"{timestamp}\">{content}</d>\n";
                }
                else if (cmd == "SEND_GIFT")
                {
                    var data = root.GetProperty("data");
                    var giftName = data.GetProperty("giftName").GetString();
                    var num = data.GetProperty("num").GetInt32();
                    var uname = data.GetProperty("uname").GetString();
                    var action = data.GetProperty("action").GetString();
                    var price = data.TryGetProperty("price", out var p) ? p.GetInt32() : 0;
                    var uid = data.GetProperty("uid").ToString();
                    var timestamp = data.GetProperty("timestamp").GetInt64() * 1000;
                    xml = $"<gift ts=\"{timestamp}\" giftname=\"{giftName}\" giftcount=\"{num}\" price=\"{price}\" user=\"{uname}\" uid=\"{uid}\" timestamp=\"{timestamp}\" />\n";
                }
                else if (cmd == "SUPER_CHAT_MESSAGE")
                {
                    var data = root.GetProperty("data");
                    var userInfo = data.GetProperty("user_info");
                    var uname = userInfo.GetProperty("uname").GetString();
                    var uid = userInfo.GetProperty("uid").ToString();
                    var price = data.GetProperty("price").GetInt32();
                    var message = data.GetProperty("message").GetString();
                    var timestamp = data.GetProperty("ts").GetInt64() * 1000;
                    xml = $"<sc price=\"{price}\" user=\"{uname}\" uid=\"{uid}\" timestamp=\"{timestamp}\">{message}</sc>\n";
                }
                else if (cmd == "GUARD_BUY")
                {
                    var data = root.GetProperty("data");
                    var uname = data.GetProperty("username").GetString();
                    var uid = data.GetProperty("uid").ToString();
                    var giftName = data.GetProperty("gift_name").GetString();
                    var num = data.GetProperty("num").GetInt32();
                    var price = data.GetProperty("price").GetInt32();
                    var guardLevel = data.GetProperty("guard_level").GetInt32();
                    var timestamp = data.GetProperty("start_time").GetInt64() * 1000;
                    xml = $"<guard guard_level=\"{guardLevel}\" guard_name=\"{giftName}\" num=\"{num}\" price=\"{price}\" user=\"{uname}\" uid=\"{uid}\" timestamp=\"{timestamp}\" />\n";
                }

                if (!string.IsNullOrEmpty(xml))
                {
                    _ = WriteToRedisAsync(xml);
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private async Task StartRedisSessionAsync()
    {
        try
        {
            // Check for existing session via delegate
            if (CheckActiveSession != null)
            {
                var existingKey = await CheckActiveSession.Invoke(_roomId);
                if (!string.IsNullOrEmpty(existingKey))
                {
                    _currentSessionKey = existingKey;
                    _logger.LogInformation($"Resuming existing Redis session: {_currentSessionKey}");
                    return;
                }
            }

            var now = DateTime.Now;
            var dateStr = now.ToString("yyyy-MM-dd HH-mm-ss");
            var safeTitle = string.Join("_", _title.Split(Path.GetInvalidFileNameChars()));
            var filename = $"{dateStr} {safeTitle}.xml";
            
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _currentSessionKey = $"danmaku:session:{_roomId}:{timestamp}";
            
            var meta = new Dictionary<string, string>
            {
                { "room_id", _roomId.ToString() },
                { "room_title", _title },
                { "user_name", _userName },
                { "video_start_time", timestamp.ToString() },
                { "filename", filename }
            };
            
            await _redis.SetMetadataAsync(_currentSessionKey + ":meta", meta);
            await _redis.SetLiveSessionKeyAsync(_roomId, _currentSessionKey);
            
            if (OnSessionStarted != null)
            {
                await OnSessionStarted.Invoke(_roomId, _title, _userName, timestamp, _currentSessionKey);
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
                var sessionKey = await CheckActiveSession(_roomId);
                if (!string.IsNullOrEmpty(sessionKey))
                {
                    _logger.LogInformation($"Found stale session {sessionKey} for room {_roomId}. Closing it.");
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
            
            // if (messages.Count == 0 && meta.Count == 0) return;
            
            var sb = new StringBuilder();
            sb.Append("<xml>\n");
            if (meta.TryGetValue("room_id", out var rid)) sb.Append($"<room_id>{rid}</room_id>\n");
            if (meta.TryGetValue("room_title", out var title)) sb.Append($"<room_title>{title}</room_title>\n");
            if (meta.TryGetValue("user_name", out var uname)) sb.Append($"<user_name>{uname}</user_name>\n");
            if (meta.TryGetValue("video_start_time", out var time)) sb.Append($"<video_start_time>{time}</video_start_time>\n");
            
            foreach (var msg in messages)
            {
                sb.Append(msg);
            }
            sb.Append("\n</xml>");
            
            var filename = meta.ContainsKey("filename") ? meta["filename"] : $"{DateTime.Now:yyyy-MM-dd HH-mm-ss} {_roomId}.xml";
            var roomDir = Path.Combine(_danmakuDir, _roomId.ToString());
            if (!Directory.Exists(roomDir)) Directory.CreateDirectory(roomDir);
            var filePath = Path.Combine(roomDir, filename);
            var tempPath = filePath + ".tmp";
            
            await File.WriteAllTextAsync(tempPath, sb.ToString());
            
            if (File.Exists(filePath)) File.Delete(filePath);
            File.Move(tempPath, filePath);
            
            _logger.LogInformation($"Dumped Redis to {filePath}");
            
            var endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            if (OnSessionEnded != null)
            {
                await OnSessionEnded.Invoke(_roomId, endTime, filePath);
            }

            await _redis.DeleteKeyAsync(_currentSessionKey + ":list");
            await _redis.DeleteKeyAsync(_currentSessionKey + ":meta");
            await _redis.ClearLiveSessionKeyAsync(_roomId);
            _currentSessionKey = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dump Redis to XML");
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
