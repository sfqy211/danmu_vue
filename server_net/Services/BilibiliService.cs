using System.Text.Json;

namespace Danmu.Server.Services;

public class BilibiliService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BilibiliService> _logger;

    public BilibiliService(HttpClient httpClient, ILogger<BilibiliService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.bilibili.com");
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
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

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

    public async Task<(string Token, string Host)> GetDanmakuConfAsync(long roomId)
    {
        // Try new API first
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?id={roomId}&type=0");
            var cookie = GetCookie();
            if (!string.IsNullOrEmpty(cookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            }
            request.Headers.Add("Referer", $"https://live.bilibili.com/{roomId}");
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
                
                _logger.LogInformation($"Got danmaku conf for {roomId}: token len={token?.Length}, host={host}");
                return (token ?? "", $"wss://{host}:{port}/sub");
            }
            else 
            {
                _logger.LogWarning($"GetDanmakuConf failed for {roomId}, code={root.GetProperty("code").GetInt32()}, msg={root.GetProperty("message").GetString()}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to get danmaku conf (new API) for {roomId}");
        }

        // Fallback to old API
        try 
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.live.bilibili.com/room/v1/Danmu/getConf?room_id={roomId}&platform=pc&player=web");
            var cookie = GetCookie();
            if (!string.IsNullOrEmpty(cookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            }
            request.Headers.Add("Referer", $"https://live.bilibili.com/{roomId}");
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
                
                _logger.LogInformation($"Got danmaku conf (old API) for {roomId}: token len={token?.Length}, host={host}");
                return (token ?? "", $"wss://{host}:{port}/sub");
            }
             else 
            {
                _logger.LogWarning($"GetDanmakuConf (old API) failed for {roomId}, code={root.GetProperty("code").GetInt32()}, msg={root.GetProperty("msg").GetString()}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to get danmaku conf (old API) for {roomId}");
        }
        
        _logger.LogWarning($"Using fallback for {roomId}");
        // Fallback
        return ("", "wss://broadcastlv.chat.bilibili.com/sub");
    }
}
