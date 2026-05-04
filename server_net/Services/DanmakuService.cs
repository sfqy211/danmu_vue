using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Danmu.Server.Data;
using Danmu.Server.Models;
using Danmu.Server.Utils;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Services;

public class DanmakuService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new();
    private static readonly SemaphoreSlim FileProcessingSemaphore = new(4, 4);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DanmakuService> _logger;
    private readonly RedisService _redis;
    private readonly string _danmakuDir;
    private readonly string _danmakuTmpDir;

    public DanmakuService(IServiceScopeFactory scopeFactory, ILogger<DanmakuService> logger, RedisService redis)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _redis = redis;
        _danmakuDir = Environment.GetEnvironmentVariable("DANMAKU_DIR")
            ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../server/data/danmaku"));
        _danmakuTmpDir = Environment.GetEnvironmentVariable("DANMAKU_TMP_DIR")
            ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../server/data/danmaku_tmp"));
    }

    private DanmuContext GetDb(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<DanmuContext>();

    public async Task<Session?> GetActiveSessionAsync(string uid, long roomId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = GetDb(scope);
        var roomIdStr = roomId.ToString();

        return await db.Sessions
            .Where(s => (s.EndTime == null || s.EndTime == 0) &&
                        ((!string.IsNullOrEmpty(uid) && s.Uid == uid) ||
                         (string.IsNullOrEmpty(s.Uid) && s.RoomId == roomIdStr)))
            .OrderByDescending(s => s.StartTime)
            .FirstOrDefaultAsync();
    }

    public async Task ReconcileTmpFilesAsync(string uid, long roomId, long? currentLiveStartTime, bool isLive)
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            uid = roomId.ToString();
        }

        var roomTmpDir = Path.Combine(_danmakuTmpDir, uid);
        if (!Directory.Exists(roomTmpDir))
        {
            return;
        }

        var tmpFiles = Directory.GetFiles(roomTmpDir, "*.jsonl", SearchOption.TopDirectoryOnly);
        if (tmpFiles.Length == 0)
        {
            return;
        }

        var keepFileName = isLive && currentLiveStartTime.HasValue && currentLiveStartTime.Value > 0
            ? $"{currentLiveStartTime.Value}.jsonl"
            : null;

        foreach (var tmpFile in tmpFiles)
        {
            if (!string.IsNullOrEmpty(keepFileName)
                && string.Equals(Path.GetFileName(tmpFile), keepFileName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Restoring tmp file to Redis for active live session: {TmpFile}", tmpFile);
                await RestoreTmpToRedisAsync(uid, roomId, tmpFile, currentLiveStartTime!.Value);
                continue;
            }

            await PromoteTmpFileAsync(uid, roomId, tmpFile);
        }
    }

    private async Task RestoreTmpToRedisAsync(string uid, long roomId, string tmpFilePath, long liveStartTime)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync(tmpFilePath);
            var messageLines = new List<string>();
            SessionFileMeta? metaInfo = null;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (TryGetString(root, "kind") == "meta")
                    {
                        metaInfo = new SessionFileMeta
                        {
                            Title = TryGetString(root, "title") ?? "未知直播",
                            UserName = TryGetString(root, "userName") ?? "未知主播",
                            RoomId = TryGetString(root, "roomId") ?? roomId.ToString(),
                            Uid = TryGetString(root, "uid") ?? uid,
                            RecordStartTimestamp = TryGetInt64(root, "startTime") ?? liveStartTime
                        };
                        continue;
                    }
                }
                catch { }

                messageLines.Add(line);
            }

            if (messageLines.Count == 0 && metaInfo == null)
            {
                _logger.LogWarning("Tmp file {TmpFile} is empty or has no valid data, deleting", tmpFilePath);
                File.Delete(tmpFilePath);
                return;
            }

            var sessionKey = $"danmaku:session:{uid}:{liveStartTime}";
            var listKey = sessionKey + ":list";
            var metaKey = sessionKey + ":meta";

            var existingRedisCount = await _redis.GetListLengthAsync(listKey);
            var restoredMessageCount = messageLines.Count;

            if (existingRedisCount > messageLines.Count)
            {
                var redisTail = await _redis.GetListRangeAsync(listKey, messageLines.Count, existingRedisCount - 1);
                if (redisTail.Count > 0)
                {
                    await File.AppendAllLinesAsync(tmpFilePath, redisTail, System.Text.Encoding.UTF8);
                    restoredMessageCount += redisTail.Count;
                    _logger.LogInformation(
                        "Preserved {TailCount} Redis-only messages by appending them to tmp file {TmpFile}",
                        redisTail.Count,
                        tmpFilePath);
                }
            }
            else if (existingRedisCount < messageLines.Count)
            {
                // Redis is empty or behind tmp, rebuild it from the tmp baseline.
                await _redis.DeleteKeyAsync(listKey);
                await _redis.PushMessagesAsync(listKey, messageLines);
                restoredMessageCount = messageLines.Count;
            }
            else if (existingRedisCount == 0 && messageLines.Count == 0)
            {
                _logger.LogInformation("Tmp file {TmpFile} has no messages yet; restoring metadata only", tmpFilePath);
            }

            // 构建 meta
            var meta = new Dictionary<string, string>
            {
                { "uid", metaInfo?.Uid ?? uid },
                { "room_id", metaInfo?.RoomId ?? roomId.ToString() },
                { "real_room_id", metaInfo?.RoomId ?? roomId.ToString() },
                { "room_title", metaInfo?.Title ?? "未知直播" },
                { "user_name", metaInfo?.UserName ?? "未知主播" },
                { "video_start_time", liveStartTime.ToString() },
                { "start_time_is_fallback", "0" },
                { "filename", $"{liveStartTime}.jsonl" },
                { "dump_offset", restoredMessageCount.ToString() }
            };

            await _redis.DeleteKeyAsync(metaKey);
            await _redis.SetMetadataAsync(metaKey, meta);
            await _redis.SetLiveSessionKeyAsync(uid, sessionKey);

            _logger.LogInformation(
                "Restored active tmp file {TmpFile} to Redis session {SessionKey} (tmpMessages={TmpCount}, redisExisting={RedisCount}, dumpOffset={DumpOffset})",
                tmpFilePath, sessionKey, messageLines.Count, existingRedisCount, restoredMessageCount);

            // 更新 DB session
            using var scope = _scopeFactory.CreateScope();
            var db = GetDb(scope);
            var roomIdStr = roomId.ToString();

            var session = await db.Sessions
                .Where(s =>
                    (s.EndTime == null || s.EndTime == 0) &&
                    ((!string.IsNullOrEmpty(uid) && s.Uid == uid) ||
                     (string.IsNullOrEmpty(s.Uid) && s.RoomId == roomIdStr)))
                .OrderByDescending(s => s.StartTime)
                .FirstOrDefaultAsync();

            if (session != null)
            {
                session.FilePath = "redis:" + sessionKey;
                session.Title = metaInfo?.Title ?? session.Title;
                session.UserName = metaInfo?.UserName ?? session.UserName;
                session.StartTime = liveStartTime;
                await db.SaveChangesAsync();
                _logger.LogInformation(
                    "Updated DB session {SessionId} FilePath to redis:{SessionKey}",
                    session.Id, sessionKey);
            }
            else if (restoredMessageCount > 0)
            {
                session = new Session
                {
                    Uid = uid,
                    RoomId = roomIdStr,
                    Title = metaInfo?.Title ?? "未知直播",
                    UserName = metaInfo?.UserName ?? "未知主播",
                    StartTime = liveStartTime,
                    EndTime = 0,
                    FilePath = "redis:" + sessionKey,
                    SummaryJson = "{}",
                    GiftSummaryJson = "{}",
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                };
                db.Sessions.Add(session);
                await db.SaveChangesAsync();
                _logger.LogInformation(
                    "Created new DB session for recovered Redis session {SessionKey}",
                    sessionKey);
            }
            else
            {
                _logger.LogInformation("Skipped creating DB session for meta-only tmp file {TmpFile}", tmpFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to restore tmp file {TmpFile} to Redis for uid {Uid}, room {RoomId}",
                tmpFilePath, uid, roomId);
        }
    }

    public async Task PromoteTmpFileAsync(string uid, long roomId, string tmpFilePath)
    {
        if (!File.Exists(tmpFilePath))
        {
            return;
        }

        var parsed = await ParseJsonlFileAsync(tmpFilePath);
        if (parsed == null)
        {
            return;
        }

        var effectiveUid = !string.IsNullOrWhiteSpace(parsed.Meta.Uid) ? parsed.Meta.Uid : uid;
        var effectiveRoomId = long.TryParse(parsed.Meta.RoomId, out var parsedRoomId) ? parsedRoomId : roomId;
        var startTimestamp = parsed.Meta.RecordStartTimestamp > 0
            ? parsed.Meta.RecordStartTimestamp
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var title = string.IsNullOrWhiteSpace(parsed.Meta.Title) ? "未知直播" : parsed.Meta.Title;

        var roomDir = Path.Combine(_danmakuDir, effectiveUid);
        Directory.CreateDirectory(roomDir);
        var dateStr = DateTimeOffset.FromUnixTimeMilliseconds(startTimestamp).LocalDateTime.ToString("yyyy-MM-dd HH-mm-ss");
        var finalFileName = $"{dateStr} {BilibiliRecorder.SanitizeFileName(title)}.jsonl";
        var finalFilePath = Path.Combine(roomDir, finalFileName);

        _logger.LogInformation("Promoting tmp danmaku file {TmpFile} to {FinalFile}", tmpFilePath, finalFilePath);
        FileUtils.MoveFileWithFallback(tmpFilePath, finalFilePath, _logger);

        var activeSession = await GetActiveSessionAsync(effectiveUid, effectiveRoomId);
        if (activeSession != null)
        {
            var endTime = parsed.Messages.Count > 0
                ? parsed.Messages[^1].Timestamp
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await CloseSessionAsync(effectiveUid, effectiveRoomId, endTime, finalFilePath);
        }
        else
        {
            await ProcessFileAsync(finalFilePath);
        }
    }

    public async Task CreateLiveSessionAsync(string uid, long roomId, string title, string userName, long startTime, string sessionKey)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = GetDb(scope);
        var roomIdStr = roomId.ToString();

        var session = await db.Sessions.FirstOrDefaultAsync(s =>
            (s.EndTime == null || s.EndTime == 0) &&
            ((!string.IsNullOrEmpty(uid) && s.Uid == uid) ||
             (string.IsNullOrEmpty(s.Uid) && s.RoomId == roomIdStr)));

        if (session == null)
        {
            session = new Session
            {
                Uid = uid,
                RoomId = roomIdStr,
                Title = title,
                UserName = userName,
                StartTime = startTime,
                EndTime = 0,
                FilePath = "redis:" + sessionKey,
                SummaryJson = "{}",
                GiftSummaryJson = "{}",
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };
            db.Sessions.Add(session);
        }
        else
        {
            session.Uid = uid;
            session.RoomId = roomIdStr;
            session.Title = title;
            session.UserName = userName;
            session.StartTime = startTime;
            session.FilePath = "redis:" + sessionKey;
        }

        await db.SaveChangesAsync();
    }

    public async Task UpdateLiveSessionTitleAsync(string uid, long roomId, string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;

        using var scope = _scopeFactory.CreateScope();
        var db = GetDb(scope);
        var roomIdStr = roomId.ToString();

        var session = await db.Sessions
            .Where(s => (s.EndTime == null || s.EndTime == 0) &&
                        ((!string.IsNullOrEmpty(uid) && s.Uid == uid) ||
                         (string.IsNullOrEmpty(s.Uid) && s.RoomId == roomIdStr)))
            .OrderByDescending(s => s.StartTime)
            .FirstOrDefaultAsync();

        if (session != null && session.Title != title)
        {
            session.Title = title;
            await db.SaveChangesAsync();
        }
    }

    public async Task CloseSessionAsync(string uid, long roomId, long endTime, string finalFilePath)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = GetDb(scope);
        var roomIdStr = roomId.ToString();

        var session = await db.Sessions
            .Where(s => (s.EndTime == null || s.EndTime == 0) &&
                        ((!string.IsNullOrEmpty(uid) && s.Uid == uid) ||
                         (string.IsNullOrEmpty(s.Uid) && s.RoomId == roomIdStr)))
            .OrderByDescending(s => s.StartTime)
            .FirstOrDefaultAsync();

        if (session != null)
        {
            session.Uid = uid;
            session.RoomId = roomIdStr;
            session.EndTime = endTime;
            if (!string.IsNullOrEmpty(finalFilePath))
            {
                session.FilePath = Path.GetRelativePath(_danmakuDir, finalFilePath).Replace("\\", "/");
                await db.SaveChangesAsync();
                await ProcessFileAsync(finalFilePath);
            }
            else
            {
                await db.SaveChangesAsync();
            }
        }
    }

    public async Task<object> GetDanmakuPagedAsync(int sessionId, int page, int pageSize)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = GetDb(scope);
        var session = await db.Sessions.FindAsync(sessionId);

        if (session == null || string.IsNullOrEmpty(session.FilePath))
        {
            return new { total = 0, list = new List<object>(), page, pageSize, totalPages = 0 };
        }

        List<DanmakuMessage> messages;
        if (session.FilePath.StartsWith("redis:", StringComparison.Ordinal))
        {
            var key = session.FilePath.Substring(6);
            var lines = await _redis.GetMessagesAsync(key + ":list");
            messages = ParseRecordedEventLines(lines);
        }
        else
        {
            var fullPath = ResolveSessionFilePath(session);
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                return new { total = 0, list = new List<object>(), page, pageSize, totalPages = 0 };
            }

            messages = await LoadMessagesFromFileAsync(fullPath);
        }

        var displayable = messages.Where(m =>
            m.Type == "comment" ||
            m.Type == "super_chat" ||
            m.Type == "give_gift" ||
            m.Type == "guard" ||
            m.Type == "gift_combo" ||
            m.Type == "enter" ||
            m.Type == "follow" ||
            m.Type == "share" ||
            m.Type == "interact" ||
            m.Type == "room_change").ToList();

        // Merge bilingual SC: if consecutive SC have same user/uid/timestamp/price, keep only the first one
        displayable = MergeBilingualSuperChats(displayable);

        var total = displayable.Count;
        var safePageSize = Math.Max(1, pageSize);
        var safePage = Math.Max(1, page);
        var paged = displayable
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(m => new
            {
                time = Math.Max(0, (m.Timestamp - (session.StartTime ?? 0)) / 1000.0),
                timestamp = m.Timestamp,
                sender = m.Sender.Name,
                uid = m.Sender.Uid,
                text = m.Text,
                textJpn = m.TextJpn,
                isSC = m.Type == "super_chat",
                type = m.Type,
                rawCommand = m.RawCommand,
                name = m.Name,
                count = m.Count,
                price = m.Price,
                isPriceTotal = m.IsPriceTotal,
                guardLevel = m.GuardLevel,
                medalLevel = m.MedalLevel,
                medalName = m.MedalName,
                medalAnchor = m.MedalAnchor,
                medalRoomId = m.MedalRoomId,
                medalGuardLevel = m.MedalGuardLevel,
                medalIsLight = m.MedalIsLight,
                medalAnchorUid = m.MedalAnchorUid,
                ulLevel = m.UlLevel,
                wealthLevel = m.WealthLevel,
                coinType = m.CoinType,
                duration = m.Duration,
                id = $"{m.Timestamp}-{m.Sender.Uid}"
            })
            .ToList();

        return new
        {
            total,
            list = paged,
            page = safePage,
            pageSize = safePageSize,
            totalPages = (int)Math.Ceiling(total / (double)safePageSize)
        };
    }

    public async Task<AnalysisResult?> ProcessFileAsync(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension != ".xml" && extension != ".jsonl") return null;

        var fileLock = FileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync();
        await FileProcessingSemaphore.WaitAsync();
        try
        {
            var parsed = extension == ".jsonl"
                ? await ParseJsonlFileAsync(filePath)
                : await ParseLegacyXmlFileAsync(filePath);

            if (parsed == null) return null;

            var analysis = BuildAnalysis(parsed.Messages);
            var giftAnalysis = BuildGiftAnalysis(parsed.Messages);
            var relativePath = Path.GetRelativePath(_danmakuDir, filePath).Replace("\\", "/");

            using var scope = _scopeFactory.CreateScope();
            var db = GetDb(scope);

            var existingSession = await db.Sessions.FirstOrDefaultAsync(s => s.FilePath == relativePath);
            if (existingSession == null && !string.IsNullOrEmpty(parsed.Meta.Uid))
            {
                existingSession = await db.Sessions.FirstOrDefaultAsync(s => s.Uid == parsed.Meta.Uid && s.StartTime == parsed.Meta.RecordStartTimestamp);
            }
            if (existingSession == null && !string.IsNullOrEmpty(parsed.Meta.RoomId))
            {
                existingSession = await db.Sessions.FirstOrDefaultAsync(s => s.RoomId == parsed.Meta.RoomId && s.StartTime == parsed.Meta.RecordStartTimestamp);
            }

            if (existingSession == null)
            {
                existingSession = new Session
                {
                    Uid = parsed.Meta.Uid,
                    RoomId = parsed.Meta.RoomId,
                    Title = parsed.Meta.Title,
                    UserName = parsed.Meta.UserName,
                    StartTime = parsed.Meta.RecordStartTimestamp,
                    EndTime = parsed.Messages.Count > 0 ? parsed.Messages.Last().Timestamp : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    FilePath = relativePath,
                    SummaryJson = JsonSerializer.Serialize(analysis, JsonOptions),
                    GiftSummaryJson = JsonSerializer.Serialize(giftAnalysis, JsonOptions),
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                };
                db.Sessions.Add(existingSession);
                await db.SaveChangesAsync();
            }
            else
            {
                existingSession.Uid = parsed.Meta.Uid ?? existingSession.Uid;
                existingSession.RoomId = parsed.Meta.RoomId ?? existingSession.RoomId;
                existingSession.Title = parsed.Meta.Title;
                existingSession.UserName = parsed.Meta.UserName;
                existingSession.EndTime = parsed.Messages.Count > 0
                    ? parsed.Messages.Last().Timestamp
                    : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                existingSession.FilePath = relativePath;
                existingSession.SummaryJson = JsonSerializer.Serialize(analysis, JsonOptions);
                existingSession.GiftSummaryJson = JsonSerializer.Serialize(giftAnalysis, JsonOptions);
                await db.SaveChangesAsync();
            }

            var oldRequests = db.SongRequests.Where(r => r.SessionId == existingSession.Id);
            db.SongRequests.RemoveRange(oldRequests);
            await db.SaveChangesAsync();

            foreach (var sr in BuildSongRequests(parsed.Messages))
            {
                sr.SessionId = existingSession.Id;
                sr.RoomId = parsed.Meta.RoomId;
                sr.Uid = sr.Uid ?? parsed.Meta.Uid;
                db.SongRequests.Add(sr);
            }
            await db.SaveChangesAsync();

            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file {FilePath}", filePath);
            return null;
        }
        finally
        {
            FileProcessingSemaphore.Release();
            fileLock.Release();
        }
    }

    public async Task<Session?> ImportLegacyXmlAsJsonlAsync(string sourceXmlPath, string? targetFilePath = null)
    {
        if (!File.Exists(sourceXmlPath)) return null;
        if (!string.Equals(Path.GetExtension(sourceXmlPath), ".xml", StringComparison.OrdinalIgnoreCase)) return null;

        var parsed = await ParseLegacyXmlFileAsync(sourceXmlPath);
        if (parsed == null) return null;

        var uidOrRoom = !string.IsNullOrWhiteSpace(parsed.Meta.Uid)
            ? parsed.Meta.Uid
            : (!string.IsNullOrWhiteSpace(parsed.Meta.RoomId) ? parsed.Meta.RoomId : InferUidFromPath(sourceXmlPath));
        if (string.IsNullOrWhiteSpace(uidOrRoom))
        {
            uidOrRoom = "unknown";
        }

        var startTimestamp = parsed.Meta.RecordStartTimestamp > 0
            ? parsed.Meta.RecordStartTimestamp
            : new DateTimeOffset(File.GetLastWriteTimeUtc(sourceXmlPath)).ToUnixTimeMilliseconds();
        var dateStr = DateTimeOffset.FromUnixTimeMilliseconds(startTimestamp).LocalDateTime.ToString("yyyy-MM-dd HH-mm-ss");
        var finalPath = targetFilePath;
        if (string.IsNullOrWhiteSpace(finalPath))
        {
            var roomDir = Path.Combine(_danmakuDir, uidOrRoom);
            Directory.CreateDirectory(roomDir);
            var finalFileName = $"{dateStr} {BilibiliRecorder.SanitizeFileName(parsed.Meta.Title)}.jsonl";
            finalPath = GetAvailableFilePath(Path.Combine(roomDir, finalFileName));
        }
        else
        {
            var targetDir = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrWhiteSpace(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }
        }

        var metaLine = JsonSerializer.Serialize(new
        {
            kind = "meta",
            version = "danmu-jsonl-v1",
            uid = parsed.Meta.Uid,
            roomId = parsed.Meta.RoomId,
            realRoomId = parsed.Meta.RoomId,
            title = parsed.Meta.Title,
            userName = parsed.Meta.UserName,
            startTime = startTimestamp,
            startTimeIsFallback = parsed.Meta.RecordStartTimestamp <= 0
        }, JsonOptions);

        var lines = new List<string> { metaLine };
        lines.AddRange(parsed.Messages.Select(m => JsonSerializer.Serialize(ToRecordedEvent(m), JsonOptions)));
        await File.WriteAllLinesAsync(finalPath, lines, Encoding.UTF8);

        var analysis = await ProcessFileAsync(finalPath);
        if (analysis == null)
        {
            try
            {
                File.Delete(finalPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete converted JSONL file after import processing failed: {FilePath}", finalPath);
            }
            return null;
        }

        var relativePath = Path.GetRelativePath(_danmakuDir, finalPath).Replace("\\", "/");
        using var scope = _scopeFactory.CreateScope();
        var db = GetDb(scope);
        return await db.Sessions.FirstOrDefaultAsync(s => s.FilePath == relativePath);
    }

    private async Task<List<DanmakuMessage>> LoadMessagesFromFileAsync(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension == ".jsonl")
        {
            var parsed = await ParseJsonlFileAsync(filePath);
            return parsed?.Messages ?? new List<DanmakuMessage>();
        }

        var parsedXml = await ParseLegacyXmlFileAsync(filePath);
        return parsedXml?.Messages ?? new List<DanmakuMessage>();
    }

    private async Task<ParsedSessionContent?> ParseJsonlFileAsync(string filePath)
    {
        var lines = await File.ReadAllLinesAsync(filePath);
        if (lines.Length == 0) return null;

        var messages = new List<DanmakuMessage>();
        var meta = new SessionFileMeta
        {
            Title = "未知直播",
            UserName = "未知主播",
            RoomId = "",
            Uid = "",
            RecordStartTimestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds()
        };

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var kind = TryGetString(root, "kind");
            if (kind == "meta")
            {
                meta = new SessionFileMeta
                {
                    Title = TryGetString(root, "title") ?? meta.Title,
                    UserName = TryGetString(root, "userName") ?? meta.UserName,
                    RoomId = TryGetString(root, "roomId") ?? meta.RoomId,
                    Uid = TryGetString(root, "uid") ?? meta.Uid,
                    RecordStartTimestamp = TryGetInt64(root, "startTime") ?? meta.RecordStartTimestamp
                };
                continue;
            }

            var recordedEvent = JsonSerializer.Deserialize<RecordedDanmakuEvent>(line, JsonOptions);
            if (recordedEvent == null) continue;

            // For JPN SC, merge text into previous SC if same user/timestamp/price
            if (recordedEvent.RawCommand == "SUPER_CHAT_MESSAGE_JPN" && messages.Count > 0)
            {
                var last = messages[^1];
                if (last.Type == "super_chat"
                    && last.Sender.Uid == (recordedEvent.Uid ?? "")
                    && Math.Abs(last.Timestamp - recordedEvent.Timestamp) <= 2000
                    && last.Price == recordedEvent.Price)
                {
                    if (last.Text != recordedEvent.Text)
                    {
                        last.TextJpn = recordedEvent.Text;
                    }
                    continue;
                }
            }

            messages.Add(MapRecordedEvent(recordedEvent));
        }

        messages.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return new ParsedSessionContent(meta, messages);
    }

    private async Task<ParsedSessionContent?> ParseLegacyXmlFileAsync(string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        var titleMatch = Regex.Match(content, @"<room_title>(.*?)</room_title>");
        var userMatch = Regex.Match(content, @"<user_name>(.*?)</user_name>");
        var roomMatch = Regex.Match(content, @"<room_id>(.*?)</room_id>");
        var uidMatch = Regex.Match(content, @"<uid>(.*?)</uid>");
        var startMatch = Regex.Match(content, @"<video_start_time>(.*?)</video_start_time>");

        long.TryParse(startMatch.Groups[1].Value, out var recordStartTimestamp);
        if (recordStartTimestamp == 0)
        {
            recordStartTimestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();
        }

        var meta = new SessionFileMeta
        {
            Title = titleMatch.Success ? titleMatch.Groups[1].Value : "未知直播",
            UserName = userMatch.Success ? userMatch.Groups[1].Value : "未知主播",
            RoomId = roomMatch.Success ? roomMatch.Groups[1].Value : "",
            Uid = uidMatch.Success ? uidMatch.Groups[1].Value : InferUidFromPath(filePath),
            RecordStartTimestamp = recordStartTimestamp
        };

        return new ParsedSessionContent(meta, ParseLegacyXmlContent(content));
    }

    private List<DanmakuMessage> ParseRecordedEventLines(IEnumerable<string> lines)
    {
        var messages = new List<DanmakuMessage>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var recordedEvent = JsonSerializer.Deserialize<RecordedDanmakuEvent>(line, JsonOptions);
            if (recordedEvent == null) continue;

            // For JPN SC, merge text into previous SC if same user/timestamp/price
            if (recordedEvent.RawCommand == "SUPER_CHAT_MESSAGE_JPN" && messages.Count > 0)
            {
                var last = messages[^1];
                if (last.Type == "super_chat"
                    && last.Sender.Uid == (recordedEvent.Uid ?? "")
                    && Math.Abs(last.Timestamp - recordedEvent.Timestamp) <= 2000
                    && last.Price == recordedEvent.Price)
                {
                    if (last.Text != recordedEvent.Text)
                    {
                        last.TextJpn = recordedEvent.Text;
                    }
                    continue;
                }
            }

            messages.Add(MapRecordedEvent(recordedEvent));
        }

        messages.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return messages;
    }

    private List<DanmakuMessage> ParseLegacyXmlContent(string content)
    {
        var messages = new List<DanmakuMessage>();
        if (string.IsNullOrEmpty(content)) return messages;

        var danmakuRegex = new Regex(@"<d p=""([^""]+)"" user=""([^""]+)"" uid=""([^""]+)"" timestamp=""([^""]+)""[^>]*>(.*?)</d>");
        foreach (Match match in danmakuRegex.Matches(content))
        {
            long.TryParse(match.Groups[4].Value, out var timestamp);
            messages.Add(new DanmakuMessage
            {
                Type = "comment",
                Text = match.Groups[5].Value,
                Timestamp = timestamp,
                Sender = new Sender { Name = match.Groups[2].Value, Uid = match.Groups[3].Value }
            });
        }

        var giftRegex = new Regex(@"<gift ts=""[^""]+"" giftname=""([^""]+)"" giftcount=""([^""]+)"" price=""([^""]+)"" user=""([^""]+)"" uid=""([^""]+)"" timestamp=""([^""]+)""");
        foreach (Match match in giftRegex.Matches(content))
        {
            double.TryParse(match.Groups[3].Value, out var priceRaw);
            int.TryParse(match.Groups[2].Value, out var count);
            long.TryParse(match.Groups[6].Value, out var timestamp);
            messages.Add(new DanmakuMessage
            {
                Type = "give_gift",
                Name = match.Groups[1].Value,
                Count = count > 0 ? count : 1,
                Price = NormalizeXmlGoldSeeds(priceRaw),
                IsPriceTotal = false,
                Timestamp = timestamp,
                Sender = new Sender { Name = match.Groups[4].Value, Uid = match.Groups[5].Value }
            });
        }

        var scRegex = new Regex(@"<sc (?:ts=""([^""]+)"" )?[^>]*price=""([^""]+)""[^>]*user=""([^""]+)""[^>]*uid=""([^""]+)""[^>]*timestamp=""([^""]+)""[^>]*>(.*?)</sc>");
        foreach (Match match in scRegex.Matches(content))
        {
            double.TryParse(match.Groups[2].Value, out var priceRaw);
            long.TryParse(match.Groups[5].Value, out var timestamp);
            messages.Add(new DanmakuMessage
            {
                Type = "super_chat",
                Text = match.Groups[6].Value,
                Price = NormalizeXmlGoldSeeds(priceRaw),
                IsPriceTotal = true,
                Timestamp = timestamp,
                Sender = new Sender { Name = match.Groups[3].Value, Uid = match.Groups[4].Value }
            });
        }

        var guardRegex = new Regex(@"<guard\s+([^>]*)>", RegexOptions.IgnoreCase);
        foreach (Match match in guardRegex.Matches(content))
        {
            var attrs = match.Groups[1].Value;
            double.TryParse(GetXmlAttribute(attrs, "price"), out var priceRaw);
            int.TryParse(GetXmlAttribute(attrs, "guard_level") ?? GetXmlAttribute(attrs, "level"), out var level);
            int.TryParse(GetXmlAttribute(attrs, "num") ?? GetXmlAttribute(attrs, "giftcount"), out var count);
            long.TryParse(GetXmlAttribute(attrs, "timestamp"), out var timestamp);

            messages.Add(new DanmakuMessage
            {
                Type = "guard",
                Name = GetXmlAttribute(attrs, "guard_name") ?? GetXmlAttribute(attrs, "giftname") ?? "舰长",
                GuardLevel = level > 0 ? level : 3,
                Count = count > 0 ? count : 1,
                Price = NormalizeXmlGoldSeeds(priceRaw),
                IsPriceTotal = true,
                Timestamp = timestamp,
                Sender = new Sender
                {
                    Name = GetXmlAttribute(attrs, "user") ?? "",
                    Uid = GetXmlAttribute(attrs, "uid") ?? ""
                }
            });
        }

        messages.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return messages;
    }

    private static double NormalizeXmlGoldSeeds(double rawPrice)
    {
        return rawPrice <= 0 ? 0 : rawPrice / 1000.0;
    }

    private static string? GetXmlAttribute(string attributes, string name)
    {
        var match = Regex.Match(attributes, $@"\b{Regex.Escape(name)}=""([^""]*)""", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private AnalysisResult BuildAnalysis(List<DanmakuMessage> messages)
    {
        var analysis = new AnalysisResult { TotalCount = messages.Count };
        var timelineMap = new Dictionary<long, int>();
        var keywordMap = new Dictionary<string, int>();

        foreach (var msg in messages)
        {
            var userName = msg.Sender.Name ?? "Unknown";
            if (!analysis.UserStats.ContainsKey(userName))
            {
                analysis.UserStats[userName] = new UserStat { Uid = msg.Sender.Uid };
            }

            analysis.UserStats[userName].Count++;
            if (msg.Type == "super_chat") analysis.UserStats[userName].ScCount++;

            var bucket = (msg.Timestamp / 60000) * 60000;
            if (!timelineMap.ContainsKey(bucket)) timelineMap[bucket] = 0;
            timelineMap[bucket]++;

            if (msg.Type == "comment" && !string.IsNullOrWhiteSpace(msg.Text) && msg.Text.Length > 1)
            {
                foreach (var word in msg.Text.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (word.Length <= 1) continue;
                    if (!keywordMap.ContainsKey(word)) keywordMap[word] = 0;
                    keywordMap[word]++;
                }
            }
        }

        analysis.Timeline = timelineMap.OrderBy(k => k.Key).Select(k => new List<object> { k.Key, k.Value }).ToList();
        analysis.TopKeywords = keywordMap.OrderByDescending(k => k.Value).Take(20)
            .Select(k => new KeywordStat { Word = k.Key, Count = k.Value })
            .ToList();

        return analysis;
    }

    private GiftAnalysisResult BuildGiftAnalysis(List<DanmakuMessage> messages)
    {
        var giftAnalysis = new GiftAnalysisResult();
        var giftTimelineMap = new Dictionary<long, double>();
        var giftCountMap = new Dictionary<string, GiftStat>();

        foreach (var msg in messages)
        {
            if (msg.Type != "give_gift" && msg.Type != "gift_combo" && msg.Type != "super_chat" && msg.Type != "guard")
            {
                continue;
            }

            var userName = msg.Sender.Name ?? "Unknown";
            if (!giftAnalysis.UserStats.ContainsKey(userName))
            {
                giftAnalysis.UserStats[userName] = new GiftUserStat { Uid = msg.Sender.Uid };
            }

            var count = msg.Count ?? 1;
            var eventAmount = msg.IsPriceTotal ? (msg.Price ?? 0) : (msg.Price ?? 0) * count;
            var stats = giftAnalysis.UserStats[userName];
            stats.TotalPrice += eventAmount;
            giftAnalysis.TotalPrice += eventAmount;

            if (msg.Type == "give_gift" || msg.Type == "gift_combo")
            {
                stats.GiftPrice += eventAmount;
                var giftName = msg.Name ?? "Unknown";
                if (!giftCountMap.ContainsKey(giftName)) giftCountMap[giftName] = new GiftStat { Name = giftName };
                giftCountMap[giftName].Count += count;
                giftCountMap[giftName].Price += eventAmount;
            }
            else if (msg.Type == "super_chat")
            {
                stats.ScPrice += eventAmount;
            }
            else
            {
                stats.GuardPrice += eventAmount;
                giftAnalysis.GuardStats.TotalPrice += eventAmount;
                giftAnalysis.GuardStats.Count += count;
                var level = (msg.GuardLevel ?? 3).ToString();
                if (!giftAnalysis.GuardStats.CountByLevel.ContainsKey(level)) giftAnalysis.GuardStats.CountByLevel[level] = 0;
                giftAnalysis.GuardStats.CountByLevel[level] += count;
            }

            var bucket = (msg.Timestamp / 60000) * 60000;
            if (!giftTimelineMap.ContainsKey(bucket)) giftTimelineMap[bucket] = 0;
            giftTimelineMap[bucket] += eventAmount;
        }

        giftAnalysis.Timeline = giftTimelineMap.OrderBy(k => k.Key)
            .Select(k => new List<object> { k.Key, Math.Round(k.Value, 1) })
            .ToList();
        giftAnalysis.TopGifts = giftCountMap.Values.OrderByDescending(g => g.Price).Take(20).ToList();
        giftAnalysis.TotalPrice = Math.Round(giftAnalysis.TotalPrice, 1);

        foreach (var u in giftAnalysis.UserStats.Values)
        {
            u.TotalPrice = Math.Round(u.TotalPrice, 1);
            u.GiftPrice = Math.Round(u.GiftPrice, 1);
            u.ScPrice = Math.Round(u.ScPrice, 1);
            u.GuardPrice = Math.Round(u.GuardPrice, 1);
        }

        return giftAnalysis;
    }

    private IEnumerable<SongRequest> BuildSongRequests(List<DanmakuMessage> messages)
    {
        foreach (var msg in messages)
        {
            if (msg.Type != "comment" || string.IsNullOrWhiteSpace(msg.Text)) continue;
            var text = msg.Text.Trim();
            if (!text.StartsWith("点歌", StringComparison.Ordinal)) continue;

            var songName = text.Substring(2).TrimStart(' ', ':', '：', '➖');
            if (string.IsNullOrWhiteSpace(songName)) continue;

            yield return new SongRequest
            {
                SongName = songName,
                UserName = msg.Sender.Name,
                Uid = msg.Sender.Uid,
                CreatedAt = msg.Timestamp
            };
        }
    }

    private DanmakuMessage MapRecordedEvent(RecordedDanmakuEvent recordedEvent)
    {
        return new DanmakuMessage
        {
            Type = recordedEvent.Type switch
            {
                "gift" => "give_gift",
                _ => recordedEvent.Type
            },
            Timestamp = recordedEvent.Timestamp,
            Text = recordedEvent.Text,
            TextJpn = recordedEvent.TextJpn,
            Price = recordedEvent.Price,
            IsPriceTotal = recordedEvent.IsPriceTotal,
            Name = recordedEvent.Name,
            Count = recordedEvent.Count > 0 ? recordedEvent.Count : 1,
            GuardLevel = recordedEvent.GuardLevel,
            MedalLevel = recordedEvent.MedalLevel,
            MedalName = recordedEvent.MedalName,
            MedalAnchor = recordedEvent.MedalAnchor,
            MedalRoomId = recordedEvent.MedalRoomId,
            MedalGuardLevel = recordedEvent.MedalGuardLevel,
            MedalIsLight = recordedEvent.MedalIsLight,
            MedalAnchorUid = recordedEvent.MedalAnchorUid,
            UlLevel = recordedEvent.UlLevel,
            WealthLevel = recordedEvent.WealthLevel,
            CoinType = recordedEvent.CoinType,
            RawCommand = recordedEvent.RawCommand,
            Duration = recordedEvent.Duration,
            Sender = new Sender
            {
                Name = recordedEvent.User ?? "",
                Uid = recordedEvent.Uid ?? ""
            }
        };
    }

    private static List<DanmakuMessage> MergeBilingualSuperChats(List<DanmakuMessage> messages)
    {
        var result = new List<DanmakuMessage>();
        DanmakuMessage? lastSc = null;

        foreach (var msg in messages)
        {
            if (msg.Type != "super_chat")
            {
                result.Add(msg);
                lastSc = null;
                continue;
            }

            if (lastSc != null
                && lastSc.Sender.Uid == msg.Sender.Uid
                && lastSc.Sender.Name == msg.Sender.Name
                && Math.Abs(lastSc.Timestamp - msg.Timestamp) <= 2000
                && lastSc.Price == msg.Price)
            {
                // Same SC, check if content is different
                if (lastSc.Text != msg.Text)
                {
                    // Different content = bilingual, attach JPN text
                    lastSc.TextJpn = msg.Text;
                }
                // Skip duplicate
                continue;
            }

            result.Add(msg);
            lastSc = msg;
        }

        return result;
    }

    private string? ResolveSessionFilePath(Session session)
    {
        if (string.IsNullOrWhiteSpace(session.FilePath)) return null;

        var directPath = Path.Combine(_danmakuDir, session.FilePath);
        if (File.Exists(directPath)) return directPath;

        var basename = Path.GetFileName(session.FilePath);
        if (!string.IsNullOrWhiteSpace(session.Uid))
        {
            var uidPath = Path.Combine(_danmakuDir, session.Uid, basename);
            if (File.Exists(uidPath)) return uidPath;
        }

        if (!string.IsNullOrWhiteSpace(session.RoomId))
        {
            var roomPath = Path.Combine(_danmakuDir, session.RoomId, basename);
            if (File.Exists(roomPath)) return roomPath;
        }

        var rootPath = Path.Combine(_danmakuDir, basename);
        return File.Exists(rootPath) ? rootPath : directPath;
    }

    private static string InferUidFromPath(string filePath)
    {
        var parent = Directory.GetParent(filePath);
        return parent?.Name ?? "";
    }

    private static string GetAvailableFilePath(string preferredPath)
    {
        if (!File.Exists(preferredPath)) return preferredPath;

        var directory = Path.GetDirectoryName(preferredPath) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(preferredPath);
        var extension = Path.GetExtension(preferredPath);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(directory, $"{fileName} ({i}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static RecordedDanmakuEvent ToRecordedEvent(DanmakuMessage message)
    {
        return new RecordedDanmakuEvent
        {
            Type = message.Type == "give_gift" ? "gift" : message.Type,
            Timestamp = message.Timestamp,
            Text = message.Text,
            TextJpn = message.TextJpn,
            Name = message.Name,
            Count = message.Count ?? 1,
            Price = message.Price,
            IsPriceTotal = message.IsPriceTotal,
            GuardLevel = message.GuardLevel,
            User = message.Sender.Name,
            Uid = message.Sender.Uid,
            RawCommand = message.Type switch
            {
                "comment" => "DANMU_MSG",
                "give_gift" => "SEND_GIFT",
                "super_chat" => "SUPER_CHAT_MESSAGE",
                "guard" => "GUARD_BUY",
                _ => message.Type
            }
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

    private sealed record ParsedSessionContent(SessionFileMeta Meta, List<DanmakuMessage> Messages);

    private sealed record SessionFileMeta
    {
        public string Title { get; init; } = "未知直播";
        public string UserName { get; init; } = "未知主播";
        public string RoomId { get; init; } = "";
        public string Uid { get; init; } = "";
        public long RecordStartTimestamp { get; init; }
    }
}
