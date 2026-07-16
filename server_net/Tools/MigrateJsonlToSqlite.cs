using System.Text.Json;
using Danmu.Server.Models;
using Danmu.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Danmu.Server.Tools;

/// <summary>
/// ADR-3 一次性迁移工具：将历史 JSONL 弹幕文件批量转换为按 Session 分库的 SQLite 文件，
/// 并更新 MySQL/主 SQLite 中 Sessions 表的 FilePath 字段为 "sqlite:{uid}/{file}.db"。
///
/// 用法：
///   dotnet run --project server_net -- migrate-jsonl [--dry-run] [--delete-source] [--danmaku-dir &lt;PATH&gt;]
///
/// 幂等性：
/// - 已是 sqlite: 前缀的 Session 跳过。
/// - 对应 .db 文件已存在则覆盖重建。
/// - 默认不删除源 JSONL 文件，除非显式传入 --delete-source。
/// </summary>
public static class MigrateJsonlToSqlite
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<int> RunAsync(string[] args)
    {
        var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
        var deleteSource = args.Contains("--delete-source", StringComparer.OrdinalIgnoreCase);
        var verbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);

        var danmakuDir = Environment.GetEnvironmentVariable("DANMAKU_DIR");
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--danmaku-dir", StringComparison.OrdinalIgnoreCase))
            {
                danmakuDir = args[i + 1];
            }
        }

        if (string.IsNullOrWhiteSpace(danmakuDir))
        {
            danmakuDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../server/data/danmaku"));
        }
        danmakuDir = Path.GetFullPath(danmakuDir);

        Console.WriteLine($"[migrate-jsonl] danmakuDir={danmakuDir}");
        Console.WriteLine($"[migrate-jsonl] dryRun={dryRun}, deleteSource={deleteSource}, verbose={verbose}");

        if (!Directory.Exists(danmakuDir))
        {
            Console.WriteLine($"[migrate-jsonl] 目录不存在，无需迁移: {danmakuDir}");
            return 0;
        }

        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Logging.ClearProviders();
        hostBuilder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });
        hostBuilder.Logging.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);

        // 复用 Program.cs 的 .env 加载逻辑（已在入口处执行）
        var dbPath = Environment.GetEnvironmentVariable("SQLITE_DB_PATH") ?? "server/data/danmaku.db";
        var projectRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
        var fullDbPath = Path.GetFullPath(Path.Combine(projectRoot, dbPath));

        hostBuilder.Services.AddDbContext<Danmu.Server.Data.DanmuContext>(options =>
        {
            options.UseSqlite($"Data Source={fullDbPath}", o => o.CommandTimeout(60));
        });
        hostBuilder.Services.AddSingleton<SessionDbService>();
        // Issue #18: Removed unused registrations (DanmakuService, RedisService, BiliAccountService)
        // These were never resolved and added unnecessary startup dependencies
        hostBuilder.Services.AddHttpClient();

        var host = hostBuilder.Build();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Danmu.Server.Data.DanmuContext>();
        var sessionDb = scope.ServiceProvider.GetRequiredService<SessionDbService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<MigrateJsonlToSqliteMarker>>();

        await db.Database.EnsureCreatedAsync();

        var sessions = await db.Sessions
            .Where(s => s.FilePath != null && s.FilePath != "")
            .ToListAsync();

        var stats = new MigrationStats();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        foreach (var session in sessions)
        {
            stats.Total++;
            var filePath = session.FilePath!;

            // 已是 SQLite 或 Redis 直连，跳过
            if (filePath.StartsWith("sqlite:", StringComparison.Ordinal))
            {
                stats.AlreadySqlite++;
                if (verbose) logger.LogDebug("Skip (already sqlite): id={Id} path={Path}", session.Id, filePath);
                continue;
            }
            if (filePath.StartsWith("redis:", StringComparison.Ordinal))
            {
                stats.RedisActive++;
                if (verbose) logger.LogDebug("Skip (redis active): id={Id} path={Path}", session.Id, filePath);
                continue;
            }

            // 解析 JSONL 文件绝对路径
            var fullPath = ResolveLocalFilePath(danmakuDir, session, filePath);
            if (fullPath == null || !File.Exists(fullPath))
            {
                stats.MissingFile++;
                logger.LogWarning("源文件不存在: id={Id} path={Path} resolved={Resolved}", session.Id, filePath, fullPath);
                continue;
            }

            // 仅 XML 文件也尝试迁移（旧版录制格式）
            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            if (ext != ".jsonl" && ext != ".xml")
            {
                stats.Skipped++;
                if (verbose) logger.LogDebug("Skip (not jsonl/xml): id={Id} path={Path}", session.Id, fullPath);
                continue;
            }

            if (dryRun)
            {
                // Issue #16: Separate counter for dry-run to avoid inflating Migrated count
                stats.DryRunCount++;
                logger.LogInformation("[DRY-RUN] 将迁移: id={Id} path={Path}", session.Id, fullPath);
                continue;
            }

            try
            {
                var parsed = ext == ".jsonl"
                    ? await ParseJsonlAsync(fullPath, logger)
                    : await ParseXmlAsync(fullPath);

                if (parsed == null || parsed.Messages.Count == 0)
                {
                    stats.Skipped++;
                    logger.LogWarning("解析为空，跳过: id={Id} path={Path}", session.Id, fullPath);
                    continue;
                }

                var uid = !string.IsNullOrWhiteSpace(parsed.Meta.Uid)
                    ? parsed.Meta.Uid
                    : (!string.IsNullOrWhiteSpace(session.Uid) ? session.Uid
                       : (!string.IsNullOrWhiteSpace(parsed.Meta.RoomId) ? parsed.Meta.RoomId : "unknown"));
                var startTimestamp = parsed.Meta.RecordStartTimestamp > 0
                    ? parsed.Meta.RecordStartTimestamp
                    : (session.StartTime ?? new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath)).ToUnixTimeMilliseconds());

                var dateStr = DateTimeOffset.FromUnixTimeMilliseconds(startTimestamp).LocalDateTime.ToString("yyyy-MM-dd HH-mm-ss");
                var title = string.IsNullOrWhiteSpace(parsed.Meta.Title) ? "未知直播" : parsed.Meta.Title;
                // Issue #10: Include session ID in filename to prevent collisions
                var dbFileName = $"{dateStr}_{session.Id}_{SanitizeFileName(title)}.db";
                var sqlitePath = $"sqlite:{uid}/{dbFileName}";

                await sessionDb.ImportMessagesAsync(sqlitePath, parsed.Messages);

                session.FilePath = sqlitePath;
                if (!string.IsNullOrWhiteSpace(parsed.Meta.Uid)) session.Uid = parsed.Meta.Uid;
                if (!string.IsNullOrWhiteSpace(parsed.Meta.RoomId)) session.RoomId = parsed.Meta.RoomId;
                if (!string.IsNullOrWhiteSpace(parsed.Meta.Title)) session.Title = parsed.Meta.Title;
                if (!string.IsNullOrWhiteSpace(parsed.Meta.UserName)) session.UserName = parsed.Meta.UserName;
                if (parsed.Messages.Count > 0) session.EndTime = parsed.Messages[^1].Timestamp;
                session.StartTime = startTimestamp;

                await db.SaveChangesAsync();
                stats.Migrated++;

                if (deleteSource)
                {
                    try
                    {
                        File.Delete(fullPath);
                        stats.SourceDeleted++;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "删除源文件失败: {Path}", fullPath);
                    }
                }

                if (verbose || stats.Migrated % 10 == 0)
                {
                    logger.LogInformation("已迁移 id={Id} -> {SqlitePath} ({Count} 条消息)",
                        session.Id, sqlitePath, parsed.Messages.Count);
                }
            }
            catch (Exception ex)
            {
                stats.Failed++;
                logger.LogError(ex, "迁移失败: id={Id} path={Path}", session.Id, fullPath);
            }
        }

        sw.Stop();
        Console.WriteLine($"[migrate-jsonl] 完成，耗时 {sw.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"  总数: {stats.Total}");
        if (dryRun)
        {
            Console.WriteLine($"  [DRY-RUN] 将迁移: {stats.DryRunCount}");
        }
        else
        {
            Console.WriteLine($"  已迁移: {stats.Migrated}");
        }
        Console.WriteLine($"  已是 SQLite: {stats.AlreadySqlite}");
        Console.WriteLine($"  Redis 活跃: {stats.RedisActive}");
        Console.WriteLine($"  源文件缺失: {stats.MissingFile}");
        Console.WriteLine($"  跳过: {stats.Skipped}");
        Console.WriteLine($"  失败: {stats.Failed}");
        if (deleteSource) Console.WriteLine($"  源文件已删除: {stats.SourceDeleted}");

        return stats.Failed > 0 ? 1 : 0;
    }

    private static string? ResolveLocalFilePath(string danmakuDir, Danmu.Server.Models.Session session, string filePath)
    {
        try
        {
            // Issue #4: Path traversal guard helper
            static string? SafeCombine(string baseDir, string relative)
            {
                var combined = Path.GetFullPath(Path.Combine(baseDir, relative));
                return combined.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) ? combined : null;
            }

            var direct = SafeCombine(danmakuDir, filePath);
            if (direct != null && File.Exists(direct)) return direct;

            var basename = Path.GetFileName(filePath);
            if (!string.IsNullOrEmpty(session.Uid))
            {
                var p = SafeCombine(danmakuDir, Path.Combine(session.Uid, basename));
                if (p != null && File.Exists(p)) return p;
            }
            if (!string.IsNullOrEmpty(session.RoomId))
            {
                var p = SafeCombine(danmakuDir, Path.Combine(session.RoomId, basename));
                if (p != null && File.Exists(p)) return p;
            }
            var root = SafeCombine(danmakuDir, basename);
            if (root != null && File.Exists(root)) return root;

            return direct;
        }
        catch (Exception ex)
        {
            // Issue #4: Log instead of silently swallowing
            System.Diagnostics.Debug.WriteLine($"ResolveLocalFilePath failed: {ex.Message}");
            return null;
        }
    }

    private static async Task<ParsedContent?> ParseJsonlAsync(string filePath, ILogger logger)
    {
        // Issue #7: Use StreamReader for streaming instead of loading entire file into memory
        var messages = new List<DanmakuMessage>();
        var meta = new MetaInfo
        {
            Title = "未知直播",
            UserName = "未知主播",
            RoomId = "",
            Uid = "",
            RecordStartTimestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds()
        };

        var hasContent = false;
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        string? rawLine;
        while ((rawLine = await reader.ReadLineAsync()) != null)
        {
            hasContent = true;
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            try
            {
                // Issue #8: Single JSON parse
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("kind", out var kindProp) &&
                    kindProp.ValueKind == JsonValueKind.String &&
                    kindProp.GetString() == "meta")
                {
                    meta = new MetaInfo
                    {
                        Title = TryGetString(root, "title") ?? meta.Title,
                        UserName = TryGetString(root, "userName") ?? meta.UserName,
                        RoomId = TryGetString(root, "roomId") ?? meta.RoomId,
                        Uid = TryGetString(root, "uid") ?? meta.Uid,
                        RecordStartTimestamp = TryGetInt64(root, "startTime") ?? meta.RecordStartTimestamp
                    };
                    continue;
                }

                var evt = JsonSerializer.Deserialize<RecordedDanmakuEvent>(root.GetRawText(), JsonOptions);
                if (evt == null) continue;

                messages.Add(MapEvent(evt));
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "JSONL 行解析失败: {File}", filePath);
            }
        }

        if (!hasContent) return null;

        messages.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return new ParsedContent(meta, messages);
    }

    private static async Task<ParsedContent?> ParseXmlAsync(string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        var meta = new MetaInfo
        {
            Title = "未知直播",
            UserName = "未知主播",
            RoomId = "",
            Uid = "",
            RecordStartTimestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds()
        };

        // 复用 DanmakuService 的 XML 解析逻辑过于复杂，这里只做最小化解析
        // 完整迁移建议先用 ImportLegacyXmlToSqliteAsync 单独处理 XML
        var messages = new List<DanmakuMessage>();
        var danmakuRegex = new System.Text.RegularExpressions.Regex(
            @"<d p=""([^""]+)"" user=""([^""]+)"" uid=""([^""]+)"" timestamp=""([^""]+)""[^>]*>(.*?)</d>");
        foreach (System.Text.RegularExpressions.Match match in danmakuRegex.Matches(content))
        {
            long.TryParse(match.Groups[4].Value, out var ts);
            messages.Add(new DanmakuMessage
            {
                Type = "comment",
                Text = match.Groups[5].Value,
                Timestamp = ts,
                Sender = new Sender { Name = match.Groups[2].Value, Uid = match.Groups[3].Value }
            });
        }

        messages.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return new ParsedContent(meta, messages);
    }

    private static DanmakuMessage MapEvent(RecordedDanmakuEvent e)
    {
        return new DanmakuMessage
        {
            Type = e.Type switch { "gift" => "give_gift", _ => e.Type },
            Timestamp = e.Timestamp,
            Text = e.Text,
            TextJpn = e.MessageTrans ?? e.MessageJpn ?? e.TextJpn,
            Price = e.Price,
            IsPriceTotal = e.IsPriceTotal,
            Name = e.Name,
            Count = e.Count > 0 ? e.Count : 1,
            GuardLevel = e.GuardLevel,
            MedalLevel = e.MedalLevel,
            MedalName = e.MedalName,
            CoinType = e.CoinType,
            TotalCoin = e.TotalCoin,
            RawCommand = e.RawCommand,
            Duration = e.Duration,
            Face = e.Face,
            Emots = e.Emots,
            DmType = e.DmType,
            UlLevel = e.UlLevel,
            WealthLevel = e.WealthLevel,
            MedalAnchor = e.MedalAnchor,
            MedalRoomId = e.MedalRoomId,
            MedalGuardLevel = e.MedalGuardLevel,
            MedalIsLight = e.MedalIsLight,
            MedalAnchorUid = e.MedalAnchorUid,
            Sender = new Sender { Name = e.User ?? "", Uid = e.Uid ?? "" }
        };
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            _ => v.ToString()
        };
    }

    private static long? TryGetInt64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt64(out var r) => r,
            JsonValueKind.String when long.TryParse(v.GetString(), out var r) => r,
            _ => null
        };
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(invalid.Contains(c) ? '_' : c);
        }
        var result = sb.ToString().Trim();
        // Issue #17: Fallback for empty result
        return string.IsNullOrEmpty(result) ? "untitled" : result;
    }

    private sealed record ParsedContent(MetaInfo Meta, List<DanmakuMessage> Messages);

    private sealed class MetaInfo
    {
        public string Title { get; set; } = "";
        public string UserName { get; set; } = "";
        public string RoomId { get; set; } = "";
        public string Uid { get; set; } = "";
        public long RecordStartTimestamp { get; set; }
    }

    private sealed class MigrationStats
    {
        public int Total;
        public int Migrated;
        public int DryRunCount; // Issue #16: Separate counter for dry-run
        public int AlreadySqlite;
        public int RedisActive;
        public int MissingFile;
        public int Skipped;
        public int Failed;
        public int SourceDeleted;
    }

    // 标记类，仅用于获取 ILogger<MigrateJsonlToSqliteMarker>
    internal sealed class MigrateJsonlToSqliteMarker;
}
