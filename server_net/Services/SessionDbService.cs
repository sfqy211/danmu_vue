using System.Collections.Concurrent;
using System.Security;
using System.Text.Json;
using Danmu.Server.Models;
using Microsoft.Data.Sqlite;

namespace Danmu.Server.Services;

/// <summary>
/// 管理按 Session 分库的 SQLite 文件（ADR-3）。
/// 每个 .db 文件路径：danmaku/{uid}/{yyyy-MM-dd HH-mm-ss}.db
/// 直播期间由录制器持有打开的连接批量写入；结束后按需打开只读连接查询。
/// </summary>
public class SessionDbService
{
    private static readonly JsonSerializerOptions EmotJsonOptions = new(JsonSerializerDefaults.Web);
    // Issue #6a: Static constant for SQL type filter to prevent accidental injection
    private const string DisplayableTypesSql = "('comment','super_chat','gift','guard','gift_combo')";

    private readonly ILogger<SessionDbService> _logger;
    private readonly string _danmakuDir;
    // 活跃录制器持有的连接，按 uid 索引（同一 uid 同一时刻只有一个活跃 session）
    private readonly ConcurrentDictionary<string, SqliteConnection> _openConnections = new();

    public SessionDbService(ILogger<SessionDbService> logger)
    {
        _logger = logger;
        _danmakuDir = Environment.GetEnvironmentVariable("DANMAKU_DIR")
            ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../server/data/danmaku"));
    }

    // ─── 路径辅助 ───────────────────────────────────────────────

    /// <summary>
    /// 根据直播开始时间戳生成 .db 文件名（不含标题，标题可能变更）。
    /// </summary>
    public static string BuildDbFileName(long startTimestamp)
    {
        return $"{FormatStartTimestamp(startTimestamp)}.db";
    }

