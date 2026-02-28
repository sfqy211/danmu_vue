using Danmu.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(DanmuContext db, ILogger logger)
    {
        // 1. Ensure DB is created
        await db.Database.EnsureCreatedAsync();

        // 2. Schema Migration: Check and add columns if missing (SQLite specific)
        try 
        {
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(rooms);";
            using var reader = await command.ExecuteReaderAsync();
            
            var hasGroupName = false;
            var hasPlaylistUrl = false;
            var hasSortOrder = false;
            var hasAutoRecord = false;
            var hasFollowers = false;
            var hasGuardNum = false;
            var hasVideoCount = false;
            var hasLastLiveTime = false;
            var hasUpdatedAt = false;
            
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1); // name column
                
                if (string.Equals(name, "group_name", StringComparison.OrdinalIgnoreCase)) hasGroupName = true;
                if (string.Equals(name, "playlist_url", StringComparison.OrdinalIgnoreCase)) hasPlaylistUrl = true;
                if (string.Equals(name, "sort_order", StringComparison.OrdinalIgnoreCase)) hasSortOrder = true;
                if (string.Equals(name, "auto_record", StringComparison.OrdinalIgnoreCase)) hasAutoRecord = true;
                if (string.Equals(name, "followers", StringComparison.OrdinalIgnoreCase)) hasFollowers = true;
                if (string.Equals(name, "guard_num", StringComparison.OrdinalIgnoreCase)) hasGuardNum = true;
                if (string.Equals(name, "video_count", StringComparison.OrdinalIgnoreCase)) hasVideoCount = true;
                if (string.Equals(name, "last_live_time", StringComparison.OrdinalIgnoreCase)) hasLastLiveTime = true;
                if (string.Equals(name, "updated_at", StringComparison.OrdinalIgnoreCase)) hasUpdatedAt = true;
            }
            reader.Close();

            if (!hasAutoRecord)
            {
                logger.LogInformation("Adding missing column 'auto_record' to 'rooms' table...");
                command.CommandText = "ALTER TABLE rooms ADD COLUMN auto_record INTEGER DEFAULT 1;";
                await command.ExecuteNonQueryAsync();
            }

            if (!hasGroupName)
            {
                logger.LogInformation("Adding missing column 'group_name' to 'rooms' table...");
                command.CommandText = "ALTER TABLE rooms ADD COLUMN group_name TEXT;";
                await command.ExecuteNonQueryAsync();
            }

            if (!hasPlaylistUrl)
            {
                logger.LogInformation("Adding missing column 'playlist_url' to 'rooms' table...");
                command.CommandText = "ALTER TABLE rooms ADD COLUMN playlist_url TEXT;";
                await command.ExecuteNonQueryAsync();
            }

            if (!hasSortOrder)
            {
                logger.LogInformation("Adding missing column 'sort_order' to 'rooms' table...");
                command.CommandText = "ALTER TABLE rooms ADD COLUMN sort_order INTEGER DEFAULT 0;";
                await command.ExecuteNonQueryAsync();
            }

            if (!hasFollowers)
            {
                logger.LogInformation("Adding missing column 'followers' to 'rooms' table...");
                command.CommandText = "ALTER TABLE rooms ADD COLUMN followers INTEGER DEFAULT 0;";
                await command.ExecuteNonQueryAsync();
            }

            if (!hasGuardNum)
            {
                logger.LogInformation("Adding missing column 'guard_num' to 'rooms' table...");
                command.CommandText = "ALTER TABLE rooms ADD COLUMN guard_num INTEGER DEFAULT 0;";
                await command.ExecuteNonQueryAsync();
            }

            if (!hasVideoCount)
            {
                logger.LogInformation("Adding missing column 'video_count' to 'rooms' table...");
                command.CommandText = "ALTER TABLE rooms ADD COLUMN video_count INTEGER DEFAULT 0;";
                await command.ExecuteNonQueryAsync();
            }

            if (!hasLastLiveTime)
            {
                logger.LogInformation("Adding missing column 'last_live_time' to 'rooms' table...");
                command.CommandText = "ALTER TABLE rooms ADD COLUMN last_live_time INTEGER DEFAULT 0;";
                await command.ExecuteNonQueryAsync();
            }

            if (!hasUpdatedAt)
            {
                logger.LogInformation("Adding missing column 'updated_at' to 'rooms' table...");
                command.CommandText = "ALTER TABLE rooms ADD COLUMN updated_at TEXT;";
                await command.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking/migrating schema");
        }

        // 2.5 Clean up duplicates before applying unique constraint on UID
        try 
        {
            logger.LogInformation("Cleaning up duplicate rooms by UID (keeping max RoomId)...");
            // Keep the row with the largest RoomId for each UID (assuming long ID > short ID)
            var cleanupSql = @"
                DELETE FROM rooms 
                WHERE id NOT IN (
                    SELECT t1.id 
                    FROM rooms t1
                    JOIN (
                        SELECT uid, MAX(room_id) as max_rid 
                        FROM rooms 
                        WHERE uid IS NOT NULL 
                        GROUP BY uid
                    ) t2 ON t1.uid = t2.uid AND t1.room_id = t2.max_rid
                );";
            await db.Database.ExecuteSqlRawAsync(cleanupSql);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error cleaning up duplicates");
        }

        // 3. Seed Data from seed_vups.sql if table is empty OR force update
        // Use a flag or check if specific homepage display rooms are missing to decide whether to re-seed/update
        // For this refactor, we want to ensure these VUPs exist with correct new fields.
        // We will execute the SQL script which uses UPSERT (ON CONFLICT DO UPDATE) syntax.
        
        logger.LogInformation("Seeding/Updating VUP data from SQL script...");
        try
        {
            var seedPath = Path.Combine(Directory.GetCurrentDirectory(), "Data/seed_vups.sql");
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
}
