using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace Danmu.Server.Services;

public class BilibiliService
{
    private const string BiliTicketApi = "https://api.bilibili.com/bapis/bilibili.api.ticket.v1.Ticket/GenWebTicket";
    private const string BiliTicketKeyId = "ec02";
    private const string BiliTicketHmacKey = "XgwSnGZ1p";
    private const int BiliTicketRefreshBufferSeconds = 3600;

    private readonly HttpClient _httpClient;
    private readonly ILogger<BilibiliService> _logger;
    private readonly BiliAccountService _accountService;
    private readonly BiliRateLimiter _rateLimiter;
    private readonly WbiSigner _wbiSigner;
    private readonly SemaphoreSlim _biliTicketLock = new(1, 1);
    private readonly object _deviceFingerprintLock = new();

    private const int MaxWbiRetry = 3;

    private volatile string? _biliTicket;
    private long _biliTicketExpiresAtUnix;
    private volatile string? _buvid3;
    private volatile string? _buvid4;
    private volatile string? _bNut;
    private volatile string? _bLsid;
    private long _bLsidExpiresAtUnix;

    public BilibiliService(HttpClient httpClient, ILogger<BilibiliService> logger, BiliAccountService accountService, BiliRateLimiter rateLimiter)
    {
        _httpClient = httpClient;
        _logger = logger;
        _accountService = accountService;
        _rateLimiter = rateLimiter;
        _wbiSigner = new WbiSigner(_httpClient, _logger);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    internal sealed class WbiSigner
    {
        internal static readonly int[] MixinKeyEncTab = {
            46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35, 27, 43, 5, 49,
            33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13, 37, 48, 7, 16, 24, 55, 40, 61,
            26, 17, 0, 1, 60, 51, 30, 4, 22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11, 36, 20, 34, 44, 52
        };

        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _cacheLock = new(1, 1);

        private string? _cachedMixinKey;
        private DateTimeOffset _cacheExpiresAt = DateTimeOffset.MinValue;

        public WbiSigner(HttpClient httpClient, ILogger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public void Invalidate()
        {
            _cachedMixinKey = null;
            _cacheExpiresAt = DateTimeOffset.MinValue;
        }

        internal void SetCachedMixinKey(string mixinKey, DateTimeOffset expiresAt)
        {
            _cachedMixinKey = mixinKey;
            _cacheExpiresAt = expiresAt;
        }

        public async Task<HttpRequestMessage> CreateSignedRequestAsync(
            string baseUrl,
            IDictionary<string, string?> parameters,
            string? cookie = null,
            string? origin = null,
            string? biliTicket = null,
            DeviceFingerprintHeaders? deviceFingerprint = null)
        {
            var signedParams = await SignParamsAsync(parameters);
            var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(baseUrl, signedParams));
            if (!string.IsNullOrWhiteSpace(cookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            }

            if (!string.IsNullOrWhiteSpace(origin))
            {
                request.Headers.TryAddWithoutValidation("Origin", origin);
            }

            if (!string.IsNullOrWhiteSpace(biliTicket))
            {
                request.Headers.TryAddWithoutValidation("bili_ticket", biliTicket);
                request.Headers.TryAddWithoutValidation("x-bili-ticket", biliTicket);
            }

            ApplyDeviceFingerprintHeaders(request, deviceFingerprint);

            return request;
        }

        private static void ApplyDeviceFingerprintHeaders(HttpRequestMessage request, DeviceFingerprintHeaders? deviceFingerprint)
        {
            if (deviceFingerprint == null)
            {
                return;
            }

            request.Headers.TryAddWithoutValidation("buvid3", deviceFingerprint.Buvid3);
            request.Headers.TryAddWithoutValidation("buvid4", deviceFingerprint.Buvid4);
            request.Headers.TryAddWithoutValidation("b_nut", deviceFingerprint.BNut);
            request.Headers.TryAddWithoutValidation("b_lsid", deviceFingerprint.BLsid);
        }

        internal async Task<IReadOnlyDictionary<string, string>> SignParamsAsync(IDictionary<string, string?> parameters)
        {
            var mixinKey = await GetMixinKeyAsync();
            var signedParams = parameters
                .Where(kv => kv.Value != null)
                .ToDictionary(kv => kv.Key, kv => SanitizeValue(kv.Value ?? string.Empty), StringComparer.Ordinal);

            signedParams["wts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            var sorted = signedParams
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToArray();

            var query = BuildQueryString(sorted);
            var input = query + mixinKey;
            var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
            signedParams["w_rid"] = Convert.ToHexString(hash).ToLowerInvariant();
            return signedParams;
        }

        private async Task<string> GetMixinKeyAsync()
        {
            if (!string.IsNullOrWhiteSpace(_cachedMixinKey) && DateTimeOffset.UtcNow < _cacheExpiresAt)
            {
                return _cachedMixinKey;
            }

            await _cacheLock.WaitAsync();
            try
            {
                if (!string.IsNullOrWhiteSpace(_cachedMixinKey) && DateTimeOffset.UtcNow < _cacheExpiresAt)
                {
                    return _cachedMixinKey;
                }

                var navRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.bilibili.com/x/web-interface/nav");
                var navResponse = await _httpClient.SendAsync(navRequest);
                navResponse.EnsureSuccessStatusCode();

                var json = await navResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement.GetProperty("data");
                var wbiImg = data.GetProperty("wbi_img");

                var imgUrl = wbiImg.GetProperty("img_url").GetString();
                var subUrl = wbiImg.GetProperty("sub_url").GetString();
                var imgKey = ExtractKeyFromUrl(imgUrl);
                var subKey = ExtractKeyFromUrl(subUrl);
                var raw = imgKey + subKey;
                var mixed = new string(MixinKeyEncTab.Where(i => i < raw.Length).Select(i => raw[i]).ToArray());

                _cachedMixinKey = mixed[..Math.Min(32, mixed.Length)];
                _cacheExpiresAt = DateTimeOffset.UtcNow.AddMinutes(60);
                return _cachedMixinKey;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh WBI mixin key");
                throw;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        internal static string ExtractKeyFromUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            var lastSegment = url.Split('/').LastOrDefault() ?? string.Empty;
            var dotIndex = lastSegment.IndexOf('.');
            return dotIndex >= 0 ? lastSegment[..dotIndex] : lastSegment;
        }

        internal static string SanitizeValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                if (c != '!' && c != '\'' && c != '(' && c != ')' && c != '*')
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string BuildUrl(string baseUrl, IEnumerable<KeyValuePair<string, string>> parameters)
        {
            var separator = baseUrl.Contains('?') ? "&" : "?";
            return baseUrl + separator + BuildQueryString(parameters);
        }

        internal static string BuildQueryString(IEnumerable<KeyValuePair<string, string>> parameters)
        {
            return string.Join("&", parameters.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        }
    }

    internal static WbiSigner WbiSignerTests_CreateWithHandler(HttpMessageHandler handler)
    {
        return new WbiSigner(new HttpClient(handler), NullLoggerFactory.Instance.CreateLogger("test"));
    }

    private async Task<string> SendBiliGetAsync(
        string baseUrl,
        IDictionary<string, string?> parameters,
        string? cookie = null,
        string? referer = null,
        string? origin = null,
        bool useWbi = false,
        CancellationToken cancellationToken = default)
    {
        for (var retry = 0; retry < MaxWbiRetry; retry++)
        {
            await _rateLimiter.WaitForAsync(cancellationToken);
            var biliTicket = await GetBiliTicketAsync();
            var deviceFingerprint = GetOrCreateDeviceFingerprintHeaders();
            using var request = useWbi
                ? await _wbiSigner.CreateSignedRequestAsync(baseUrl, parameters, cookie, origin, biliTicket, deviceFingerprint)
                : CreateStandardGetRequest(baseUrl, parameters, cookie, referer, origin, biliTicket, deviceFingerprint);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (useWbi && TryGetApiCode(json, out var code) && code == -403 && retry < MaxWbiRetry - 1)
            {
                _logger.LogWarning("WBI request got -403, invalidating mixin key and retrying: {Url}", baseUrl);
                _wbiSigner.Invalidate();
                InvalidateBiliTicket();
                continue;
            }

            return json;
        }

        throw new InvalidOperationException($"WBI request failed after {MaxWbiRetry} retries: {baseUrl}");
    }

    private static HttpRequestMessage CreateStandardGetRequest(
        string baseUrl,
        IDictionary<string, string?> parameters,
        string? cookie = null,
        string? referer = null,
        string? origin = null,
        string? biliTicket = null,
        DeviceFingerprintHeaders? deviceFingerprint = null)
    {
        var filtered = parameters
            .Where(kv => kv.Value != null)
            .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value ?? string.Empty));

        var separator = baseUrl.Contains('?') ? "&" : "?";
        var url = baseUrl + separator + string.Join("&", filtered.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        if (!string.IsNullOrWhiteSpace(referer))
        {
            request.Headers.TryAddWithoutValidation("Referer", referer);
        }

        if (!string.IsNullOrWhiteSpace(origin))
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        if (!string.IsNullOrWhiteSpace(biliTicket))
        {
            request.Headers.TryAddWithoutValidation("bili_ticket", biliTicket);
            request.Headers.TryAddWithoutValidation("x-bili-ticket", biliTicket);
        }

        if (deviceFingerprint != null)
        {
            request.Headers.TryAddWithoutValidation("buvid3", deviceFingerprint.Buvid3);
            request.Headers.TryAddWithoutValidation("buvid4", deviceFingerprint.Buvid4);
            request.Headers.TryAddWithoutValidation("b_nut", deviceFingerprint.BNut);
            request.Headers.TryAddWithoutValidation("b_lsid", deviceFingerprint.BLsid);
        }

        return request;
    }

    internal sealed record DeviceFingerprintHeaders(string Buvid3, string Buvid4, string BNut, string BLsid);

    private DeviceFingerprintHeaders GetOrCreateDeviceFingerprintHeaders()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!string.IsNullOrWhiteSpace(_buvid3)
            && !string.IsNullOrWhiteSpace(_buvid4)
            && !string.IsNullOrWhiteSpace(_bNut)
            && !string.IsNullOrWhiteSpace(_bLsid)
            && now < _bLsidExpiresAtUnix)
        {
            return new DeviceFingerprintHeaders(_buvid3, _buvid4, _bNut, _bLsid);
        }

        lock (_deviceFingerprintLock)
        {
            now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (string.IsNullOrWhiteSpace(_buvid3))
            {
                _buvid3 = GenerateBuvid3();
            }

            if (string.IsNullOrWhiteSpace(_buvid4))
            {
                _buvid4 = GenerateBuvid4();
            }

            if (string.IsNullOrWhiteSpace(_bNut))
            {
                _bNut = now.ToString();
            }

            if (string.IsNullOrWhiteSpace(_bLsid) || now >= _bLsidExpiresAtUnix)
            {
                _bLsid = GenerateBLsid();
                _bLsidExpiresAtUnix = now + 1800;
            }

            return new DeviceFingerprintHeaders(_buvid3!, _buvid4!, _bNut!, _bLsid!);
        }
    }

    internal static string GenerateBuvid3()
    {
        var uuid = Guid.NewGuid().ToString("D");
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return $"{uuid}{ts}infoc";
    }

    internal static string GenerateBuvid4()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    internal static string GenerateBLsid()
    {
        var timestampHex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("X");
        var randomHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        return $"{timestampHex}_{randomHex}";
    }

    private void InvalidateBiliTicket()
    {
        _biliTicket = null;
        _biliTicketExpiresAtUnix = 0;
    }

    private async Task<string?> GetBiliTicketAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!string.IsNullOrWhiteSpace(_biliTicket) && now < _biliTicketExpiresAtUnix - BiliTicketRefreshBufferSeconds)
        {
            return _biliTicket;
        }

        await _biliTicketLock.WaitAsync();
        try
        {
            now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!string.IsNullOrWhiteSpace(_biliTicket) && now < _biliTicketExpiresAtUnix - BiliTicketRefreshBufferSeconds)
            {
                return _biliTicket;
            }

            var timestamp = now;
            var hexsign = GenerateBiliTicketSign(timestamp);
            var ticketUrl = $"{BiliTicketApi}?key_id={Uri.EscapeDataString(BiliTicketKeyId)}&hexsign={Uri.EscapeDataString(hexsign)}&context[ts]={timestamp}&csrf=";
            using var request = new HttpRequestMessage(HttpMethod.Post, ticketUrl);
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Handle non-zero code responses gracefully (e.g. "empty ts field")
            if (!root.TryGetProperty("code", out var codeEl) || codeEl.GetInt32() != 0)
            {
                var message = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : "Unknown error";
                _logger.LogWarning("Failed to refresh bili_ticket (code != 0): {Message}", message);
                // Return stale ticket with backoff to avoid hammering the API
                if (!string.IsNullOrWhiteSpace(_biliTicket))
                {
                    _biliTicketExpiresAtUnix = timestamp + 300; // retry in 5 minutes
                    return _biliTicket;
                }
                return null;
            }

            // Safely parse data/ticket — the response shape may vary
            if (!root.TryGetProperty("data", out var data))
            {
                _logger.LogWarning("Failed to refresh bili_ticket: response missing 'data' field");
                if (!string.IsNullOrWhiteSpace(_biliTicket))
                {
                    _biliTicketExpiresAtUnix = timestamp + 300;
                    return _biliTicket;
                }
                return null;
            }

            var ticket = data.TryGetProperty("ticket", out var ticketEl) ? ticketEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(ticket))
            {
                _logger.LogWarning("Failed to refresh bili_ticket: empty or missing ticket");
                if (!string.IsNullOrWhiteSpace(_biliTicket))
                {
                    _biliTicketExpiresAtUnix = timestamp + 300;
                    return _biliTicket;
                }
                return null;
            }