    private static string FormatStartTimestamp(long startTimestamp)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(startTimestamp).LocalDateTime.ToString("yyyy-MM-dd HH-mm-ss");
    }

    /// <summary>
    /// 返回相对路径：{uid}/{yyyy-MM-dd HH-mm-ss}.db，用于存入 Session.FilePath（带 sqlite: 前缀）。
    /// </summary>
    public string GetRelativeDbPath(string uid, long startTimestamp)
    {
        return $"{uid}/{BuildDbFileName(startTimestamp)}";
    }

    /// <summary>
    /// 将 Session.FilePath 中的 sqlite: 相对路径解析为绝对路径。
    /// </summary>
    public string ResolveAbsolutePath(string sqliteRelativePath)
    {
        var clean = sqliteRelativePath.StartsWith("sqlite:", StringComparison.Ordinal)
            ? sqliteRelativePath.Substring("sqlite:".Length)
            : sqliteRelativePath;
        var resolved = Path.GetFullPath(Path.Combine(_danmakuDir, clean));
        // Issue #2: Path traversal guard
        if (!resolved.StartsWith(_danmakuDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException($"Path traversal detected: {sqliteRelativePath}");
        }
        return resolved;
    }

    private string GetAbsolutePath(string uid, long startTimestamp)
    {
        return Path.Combine(_danmakuDir, GetRelativeDbPath(uid, startTimestamp));
    }

    // ─── 连接管理（直播期间，由录制器调用） ─────────────────────

    /// <summary>
    /// 为活跃 session 打开 SQLite 连接，建表并启用 WAL。直播期间保持打开。
    /// </summary>
    public async Task<SqliteConnection> OpenSessionAsync(string uid, long startTimestamp)
    {
        var dbPath = GetAbsolutePath(uid, startTimestamp);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        // 同一个 uid + 同一场次 → 复用已打开的连接。
        //
        // 这里原本是"先关掉旧的再开新的"，在当时只有本机录制器一个消费者的前提下没问题。
        // 但引入分布式采集后，本机录制器和上报通路可能同时对同一个 uid 的场次库写入
        // （比如节点在录、中心本机也在录同一场）。
        // 关掉旧连接会让另一个持有者手里的 SqliteConnection 变成 closed，
        // 之后它再写就报 "BeginTransaction can only be called when the connection is open"。
        //
        // 复用连接是安全的：SQLite 同文件多连接本身也支持（WAL 模式），
        // 而共享同一条连接更是天然串行。
        if (_openConnections.TryGetValue(uid, out var existing) &&
            existing.State == System.Data.ConnectionState.Open &&
            string.Equals(existing.DataSource, dbPath, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        // 换了场次文件才关旧连接
        if (_openConnections.TryRemove(uid, out var oldConn))
        {
            try { await oldConn.CloseAsync(); await oldConn.DisposeAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to close old connection for uid {Uid}", uid); }
        }

        // SqliteConnectionStringBuilder handles special chars (e.g. ';') in file paths
var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        await conn.OpenAsync();

        try
        {
            await EnableWalAndCreateSchemaAsync(conn);
        }
        catch
        {
            // Issue #1: Dispose connection if schema creation fails
            await conn.DisposeAsync();
            throw;
        }

        _openConnections[uid] = conn;
        _logger.LogInformation("Opened session SQLite DB {DbPath}", dbPath);
        return conn;
    }

    /// <summary>
    /// 关闭指定 uid 的活跃连接（直播结束时调用）。
    /// </summary>
    public async Task CloseSessionAsync(string uid)
    {
        if (_openConnections.TryRemove(uid, out var conn))
        {
            try
            {
                await conn.CloseAsync();
                await conn.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to close session SQLite connection for uid {Uid}", uid);
            }
        }
    }

    /// <summary>
    /// 获取已打开的连接（录制器批量写入时用）。
    /// </summary>
    public SqliteConnection? GetOpenConnection(string uid)
    {
        return _openConnections.TryGetValue(uid, out var conn) ? conn : null;
    }

    // ─── 建表 ───────────────────────────────────────────────────

    private static async Task EnableWalAndCreateSchemaAsync(SqliteConnection conn)
    {
        await using var walCmd = conn.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode=WAL;";
        await walCmd.ExecuteNonQueryAsync();

        // 双实例部署（蓝绿切换期间）可能同时写同一个场次库：
        // WAL 允许多读者但只允许单写者，遇到写锁时等待而不是立即报错。
        await using var busyCmd = conn.CreateCommand();
        busyCmd.CommandText = "PRAGMA busy_timeout=15000;";
        await busyCmd.ExecuteNonQueryAsync();

        // WAL 下 NORMAL 已足够安全（崩溃最多丢最后几条，不会损坏库），
        // 且明显快于 FULL，适合高频批量写入。
        await using var syncCmd = conn.CreateCommand();
        syncCmd.CommandText = "PRAGMA synchronous=NORMAL;";
        await syncCmd.ExecuteNonQueryAsync();

        await using var schemaCmd = conn.CreateCommand();
        schemaCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS danmaku_messages (
                id                INTEGER PRIMARY KEY AUTOINCREMENT,
                type              TEXT NOT NULL DEFAULT 'comment',
                timestamp         INTEGER NOT NULL,
                user              TEXT NOT NULL DEFAULT '',
                uid               TEXT NOT NULL DEFAULT '',
                text              TEXT,
                text_jpn          TEXT,
                name              TEXT,
                count             INTEGER NOT NULL DEFAULT 1,
                price             REAL,
                is_price_total    INTEGER NOT NULL DEFAULT 0,
                total_coin        REAL,
                discount_price    REAL,
                guard_level       INTEGER,
                medal_level       INTEGER,
                medal_name        TEXT,
                coin_type         TEXT,
                duration          INTEGER,
                face              TEXT,
                emots             TEXT,
                dm_type           INTEGER,
                raw_command       TEXT,
                ul_level          INTEGER,
                wealth_level      INTEGER,
                medal_anchor      TEXT,
                medal_room_id     INTEGER,
                medal_guard_level INTEGER,
                medal_is_light    INTEGER,
                medal_anchor_uid  TEXT,
                message_id        TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_danmaku_timestamp ON danmaku_messages(timestamp);
            CREATE INDEX IF NOT EXISTS idx_danmaku_type ON danmaku_messages(type);
            """;
        await schemaCmd.ExecuteNonQueryAsync();

        // 老场次库没有 message_id 列：补列后再建唯一索引。
        // 唯一索引 + INSERT OR IGNORE 让多节点重复上报的同一条弹幕自动被丢弃，
        // 这是跨机去重的落点（NULL 不参与唯一约束，历史数据兼容）。
        await EnsureMessageIdColumnAsync(conn);

        await using var idxCmd = conn.CreateCommand();
        idxCmd.CommandText = """
            CREATE UNIQUE INDEX IF NOT EXISTS ux_danmaku_message_id
                ON danmaku_messages(message_id) WHERE message_id IS NOT NULL;
            """;
        await idxCmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 为已存在的场次库补 message_id 列（SQLite 不支持 ADD COLUMN IF NOT EXISTS，先查 pragma）。
    /// </summary>
    private static async Task EnsureMessageIdColumnAsync(SqliteConnection conn)
    {
        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "PRAGMA table_info(danmaku_messages);";
        var hasColumn = false;
        await using (var reader = await checkCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (string.Equals(reader.GetString(1), "message_id", StringComparison.OrdinalIgnoreCase))
                {
                    hasColumn = true;
                    break;
                }
            }
        }

        if (hasColumn) return;

        await using var alterCmd = conn.CreateCommand();
        alterCmd.CommandText = "ALTER TABLE danmaku_messages ADD COLUMN message_id TEXT;";
        await alterCmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 为迁移脚本等场景建表（独立连接，用完即关）。
    /// </summary>
    public static async Task EnsureSchemaAsync(SqliteConnection conn)
    {
        await EnableWalAndCreateSchemaAsync(conn);
    }

    // ─── 批量写入 ───────────────────────────────────────────────

    /// <summary>
    /// 在一个事务内批量 INSERT。使用录制器持有的活跃连接。
    /// </summary>
    public async Task WriteBatchAsync(SqliteConnection conn, IEnumerable<RecordedDanmakuEvent> events)
    {
        // 连接可能已被外部关掉（历史上 CloseSessionAsync 按 uid 关共享连接导致的）。
        // 写入前自愈一次：调用方拿的是缓存的 conn 引用，这里无法替换它，
        // 所以只能在校验失败时显式抛出可识别的错误，由调用方重新 OpenSessionAsync。
        if (conn.State != System.Data.ConnectionState.Open)
        {
            throw new InvalidOperationException(
                $"场次库连接已关闭（DataSource={conn.DataSource}），请重新 OpenSessionAsync 后再写入");
        }

        await using var tx = await conn.BeginTransactionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = """
            INSERT OR IGNORE INTO danmaku_messages
                (type, timestamp, user, uid, text, text_jpn, name, count, price, is_price_total,
                 total_coin, discount_price, guard_level, medal_level, medal_name, coin_type,
                 duration, face, emots, dm_type, raw_command, ul_level, wealth_level,
                 medal_anchor, medal_room_id, medal_guard_level, medal_is_light, medal_anchor_uid,
                 message_id)
            VALUES
                (@type, @timestamp, @user, @uid, @text, @text_jpn, @name, @count, @price, @is_price_total,
                 @total_coin, @discount_price, @guard_level, @medal_level, @medal_name, @coin_type,
                 @duration, @face, @emots, @dm_type, @raw_command, @ul_level, @wealth_level,
                 @medal_anchor, @medal_room_id, @medal_guard_level, @medal_is_light, @medal_anchor_uid,
                 @message_id);
            """;

        // 预创建参数（Issue #14: 使用正确的 SQLite 类型）
        var pType = AddParam(cmd, "@type", SqliteType.Text);
        var pTimestamp = AddParam(cmd, "@timestamp", SqliteType.Integer);
        var pUser = AddParam(cmd, "@user", SqliteType.Text);
        var pUid = AddParam(cmd, "@uid", SqliteType.Text);
        var pText = AddParam(cmd, "@text", SqliteType.Text);
        var pTextJpn = AddParam(cmd, "@text_jpn", SqliteType.Text);
        var pName = AddParam(cmd, "@name", SqliteType.Text);
        var pCount = AddParam(cmd, "@count", SqliteType.Integer);
        var pPrice = AddParam(cmd, "@price", SqliteType.Real);
        var pIsPriceTotal = AddParam(cmd, "@is_price_total", SqliteType.Integer);
        var pTotalCoin = AddParam(cmd, "@total_coin", SqliteType.Real);
        var pDiscountPrice = AddParam(cmd, "@discount_price", SqliteType.Real);
        var pGuardLevel = AddParam(cmd, "@guard_level", SqliteType.Integer);
        var pMedalLevel = AddParam(cmd, "@medal_level", SqliteType.Integer);
        var pMedalName = AddParam(cmd, "@medal_name", SqliteType.Text);
        var pCoinType = AddParam(cmd, "@coin_type", SqliteType.Text);
        var pDuration = AddParam(cmd, "@duration", SqliteType.Integer);
        var pFace = AddParam(cmd, "@face", SqliteType.Text);
        var pEmots = AddParam(cmd, "@emots", SqliteType.Text);
        var pDmType = AddParam(cmd, "@dm_type", SqliteType.Integer);
        var pRawCommand = AddParam(cmd, "@raw_command", SqliteType.Text);
        var pUlLevel = AddParam(cmd, "@ul_level", SqliteType.Integer);
        var pWealthLevel = AddParam(cmd, "@wealth_level", SqliteType.Integer);
        var pMedalAnchor = AddParam(cmd, "@medal_anchor", SqliteType.Text);
        var pMedalRoomId = AddParam(cmd, "@medal_room_id", SqliteType.Integer);
        var pMedalGuardLevel = AddParam(cmd, "@medal_guard_level", SqliteType.Integer);
        var pMedalIsLight = AddParam(cmd, "@medal_is_light", SqliteType.Integer);
        var pMedalAnchorUid = AddParam(cmd, "@medal_anchor_uid", SqliteType.Text);
        var pMessageId = AddParam(cmd, "@message_id", SqliteType.Text);

        foreach (var e in events)
        {
            pType.Value = e.Type;
            pTimestamp.Value = e.Timestamp;
            pUser.Value = e.User ?? "";
            pUid.Value = e.Uid ?? "";
            pText.Value = (object?)e.Text ?? DBNull.Value;
            // text_jpn：优先日语原文 message_jpn，其次机翻 message_trans，最后 text_jpn
            // （与 bilidanmu 的取舍一致：双语 SC 的 message_jpn 才是真正的日语，机翻只是兜底）
            pTextJpn.Value = (object?)(e.MessageJpn ?? e.MessageTrans ?? e.TextJpn) ?? DBNull.Value;
            pName.Value = (object?)e.Name ?? DBNull.Value;
            pCount.Value = e.Count > 0 ? e.Count : 1;
            pPrice.Value = (object?)e.Price ?? DBNull.Value;
            pIsPriceTotal.Value = e.IsPriceTotal ? 1 : 0;
            pTotalCoin.Value = (object?)e.TotalCoin ?? DBNull.Value;
            pDiscountPrice.Value = (object?)e.DiscountPrice ?? DBNull.Value;
            pGuardLevel.Value = (object?)e.GuardLevel ?? DBNull.Value;
            pMedalLevel.Value = (object?)e.MedalLevel ?? DBNull.Value;
            pMedalName.Value = (object?)e.MedalName ?? DBNull.Value;
            pCoinType.Value = (object?)e.CoinType ?? DBNull.Value;
            pDuration.Value = (object?)e.Duration ?? DBNull.Value;
            pFace.Value = (object?)e.Face ?? DBNull.Value;
            pEmots.Value = e.Emots != null ? JsonSerializer.Serialize(e.Emots, EmotJsonOptions) : DBNull.Value;
            pDmType.Value = (object?)e.DmType ?? DBNull.Value;
            pRawCommand.Value = (object?)e.RawCommand ?? DBNull.Value;
            pUlLevel.Value = (object?)e.UlLevel ?? DBNull.Value;
            pWealthLevel.Value = (object?)e.WealthLevel ?? DBNull.Value;
            pMedalAnchor.Value = (object?)e.MedalAnchor ?? DBNull.Value;
            pMedalRoomId.Value = (object?)e.MedalRoomId ?? DBNull.Value;
            pMedalGuardLevel.Value = (object?)e.MedalGuardLevel ?? DBNull.Value;
            pMedalIsLight.Value = e.MedalIsLight.HasValue ? (e.MedalIsLight.Value ? 1 : 0) : DBNull.Value;
            pMedalAnchorUid.Value = (object?)e.MedalAnchorUid ?? DBNull.Value;
            pMessageId.Value = (object?)e.MessageId ?? DBNull.Value;

            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    private static SqliteParameter AddParam(SqliteCommand cmd, string name, SqliteType? type = null)
    {
        // Issue #14: Use correct SQLite types for better performance
        SqliteParameter p;
        if (type.HasValue)
        {
            p = cmd.Parameters.Add(name, type.Value);
        }
        else
        {
            p = cmd.CreateParameter();
            p.ParameterName = name;
            cmd.Parameters.Add(p);
        }
        return p;
    }

    // ─── 读取（已结束 session，按需开连接） ─────────────────────

    /// <summary>
    /// 分页读取弹幕消息（已结束 session）。返回 DanmakuMessage 列表。
    /// </summary>
    public async Task<(List<DanmakuMessage> Messages, int Total)> GetPagedAsync(string sqliteRelativePath, int page, int pageSize)
    {
        var dbPath = ResolveAbsolutePath(sqliteRelativePath);
        if (!File.Exists(dbPath))
        {
            return (new List<DanmakuMessage>(), 0);
        }

        await using // SqliteConnectionStringBuilder handles special chars (e.g. ';') in file paths
var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        await conn.OpenAsync();

        try
        {
        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM danmaku_messages WHERE type IN {DisplayableTypesSql}";
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync() ?? 0);

        var safePageSize = Math.Max(1, pageSize);
        var safePage = Math.Max(1, page);
        var offset = (safePage - 1) * safePageSize;

        await using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = $"""
            SELECT type, timestamp, user, uid, text, text_jpn, name, count, price, is_price_total,
                   total_coin, guard_level, medal_level, medal_name, coin_type, duration, face, emots, dm_type, raw_command,
                   ul_level, wealth_level,
                   medal_anchor, medal_room_id, medal_guard_level, medal_is_light, medal_anchor_uid,
                   message_id
            FROM danmaku_messages
            WHERE type IN {DisplayableTypesSql}
            ORDER BY timestamp ASC, id ASC
            LIMIT @pageSize OFFSET @offset;
            """;
        selectCmd.Parameters.AddWithValue("@pageSize", safePageSize);
        selectCmd.Parameters.AddWithValue("@offset", offset);

        var messages = new List<DanmakuMessage>();
        await using var reader = await selectCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            messages.Add(ReadRowToMessage(reader));
        }

        return (messages, total);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("no such table"))
        {
            // Migrated .db may be an empty shell if migration failed; return empty
            _logger.LogWarning("Table danmaku_messages missing in {DbPath}, returning empty result", dbPath);
            return (new List<DanmakuMessage>(), 0);
        }
    }

    /// <summary>
    /// 读取全部弹幕消息（用于直播结束后的统计计算）。
    /// </summary>
    public async Task<List<DanmakuMessage>> LoadAllMessagesAsync(string sqliteRelativePath)
    {
        var dbPath = ResolveAbsolutePath(sqliteRelativePath);
        if (!File.Exists(dbPath))
        {
            return new List<DanmakuMessage>();
        }

        await using // SqliteConnectionStringBuilder handles special chars (e.g. ';') in file paths
var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT type, timestamp, user, uid, text, text_jpn, name, count, price, is_price_total,
                   total_coin, guard_level, medal_level, medal_name, coin_type, duration, face, emots, dm_type, raw_command,
                   ul_level, wealth_level,
                   medal_anchor, medal_room_id, medal_guard_level, medal_is_light, medal_anchor_uid,
                   message_id
            FROM danmaku_messages
            ORDER BY timestamp ASC, id ASC;
            """;

        var messages = new List<DanmakuMessage>();
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                messages.Add(ReadRowToMessage(reader));
            }
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("no such table"))
        {
            _logger.LogWarning("Table danmaku_messages missing in {DbPath}, returning empty result", dbPath);
        }
        return messages;
    }

    /// <summary>
    /// 删除指定 session 的 .db 文件（管理后台删除 session 时调用）。
    /// </summary>
    public void DeleteDbFile(string sqliteRelativePath)
    {
        var dbPath = ResolveAbsolutePath(sqliteRelativePath);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = dbPath + suffix;
            if (File.Exists(path))
            {
                try { File.Delete(path); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete {Path}", path);
                }
            }
        }
    }

    /// <summary>
    /// 一次性导入消息到指定 SQLite session 文件（用于 XML 上传导入）。
    /// 使用独立连接，写入后关闭。若文件已存在则覆盖重建。
    /// </summary>
    public async Task ImportMessagesAsync(string sqliteRelativePath, List<DanmakuMessage> messages)
    {
        var dbPath = ResolveAbsolutePath(sqliteRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        // Issue #5: Write to temp file first, then atomically replace
        var tempPath = dbPath + ".tmp";
        try
        {
            // Clean up any leftover temp file
            if (File.Exists(tempPath)) File.Delete(tempPath);

            await using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = tempPath }.ToString());
            await conn.OpenAsync();
            await EnableWalAndCreateSchemaAsync(conn);

            if (messages.Count > 0)
            {
                var events = messages.Select(ToRecordedEventForImport).ToList();
                await WriteBatchAsync(conn, events);
            }

            // WAL checkpoint: flush WAL into main file before closing and moving
            await using var pragmaCmd = conn.CreateCommand();
            pragmaCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await pragmaCmd.ExecuteNonQueryAsync();

            // Close connection before moving file
            await conn.CloseAsync();

            // Delete old files and atomically replace
            DeleteDbFile(sqliteRelativePath);
            File.Move(tempPath, dbPath);

            _logger.LogInformation("Imported {Count} messages into SQLite session {Path}", messages.Count, sqliteRelativePath);
        }
        catch
        {
            // Clean up temp file on failure
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            // Also clean up temp WAL/SHM files
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                try { if (File.Exists(tempPath + suffix)) File.Delete(tempPath + suffix); } catch { }
            }
            throw;
        }
    }

    private static RecordedDanmakuEvent ToRecordedEventForImport(DanmakuMessage m)
    {
        return new RecordedDanmakuEvent
        {
            Type = m.Type == "give_gift" ? "gift" : m.Type,
            Timestamp = m.Timestamp,
            Text = m.Text,
            TextJpn = m.TextJpn,
            Name = m.Name,
            Count = m.Count ?? 1,
            Price = m.Price,
            IsPriceTotal = m.IsPriceTotal,
            TotalCoin = m.TotalCoin,
            GuardLevel = m.GuardLevel,
            MedalLevel = m.MedalLevel,
            MedalName = m.MedalName,
            User = m.Sender.Name,
            Uid = m.Sender.Uid,
            Face = m.Face,
            Emots = m.Emots,
            DmType = m.DmType,
            UlLevel = m.UlLevel,
            WealthLevel = m.WealthLevel,
            MedalAnchor = m.MedalAnchor,
            MedalRoomId = m.MedalRoomId,
            MedalGuardLevel = m.MedalGuardLevel,
            MedalIsLight = m.MedalIsLight,
            MedalAnchorUid = m.MedalAnchorUid,
            MessageId = m.MessageId,
            RawCommand = m.RawCommand
        };
    }

    // ─── 行 → DanmakuMessage 映射 ───────────────────────────────

    // Issue #15: Column ordinal cache to avoid repeated GetOrdinal lookups
    private static readonly string[] ColumnNames = [
        "type", "timestamp", "user", "uid", "text", "text_jpn", "name", "count",
        "price", "is_price_total", "total_coin", "guard_level", "medal_level",
        "medal_name", "coin_type", "duration", "face", "emots", "dm_type", "raw_command",
        "ul_level", "wealth_level", "medal_anchor", "medal_room_id", "medal_guard_level",
        "medal_is_light", "medal_anchor_uid", "message_id"
    ];

    private static DanmakuMessage ReadRowToMessage(SqliteDataReader reader)
    {
        // Issue #15: Cache ordinals before the loop (called from a loop context)
        var ordinals = new int[ColumnNames.Length];
        for (var i = 0; i < ColumnNames.Length; i++)
        {
            ordinals[i] = reader.GetOrdinal(ColumnNames[i]);
        }

        var type = reader.GetString(ordinals[0]); // type
        var mappedType = type switch
        {
            "gift" => "give_gift",
            _ => type
        };

        Dictionary<string, EmoticonInfo>? emots = null;
        if (!reader.IsDBNull(ordinals[17])) // emots
        {
            var emotsJson = reader.GetString(ordinals[17]);
            if (!string.IsNullOrWhiteSpace(emotsJson))
            {
                try
                {
                    emots = JsonSerializer.Deserialize<Dictionary<string, EmoticonInfo>>(emotsJson, EmotJsonOptions);
                }
                catch (JsonException ex)
                {
                    // Issue: Log instead of silently swallowing
                    System.Diagnostics.Debug.WriteLine($"Failed to parse emots JSON: {ex.Message}");
                }
            }
        }

        return new DanmakuMessage
        {
            Type = mappedType,
            Timestamp = reader.GetInt64(ordinals[1]), // timestamp
            Text = reader.IsDBNull(ordinals[4]) ? null : reader.GetString(ordinals[4]), // text
            TextJpn = reader.IsDBNull(ordinals[5]) ? null : reader.GetString(ordinals[5]), // text_jpn
            Name = reader.IsDBNull(ordinals[6]) ? null : reader.GetString(ordinals[6]), // name
            Count = reader.GetInt32(ordinals[7]), // count
            Price = reader.IsDBNull(ordinals[8]) ? null : reader.GetDouble(ordinals[8]), // price
            IsPriceTotal = reader.GetInt32(ordinals[9]) == 1, // is_price_total
            TotalCoin = reader.IsDBNull(ordinals[10]) ? null : reader.GetDouble(ordinals[10]), // total_coin
            GuardLevel = reader.IsDBNull(ordinals[11]) ? null : reader.GetInt32(ordinals[11]), // guard_level
            MedalLevel = reader.IsDBNull(ordinals[12]) ? null : reader.GetInt32(ordinals[12]), // medal_level
            MedalName = reader.IsDBNull(ordinals[13]) ? null : reader.GetString(ordinals[13]), // medal_name
            CoinType = reader.IsDBNull(ordinals[14]) ? null : reader.GetString(ordinals[14]), // coin_type
            Duration = reader.IsDBNull(ordinals[15]) ? null : reader.GetInt32(ordinals[15]), // duration
            Face = reader.IsDBNull(ordinals[16]) ? null : reader.GetString(ordinals[16]), // face
            Emots = emots,
            DmType = reader.IsDBNull(ordinals[18]) ? null : reader.GetInt32(ordinals[18]), // dm_type
            RawCommand = reader.IsDBNull(ordinals[19]) ? null : reader.GetString(ordinals[19]), // raw_command
            UlLevel = reader.IsDBNull(ordinals[20]) ? null : reader.GetInt32(ordinals[20]), // ul_level
            WealthLevel = reader.IsDBNull(ordinals[21]) ? null : reader.GetInt32(ordinals[21]), // wealth_level
            MedalAnchor = reader.IsDBNull(ordinals[22]) ? null : reader.GetString(ordinals[22]), // medal_anchor
            MedalRoomId = reader.IsDBNull(ordinals[23]) ? null : reader.GetInt32(ordinals[23]), // medal_room_id
            MedalGuardLevel = reader.IsDBNull(ordinals[24]) ? null : reader.GetInt32(ordinals[24]), // medal_guard_level
            MedalIsLight = reader.IsDBNull(ordinals[25]) ? null : reader.GetInt32(ordinals[25]) == 1, // medal_is_light
            MedalAnchorUid = reader.IsDBNull(ordinals[26]) ? null : (long?)reader.GetInt64(ordinals[26]), // medal_anchor_uid
            MessageId = reader.IsDBNull(ordinals[27]) ? null : reader.GetString(ordinals[27]), // message_id
            Sender = new Sender
            {
                Name = reader.GetString(ordinals[2]), // user
                Uid = reader.GetString(ordinals[3]) // uid
            }
        };
    }
}
