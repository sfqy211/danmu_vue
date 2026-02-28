using System.Text.Json;

namespace Danmu.Server.Services;

public class BilibiliService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BilibiliService> _logger;
    private readonly string? _cookie;

    public BilibiliService(HttpClient httpClient, ILogger<BilibiliService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.bilibili.com");

        _cookie = LoadCookie(logger);
        if (!string.IsNullOrEmpty(_cookie))
        {
            logger.LogInformation("Bilibili Cookie loaded successfully.");
        }
        else
        {
            logger.LogWarning("Bilibili Cookie NOT found in environment or .env files. Some API requests may fail.");
        }
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

    private static string? LoadCookie(ILogger logger)
    {
        // Try env var first
        var envCookie = NormalizeCookie(Environment.GetEnvironmentVariable("BILI_COOKIE"));
        if (!string.IsNullOrEmpty(envCookie)) return envCookie;

        // Try .env files
        var root = Directory.GetCurrentDirectory();
        var paths = new[] 
        { 
            Path.GetFullPath(Path.Combine(root, ".env")),
            Path.GetFullPath(Path.Combine(root, "../.env")),
            Path.GetFullPath(Path.Combine(root, "../server/.env"))
        };

        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            try 
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    var parts = line.Split('=', 2);
                    if (parts.Length != 2) continue;
                    if (!string.Equals(parts[0].Trim(), "BILI_COOKIE", StringComparison.OrdinalIgnoreCase)) continue;
                    
                    var cookie = NormalizeCookie(parts[1]);
                    if (!string.IsNullOrEmpty(cookie))
                    {
                        logger.LogInformation($"Found BILI_COOKIE in {path}");
                        return cookie;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Failed to read {path}: {ex.Message}");
            }
        }
        return null;
    }

    private string? GetCookie() => _cookie;

    public async Task<string?> GetAvatarUrlAsync(string uid)
    {
        try
        {
            var url = $"https://api.bilibili.com/x/web-interface/card?mid={uid}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var cookie = GetCookie();
            if (!string.IsNullOrEmpty(cookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            }
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("code", out var code) && code.GetInt32() != 0)
            {
                var msg = root.TryGetProperty("message", out var m) ? m.GetString() : "Unknown error";
                _logger.LogWarning($"Bilibili API Error for UID {uid}: {msg}");
                return null;
            }

            if (root.TryGetProperty("data", out var data) && 
                data.TryGetProperty("card", out var card) &&
                card.TryGetProperty("face", out var face))
            {
                return face.GetString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to get avatar for UID {uid}");
        }
        return null;
    }

    public async Task<(string? Title, string? UserName, int LiveStatus, string? CoverUrl, string? Uid)> GetRoomInfoAsync(long roomId)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.live.bilibili.com/room/v1/Room/get_info?room_id={roomId}");
            var cookie = GetCookie();
            if (!string.IsNullOrEmpty(cookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            }
            request.Headers.TryAddWithoutValidation("Referer", $"https://live.bilibili.com/{roomId}");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("code", out var code) && code.GetInt32() != 0)
            {
                var msg = root.TryGetProperty("msg", out var m) ? m.GetString() : "Unknown error";
                _logger.LogWarning($"Bilibili API Error for Room {roomId}: {msg}");
                return (null, null, 0, null, null);
            }

            if (root.TryGetProperty("data", out var data))
            {
                var title = data.GetProperty("title").GetString();
                var liveStatus = data.GetProperty("live_status").GetInt32();
                
                string? cover = null;
                if (data.TryGetProperty("user_cover", out var uc) && uc.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(uc.GetString())) cover = uc.GetString();
                else if (data.TryGetProperty("cover", out var c) && c.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(c.GetString())) cover = c.GetString();
                else if (data.TryGetProperty("keyframe", out var k) && k.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(k.GetString())) cover = k.GetString();

                string? uid = null;
                if (data.TryGetProperty("uid", out var u)) uid = u.ToString();

                // Get Anchor Info
                string? userName = "Unknown";
                try 
                {
                    var userReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.live.bilibili.com/live_user/v1/UserInfo/get_anchor_in_room?roomid={roomId}");
                    if (!string.IsNullOrEmpty(cookie)) userReq.Headers.TryAddWithoutValidation("Cookie", cookie);
                    userReq.Headers.TryAddWithoutValidation("Referer", $"https://live.bilibili.com/{roomId}");
                    
                    var userRes = await _httpClient.SendAsync(userReq);
                    if (userRes.IsSuccessStatusCode)
                    {
                        var userJson = await userRes.Content.ReadAsStringAsync();
                        using var userDoc = JsonDocument.Parse(userJson);
                        if (userDoc.RootElement.TryGetProperty("data", out var userData) && userData.ValueKind != JsonValueKind.Null)
                        {
                            if (userData.TryGetProperty("info", out var info) && info.TryGetProperty("uname", out var uname))
                            {
                                userName = uname.GetString();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get anchor info");
                }

                return (title, userName, liveStatus, cover, uid);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to get room info for Room {roomId}");
        }
        return (null, null, 0, null, null);
    }

    public async Task<(int LiveStatus, long? LiveStartTime)> GetRoomLiveStatusAsync(long roomId)
    {
        try
        {
            var initReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.live.bilibili.com/room/v1/Room/room_init?id={roomId}");
            var cookie = GetCookie();
            if (!string.IsNullOrEmpty(cookie))
            {
                initReq.Headers.TryAddWithoutValidation("Cookie", cookie);
            }
            initReq.Headers.TryAddWithoutValidation("Referer", $"https://live.bilibili.com/{roomId}");
            
            var initRes = await _httpClient.SendAsync(initReq);
            initRes.EnsureSuccessStatusCode();

            var initJson = await initRes.Content.ReadAsStringAsync();
            using var initDoc = JsonDocument.Parse(initJson);
            var initRoot = initDoc.RootElement;

            if (!initRoot.TryGetProperty("data", out var initData))
            {
                return (0, null);
            }

            var liveStatus = initData.TryGetProperty("live_status", out var ls) ? ls.GetInt32() : 0;
            var realRoomId = initData.TryGetProperty("room_id", out var rid) ? rid.GetInt64() : roomId;
            long? initLiveTime = null;
            if (initData.TryGetProperty("live_time", out var lt))
            {
                if (lt.ValueKind == JsonValueKind.Number)
                {
                    initLiveTime = lt.GetInt64();
                }
                else if (lt.ValueKind == JsonValueKind.String && long.TryParse(lt.GetString(), out var parsed))
                {
                    initLiveTime = parsed;
                }
            }

            if (liveStatus != 1)
            {
                return (liveStatus, null);
            }

            if (initLiveTime.HasValue && initLiveTime.Value > 0)
            {
                return (liveStatus, initLiveTime.Value * 1000);
            }

            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.live.bilibili.com/room/v1/Room/get_info?room_id={realRoomId}");
            if (!string.IsNullOrEmpty(cookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            }
            request.Headers.TryAddWithoutValidation("Referer", $"https://live.bilibili.com/{realRoomId}");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("code", out var code) && code.GetInt32() != 0)
            {
                return (liveStatus, null);
            }

            if (root.TryGetProperty("data", out var data))
            {
                long? liveStartTime = null;
                if (data.TryGetProperty("live_start_time", out var lst))
                {
                    if (lst.ValueKind == JsonValueKind.Number)
                    {
                        liveStartTime = lst.GetInt64();
                    }
                    else if (lst.ValueKind == JsonValueKind.String && long.TryParse(lst.GetString(), out var parsed))
                    {
                        liveStartTime = parsed;
                    }
                }
                if (liveStartTime == 0) liveStartTime = null;
                if (liveStartTime.HasValue && liveStartTime.Value < 1_000_000_000_000)
                {
                    liveStartTime = liveStartTime.Value * 1000;
                }
                return (liveStatus, liveStartTime);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to get live status for Room {roomId}");
        }
        return (0, null);
    }

    public async Task<byte[]?> DownloadImageAsync(string url)
    {
        try
        {
            return await _httpClient.GetByteArrayAsync(url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to download image: {url}");
            return null;
        }
    }

    public async Task<(string Token, string Host, long RealRoomId)> GetDanmakuConfAsync(long roomId)
    {
        var realRoomId = await GetRealRoomIdAsync(roomId);
        if (realRoomId <= 0) realRoomId = roomId;
        // Try new API first
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?id={realRoomId}&type=0");
            var cookie = GetCookie();
            if (!string.IsNullOrEmpty(cookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            }
            request.Headers.Add("Referer", $"https://live.bilibili.com/{realRoomId}");
            request.Headers.Add("Origin", "https://live.bilibili.com");
            
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("code", out var code) && code.GetInt32() == 0)
            {
                var data = root.GetProperty("data");
                var token = data.GetProperty("token").GetString();
                var hostList = data.GetProperty("host_list");
                var host = hostList[0].GetProperty("host").GetString();
                var port = hostList[0].GetProperty("wss_port").GetInt32();
                
                _logger.LogInformation($"Got danmaku conf for {realRoomId}: token len={token?.Length}, host={host}");
                return (token ?? "", $"wss://{host}:{port}/sub", realRoomId);
            }
            else 
            {
                _logger.LogWarning($"GetDanmakuConf failed for {realRoomId}, code={root.GetProperty("code").GetInt32()}, msg={root.GetProperty("message").GetString()}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to get danmaku conf (new API) for {realRoomId}");
        }

        // Fallback to old API
        try 
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.live.bilibili.com/room/v1/Danmu/getConf?room_id={realRoomId}&platform=pc&player=web");
            var cookie = GetCookie();
            if (!string.IsNullOrEmpty(cookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            }
            request.Headers.Add("Referer", $"https://live.bilibili.com/{realRoomId}");
            request.Headers.Add("Origin", "https://live.bilibili.com");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("code", out var code) && code.GetInt32() == 0)
            {
                var data = root.GetProperty("data");
                var token = data.GetProperty("token").GetString();
                var hostList = data.GetProperty("host_server_list");
                var host = hostList[0].GetProperty("host").GetString();
                var port = hostList[0].GetProperty("wss_port").GetInt32();
                
                _logger.LogInformation($"Got danmaku conf (old API) for {realRoomId}: token len={token?.Length}, host={host}");
                return (token ?? "", $"wss://{host}:{port}/sub", realRoomId);
            }
             else 
            {
                _logger.LogWarning($"GetDanmakuConf (old API) failed for {realRoomId}, code={root.GetProperty("code").GetInt32()}, msg={root.GetProperty("msg").GetString()}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to get danmaku conf (old API) for {realRoomId}");
        }
        
        _logger.LogWarning($"Using fallback for {realRoomId}");
        // Fallback
        return ("", "wss://broadcastlv.chat.bilibili.com/sub", realRoomId);
    }

    public async Task<(long RoomId, string Name)> GetRoomInfoByUidAsync(long uid)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.live.bilibili.com/live_user/v1/Master/info?uid={uid}");
            var cookie = GetCookie();
            if (!string.IsNullOrEmpty(cookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            }
            request.Headers.TryAddWithoutValidation("Referer", "https://live.bilibili.com/");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("code", out var code) && code.GetInt32() == 0)
            {
                if (root.TryGetProperty("data", out var data))
                {
                    var roomId = data.TryGetProperty("room_id", out var rid) ? rid.GetInt64() : 0;
                    var name = "";
                    if (data.TryGetProperty("info", out var info) && info.TryGetProperty("uname", out var uname))
                    {
                        name = uname.GetString() ?? "";
                    }
                    return (roomId, name);
                }
            }
            else 
            {
                var msg = root.TryGetProperty("msg", out var m) ? m.GetString() : "Unknown error";
                _logger.LogWarning($"GetRoomInfoByUidAsync failed for UID {uid}: {msg}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to get room info for UID {uid}");
        }
        return (0, "");
    }

    public async Task<long> GetRealRoomIdAsync(long roomId)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.live.bilibili.com/room/v1/Room/room_init?id={roomId}");
            var cookie = GetCookie();
            if (!string.IsNullOrEmpty(cookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            }
            request.Headers.TryAddWithoutValidation("Referer", $"https://live.bilibili.com/{roomId}");
            
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data) && data.TryGetProperty("room_id", out var rid))
            {
                return rid.GetInt64();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to resolve real room id for {roomId}");
        }
        return roomId;
    }

    public async Task<(int Followers, int GuardNum, int VideoCount)> GetVupStatsAsync(long roomId, string uid)
    {
        int followers = 0;
        int guardNum = 0;
        int videoCount = 0;

        try
        {
            // 1. Get Followers
            var req1 = new HttpRequestMessage(HttpMethod.Get, $"https://api.bilibili.com/x/relation/stat?vmid={uid}");
            if (!string.IsNullOrEmpty(_cookie)) req1.Headers.TryAddWithoutValidation("Cookie", _cookie);
            req1.Headers.TryAddWithoutValidation("Referer", $"https://space.bilibili.com/{uid}");
            var res1 = await _httpClient.SendAsync(req1);
            if (res1.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await res1.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("follower", out var f))
                {
                    followers = f.GetInt32();
                }
            }

            // 2. Get Video Count
            var req2 = new HttpRequestMessage(HttpMethod.Get, $"https://api.bilibili.com/x/space/navnum?mid={uid}");
            if (!string.IsNullOrEmpty(_cookie)) req2.Headers.TryAddWithoutValidation("Cookie", _cookie);
            req2.Headers.TryAddWithoutValidation("Referer", $"https://space.bilibili.com/{uid}");
            var res2 = await _httpClient.SendAsync(req2);
            if (res2.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await res2.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("video", out var v))
                {
                    videoCount = v.GetInt32();
                }
            }

            // 3. Get Guard Num
            var realRoomId = await GetRealRoomIdAsync(roomId);
            if (realRoomId <= 0) realRoomId = roomId;

            var req3 = new HttpRequestMessage(HttpMethod.Get, $"https://api.live.bilibili.com/xlive/app-room/v1/guardTab/topList?roomid={realRoomId}&page=1&ruid={uid}&page_size=0");
            if (!string.IsNullOrEmpty(_cookie)) req3.Headers.TryAddWithoutValidation("Cookie", _cookie);
            req3.Headers.TryAddWithoutValidation("Referer", $"https://live.bilibili.com/{realRoomId}");
            var res3 = await _httpClient.SendAsync(req3);
            if (res3.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await res3.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("data", out var data) && 
                    data.TryGetProperty("info", out var info) && 
                    info.TryGetProperty("num", out var n))
                {
                    guardNum = n.GetInt32();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to get stats for UID {uid}");
        }

        return (followers, guardNum, videoCount);
    }
}
