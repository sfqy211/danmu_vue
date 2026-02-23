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
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private readonly string _danmakuDir;
    private FileStream? _fileStream;
    private readonly object _fileLock = new();
    private bool _isDisposed;
    private string? _token;
    private string? _host;
    private string? _currentFilePath;
    private string _title = "未知直播";
    private string _userName = "未知主播";

    public string Status { get; private set; } = "stopped";
    public DateTime StartTime { get; private set; }
    public string Uptime => Status == "online" ? $"{(int)(DateTime.Now - StartTime).TotalMinutes}m" : "0s";
    public int Pid => _receiveTask?.Id ?? 0;

    public BilibiliRecorder(long roomId, string? name, ILogger logger)
    {
        _roomId = roomId;
        _name = name ?? roomId.ToString();
        _logger = logger;
        
        var root = Directory.GetCurrentDirectory();
        _danmakuDir = Environment.GetEnvironmentVariable("DANMAKU_DIR") 
                       ?? Path.GetFullPath(Path.Combine(root, "../data/danmaku"));
    }

    public async Task StartAsync(string token, string host, BilibiliService bilibiliService)
    {
        if (Status == "online") return;

        // Fetch Room Info for Title
        try 
        {
             var (title, userName, liveStatus, _, _) = await bilibiliService.GetRoomInfoAsync(_roomId);
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
                await ConnectAsync();
                
                // Start heartbeat
                _heartbeatTask = HeartbeatLoopAsync(token);
                
                // Receive loop blocks until connection closes
                await ReceiveLoopAsync(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Recorder error for {_roomId}, retrying in 5s...");
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
                
                CloseCurrentFile();
            }

            if (!token.IsCancellationRequested)
            {
                await Task.Delay(5000, token);
            }
        }
        
        Status = "stopped";
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

        CloseCurrentFile();
    }

    private async Task ConnectAsync()
    {
        _ws = new ClientWebSocket();
        
        // Add headers if needed
        _ws.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        // Remove other headers to mimic recorder.ts (ws) behavior which doesn't set Cookie/Origin/Referer by default
        
        long uid = 0;
        var cookie = Environment.GetEnvironmentVariable("BILI_COOKIE");
        if (!string.IsNullOrEmpty(cookie))
        {
            // _ws.Options.SetRequestHeader("Cookie", cookie); // Remove Cookie from WebSocket headers
            
            // Extract DedeUserID
            var match = Regex.Match(cookie, @"DedeUserID=([^;]+)");
            if (match.Success && long.TryParse(match.Groups[1].Value, out var parsedUid))
            {
                uid = parsedUid;
            }
        }
        
        // _ws.Options.SetRequestHeader("Cookie", cookie); // Ensure removed

        var uri = new Uri(_host ?? "wss://broadcastlv.chat.bilibili.com/sub");
        await _ws.ConnectAsync(uri, _cts!.Token);
        
        var authBody = JsonSerializer.Serialize(new
        {
            uid = 0,
            roomid = _roomId,
            protover = 2, // 2 = Zlib
            platform = "web",
            type = 2,
            key = _token
        });
        
        _logger.LogInformation($"Auth Body for {_roomId}: {authBody}");

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

    private async Task HeartbeatLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(30000, token);
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
                // Wait for connection
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
                        // Skip first 2 bytes (0x78 0x9C usually) for DeflateStream if it's zlib wrapper
                        // However, Bilibili uses raw deflate or zlib.
                        // Try raw deflate first, if fails try zlib (skip 2 bytes)
                        
                        using var ms = new MemoryStream(body);
                        // ZLibStream handles Zlib header (RFC 1950)
                        using var zs = new ZLibStream(ms, CompressionMode.Decompress);
                        using var outMs = new MemoryStream();
                        zs.CopyTo(outMs);
                        ProcessPacket(outMs.ToArray());
                    }
                    catch (Exception ex) 
                    { 
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
                    catch (Exception ex) { _logger.LogError(ex, "Brotli decompression failed"); }
                }
            }
            else if (op == 8) // CONNECT_SUCCESS
            {
                _logger.LogInformation($"Room {_roomId} auth success");
                EnsureFileOpen();
            }
            else if (op == 3) // HEARTBEAT_REPLY
            {
                // Update popularity (4 bytes int)
                if (body.Length >= 4)
                {
                    var popularity = BinaryPrimitives.ReadUInt32BigEndian(body);
                    // _logger.LogInformation($"Popularity: {popularity}");
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

                if (cmd != null && cmd.StartsWith("DANMU_MSG"))
                {
                    var info = root.GetProperty("info");
                    var content = info[1].GetString();
                    var user = info[2][1].GetString();
                    var uid = info[2][0].ToString(); // UID can be long
                    var timestamp = info[0][4].GetInt64(); // timestamp
                    
                    // Format: time,mode,size,color,timestamp,pool,uid,rowId
                    // We only care about timestamp and uid really for our parser
                    xml = $"<d p=\"{timestamp},1,25,16777215,{timestamp},0,{uid},0\" user=\"{user}\" uid=\"{uid}\" timestamp=\"{timestamp}\">{content}</d>\n";
                }
                else if (cmd == "SEND_GIFT")
                {
                    var data = root.GetProperty("data");
                    var giftName = data.GetProperty("giftName").GetString();
                    var num = data.GetProperty("num").GetInt32();
                    var uname = data.GetProperty("uname").GetString();
                    var action = data.GetProperty("action").GetString();
                    var price = data.TryGetProperty("price", out var p) ? p.GetInt32() : 0; // price is usually int
                    var uid = data.GetProperty("uid").ToString();
                    var timestamp = data.GetProperty("timestamp").GetInt64() * 1000; // Gift timestamp is usually seconds
                    
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
                    WriteToFile(xml);
                }
            }
        }
        catch (Exception)
        {
            // _logger.LogError(ex, "Error parsing message");
        }
    }

    private void EnsureFileOpen()
    {
        lock (_fileLock)
        {
            if (_fileStream != null) return;
            
            // Create directory: danmakuDir/roomId/
            var roomDir = Path.Combine(_danmakuDir, _roomId.ToString());
            if (!Directory.Exists(roomDir)) Directory.CreateDirectory(roomDir);
            
            // File name: yyyy-MM-dd HH-mm-ss Title.xml
            var now = DateTime.Now;
            var dateStr = now.ToString("yyyy-MM-dd HH-mm-ss");
            var safeTitle = string.Join("_", _title.Split(Path.GetInvalidFileNameChars()));
            var filename = $"{dateStr} {safeTitle}.xml";
            
            _currentFilePath = Path.Combine(roomDir, filename);
            
            _fileStream = new FileStream(_currentFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            
            // Header with metadata
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var header = $"<xml>\n<room_id>{_roomId}</room_id>\n<room_title>{_title}</room_title>\n<user_name>{_userName}</user_name>\n<video_start_time>{timestamp}</video_start_time>\n";
            var bytes = Encoding.UTF8.GetBytes(header);
            _fileStream.Write(bytes, 0, bytes.Length);
            _fileStream.Flush();
            
            _logger.LogInformation($"Started recording to {_currentFilePath}");
        }
    }

    private void WriteToFile(string content)
    {
        lock (_fileLock)
        {
            if (_fileStream == null)
            {
                // If file is closed but we receive data (rare race condition or logic error), try to reopen
                // EnsureFileOpen(); // Be careful not to create new files for every packet if it fails
                // For now, just drop or log?
                // Actually, if we are in ReceiveLoop, file should be open.
                // If ConnectSuccess was called, it opened file.
                return;
            }

            if (_fileStream != null)
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                _fileStream.Write(bytes, 0, bytes.Length);
                _fileStream.Flush();
            }
        }
    }
    
    private void CloseCurrentFile()
    {
        lock (_fileLock)
        {
            if (_fileStream != null)
            {
                try
                {
                    var footer = "\n</xml>";
                    var bytes = Encoding.UTF8.GetBytes(footer);
                    _fileStream.Write(bytes, 0, bytes.Length);
                    _fileStream.Flush();
                }
                catch { }
                finally
                {
                    _fileStream.Dispose();
                    _fileStream = null;
                }
            }
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
