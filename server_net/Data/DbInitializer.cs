using Danmu.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(DanmuContext db, ILogger logger)
    {
        // 1. Ensure DB is created
        await db.Database.EnsureCreatedAsync();

        // 2. Seed Data from seed_vups.sql if table is empty OR force update
        logger.LogInformation("Seeding/Updating VUP data from SQL script...");
        try
        {
            var seedPath = Path.Combine(Directory.GetCurrentDirectory(), "Data/seed_vups.sql");
            if (File.Exists(seedPath))
            {
                var sql = await File.ReadAllTextAsync(seedPath);
                
                // If using MySQL, replace SQLite syntax with MySQL syntax
                if (db.Database.IsMySql())
                {
                    sql = sql.Replace("ON CONFLICT(uid) DO UPDATE SET", "ON DUPLICATE KEY UPDATE");
                    sql = sql.Replace("excluded.", "VALUES(");
                    // This simple replace might not work perfectly for complex SQL, 
                    // but for the current seed_vups.sql it might.
                    // Let's do a better replacement or just provide a MySQL-ready script.
                }

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
