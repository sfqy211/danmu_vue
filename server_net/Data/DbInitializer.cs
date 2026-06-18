using Danmu.Server.Models;
using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(DanmuContext db, ILogger logger)
    {
        // 1. Ensure DB is created
        await db.Database.EnsureCreatedAsync();
        await EnsureSchemaAsync(db, logger);

        // 2. Seed Data from seed_vups.sql if table is empty OR force update
        if (await db.Rooms.AnyAsync())
        {
            logger.LogInformation("Database already contains rooms, skipping initial seed.");
            return;
        }

        logger.LogInformation("Seeding VUP data from SQL script...");
        try
        {
            var seedPath = Path.Combine(Directory.GetCurrentDirectory(), "Data/seed_vups_sqlite.sql");
            if (File.Exists(seedPath))
            {
                var sql = await File.ReadAllTextAsync(seedPath);
                await db.Database.ExecuteSqlRawAsync(sql);
                logger.LogInformation("Database seeded/updated successfully.");
            }
            else
            {
                logger.LogWarning($"Seed file not found at: {seedPath}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error seeding database");
        }
    }

    private static async Task EnsureSchemaAsync(DanmuContext db, ILogger logger)
    {
        try
        {
            if (!await ColumnExistsAsync(db, "sessions", "uid"))
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE sessions ADD COLUMN uid TEXT");
                logger.LogInformation("Added sessions.uid column.");
            }

            if (!await IndexExistsAsync(db, "sessions", "ix_sessions_uid"))
            {
                await db.Database.ExecuteSqlRawAsync("CREATE INDEX ix_sessions_uid ON sessions(uid)");
                logger.LogInformation("Created ix_sessions_uid index.");
            }

            if (!await TableExistsAsync(db, "bili_accounts"))
            {
                var createSql = @"CREATE TABLE bili_accounts (
                    uid INTEGER NOT NULL PRIMARY KEY,
                    name TEXT,
                    face TEXT,
                    access_token TEXT,
                    refresh_token TEXT,
                    cookie_json TEXT,
                    expires_at TEXT,
                    is_active INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                )";
                await db.Database.ExecuteSqlRawAsync(createSql);
                logger.LogInformation("Created bili_accounts table.");
            }

            if (!await TableExistsAsync(db, "changelog_entries"))
            {
                var createSql = @"CREATE TABLE changelog_entries (
                    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    version TEXT NOT NULL,
                    date TEXT NOT NULL,
                    content TEXT NOT NULL,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                )";
                await db.Database.ExecuteSqlRawAsync(createSql);
                logger.LogInformation("Created changelog_entries table.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error ensuring database schema");
        }
    }

    private static async Task<bool> ColumnExistsAsync(DanmuContext db, string tableName, string columnName)
    {
        var escapedTable = EscapeSqlLiteral(tableName);
        var escapedColumn = EscapeSqlLiteral(columnName);

        var sql = $"SELECT COUNT(*) FROM pragma_table_info('{escapedTable}') WHERE name = '{escapedColumn}'";
        return await ExecuteScalarLongAsync(db, sql) > 0;
    }

    private static async Task<bool> IndexExistsAsync(DanmuContext db, string tableName, string indexName)
    {
        var escapedTable = EscapeSqlLiteral(tableName);
        var escapedIndex = EscapeSqlLiteral(indexName);

        var sql = $"SELECT COUNT(*) FROM pragma_index_list('{escapedTable}') WHERE name = '{escapedIndex}'";
        return await ExecuteScalarLongAsync(db, sql) > 0;
    }

    private static async Task<bool> TableExistsAsync(DanmuContext db, string tableName)
    {
        var escapedTable = EscapeSqlLiteral(tableName);

        var sql = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{escapedTable}'";
        return await ExecuteScalarLongAsync(db, sql) > 0;
    }

    private static async Task<long> ExecuteScalarLongAsync(DanmuContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var value = await command.ExecuteScalarAsync();
            return Convert.ToInt64(value ?? 0);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''");
    }
}
