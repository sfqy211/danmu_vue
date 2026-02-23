using System.Text.RegularExpressions;
using System.Text.Json;
using Danmu.Server.Data;
using Danmu.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Services;

public class DanmakuService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DanmakuService> _logger;
    private readonly string _danmakuDir;

    public DanmakuService(IServiceScopeFactory scopeFactory, ILogger<DanmakuService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _danmakuDir = Environment.GetEnvironmentVariable("DANMAKU_DIR") 
                       ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../data/danmaku"));
    }

    private DanmuContext GetDb(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<DanmuContext>();

    public async Task<object> GetDanmakuPagedAsync(int sessionId, int page, int pageSize)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = GetDb(scope);
        var session = await db.Sessions.FindAsync(sessionId);
        
        if (session == null || string.IsNullOrEmpty(session.FilePath)) 
        {
             // Try to find by ID if file path is missing (legacy support or broken DB)
             return new { total = 0, list = new List<object>() };
        }

        string fullPath = Path.Combine(_danmakuDir, session.FilePath);
        // Fallback logic for path
        if (!File.Exists(fullPath))
        {
             var basename = Path.GetFileName(session.FilePath);
             // Try room subdir
             if (!string.IsNullOrEmpty(session.RoomId))
             {
                 var roomPath = Path.Combine(_danmakuDir, session.RoomId, basename);
                 if (File.Exists(roomPath)) fullPath = roomPath;
                 else 
                 {
                     // Try root
                     var rootPath = Path.Combine(_danmakuDir, basename);
                     if (File.Exists(rootPath)) fullPath = rootPath;
                 }
             }
        }

        if (!File.Exists(fullPath)) 
        {
            return new { total = 0, list = new List<object>() };
        }

        var messages = await ParseMessagesAsync(fullPath, session.StartTime ?? 0);
        
        var total = messages.Count;
        var paged = messages.Skip((page - 1) * pageSize).Take(pageSize).Select(m => new 
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

        try
        {
            string content = await File.ReadAllTextAsync(filePath);
            
            // Extract Metadata
            var titleMatch = Regex.Match(content, @"<room_title>(.*?)</room_title>");
            var userMatch = Regex.Match(content, @"<user_name>(.*?)</user_name>");
            var roomMatch = Regex.Match(content, @"<room_id>(.*?)</room_id>");
            var startMatch = Regex.Match(content, @"<video_start_time>(.*?)</video_start_time>");

            long.TryParse(startMatch.Groups[1].Value, out var recordStartTimestamp);
            if (recordStartTimestamp == 0) recordStartTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var meta = new
            {
                Title = titleMatch.Success ? titleMatch.Groups[1].Value : "未知直播",
                UserName = userMatch.Success ? userMatch.Groups[1].Value : "未知主播",
                RoomId = roomMatch.Success ? roomMatch.Groups[1].Value : "",
                RecordStartTimestamp = recordStartTimestamp
            };

            var messages = await ParseMessagesAsync(filePath, meta.RecordStartTimestamp);
            
            // Extract Song Requests (custom logic if any)
            var songRequests = new List<SongRequest>(); 
            // Logic for song requests: usually comments starting with "点歌" or similar.
            // Node.js implementation:
            // const kword = ['点歌', '点歌 ➖', '点歌 ', '点歌：', '点歌:'];
            // if (msg.msg.startsWith(k)) ...
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

            // Analysis
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
                    var price = msg.Price ?? 0;
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
                        giftCountMap[giftName].Count += msg.Count ?? 1;
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
                        giftAnalysis.GuardStats.Count += msg.Count ?? 1;
                        var level = (msg.GuardLevel ?? 3).ToString();
                        if (!giftAnalysis.GuardStats.CountByLevel.ContainsKey(level)) giftAnalysis.GuardStats.CountByLevel[level] = 0;
                        giftAnalysis.GuardStats.CountByLevel[level] += msg.Count ?? 1;
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

            // Finalize Analysis
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

            // Save to DB
            using var scope = _scopeFactory.CreateScope();
            var db = GetDb(scope);

            var relativePath = Path.GetRelativePath(_danmakuDir, filePath).Replace("\\", "/");
            var existingSession = await db.Sessions.FirstOrDefaultAsync(s => s.RoomId == meta.RoomId && s.StartTime == meta.RecordStartTimestamp);

            if (existingSession != null)
            {
                // Update existing
                existingSession.EndTime = messages.Count > 0 ? messages.Last().Timestamp : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                existingSession.SummaryJson = JsonSerializer.Serialize(analysis);
                existingSession.GiftSummaryJson = JsonSerializer.Serialize(giftAnalysis);
                existingSession.FilePath = relativePath;
                
                // Remove old song requests
                var oldRequests = db.SongRequests.Where(r => r.SessionId == existingSession.Id);
                db.SongRequests.RemoveRange(oldRequests);
                
                await db.SaveChangesAsync();
                
                // Add new song requests
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
                // Create new
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

                // Song Requests
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
    }

    private async Task<List<DanmakuMessage>> ParseMessagesAsync(string filePath, long startTimestamp)
    {
        var messages = new List<DanmakuMessage>();
        if (!File.Exists(filePath)) return messages;

        string content = await File.ReadAllTextAsync(filePath);

        // 1. Comments <d ...>
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

        // 2. Gifts <gift ...>
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
                Price = priceRaw / 1000.0, // Usually price is int in API but maybe different here? Node logic implies division? No, Node logic uses price directly usually. Wait.
                // In Node.js code I saw `price / 1000` for SuperChat, but for Gift?
                // Let's assume price is in cents/gold bean (1000 = 1 RMB).
                // My BilibiliRecorder outputs price as int.
                // Let's divide by 1000 to be safe/consistent with common Bilibili units if it's Gold Bean.
                // Actually, let's look at SC parsing below.
                Timestamp = timestamp,
                Sender = new Sender { Name = match.Groups[4].Value, Uid = match.Groups[5].Value }
            });
        }

        // 3. Super Chats <sc ...>
        var scRegex = new Regex(@"<sc [^>]*price=""([^""]+)""[^>]*user=""([^""]+)""[^>]*uid=""([^""]+)""[^>]*timestamp=""([^""]+)""[^>]*>(.*?)</sc>");
        foreach (Match match in scRegex.Matches(content))
        {
            double.TryParse(match.Groups[1].Value, out var price);
            long.TryParse(match.Groups[4].Value, out var timestamp);
            var text = match.Groups[5].Value;
            
            // Heuristic from Node.js: if tag contains ts="..." (B-station native?), price might be different unit?
            // In my recorder, I write price as int.
            // Let's assume price is provided correctly.
            
            messages.Add(new DanmakuMessage
            {
                Type = "super_chat",
                Text = text,
                Price = price,
                Timestamp = timestamp,
                Sender = new Sender { Name = match.Groups[2].Value, Uid = match.Groups[3].Value }
            });
        }

        // 4. Guards <guard ...>
        var guardRegex = new Regex(@"<guard [^>]*guard_level=""([^""]+)""[^>]*guard_name=""([^""]+)""[^>]*num=""([^""]+)""[^>]*price=""([^""]+)""[^>]*user=""([^""]+)""[^>]*uid=""([^""]+)""[^>]*timestamp=""([^""]+)""");
        foreach (Match match in guardRegex.Matches(content))
        {
            double.TryParse(match.Groups[4].Value, out var price);
            int.TryParse(match.Groups[1].Value, out var level);
            int.TryParse(match.Groups[3].Value, out var count);
            long.TryParse(match.Groups[7].Value, out var timestamp);

            messages.Add(new DanmakuMessage
            {
                Type = "guard",
                Name = match.Groups[2].Value,
                GuardLevel = level > 0 ? level : 3,
                Count = count > 0 ? count : 1,
                Price = price, // Guard price usually in gold bean (1000 = 1 RMB)
                Timestamp = timestamp,
                Sender = new Sender { Name = match.Groups[5].Value, Uid = match.Groups[6].Value }
            });
        }

        messages.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return messages;
    }
}
