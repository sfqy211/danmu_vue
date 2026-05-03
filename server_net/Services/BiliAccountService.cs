using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using Danmu.Server.Data;
using Danmu.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Services;

public class BiliAccountService
{
    private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
    private const string RecordingAllocRedisKey = "recording:alloc";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BiliAccountService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RedisService _redis;

    // Bilibili TV client credentials (commonly used open-source values)
    private const string AppKey = "4409e2ce8ffd12b8";
    private const string AppSec = "59b43e04ad6965f34319062b478f83dd";

    // In-memory cache for active account cookie to avoid DB query on hot path
    private string? _activeCookieCache;
    private readonly object _cacheLock = new();

    // ─── Round-robin + Failover state ──────────────────────────────────
    // Maps room uid → assigned account uid for sticky round-robin
    private readonly Dictionary<string, long> _roomAccountMap = new();
    // Accounts temporarily marked as failing (uid → failure expiry time)
    private readonly Dictionary<long, DateTime> _accountFailures = new();
    private readonly object _rotationLock = new();

    private readonly CancellationTokenSource _loopCts = new();

    // Login state tracking
    private readonly Dictionary<string, TvLoginState> _loginStates = new();

    public BiliAccountService(IServiceProvider serviceProvider, ILogger<BiliAccountService> logger, IHttpClientFactory httpClientFactory, RedisService redis)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _redis = redis;
    }

    // ─── Lifecycle ────────────────────────────────────────────────────

    public void StopAutoRefreshLoop() => _loopCts.Cancel();

    public async Task PreloadCacheAsync()
    {
        await RestoreRoomAssignmentsFromRedisAsync();
        await RefreshActiveCookieCacheAsync();
    }

    // ─── Cookie retrieval for consumers ───────────────────────────────

    public async Task<string?> GetActiveCookieStringAsync()
    {
        lock (_cacheLock)
        {
            if (_activeCookieCache != null) return _activeCookieCache;
        }
        return await RefreshActiveCookieCacheAsync();
    }

    public string? GetActiveCookieString()
    {
        lock (_cacheLock)
        {
            if (_activeCookieCache != null) return _activeCookieCache;
        }
        // Cache cold: use synchronous DB query to avoid blocking thread with GetAwaiter().GetResult()
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var account = db.BiliAccounts.AsNoTracking().FirstOrDefault(a => a.IsActive);
        var cookie = BuildCookieString(account?.CookieJson);
        lock (_cacheLock) { _activeCookieCache = cookie; }
        return cookie;
    }

    /// <summary>
    /// Get a cookie for a specific room using round-robin assignment.
    /// Each room sticks to its assigned account until that account fails.
    /// </summary>
    public string? GetCookieForRoom(string roomUid)
    {
        var result = GetCookieWithAccountForRoom(roomUid);
        return result.Cookie;
    }

    /// <summary>
    /// Get a cookie for a specific room along with the assigned account UID.
    /// </summary>
    public (string? Cookie, long? AccountUid) GetCookieWithAccountForRoom(string roomUid)
    {
        // Query DB outside the lock to avoid holding lock during I/O
        List<BiliAccount> allAccounts;
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
            allAccounts = db.BiliAccounts.AsNoTracking().ToList();
        }

        lock (_rotationLock)
        {
            // Clean up expired failures
            var now = DateTime.UtcNow;
            var expiredKeys = _accountFailures.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList();
            foreach (var key in expiredKeys) _accountFailures.Remove(key);

            if (allAccounts.Count == 0)
                return (null, null);

            // Filter out accounts with no cookie and currently failing accounts
            var available = allAccounts
                .Where(a => !string.IsNullOrWhiteSpace(a.CookieJson))
                .Where(a => !_accountFailures.ContainsKey(a.Uid))
                .ToList();

            if (available.Count == 0)
            {
                // All accounts are currently failing — wait for failures to expire instead of clearing immediately
                return (null, null);
            }

            // Check if room already has a sticky assignment
            if (_roomAccountMap.TryGetValue(roomUid, out var assignedUid))
            {
                var assigned = available.FirstOrDefault(a => a.Uid == assignedUid);
                if (assigned != null)
                    return (BuildCookieString(assigned.CookieJson), assigned.Uid);
                // Assigned account is failing or missing — fall through to reassign
            }

            // Least-loaded: pick the account with the fewest currently assigned rooms.
            // Keep stable tie-break behavior using roomUid hash so equal-load accounts do not all collapse to the first one.
            var assignmentCounts = _roomAccountMap
                .GroupBy(kv => kv.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            var selected = available
                .Select(account => new
                {
                    Account = account,
                    Load = assignmentCounts.TryGetValue(account.Uid, out var count) ? count : 0,
                    TieBreaker = Math.Abs(HashCode.Combine(roomUid, account.Uid))
                })
                .OrderBy(x => x.Load)
                .ThenBy(x => x.TieBreaker)
                .Select(x => x.Account)
                .First();
            _roomAccountMap[roomUid] = selected.Uid;
            _ = PersistRoomAssignmentAsync(roomUid, selected.Uid);
            return (BuildCookieString(selected.CookieJson), selected.Uid);
        }
    }

    /// <summary>
    /// Get all current room → account assignments.
    /// </summary>
    public Dictionary<string, long> GetRoomAssignments()
    {
        lock (_rotationLock)
        {
            return new Dictionary<string, long>(_roomAccountMap);
        }
    }

    /// <summary>
    /// Manually reassign a room to a specific account.
    /// Returns true if reassigned, false if target account not found or has no cookie.
    /// </summary>
    public bool ReassignRoom(string roomUid, long targetAccountUid)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var targetAccount = db.BiliAccounts.AsNoTracking().FirstOrDefault(a => a.Uid == targetAccountUid);
        if (targetAccount == null || string.IsNullOrWhiteSpace(targetAccount.CookieJson))
            return false;

        lock (_rotationLock)
        {
            // Mark the previously assigned account as failing if it exists
            if (_roomAccountMap.TryGetValue(roomUid, out var oldUid) && oldUid != targetAccountUid)
            {
                _accountFailures[oldUid] = DateTime.UtcNow.AddMinutes(10);
            }
            _roomAccountMap[roomUid] = targetAccountUid;
            _ = PersistRoomAssignmentAsync(roomUid, targetAccountUid);
        }
        return true;
    }

    /// <summary>
    /// Get the UID of the account currently assigned to a room (if any).
    /// </summary>
    public long? GetAssignedAccountUid(string roomUid)
    {
        lock (_rotationLock)
        {
            _roomAccountMap.TryGetValue(roomUid, out var uid);
            return uid;
        }
    }

    /// <summary>
    /// Report that a room's assigned account cookie has failed.
    /// Looks up the room's current assignment and marks that account as unavailable.
    /// </summary>
    public void ReportRoomFailure(string roomUid)
    {
        var accountUid = GetAssignedAccountUid(roomUid);
        if (accountUid.HasValue)
        {
            ReportAccountFailure(accountUid.Value);
        }
    }

    /// <summary>
    /// Report that an account's cookie has failed (e.g., got 403 or expired).
    /// This marks the account as temporarily unavailable and reassigns affected rooms.
    /// </summary>
    public void ReportAccountFailure(long accountUid)
    {
        lock (_rotationLock)
        {
            _accountFailures[accountUid] = DateTime.UtcNow.AddMinutes(10);
            // Remove room assignments that pointed to this account
            var affectedRooms = _roomAccountMap.Where(kv => kv.Value == accountUid).Select(kv => kv.Key).ToList();
            foreach (var room in affectedRooms)
            {
                _roomAccountMap.Remove(room);
                _ = RemoveRoomAssignmentAsync(room);
            }
            _logger.LogWarning("Account {Uid} reported as failing, marked unavailable for 10 minutes. {Count} rooms will be reassigned.", accountUid, affectedRooms.Count);
        }
    }

    private async Task RestoreRoomAssignmentsFromRedisAsync()
    {
        try
        {
            var stored = await _redis.GetHashAsync(RecordingAllocRedisKey);
            if (stored.Count == 0)
            {
                return;
            }

            lock (_rotationLock)
            {
                _roomAccountMap.Clear();
                foreach (var kv in stored)
                {
                    if (long.TryParse(kv.Value, out var accountUid))
                    {
                        _roomAccountMap[kv.Key] = accountUid;
                    }
                }
            }

            _logger.LogInformation("Restored {Count} room-account assignments from Redis.", stored.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore room-account assignments from Redis.");
        }
    }

    private async Task PersistRoomAssignmentAsync(string roomUid, long accountUid)
    {
        try
        {
            await _redis.SetHashFieldAsync(RecordingAllocRedisKey, roomUid, accountUid.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist room assignment for room {RoomUid} to Redis.", roomUid);
        }
    }

    private async Task RemoveRoomAssignmentAsync(string roomUid)
    {
        try
        {
            await _redis.DeleteHashFieldAsync(RecordingAllocRedisKey, roomUid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove room assignment for room {RoomUid} from Redis.", roomUid);
        }
    }

    private async Task<string?> RefreshActiveCookieCacheAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var account = await db.BiliAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.IsActive);
        var cookie = BuildCookieString(account?.CookieJson);
        lock (_cacheLock)
        {
            _activeCookieCache = cookie;
        }
        return cookie;
    }

    private void InvalidateCache()
    {
        lock (_cacheLock) { _activeCookieCache = null; }
    }

    private static string? BuildCookieString(string? cookieJson)
    {
        if (string.IsNullOrWhiteSpace(cookieJson)) return null;
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(cookieJson);
            if (dict == null || dict.Count == 0) return null;
            return string.Join("; ", dict.Select(kv => $"{kv.Key}={kv.Value}"));
        }
        catch
        {
            return null;
        }
    }

    // ─── Account CRUD ─────────────────────────────────────────────────

    public async Task<List<BiliAccount>> GetAllAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        return await db.BiliAccounts.AsNoTracking().OrderByDescending(a => a.IsActive).ThenByDescending(a => a.CreatedAt).ToListAsync();
    }

    public async Task<BiliAccount?> GetAsync(long uid)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        return await db.BiliAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Uid == uid);
    }

    public async Task DeleteAsync(long uid)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var acc = await db.BiliAccounts.FirstOrDefaultAsync(a => a.Uid == uid);
        if (acc == null) return;
        db.BiliAccounts.Remove(acc);
        await db.SaveChangesAsync();
        InvalidateCache();
    }

    public async Task SetActiveAsync(long uid)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var all = await db.BiliAccounts.ToListAsync();
        foreach (var a in all)
        {
            a.IsActive = a.Uid == uid;
        }
        await db.SaveChangesAsync();
        await RefreshActiveCookieCacheAsync();
    }

    /// <summary>
    /// Fetch user info via public API (x/web-interface/card) which does not require valid login cookie.
    /// Falls back to x/web-interface/nav if needed.
    /// </summary>
    public async Task UpdateUserInfoAsync(long uid)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var acc = await db.BiliAccounts.FirstOrDefaultAsync(a => a.Uid == uid);
        if (acc == null) throw new Exception("Account not found");

        try
        {
            var client = _httpClientFactory.CreateClient();
            // Use public card API first (no cookie required)
            var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.bilibili.com/x/web-interface/card?mid={uid}");
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            req.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com");
            var res = await client.SendAsync(req);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("code", out var code) && code.GetInt32() == 0 &&
                root.TryGetProperty("data", out var data) &&
                data.TryGetProperty("card", out var card))
            {
                if (card.TryGetProperty("name", out var name))
                    acc.Name = name.GetString();
                if (card.TryGetProperty("face", out var face))
                {
                    var faceUrl = face.GetString();
                    // Bilibili returns protocol-relative URLs like //i0.hdslb.com/...
                    if (!string.IsNullOrEmpty(faceUrl) && faceUrl.StartsWith("//"))
                        faceUrl = "https:" + faceUrl;
                    acc.Face = faceUrl;
                }
            }
            else
            {
                _logger.LogWarning("card API returned code {Code} for uid {Uid}, response: {Json}",
                    code.GetInt32(), uid, json.Substring(0, Math.Min(200, json.Length)));
            }

            acc.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update user info for {Uid}", uid);
        }
    }

    public async Task ImportFromCookieStringAsync(long uid, string cookieString)
    {
        var dict = new Dictionary<string, string>();
        foreach (var part in cookieString.Split(';'))
        {
            var kv = part.Trim().Split('=', 2);
            if (kv.Length == 2)
                dict[kv[0].Trim()] = kv[1].Trim();
        }

        var importedCookieJson = JsonSerializer.Serialize(dict);
        var importedCookie = BuildCookieString(importedCookieJson);
        if (string.IsNullOrWhiteSpace(importedCookie) || !await ValidateCookieAsync(importedCookie))
        {
            throw new InvalidOperationException("Imported cookie is invalid or expired.");
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var acc = await db.BiliAccounts.FirstOrDefaultAsync(a => a.Uid == uid);
        if (acc == null)
        {
            acc = new BiliAccount { Uid = uid };
            db.BiliAccounts.Add(acc);
        }
        acc.CookieJson = importedCookieJson;
        acc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await UpdateUserInfoAsync(uid);
        InvalidateCache();
    }

    public async Task<bool> ValidateCookieAsync(long uid)
    {
        var account = await GetAsync(uid);
        if (account == null)
        {
            return false;
        }

        var cookie = BuildCookieString(account.CookieJson);
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return false;
        }

        return await ValidateCookieAsync(cookie);
    }

    public async Task<bool> RefreshWebCookieAsync(long uid)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var acc = await db.BiliAccounts.FirstOrDefaultAsync(a => a.Uid == uid);
        if (acc == null)
            throw new Exception("Account not found");

        var cookieDict = DeserializeCookieDict(acc.CookieJson);
        if (cookieDict == null || cookieDict.Count == 0)
            throw new Exception("No cookie data");

        if (!cookieDict.TryGetValue("bili_jct", out var biliJct) || string.IsNullOrWhiteSpace(biliJct))
            throw new Exception("Cookie missing bili_jct");

        var refreshToken = acc.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            cookieDict.TryGetValue("ac_time_value", out refreshToken);
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new Exception("No refresh token available for web cookie refresh");

        var cookieString = BuildCookieString(acc.CookieJson);
        if (string.IsNullOrWhiteSpace(cookieString))
            throw new Exception("Failed to build cookie string");

        var (hash, publicKey) = await GetPassportRsaKeyAsync();
        var correspondPath = GenerateCorrespondPath(hash, publicKey);
        var refreshCsrf = await ExtractRefreshCsrfAsync(correspondPath, cookieString);
        if (string.IsNullOrWhiteSpace(refreshCsrf))
            throw new Exception("Failed to get refresh_csrf");

        var refreshResult = await PostCookieRefreshAsync(biliJct, refreshCsrf, refreshToken, cookieString);

        var mergedCookies = new Dictionary<string, string>(cookieDict, StringComparer.Ordinal);
        foreach (var kv in refreshResult.CookieDict)
        {
            mergedCookies[kv.Key] = kv.Value;
        }

        if (!string.IsNullOrWhiteSpace(refreshResult.RefreshToken))
        {
            acc.RefreshToken = refreshResult.RefreshToken;
            mergedCookies["ac_time_value"] = refreshResult.RefreshToken;
        }

        acc.CookieJson = JsonSerializer.Serialize(mergedCookies);
        acc.ExpiresAt = refreshResult.SessdataExpiresAt;
        acc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await PostConfirmRefreshAsync(mergedCookies.GetValueOrDefault("bili_jct") ?? biliJct, acc.RefreshToken ?? string.Empty, BuildCookieString(acc.CookieJson));

        await RefreshActiveCookieCacheAsync();
        return true;
    }

    // ─── TV QR Code Login ─────────────────────────────────────────────

    public async Task<(string url, string id)> StartTvLoginAsync()
    {
        var id = Guid.NewGuid().ToString("N");
        var (authCode, qrUrl) = await RequestTvAuthCodeAsync();
        var state = new TvLoginState
        {
            Id = id,
            AuthCode = authCode,
            Status = "scan"
        };
        lock (_loginStates)
        {
            _loginStates[id] = state;
        }

        _ = PollTvLoginAsync(id, authCode);
        return (qrUrl, id);
    }

    public TvLoginState? GetLoginState(string id)
    {
        lock (_loginStates)
        {
            _loginStates.TryGetValue(id, out var state);
            return state;
        }
    }

    public void CancelLogin(string id)
    {
        lock (_loginStates)
        {
            if (_loginStates.TryGetValue(id, out var state))
            {
                state.Cts.Cancel();
                _loginStates.Remove(id);
            }
        }
    }

    private async Task<(string authCode, string url)> RequestTvAuthCodeAsync()
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var paramDict = new SortedDictionary<string, string>
        {
            ["appkey"] = AppKey,
            ["local_id"] = "0",
            ["ts"] = ts.ToString()
        };
        var sign = TvSign(paramDict);
        paramDict["sign"] = sign;

        var client = _httpClientFactory.CreateClient();
        var content = new FormUrlEncodedContent(paramDict);
        // Reference uses http:// not https:// for passport API
        var res = await client.PostAsync("http://passport.bilibili.com/x/passport-tv-login/qrcode/auth_code", content);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("code", out var code) && code.GetInt32() != 0)
        {
            var msg = root.TryGetProperty("message", out var m) ? m.GetString() : "TV auth failed";
            throw new Exception(msg);
        }
        var data = root.GetProperty("data");
        var authCode = data.GetProperty("auth_code").GetString()!;
        var url = data.GetProperty("url").GetString()!;
        return (authCode, url);
    }

    private async Task PollTvLoginAsync(string id, string authCode)
    {
        TvLoginState state;
        lock (_loginStates) { state = _loginStates[id]; }

        try
        {
            while (!state.Cts.Token.IsCancellationRequested)
            {
                await Task.Delay(2000, state.Cts.Token);
                var (result, hasValue) = await PollTvAuthCodeAsync(authCode);
                if (!hasValue) continue;

                if (result == null || result.Code != 0)
                {
                    state.Status = "error";
                    state.FailReason = result?.Message ?? "Login failed";
                    lock (_loginStates) { _loginStates.Remove(id); }
                    break;
                }

                var accessToken = result.AccessToken!;
                var refreshToken = result.RefreshToken!;
                var expiresIn = result.ExpiresIn;

                var cookieDict = new Dictionary<string, string>();
                long? sessdataExpires = null;
                foreach (var c in result.Cookies!)
                {
                    cookieDict[c.Name] = c.Value;
                    if (c.Name == "SESSDATA" && c.Expires.HasValue)
                    {
                        sessdataExpires = c.Expires.Value;
                    }
                }

                var mid = result.Mid;
                if (mid == 0)
                {
                    if (cookieDict.TryGetValue("DedeUserID", out var dedeUid) && long.TryParse(dedeUid, out var parsedUid))
                        mid = parsedUid;
                }

                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
                var acc = await db.BiliAccounts.FirstOrDefaultAsync(a => a.Uid == mid);
                if (acc == null)
                {
                    acc = new BiliAccount { Uid = mid };
                    db.BiliAccounts.Add(acc);
                }
                acc.AccessToken = accessToken;
                acc.RefreshToken = refreshToken;
                acc.CookieJson = JsonSerializer.Serialize(cookieDict);
                acc.ExpiresAt = sessdataExpires.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(sessdataExpires.Value).UtcDateTime
                    : DateTime.UtcNow.AddSeconds(expiresIn);
                acc.UpdatedAt = DateTime.UtcNow;
                // Newly logged-in account becomes active automatically
                acc.IsActive = true;
                await db.SaveChangesAsync();

                // Fetch user info with error handling
                _ = Task.Run(async () =>
                {
                    try { await UpdateUserInfoAsync(mid); }
                    catch (Exception ex) { _logger.LogWarning(ex, "UpdateUserInfoAsync failed after login for {Uid}", mid); }
                });

                state.Status = "completed";
                state.Uid = mid;
                await RefreshActiveCookieCacheAsync();
                // Keep state for 30s so frontend can poll and see "completed"
                _ = Task.Run(async () => { await Task.Delay(30000); lock (_loginStates) { _loginStates.Remove(id); } });
                break;
            }
        }
        catch (OperationCanceledException)
        {
            lock (_loginStates) { _loginStates.Remove(id); }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TV login poll error");
            state.Status = "error";
            state.FailReason = ex.Message;
            lock (_loginStates) { _loginStates.Remove(id); }
        }
    }

    private async Task<(TvPollResult? result, bool hasValue)> PollTvAuthCodeAsync(string authCode)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var paramDict = new SortedDictionary<string, string>
        {
            ["appkey"] = AppKey,
            ["auth_code"] = authCode,
            ["local_id"] = "0",
            ["ts"] = ts.ToString()
        };
        paramDict["sign"] = TvSign(paramDict);

        var client = _httpClientFactory.CreateClient();
        var content = new FormUrlEncodedContent(paramDict);
        var res = await client.PostAsync("http://passport.bilibili.com/x/passport-tv-login/qrcode/poll", content);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("code", out var codeEl))
        {
            var c = codeEl.GetInt32();
            if (c == 86039) return (null, false);
            if (c == 86038) throw new Exception("QR code expired");
            if (c != 0)
            {
                return (new TvPollResult
                {
                    Code = c,
                    Message = root.TryGetProperty("message", out var m) ? m.GetString() : null
                }, true);
            }
        }

        var data = root.GetProperty("data");
        var result = new TvPollResult { Code = 0 };
        if (data.TryGetProperty("access_token", out var at)) result.AccessToken = at.GetString();
        if (data.TryGetProperty("refresh_token", out var rt)) result.RefreshToken = rt.GetString();
        if (data.TryGetProperty("expires_in", out var ei)) result.ExpiresIn = ei.GetInt64();
        if (data.TryGetProperty("cookie_info", out var ci))
        {
            result.Cookies = new List<TvCookieItem>();
            foreach (var c in ci.GetProperty("cookies").EnumerateArray())
            {
                result.Cookies.Add(new TvCookieItem
                {
                    Name = c.GetProperty("name").GetString()!,
                    Value = c.GetProperty("value").GetString()!,
                    Expires = c.TryGetProperty("expires", out var exp) ? exp.GetInt64() : null
                });
            }
        }
        if (data.TryGetProperty("mid", out var midEl)) result.Mid = midEl.GetInt64();
        if (data.TryGetProperty("token_info", out var ti) && ti.TryGetProperty("mid", out var tiMid)) result.Mid = tiMid.GetInt64();
        return (result, true);
    }

    // ─── Token Refresh ────────────────────────────────────────────────

    public async Task RefreshAuthAsync(long uid)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var acc = await db.BiliAccounts.FirstOrDefaultAsync(a => a.Uid == uid);
        if (acc == null) throw new Exception("Account not found");
        if (string.IsNullOrEmpty(acc.RefreshToken)) throw new Exception("No refresh token");

        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var paramDict = new SortedDictionary<string, string>
        {
            ["access_key"] = acc.AccessToken ?? "",
            ["actionKey"] = "appkey",
            ["appkey"] = AppKey,
            ["refresh_token"] = acc.RefreshToken,
            ["ts"] = ts.ToString()
        };
        paramDict["sign"] = TvSign(paramDict);

        var client = _httpClientFactory.CreateClient();
        var content = new FormUrlEncodedContent(paramDict);
        var res = await client.PostAsync("https://passport.bilibili.com/x/passport-login/oauth2/refresh_token", content);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("code", out var code) && code.GetInt32() != 0)
        {
            var msg = root.TryGetProperty("message", out var m) ? m.GetString() : "Refresh failed";
            throw new Exception(msg);
        }

        var data = root.GetProperty("data");

        // Parse token_info: some APIs nest under data.token_info, others put fields directly in data
        string accessToken;
        string refreshToken;
        long expiresIn;
        JsonElement cookieInfo;
        if (data.TryGetProperty("token_info", out var tokenInfo))
        {
            accessToken = tokenInfo.GetProperty("access_token").GetString()!;
            refreshToken = tokenInfo.GetProperty("refresh_token").GetString()!;
            expiresIn = tokenInfo.GetProperty("expires_in").GetInt64();
            // cookie_info may be at data level or token_info level
            if (!tokenInfo.TryGetProperty("cookie_info", out cookieInfo))
                cookieInfo = data.GetProperty("cookie_info");
        }
        else
        {
            accessToken = data.GetProperty("access_token").GetString()!;
            refreshToken = data.GetProperty("refresh_token").GetString()!;
            expiresIn = data.GetProperty("expires_in").GetInt64();
            cookieInfo = data.GetProperty("cookie_info");
        }

        acc.AccessToken = accessToken;
        acc.RefreshToken = refreshToken;
        var cookies = cookieInfo.GetProperty("cookies").EnumerateArray();

        var cookieDict = new Dictionary<string, string>();
        long? sessdataExpires = null;
        foreach (var c in cookies)
        {
            var name = c.GetProperty("name").GetString()!;
            var value = c.GetProperty("value").GetString()!;
            cookieDict[name] = value;
            if (name == "SESSDATA" && c.TryGetProperty("expires", out var exp))
            {
                sessdataExpires = exp.GetInt64();
            }
        }
        acc.CookieJson = JsonSerializer.Serialize(cookieDict);
        acc.ExpiresAt = sessdataExpires.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(sessdataExpires.Value).UtcDateTime
            : DateTime.UtcNow.AddSeconds(expiresIn);
        acc.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await RefreshActiveCookieCacheAsync();
    }

    // ─── Auto Refresh Loop ────────────────────────────────────────────

    public void StartAutoRefreshLoop()
    {
        _ = Task.Run(async () =>
        {
            while (!_loopCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromHours(1), _loopCts.Token);
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
                    var list = await db.BiliAccounts.AsNoTracking().ToListAsync();
                    foreach (var acc in list)
                    {
                        try
                        {
                            var isHealthy = await ValidateCookieAsync(acc.Uid);
                            if (!isHealthy)
                            {
                                _logger.LogWarning("Cookie health check failed for {Uid}; reporting account failure.", acc.Uid);
                                ReportAccountFailure(acc.Uid);
                                continue;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Cookie health check threw for {Uid}", acc.Uid);
                        }

                        if (acc.ExpiresAt.HasValue && acc.ExpiresAt.Value - DateTime.UtcNow < TimeSpan.FromDays(10))
                        {
                            try
                            {
                                await RefreshAuthAsync(acc.Uid);
                                _logger.LogInformation("Auto-refreshed auth for {Uid}", acc.Uid);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Auto-refresh failed for {Uid}", acc.Uid);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Auto-refresh loop error");
                }
            }
        }, _loopCts.Token);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates Bilibili API sign. Matches @renmu/bili-api implementation:
    /// No URL-encoding of parameter values; raw string concatenation with &.
    /// </summary>
    private async Task<bool> ValidateCookieAsync(string cookieString)
    {
        var client = _httpClientFactory.CreateClient();
        using var request = CreateBrowserRequest(HttpMethod.Get, "https://api.bilibili.com/x/web-interface/nav");
        request.Headers.TryAddWithoutValidation("Cookie", cookieString);

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("code", out var codeEl) || codeEl.GetInt32() != 0)
        {
            return false;
        }

        if (!root.TryGetProperty("data", out var data))
        {
            return false;
        }

        return data.TryGetProperty("isLogin", out var isLoginEl) && isLoginEl.ValueKind == JsonValueKind.True;
    }

    private async Task<(string Hash, string PublicKeyPem)> GetPassportRsaKeyAsync()
    {
        var client = _httpClientFactory.CreateClient();
        using var request = CreateBrowserRequest(HttpMethod.Get, "https://passport.bilibili.com/x/passport-login/web/key");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("code", out var codeEl) || codeEl.GetInt32() != 0)
        {
            var message = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : "Unknown error";
            throw new InvalidOperationException($"Failed to get passport RSA key: {message}");
        }

        var data = root.GetProperty("data");
        return (
            data.GetProperty("hash").GetString() ?? string.Empty,
            data.GetProperty("key").GetString() ?? string.Empty);
    }

    private static string GenerateCorrespondPath(string hash, string publicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(publicKeyPem))
            throw new InvalidOperationException("Invalid RSA key data");

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var plaintext = $"{hash}refresh_{timestamp}";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        var encrypted = rsa.Encrypt(Encoding.UTF8.GetBytes(plaintext), RSAEncryptionPadding.OaepSHA1);
        return Uri.EscapeDataString(Convert.ToBase64String(encrypted));
    }

    private async Task<string?> ExtractRefreshCsrfAsync(string correspondPath, string cookieString)
    {
        var client = _httpClientFactory.CreateClient();
        using var request = CreateBrowserRequest(HttpMethod.Get, $"https://www.bilibili.com/correspond/1/{correspondPath}");
        request.Headers.TryAddWithoutValidation("Cookie", cookieString);

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "<div\\s+id=\"1-name\">(?<csrf>[^<]+)</div>", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["csrf"].Value : null;
    }

    private async Task<WebCookieRefreshResult> PostCookieRefreshAsync(string biliJct, string refreshCsrf, string refreshToken, string cookieString)
    {
        var client = _httpClientFactory.CreateClient();
        using var request = CreateBrowserRequest(HttpMethod.Post, "https://passport.bilibili.com/x/passport-login/web/cookie/refresh");
        request.Headers.TryAddWithoutValidation("Cookie", cookieString);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["csrf"] = biliJct,
            ["refresh_csrf"] = refreshCsrf,
            ["refresh_token"] = refreshToken,
            ["source"] = "main_web"
        });

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("code", out var codeEl) || codeEl.GetInt32() != 0)
        {
            var message = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : "Unknown error";
            throw new InvalidOperationException($"Failed to refresh web cookie: {message}");
        }

        var data = root.GetProperty("data");
        var newRefreshToken = data.TryGetProperty("refresh_token", out var rtEl) ? rtEl.GetString() : null;
        var cookies = ExtractCookiesFromResponse(response);

        DateTime? sessdataExpiresAt = null;
        if (TryGetCookieExpiry(response, "SESSDATA", out var expiresAt))
        {
            sessdataExpiresAt = expiresAt.UtcDateTime;
        }

        return new WebCookieRefreshResult(cookies, newRefreshToken, sessdataExpiresAt);
    }

    private async Task PostConfirmRefreshAsync(string biliJct, string refreshToken, string? cookieString)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return;

        var client = _httpClientFactory.CreateClient();
        using var request = CreateBrowserRequest(HttpMethod.Post, "https://passport.bilibili.com/x/passport-login/web/confirm/refresh");
        if (!string.IsNullOrWhiteSpace(cookieString))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookieString);
        }
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["csrf"] = biliJct,
            ["refresh_token"] = refreshToken,
            ["source"] = "main_web"
        });

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("code", out var codeEl) || codeEl.GetInt32() != 0)
        {
            var message = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : "Unknown error";
            _logger.LogWarning(
                "Web cookie confirm refresh returned non-zero code for refresh token. Code={Code}, Message={Message}",
                codeEl.ValueKind == JsonValueKind.Number ? codeEl.GetInt32() : -1,
                message);
        }
    }

    private static HttpRequestMessage CreateBrowserRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
        request.Headers.TryAddWithoutValidation("Referer", "https://www.bilibili.com");
        return request;
    }

    internal static Dictionary<string, string>? DeserializeCookieDict(string? cookieJson)
    {
        if (string.IsNullOrWhiteSpace(cookieJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(cookieJson);
        }
        catch
        {
            return null;
        }
    }

    internal static Dictionary<string, string> ExtractCookiesFromResponse(HttpResponseMessage response)
    {
        var cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            return cookies;
        }

        foreach (var setCookie in setCookies)
        {
            var firstPart = setCookie.Split(';', 2)[0];
            var eqIndex = firstPart.IndexOf('=');
            if (eqIndex <= 0)
                continue;

            var name = firstPart[..eqIndex].Trim();
            var value = firstPart[(eqIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                cookies[name] = value;
            }
        }

        return cookies;
    }

    internal static bool TryGetCookieExpiry(HttpResponseMessage response, string cookieName, out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            return false;
        }

        foreach (var setCookie in setCookies)
        {
            var parts = setCookie.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            if (!parts[0].StartsWith(cookieName + "=", StringComparison.Ordinal))
                continue;

            foreach (var part in parts.Skip(1))
            {
                if (part.StartsWith("Expires=", StringComparison.OrdinalIgnoreCase)
                    && DateTimeOffset.TryParse(part[8..], out var parsed))
                {
                    expiresAt = parsed;
                    return true;
                }
            }
        }

        return false;
    }

    private static string TvSign(SortedDictionary<string, string> paramDict)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var kv in paramDict)
        {
            if (!first) sb.Append('&');
            sb.Append(kv.Key).Append('=').Append(kv.Value);
            first = false;
        }
        sb.Append(AppSec);
        var input = sb.ToString();
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

internal sealed record WebCookieRefreshResult(
    Dictionary<string, string> CookieDict,
    string? RefreshToken,
    DateTime? SessdataExpiresAt);

public class TvLoginState
{
    public string Id { get; set; } = "";
    public string AuthCode { get; set; } = "";
    public string Status { get; set; } = "scan";
    public string FailReason { get; set; } = "";
    public long? Uid { get; set; }
    public CancellationTokenSource Cts { get; set; } = new CancellationTokenSource();
}

public class TvPollResult
{
    public int Code { get; set; }
    public string? Message { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public long ExpiresIn { get; set; }
    public List<TvCookieItem>? Cookies { get; set; }
    public long Mid { get; set; }
}

public class TvCookieItem
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public long? Expires { get; set; }
}
