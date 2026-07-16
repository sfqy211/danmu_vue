using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Danmu.Server.Data;
using Danmu.Server.Models;
using Danmu.Server.Utils;
using Microsoft.Data.Sqlite;
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
    private readonly SessionDbService _sessionDb;
    private readonly string _danmakuDir;

    public DanmakuService(IServiceScopeFactory scopeFactory, ILogger<DanmakuService> logger, RedisService redis, SessionDbService sessionDb)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _redis = redis;
        _sessionDb = sessionDb;
        _danmakuDir = Environment.GetEnvironmentVariable("DANMAKU_DIR")
            ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../server/data/danmaku"));
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

        if (session == null) return;

        session.Uid = uid;
        session.RoomId = roomIdStr;
        session.EndTime = endTime;

        if (string.IsNullOrEmpty(finalFilePath))
        {
            await db.SaveChangesAsync();
            return;
        }

        // finalFilePath 形如 "sqlite:{uid}/{yyyy-MM-dd HH-mm-ss}.db"
        if (finalFilePath.StartsWith("sqlite:", StringComparison.Ordinal))
        {
            session.FilePath = finalFilePath;
            await db.SaveChangesAsync();
            // 在独立 scope 内基于 SQLite 重算统计
            await ProcessSqliteSessionAsync(finalFilePath);
            return;
        }

        // 兼容旧路径（迁移期遗留的 JSONL/XML 文件）
        session.FilePath = Path.GetRelativePath(_danmakuDir, finalFilePath).Replace("\\", "/");
        await db.SaveChangesAsync();
        await ProcessFileAsync(finalFilePath);
    }

    public async Task<object> GetDanmakuPagedAsync(int sessionId, int page, int pageSize,
        bool excludeAvatar = false, bool excludeWealthLevel = false, bool excludeFanMedal = false)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = GetDb(scope);
        var session = await db.Sessions.FindAsync(sessionId);

        if (session == null || string.IsNullOrEmpty(session.FilePath))
        {
            return new { total = 0, list = new List<object>(), page, pageSize, totalPages = 0 };
        }

        var safePageSize = Math.Max(1, pageSize);
        var safePage = Math.Max(1, page);
        var startTime = session.StartTime ?? 0;

        List<DanmakuMessage> pagedMessages;
        int total;

        if (session.FilePath.StartsWith("redis:", StringComparison.Ordinal))
        {
            var key = session.FilePath.Substring(6);
            var lines = await _redis.GetMessagesAsync(key + ":list");
            var allMessages = ParseRecordedEventLines(lines);
            var displayable = FilterDisplayable(allMessages);
            total = displayable.Count;
            pagedMessages = displayable
                .Skip((safePage - 1) * safePageSize)
                .Take(safePageSize)
                .ToList();
        }
        else if (session.FilePath.StartsWith("sqlite:", StringComparison.Ordinal))
        {
            var (msgs, totalCount) = await _sessionDb.GetPagedAsync(session.FilePath, safePage, safePageSize);
            pagedMessages = msgs;
            total = totalCount;
        }
        else
        {
            // 兼容旧路径（迁移期遗留的 JSONL/XML 文件）
            var fullPath = ResolveSessionFilePath(session);
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                return new { total = 0, list = new List<object>(), page, pageSize, totalPages = 0 };
            }

            var allMessages = await LoadMessagesFromFileAsync(fullPath);
            var displayable = FilterDisplayable(allMessages);
            total = displayable.Count;
            pagedMessages = displayable
                .Skip((safePage - 1) * safePageSize)
                .Take(safePageSize)
                .ToList();
        }

        var list = pagedMessages
            .Select(m => new
            {
                time = Math.Max(0, (m.Timestamp - startTime) / 1000.0),
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
                medalLevel = excludeFanMedal ? null : m.MedalLevel,
                medalName = excludeFanMedal ? null : m.MedalName,
                medalAnchor = excludeFanMedal ? null : m.MedalAnchor,
                medalRoomId = excludeFanMedal ? null : m.MedalRoomId,
                medalGuardLevel = excludeFanMedal ? null : m.MedalGuardLevel,
                medalIsLight = excludeFanMedal ? null : m.MedalIsLight,
                medalAnchorUid = excludeFanMedal ? null : m.MedalAnchorUid,
                ulLevel = excludeWealthLevel ? null : m.UlLevel,
                wealthLevel = excludeWealthLevel ? null : m.WealthLevel,
                coinType = m.CoinType,
                totalCoin = m.TotalCoin,
                duration = m.Duration,
                face = excludeAvatar ? null : m.Face,
                emots = m.Emots,
                dmType = m.DmType,
                id = $"{m.Timestamp}-{m.Sender.Uid}"
            })
            .ToList();

        return new
        {
            total,
            list,
            page = safePage,
            pageSize = safePageSize,
            totalPages = (int)Math.Ceiling(total / (double)safePageSize)
        };
    }

    private static List<DanmakuMessage> FilterDisplayable(List<DanmakuMessage> messages)
    {
        return messages.Where(m =>
            m.Type == "comment" ||
            m.Type == "super_chat" ||
            m.Type == "give_gift" ||
            m.Type == "guard" ||
            m.Type == "gift_combo").ToList();
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
            // Issue #13: Clean up FileLocks entry if no one else is waiting
            if (fileLock.CurrentCount == 1)
            {
                FileLocks.TryRemove(filePath, out _);
            }
        }
    }

    /// <summary>
    /// 基于 SQLite 数据库重算指定 session 的统计、礼物汇总和点歌列表。
    /// filePath 形如 "sqlite:{uid}/{yyyy-MM-dd HH-mm-ss}.db"。
    /// </summary>
    public async Task<AnalysisResult?> ProcessSqliteSessionAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !filePath.StartsWith("sqlite:", StringComparison.Ordinal))
        {
            return null;
        }

        await FileProcessingSemaphore.WaitAsync();
        try
        {
            var messages = await _sessionDb.LoadAllMessagesAsync(filePath);
            if (messages.Count == 0)
            {
                _logger.LogWarning("SQLite session {FilePath} has no messages, skip stats", filePath);
                return null;
            }

            var analysis = BuildAnalysis(messages);
            var giftAnalysis = BuildGiftAnalysis(messages);

            using var scope = _scopeFactory.CreateScope();
            var db = GetDb(scope);

            var existingSession = await db.Sessions.FirstOrDefaultAsync(s => s.FilePath == filePath);
            if (existingSession == null)
            {
                _logger.LogWarning("SQLite session {FilePath} has no matching DB row, skip stats update", filePath);
                return analysis;
            }

            existingSession.EndTime = messages[^1].Timestamp;
            existingSession.SummaryJson = JsonSerializer.Serialize(analysis, JsonOptions);
            existingSession.GiftSummaryJson = JsonSerializer.Serialize(giftAnalysis, JsonOptions);

            var oldRequests = db.SongRequests.Where(r => r.SessionId == existingSession.Id);
            db.SongRequests.RemoveRange(oldRequests);

            foreach (var sr in BuildSongRequests(messages))
            {
                sr.SessionId = existingSession.Id;
                sr.RoomId = existingSession.RoomId;
                sr.Uid = sr.Uid ?? existingSession.Uid;
                db.SongRequests.Add(sr);
            }

            await db.SaveChangesAsync();
            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing SQLite session {FilePath}", filePath);
            return null;
        }
        finally
        {
            FileProcessingSemaphore.Release();
        }
    }

    /// <summary>
    /// 将旧版 XML 弹幕文件导入为 SQLite session。
    /// 返回的 Session.FilePath 形如 "sqlite:{uid}/{yyyy-MM-dd HH-mm-ss}.db"。
    /// </summary>
    public async Task<Session?> ImportLegacyXmlToSqliteAsync(string sourceXmlPath, string? existingUid = null, long? existingStartTime = null)
    {
        if (!File.Exists(sourceXmlPath)) return null;
        if (!string.Equals(Path.GetExtension(sourceXmlPath), ".xml", StringComparison.OrdinalIgnoreCase)) return null;

        var parsed = await ParseLegacyXmlFileAsync(sourceXmlPath);
        if (parsed == null) return null;

        var uid = !string.IsNullOrWhiteSpace(parsed.Meta.Uid)
            ? parsed.Meta.Uid
            : (!string.IsNullOrWhiteSpace(existingUid) ? existingUid
               : (!string.IsNullOrWhiteSpace(parsed.Meta.RoomId) ? parsed.Meta.RoomId : InferUidFromPath(sourceXmlPath)));
        if (string.IsNullOrWhiteSpace(uid)) uid = "unknown";

        var startTimestamp = parsed.Meta.RecordStartTimestamp > 0
            ? parsed.Meta.RecordStartTimestamp
            : (existingStartTime ?? new DateTimeOffset(File.GetLastWriteTimeUtc(sourceXmlPath)).ToUnixTimeMilliseconds());

        var dateStr = DateTimeOffset.FromUnixTimeMilliseconds(startTimestamp).LocalDateTime.ToString("yyyy-MM-dd HH-mm-ss");
        var title = string.IsNullOrWhiteSpace(parsed.Meta.Title) ? "未知直播" : parsed.Meta.Title;
        var dbFileName = $"{dateStr} {BilibiliRecorder.SanitizeFileName(title)}.db";
        var sqlitePath = $"sqlite:{uid}/{dbFileName}";

        // 写入 SQLite
        await _sessionDb.ImportMessagesAsync(sqlitePath, parsed.Messages);

        // 计算统计并更新 DB
        using var scope = _scopeFactory.CreateScope();
        var db = GetDb(scope);

        var session = await db.Sessions.FirstOrDefaultAsync(s => s.FilePath == sqlitePath);
        if (session == null && !string.IsNullOrEmpty(parsed.Meta.Uid))
        {
            session = await db.Sessions.FirstOrDefaultAsync(s => s.Uid == parsed.Meta.Uid && s.StartTime == startTimestamp);
        }
        if (session == null && !string.IsNullOrEmpty(parsed.Meta.RoomId))
        {
            session = await db.Sessions.FirstOrDefaultAsync(s => s.RoomId == parsed.Meta.RoomId && s.StartTime == startTimestamp);
        }

        var analysis = BuildAnalysis(parsed.Messages);
        var giftAnalysis = BuildGiftAnalysis(parsed.Messages);
        var endTime = parsed.Messages.Count > 0
            ? parsed.Messages[^1].Timestamp
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (session == null)
        {
            session = new Session
            {
                Uid = uid,
                RoomId = parsed.Meta.RoomId ?? "",
                Title = title,
                UserName = parsed.Meta.UserName,
                StartTime = startTimestamp,
                EndTime = endTime,
                FilePath = sqlitePath,
                SummaryJson = JsonSerializer.Serialize(analysis, JsonOptions),
                GiftSummaryJson = JsonSerializer.Serialize(giftAnalysis, JsonOptions),
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };
            db.Sessions.Add(session);
            await db.SaveChangesAsync();

            foreach (var sr in BuildSongRequests(parsed.Messages))
            {
                sr.SessionId = session.Id;
                sr.RoomId = parsed.Meta.RoomId;
                sr.Uid = sr.Uid ?? uid;
                db.SongRequests.Add(sr);
            }
            await db.SaveChangesAsync();
        }
        else
        {
            session.Uid = uid;
            session.RoomId = parsed.Meta.RoomId ?? session.RoomId;
            session.Title = title;
            session.UserName = parsed.Meta.UserName;
            session.StartTime = startTimestamp;
            session.EndTime = endTime;
            session.FilePath = sqlitePath;
            session.SummaryJson = JsonSerializer.Serialize(analysis, JsonOptions);
            session.GiftSummaryJson = JsonSerializer.Serialize(giftAnalysis, JsonOptions);

            var oldRequests = db.SongRequests.Where(r => r.SessionId == session.Id);
            db.SongRequests.RemoveRange(oldRequests);
            await db.SaveChangesAsync();

            foreach (var sr in BuildSongRequests(parsed.Messages))
            {
                sr.SessionId = session.Id;
                sr.RoomId = parsed.Meta.RoomId;
                sr.Uid = sr.Uid ?? uid;
                db.SongRequests.Add(sr);
            }
            await db.SaveChangesAsync();
        }

        return session;
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
        // Issue #7: Use StreamReader for streaming instead of loading entire file into memory
        var messages = new List<DanmakuMessage>();
        var meta = new SessionFileMeta
        {
            Title = "未知直播",
            UserName = "未知主播",
            RoomId = "",
            Uid = "",
            RecordStartTimestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds()
        };

        var errorCount = 0;
        var totalLines = 0;

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        string? rawLine;
        while ((rawLine = await reader.ReadLineAsync()) != null)
        {
            totalLines++;
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            JsonDocument? doc = null;
            try
            {
                // Issue #8: Single JSON parse - use JsonDocument to inspect kind, then deserialize
                doc = JsonDocument.Parse(line);
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

                // Issue #8: Deserialize directly from JsonDocument instead of re-serializing
                var recordedEvent = JsonSerializer.Deserialize<RecordedDanmakuEvent>(root.GetRawText(), JsonOptions);
                if (recordedEvent == null) continue;

                messages.Add(MapRecordedEvent(recordedEvent));
            }
            catch (JsonException ex)
            {
                errorCount++;
                if (errorCount <= 5)
                {
                    _logger.LogWarning(ex,
                        "Skipping malformed line {LineIndex} in {FilePath}: {Snippet}",
                        totalLines, filePath,
                        line.Length > 200 ? line[..200] + "..." : line);
                }
                else if (errorCount == 6)
                {
                    _logger.LogWarning("Too many malformed lines in {FilePath}, suppressing further warnings. Total errors so far: {ErrorCount}", filePath, errorCount);
                }
            }
            finally
            {
                doc?.Dispose();
            }
        }

        if (totalLines == 0) return null;

        if (errorCount > 0)
        {
            _logger.LogWarning("Parsed {FilePath} with {ErrorCount} malformed line(s) skipped out of {TotalLines} lines",
                filePath, errorCount, totalLines);
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
        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) continue;
            try
            {
                var recordedEvent = JsonSerializer.Deserialize<RecordedDanmakuEvent>(rawLine, JsonOptions);
                if (recordedEvent == null) continue;

                messages.Add(MapRecordedEvent(recordedEvent));
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping malformed Redis message: {Snippet}",
                    rawLine.Length > 200 ? rawLine[..200] + "..." : rawLine);
            }
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

        // 舰长去重：同一用户的 GUARD_BUY 和 USER_TOAST_MSG 只计一次，优先 USER_TOAST_MSG
        var guardHasToast = new HashSet<string>(); // key: uid

        // 第一遍：收集有 USER_TOAST_MSG 的用户
        foreach (var msg in messages)
        {
            if (msg.Type != "guard") continue;
            if (msg.RawCommand == "USER_TOAST_MSG")
            {
                guardHasToast.Add(msg.Sender.Uid ?? "");
            }
        }

        foreach (var msg in messages)
        {
            if (msg.Type != "give_gift" && msg.Type != "gift_combo" && msg.Type != "super_chat" && msg.Type != "guard")
            {
                continue;
            }

            // 舰长去重：跳过已有 USER_TOAST_MSG 的 GUARD_BUY
            if (msg.Type == "guard" && msg.RawCommand == "GUARD_BUY"
                && guardHasToast.Contains(msg.Sender.Uid ?? ""))
            {
                continue;
            }

            var userName = msg.Sender.Name ?? "Unknown";
            if (!giftAnalysis.UserStats.ContainsKey(userName))
            {
                giftAnalysis.UserStats[userName] = new GiftUserStat { Uid = msg.Sender.Uid ?? "" };
            }

            var count = msg.Count ?? 1;
            // 礼物金额：优先使用 TotalCoin（实付总价），回退到 Price × Count
            double eventAmount;
            if (msg.Type == "guard")
            {
                // 舰长：Price 已是总价（IsPriceTotal=true），直接使用
                eventAmount = msg.Price ?? 0;
            }
            else if (msg.TotalCoin.HasValue && msg.TotalCoin.Value > 0)
            {
                // 礼物/SC：有实付总价则优先使用
                eventAmount = msg.TotalCoin.Value;
            }
            else
            {
                // 回退：Price × Count 或 Price（IsPriceTotal）
                eventAmount = msg.IsPriceTotal ? (msg.Price ?? 0) : (msg.Price ?? 0) * count;
            }

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
            TextJpn = recordedEvent.MessageJpn ?? recordedEvent.TextJpn,
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
            TotalCoin = recordedEvent.TotalCoin,
            RawCommand = recordedEvent.RawCommand,
            Duration = recordedEvent.Duration,
            Face = recordedEvent.Face,
            Emots = recordedEvent.Emots,
            DmType = recordedEvent.DmType,
            Sender = new Sender
            {
                Name = recordedEvent.User ?? "",
                Uid = recordedEvent.Uid ?? ""
            }
        };
    }

    private string? ResolveSessionFilePath(Session session)
    {
        if (string.IsNullOrWhiteSpace(session.FilePath)) return null;

        // Issue #2: Path traversal guard helper
        static string? SafeCombine(string baseDir, string relative)
        {
            var combined = Path.GetFullPath(Path.Combine(baseDir, relative));
            return combined.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) ? combined : null;
        }

        var directPath = SafeCombine(_danmakuDir, session.FilePath);
        if (directPath != null && File.Exists(directPath)) return directPath;

        var basename = Path.GetFileName(session.FilePath);
        if (!string.IsNullOrWhiteSpace(session.Uid))
        {
            var uidPath = SafeCombine(_danmakuDir, Path.Combine(session.Uid, basename));
            if (uidPath != null && File.Exists(uidPath)) return uidPath;
        }

        if (!string.IsNullOrWhiteSpace(session.RoomId))
        {
            var roomPath = SafeCombine(_danmakuDir, Path.Combine(session.RoomId, basename));
            if (roomPath != null && File.Exists(roomPath)) return roomPath;
        }

        var rootPath = SafeCombine(_danmakuDir, basename);
        return rootPath != null && File.Exists(rootPath) ? rootPath : directPath;
    }

    private static string InferUidFromPath(string filePath)
    {
        var parent = Directory.GetParent(filePath);
        return parent?.Name ?? "";
    }

    // Issue #19: Removed dead code GetAvailableFilePath (was unused static method with infinite loop risk)

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
            TotalCoin = message.TotalCoin,
            GuardLevel = message.GuardLevel,
            User = message.Sender.Name,
            Uid = message.Sender.Uid,
            Face = message.Face,
            Emots = message.Emots,
            DmType = message.DmType,
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