            var ttl = data.TryGetProperty("ttl", out var ttlEl) ? ttlEl.GetInt64() : 259200;

            _biliTicket = ticket;
            _biliTicketExpiresAtUnix = timestamp + ttl;
            return ticket;
        }
        finally
        {
            _biliTicketLock.Release();
        }
    }

    internal static string GenerateBiliTicketSign(long timestamp)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(BiliTicketHmacKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"ts{timestamp}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool TryGetApiCode(string json, out int code)
    {
        code = 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number)
            {
                code = codeEl.GetInt32();
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private string? GetCookie()
    {
        return _accountService.GetActiveCookieString();
    }

    /// <summary>
    /// Get cookie for a specific room (round-robin + failover).
    /// </summary>
    private string? GetCookieForRoom(string roomUid)
    {
        return _accountService.GetCookieForRoom(roomUid);
    }

    /// <summary>
    /// Report that the current account's cookie has failed, triggering failover.
    /// </summary>
    public void ReportCookieFailure(string roomUid)
    {
        _accountService.ReportRoomFailure(roomUid);
    }

    public async Task<string?> GetAvatarUrlAsync(string uid)
    {
        try
        {
            var cookie = GetCookie();
            var json = await SendBiliGetAsync(
                "https://api.bilibili.com/x/web-interface/card",
                new Dictionary<string, string?> { ["mid"] = uid },
                cookie,
                useWbi: true);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("code", out var code) && code.GetInt32() != 0)
            {
                var msg = root.TryGetProperty("message", out var m) ? m.GetString() : "Unknown error";
                _logger.LogWarning("Bilibili API Error for UID {Uid}: {Message}", uid, msg);
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
            _logger.LogError(ex, "Failed to get avatar for UID {Uid}", uid);
        }
        return null;
    }

    public async Task<(string? Title, string? UserName, int LiveStatus, string? CoverUrl, string? Uid, long? LiveStartTime, int Followers, int GuardNum, int VideoCount)> GetRoomInfoAsync(long roomId)
    {
        try
        {
            var cookie = GetCookie();
            var json = await SendBiliGetAsync(
                "https://api.live.bilibili.com/room/v1/Room/get_info",
                new Dictionary<string, string?> { ["room_id"] = roomId.ToString() },
                cookie,
                useWbi: true);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("code", out var code) && code.GetInt32() != 0)
            {
                var msg = root.TryGetProperty("msg", out var m) ? m.GetString() : "Unknown error";
                _logger.LogWarning("Bilibili API Error for Room {RoomId}: {Message}", roomId, msg);
                return (null, null, 0, null, null, null, 0, 0, 0);
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

                long? liveStartTime = null;
                if (data.TryGetProperty("live_start_time", out var lst))
                {
                    if (lst.ValueKind == JsonValueKind.Number) liveStartTime = lst.GetInt64();
                    else if (lst.ValueKind == JsonValueKind.String && long.TryParse(lst.GetString(), out var parsed)) liveStartTime = parsed;
                }
                
                // If liveStartTime is not found or 0, try live_time (from room_init)
                if (liveStartTime == null || liveStartTime == 0)
                {
                    if (data.TryGetProperty("live_time", out var lt))
                    {
                        if (lt.ValueKind == JsonValueKind.Number) liveStartTime = lt.GetInt64();
                        else if (lt.ValueKind == JsonValueKind.String && long.TryParse(lt.GetString(), out var parsed)) liveStartTime = parsed;
                    }
                }

                if (liveStartTime.HasValue && liveStartTime.Value > 0 && liveStartTime.Value < 1_000_000_000_000)
                {
                    liveStartTime = liveStartTime.Value * 1000;
                }

                // Get Anchor Info
                string? userName = "Unknown";
                try 
                {
                    var userJson = await SendBiliGetAsync(
                        "https://api.live.bilibili.com/live_user/v1/UserInfo/get_anchor_in_room",
                        new Dictionary<string, string?> { ["roomid"] = roomId.ToString() },
                        cookie,
                        referer: $"https://live.bilibili.com/{roomId}");
                    using var userDoc = JsonDocument.Parse(userJson);
                    if (userDoc.RootElement.TryGetProperty("data", out var userData) && userData.ValueKind != JsonValueKind.Null)
                    {
                        if (userData.TryGetProperty("info", out var info) && info.TryGetProperty("uname", out var uname))
                        {
                            userName = uname.GetString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get anchor info");
                }

                // Fetch stats as well
                int followers = 0, guardNum = 0, videoCount = 0;
                if (!string.IsNullOrEmpty(uid))
                {
                    (followers, guardNum, videoCount) = await GetVupStatsAsync(roomId, uid);
                }

                return (title, userName, liveStatus, cover, uid, liveStartTime, followers, guardNum, videoCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get room info for Room {RoomId}", roomId);
        }
        return (null, null, 0, null, null, null, 0, 0, 0);
    }

    public async Task<(int LiveStatus, long? LiveStartTime)> GetRoomLiveStatusAsync(long roomId)
    {
        try
        {
            var cookie = GetCookie();
            var initJson = await SendBiliGetAsync(
                "https://api.live.bilibili.com/room/v1/Room/room_init",
                new Dictionary<string, string?> { ["id"] = roomId.ToString() },
                cookie,
                useWbi: true);
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

            var json = await SendBiliGetAsync(
                "https://api.live.bilibili.com/room/v1/Room/get_info",
                new Dictionary<string, string?> { ["room_id"] = realRoomId.ToString() },
                cookie,
                useWbi: true);
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
            _logger.LogError(ex, "Failed to get live status for Room {RoomId}", roomId);
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
            _logger.LogError(ex, "Failed to download image: {Url}", url);
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
            var cookie = GetCookie();
            var json = await SendBiliGetAsync(
                "https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo",
                new Dictionary<string, string?>
                {
                    ["id"] = realRoomId.ToString(),
                    ["type"] = "0"
                },
                cookie,
                origin: "https://live.bilibili.com",
                useWbi: true);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("code", out var code) && code.GetInt32() == 0)
            {
                var data = root.GetProperty("data");
                var token = data.GetProperty("token").GetString();
                var hostList = data.GetProperty("host_list");
                var host = hostList[0].GetProperty("host").GetString();
                var port = hostList[0].GetProperty("wss_port").GetInt32();
                
                _logger.LogInformation("Got danmaku conf for {RealRoomId}: host={Host}", realRoomId, host);
                return (token ?? "", $"wss://{host}:{port}/sub", realRoomId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get danmaku conf (new API) for {RealRoomId}", realRoomId);
        }

        // Fallback to old API
        try 
        {
            var cookie = GetCookie();
            var json = await SendBiliGetAsync(
                "https://api.live.bilibili.com/room/v1/Danmu/getConf",
                new Dictionary<string, string?>
                {
                    ["room_id"] = realRoomId.ToString(),
                    ["platform"] = "pc",
                    ["player"] = "web"
                },
                cookie,
                referer: $"https://live.bilibili.com/{realRoomId}",
                origin: "https://live.bilibili.com");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("code", out var oldCode) && oldCode.GetInt32() == 0)
            {
                var data = root.GetProperty("data");
                var token = data.GetProperty("token").GetString();
                var hostList = data.GetProperty("host_server_list");
                var host = hostList[0].GetProperty("host").GetString();
                var port = hostList[0].GetProperty("wss_port").GetInt32();
                
                _logger.LogInformation("Got danmaku conf (old API) for {RealRoomId}: host={Host}", realRoomId, host);
                return (token ?? "", $"wss://{host}:{port}/sub", realRoomId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get danmaku conf (old API) for {RealRoomId}", realRoomId);
        }
        
        // Fallback
        return ("", "wss://broadcastlv.chat.bilibili.com/sub", realRoomId);
    }

    public async Task<(long RoomId, string Name)> GetRoomInfoByUidAsync(long uid)
    {
        try
        {
            var cookie = GetCookie();
            var json = await SendBiliGetAsync(
                "https://api.live.bilibili.com/live_user/v1/Master/info",
                new Dictionary<string, string?> { ["uid"] = uid.ToString() },
                cookie,
                referer: "https://live.bilibili.com/");
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
                _logger.LogWarning("GetRoomInfoByUidAsync failed for UID {Uid}: {Message}", uid, msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get room info for UID {Uid}", uid);
        }
        return (0, "");
    }

    public async Task<long> GetRealRoomIdAsync(long roomId)
    {
        try
        {
            var cookie = GetCookie();
            var json = await SendBiliGetAsync(
                "https://api.live.bilibili.com/room/v1/Room/room_init",
                new Dictionary<string, string?> { ["id"] = roomId.ToString() },
                cookie,
                origin: "https://live.bilibili.com");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data) && data.TryGetProperty("room_id", out var rid))
            {
                return rid.GetInt64();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve real room id for {RoomId}", roomId);
        }
        return roomId;
    }

    public async Task<LiveState?> GetRoomInitAsync(long roomId, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await SendBiliGetAsync(
                "https://api.live.bilibili.com/room/v1/Room/room_init",
                new Dictionary<string, string?> { ["id"] = roomId.ToString() },
                origin: "https://live.bilibili.com",
                cancellationToken: cancellationToken);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("code", out var code) || code.GetInt32() != 0)
                return null;
            if (!root.TryGetProperty("data", out var data))
                return null;

            var liveStatus = data.TryGetProperty("live_status", out var ls) ? ls.GetInt32() : 0;
            long? liveStartTime = null;
            if (data.TryGetProperty("live_time", out var lt))
            {
                if (lt.ValueKind == JsonValueKind.Number)
                    liveStartTime = lt.GetInt64();
                else if (lt.ValueKind == JsonValueKind.String && long.TryParse(lt.GetString(), out var parsed))
                    liveStartTime = parsed;
            }

            if (liveStartTime.HasValue && liveStartTime.Value > 0 && liveStartTime.Value < 1_000_000_000_000)
                liveStartTime = liveStartTime.Value * 1000;

            return new LiveState
            {
                RoomId = roomId,
                LiveStatus = liveStatus,
                LiveStartTime = liveStartTime,
                UpdatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get room init for Room {RoomId}", roomId);
            return null;
        }
    }

    public async Task<(int Followers, int GuardNum, int VideoCount)> GetVupStatsAsync(long roomId, string uid)
    {
        int followers = 0;
        int guardNum = 0;
        int videoCount = 0;
        var cookie = GetCookie();

        try
        {
            // 1. Official Bilibili APIs (Parallel for speed)
            var tasks = new List<Task>();

            // Followers
            tasks.Add(Task.Run(async () => {
                try {
                    var json = await SendBiliGetAsync(
                        "https://api.bilibili.com/x/relation/stat",
                        new Dictionary<string, string?> { ["vmid"] = uid },
                        cookie,
                        useWbi: false);
                    using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("follower", out var f))
                            followers = f.GetInt32();
                } catch {}
            }));

            // Video Count
            tasks.Add(Task.Run(async () => {
                try {
                    var json = await SendBiliGetAsync(
                        "https://api.bilibili.com/x/space/navnum",
                        new Dictionary<string, string?> { ["mid"] = uid },
                        cookie,
                        referer: $"https://space.bilibili.com/{uid}/");
                    using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("video", out var v))
                            videoCount = v.GetInt32();
                } catch {}
            }));

            // Guard Num (requires real room id)
            tasks.Add(Task.Run(async () => {
                try {
                    var realRoomId = await GetRealRoomIdAsync(roomId);
                    if (realRoomId <= 0) realRoomId = roomId;

                    var json = await SendBiliGetAsync(
                        "https://api.live.bilibili.com/xlive/app-room/v1/guardTab/topList",
                        new Dictionary<string, string?>
                        {
                            ["roomid"] = realRoomId.ToString(),
                            ["page"] = "1",
                            ["ruid"] = uid,
                            ["page_size"] = "0"
                        },
                        cookie,
                        referer: $"https://live.bilibili.com/{realRoomId}");
                    using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("data", out var data) && 
                            data.TryGetProperty("info", out var info) && 
                            info.TryGetProperty("num", out var n))
                            guardNum = n.GetInt32();
                } catch {}
            }));

            await Task.WhenAll(tasks);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get stats for UID {Uid}", uid);
        }

        return (followers, guardNum, videoCount);
    }
}
