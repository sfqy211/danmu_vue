using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Text.Json;
using Danmu.Server.Data;
using Danmu.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Services;

public class DanmakuService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new();
    private static readonly SemaphoreSlim FileProcessingSemaphore = new(4, 4);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DanmakuService> _logger;
    private readonly RedisService _redis;
    private readonly string _danmakuDir;

    public DanmakuService(IServiceScopeFactory scopeFactory, ILogger<DanmakuService> logger, RedisService redis)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _redis = redis;
        _danmakuDir = Environment.GetEnvironmentVariable("DANMAKU_DIR") 
                       ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../server/data/danmaku"));
    }

    private DanmuContext GetDb(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<DanmuContext>();

    public async Task<Session?> GetActiveSessionAsync(long roomId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = GetDb(scope);
        // Assuming EndTime == 0 means active/ongoing. Or EndTime == null.
        // Model defines EndTime as long?. 
        // Let's check for null or 0.
        return await db.Sessions
            .Where(s => s.RoomId == roomId.ToString() && (s.EndTime == null || s.EndTime == 0))
            .OrderByDescending(s => s.StartTime)
            .FirstOrDefaultAsync();
    }

    public async Task CreateLiveSessionAsync(long roomId, string title, string userName, long startTime, string sessionKey)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = GetDb(scope);
        
        // Double check if exists (though caller should check)
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.RoomId == roomId.ToString() && (s.EndTime == null || s.EndTime == 0));
        if (session == null)
        {
            session = new Session
            {
                RoomId = roomId.ToString(),
                Title = title,
                UserName = userName,
                StartTime = startTime,
                EndTime = 0, // 0 for active
                FilePath = "redis:" + sessionKey,
                SummaryJson = "{}",
                GiftSummaryJson = "{}",
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };
            db.Sessions.Add(session);
            await db.SaveChangesAsync();
            _logger.LogInformation($"Created live session {session.Id} for room {roomId}");
        }
    }
    
    public async Task CloseSessionAsync(long roomId, long endTime, string finalFilePath)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = GetDb(scope);
        
        var session = await db.Sessions
            .Where(s => s.RoomId == roomId.ToString() && (s.EndTime == null || s.EndTime == 0))
            .OrderByDescending(s => s.StartTime)
            .FirstOrDefaultAsync();
            
        if (session != null)
        {
            session.EndTime = endTime;
            // Update FilePath from redis:... to relative path of XML
            var relativePath = Path.GetRelativePath(_danmakuDir, finalFilePath).Replace("\\", "/");
            session.FilePath = relativePath;
            
            await db.SaveChangesAsync();
            
            // Trigger analysis update from the file
            await ProcessFileAsync(finalFilePath);
            
            _logger.LogInformation($"Closed session {session.Id} for room {roomId}");
        }
    }

    public async Task<object> GetDanmakuPagedAsync(int sessionId, int page, int pageSize)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = GetDb(scope);
        var session = await db.Sessions.FindAsync(sessionId);
        
        if (session == null || string.IsNullOrEmpty(session.FilePath)) 
        {
             return new { total = 0, list = new List<object>() };
        }

        List<DanmakuMessage> messages = new();

        if (session.FilePath.StartsWith("redis:"))
        {
            var key = session.FilePath.Substring(6);
            var lines = await _redis.GetMessagesAsync(key + ":list");
            var content = string.Join("\n", lines);
            messages = ParseMessagesContent(content, session.StartTime ?? 0);
        }
        else
        {
            string fullPath = Path.Combine(_danmakuDir, session.FilePath);
            if (!File.Exists(fullPath))
            {
                 var basename = Path.GetFileName(session.FilePath);
                 if (!string.IsNullOrEmpty(session.RoomId))
                 {
                     var roomPath = Path.Combine(_danmakuDir, session.RoomId, basename);
                     if (File.Exists(roomPath)) fullPath = roomPath;
                     else 
                     {
                         var rootPath = Path.Combine(_danmakuDir, basename);
                         if (File.Exists(rootPath)) fullPath = rootPath;
                     }
                 }
            }

            if (File.Exists(fullPath)) 
            {
                string content = await File.ReadAllTextAsync(fullPath);
                messages = ParseMessagesContent(content, session.StartTime ?? 0);
            }
        }
        
        var displayableMessages = messages.Where(m => 
            m.Type == "comment" || 
            m.Type == "super_chat"
        ).ToList();

        var total = displayableMessages.Count;
        var paged = displayableMessages.Skip((page - 1) * pageSize).Take(pageSize).Select(m => new 
        {
            time = Math.Max(0, (m.Timestamp - (session.StartTime ?? 0)) / 1000.0),
            timestamp = m.Timestamp,
            sender = m.Sender.Name,
            uid = m.Sender.Uid,
            text = m.Text,
            isSC = m.Type == "super_chat",
            price = m.Price,
            id = $"{m.Timestamp}-{m.Sender.Uid}"
        }).ToList();

        return new { total, list = paged };
    }

    public async Task<AnalysisResult?> ProcessFileAsync(string filePath)
    {
        if (!File.Exists(filePath) || !filePath.EndsWith(".xml")) return null;

        var fileLock = FileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync();
        await FileProcessingSemaphore.WaitAsync();
        try
        {
            string content = await File.ReadAllTextAsync(filePath);
            
            var titleMatch = Regex.Match(content, @"<room_title>(.*?)</room_title>");
            var userMatch = Regex.Match(content, @"<user_name>(.*?)</user_name>");
            var roomMatch = Regex.Match(content, @"<room_id>(.*?)</room_id>");
            var startMatch = Regex.Match(content, @"<video_start_time>(.*?)</video_start_time>");

            long.TryParse(startMatch.Groups[1].Value, out var recordStartTimestamp);
            if (recordStartTimestamp == 0)
            {
                recordStartTimestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();
            }

            var meta = new
            {
                Title = titleMatch.Success ? titleMatch.Groups[1].Value : "未知直播",
                UserName = userMatch.Success ? userMatch.Groups[1].Value : "未知主播",
                RoomId = roomMatch.Success ? roomMatch.Groups[1].Value : "",
                RecordStartTimestamp = recordStartTimestamp
            };

            var messages = ParseMessagesContent(content, meta.RecordStartTimestamp);
            
            var songRequests = new List<SongRequest>(); 
            foreach (var msg in messages)
            {
                if (msg.Type == "comment" && !string.IsNullOrEmpty(msg.Text))
                {
                    var text = msg.Text.Trim();
                    if (text.StartsWith("点歌"))
                    {
                        var songName = text.Substring(2).TrimStart(' ', ':', '：', '➖');
                        if (!string.IsNullOrEmpty(songName))
                        {
                            songRequests.Add(new SongRequest
                            {
                                SongName = songName,
                                UserName = msg.Sender.Name,
                                CreatedAt = msg.Timestamp
                            });
                        }
                    }
                }
            }

            var analysis = new AnalysisResult { TotalCount = messages.Count };
            var giftAnalysis = new GiftAnalysisResult();

            var timelineMap = new Dictionary<long, int>();
            var giftTimelineMap = new Dictionary<long, double>();
            var keywordMap = new Dictionary<string, int>();
            var giftCountMap = new Dictionary<string, GiftStat>();

            foreach (var msg in messages)
            {
                var userName = msg.Sender.Name ?? "Unknown";
                if (!analysis.UserStats.ContainsKey(userName))
                {
                    analysis.UserStats[userName] = new UserStat { Uid = msg.Sender.Uid };
                }
                analysis.UserStats[userName].Count++;
                if (msg.Type == "super_chat") analysis.UserStats[userName].ScCount++;

                if (msg.Type == "give_gift" || msg.Type == "super_chat" || msg.Type == "guard")
                {
                    var count = msg.Count ?? 1;
                    var price = (msg.Price ?? 0) * count;
                    giftAnalysis.TotalPrice += price;

                    if (!giftAnalysis.UserStats.ContainsKey(userName))
                    {
                        giftAnalysis.UserStats[userName] = new GiftUserStat { Uid = msg.Sender.Uid };
                    }
                    var stats = giftAnalysis.UserStats[userName];
                    stats.TotalPrice += price;

                    if (msg.Type == "give_gift")
                    {
                        stats.GiftPrice += price;
                        var giftName = msg.Name ?? "Unknown";
                        if (!giftCountMap.ContainsKey(giftName)) giftCountMap[giftName] = new GiftStat { Name = giftName };
                        giftCountMap[giftName].Count += count;
                        giftCountMap[giftName].Price += price;
                    }
                    else if (msg.Type == "super_chat")
                    {
                        stats.ScPrice += price;
                    }
                    else if (msg.Type == "guard")
                    {
                        stats.GuardPrice += price;
                        giftAnalysis.GuardStats.TotalPrice += price;
                        giftAnalysis.GuardStats.Count += count;
                        var level = (msg.GuardLevel ?? 3).ToString();
                        if (!giftAnalysis.GuardStats.CountByLevel.ContainsKey(level)) giftAnalysis.GuardStats.CountByLevel[level] = 0;
                        giftAnalysis.GuardStats.CountByLevel[level] += count;
                    }

                    var bucket = (msg.Timestamp / 60000) * 60000;
                    if (!giftTimelineMap.ContainsKey(bucket)) giftTimelineMap[bucket] = 0;
                    giftTimelineMap[bucket] += price;
                }

                var timeBucket = (msg.Timestamp / 60000) * 60000;
                if (!timelineMap.ContainsKey(timeBucket)) timelineMap[timeBucket] = 0;
                timelineMap[timeBucket]++;

                if (msg.Type == "comment" && !string.IsNullOrEmpty(msg.Text) && msg.Text.Length > 1)
                {
                    var words = msg.Text.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var w in words)
                    {
                        if (w.Length > 1)
                        {
                            if (!keywordMap.ContainsKey(w)) keywordMap[w] = 0;
                            keywordMap[w]++;
                        }
                    }
                }
            }

            analysis.Timeline = timelineMap.OrderBy(k => k.Key).Select(k => new List<object> { k.Key, k.Value }).ToList();
            analysis.TopKeywords = keywordMap.OrderByDescending(k => k.Value).Take(20).Select(k => new KeywordStat { Word = k.Key, Count = k.Value }).ToList();

            giftAnalysis.Timeline = giftTimelineMap.OrderBy(k => k.Key).Select(k => new List<object> { k.Key, Math.Round(k.Value, 1) }).ToList();
            giftAnalysis.TopGifts = giftCountMap.Values.OrderByDescending(g => g.Price).Take(20).ToList();
            giftAnalysis.TotalPrice = Math.Round(giftAnalysis.TotalPrice, 1);
            
            foreach (var u in giftAnalysis.UserStats.Values)
            {
                u.TotalPrice = Math.Round(u.TotalPrice, 1);
                u.GiftPrice = Math.Round(u.GiftPrice, 1);
                u.ScPrice = Math.Round(u.ScPrice, 1);
                u.GuardPrice = Math.Round(u.GuardPrice, 1);
            }

            using var scope = _scopeFactory.CreateScope();
            var db = GetDb(scope);

            var relativePath = Path.GetRelativePath(_danmakuDir, filePath).Replace("\\", "/");
            var existingSession = await db.Sessions.FirstOrDefaultAsync(s => s.FilePath == relativePath);
            if (existingSession == null)
            {
                existingSession = await db.Sessions.FirstOrDefaultAsync(s => s.RoomId == meta.RoomId && s.StartTime == meta.RecordStartTimestamp);
            }

            if (existingSession != null)
            {
                existingSession.EndTime = messages.Count > 0 ? messages.Last().Timestamp : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                existingSession.SummaryJson = JsonSerializer.Serialize(analysis);
                existingSession.GiftSummaryJson = JsonSerializer.Serialize(giftAnalysis);
                existingSession.FilePath = relativePath;
                
                var oldRequests = db.SongRequests.Where(r => r.SessionId == existingSession.Id);
                db.SongRequests.RemoveRange(oldRequests);
                
                await db.SaveChangesAsync();
                
                foreach (var sr in songRequests)
                {
                    sr.SessionId = existingSession.Id;
                    sr.RoomId = meta.RoomId;
                    db.SongRequests.Add(sr);
                }
                await db.SaveChangesAsync();
            }
            else
            {
                // This branch shouldn't really be hit if we created session on start, but good for recovery
                var session = new Session
                {
                    RoomId = meta.RoomId,
                    Title = meta.Title,
                    UserName = meta.UserName,
                    StartTime = meta.RecordStartTimestamp,
                    EndTime = messages.Count > 0 ? messages.Last().Timestamp : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    FilePath = relativePath,
                    SummaryJson = JsonSerializer.Serialize(analysis),
                    GiftSummaryJson = JsonSerializer.Serialize(giftAnalysis),
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                };

                db.Sessions.Add(session);
                await db.SaveChangesAsync();

                foreach (var sr in songRequests)
                {
                    sr.SessionId = session.Id;
                    sr.RoomId = meta.RoomId;
                    db.SongRequests.Add(sr);
                }
                await db.SaveChangesAsync();
            }

            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing file {filePath}");
            return null;
        }
        finally
        {
            FileProcessingSemaphore.Release();
            fileLock.Release();
        }
    }

    private List<DanmakuMessage> ParseMessagesContent(string content, long startTimestamp)
    {
        var messages = new List<DanmakuMessage>();
        if (string.IsNullOrEmpty(content)) return messages;

        var danmakuRegex = new Regex(@"<d p=""([^""]+)"" user=""([^""]+)"" uid=""([^""]+)"" timestamp=""([^""]+)""[^>]*>(.*?)</d>");
        foreach (Match match in danmakuRegex.Matches(content))
        {
            var text = match.Groups[5].Value;
            long.TryParse(match.Groups[4].Value, out var timestamp);
            var senderName = match.Groups[2].Value;
            var senderUid = match.Groups[3].Value;

            messages.Add(new DanmakuMessage
            {
                Type = "comment",
                Text = text,
                Timestamp = timestamp,
                Sender = new Sender { Name = senderName, Uid = senderUid }
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
                Price = priceRaw / 1000.0,
                Timestamp = timestamp,
                Sender = new Sender { Name = match.Groups[4].Value, Uid = match.Groups[5].Value }
            });
        }
        
        var scRegex = new Regex(@"<sc (?:ts=""([^""]+)"" )?[^>]*price=""([^""]+)""[^>]*user=""([^""]+)""[^>]*uid=""([^""]+)""[^>]*timestamp=""([^""]+)""[^>]*>(.*?)</sc>");
        foreach (Match match in scRegex.Matches(content))
        {
            var tsAttr = match.Groups[1].Value;
            double.TryParse(match.Groups[2].Value, out var price);
            long.TryParse(match.Groups[5].Value, out var timestamp);
            var text = match.Groups[6].Value;
            var senderName = match.Groups[3].Value;
            var senderUid = match.Groups[4].Value;

            if (!string.IsNullOrEmpty(tsAttr) && price >= 100)
            {
                price /= 1000.0;
            }
            
            messages.Add(new DanmakuMessage
            {
                Type = "super_chat",
                Text = text,
                Price = price,
                Timestamp = timestamp,
                Sender = new Sender { Name = senderName, Uid = senderUid }
            });
        }

        var guardRegex = new Regex(@"<guard [^>]*guard_level=""([^""]+)""[^>]*guard_name=""([^""]+)""[^>]*num=""([^""]+)""[^>]*price=""([^""]+)""[^>]*user=""([^""]+)""[^>]*uid=""([^""]+)""[^>]*timestamp=""([^""]+)""");
        foreach (Match match in guardRegex.Matches(content))
        {
            double.TryParse(match.Groups[4].Value, out var price);
            int.TryParse(match.Groups[1].Value, out var level);
            int.TryParse(match.Groups[3].Value, out var count);
            long.TryParse(match.Groups[7].Value, out var timestamp);

            // 根据 guard_level (1总督, 2提督, 3舰长) 识别价格单位：
            // 正常价格：舰长 198, 提督 1998, 总督 19998
            // 原始单位(金瓜子)：舰长 198000, 提督 1998000, 总督 19998000
            // 如果价格数值远大于该等级应有的金额，则判定为金瓜子单位，除以 1000
            bool isSeeds = false;
            if (level == 3 && price > 1000) isSeeds = true;      // 舰长 > 1000 必为金瓜子
            else if (level == 2 && price > 10000) isSeeds = true; // 提督 > 10000 必为金瓜子
            else if (level == 1 && price > 100000) isSeeds = true;// 总督 > 100000 必为金瓜子
            
            if (isSeeds) price /= 1000.0;

            messages.Add(new DanmakuMessage
            {
                Type = "guard",
                Name = match.Groups[2].Value,
                GuardLevel = level > 0 ? level : 3,
                Count = count > 0 ? count : 1,
                Price = price,
                Timestamp = timestamp,
                Sender = new Sender { Name = match.Groups[5].Value, Uid = match.Groups[6].Value }
            });
        }

        messages.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return messages;
    }
}
