using Danmu.Server.Data;
using Danmu.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Danmu.Server.Services;

public class ChangelogService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ChangelogService> _logger;

    public ChangelogService(IServiceProvider serviceProvider, ILogger<ChangelogService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<List<ChangelogEntry>> GetAllAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        return await db.ChangelogEntries.AsNoTracking()
            .OrderByDescending(c => c.Date)
            .ToListAsync();
    }

    public async Task<ChangelogEntry> AddAsync(string version, DateTime date, string content)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var entry = new ChangelogEntry
        {
            Version = version,
            Date = date,
            Content = content
        };
        db.ChangelogEntries.Add(entry);
        await db.SaveChangesAsync();
        _logger.LogInformation("Added changelog entry: {Version}", version);
        return entry;
    }

    public async Task<ChangelogEntry?> UpdateAsync(int id, string version, DateTime date, string content)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var entry = await db.ChangelogEntries.FirstOrDefaultAsync(c => c.Id == id);
        if (entry == null) return null;
        entry.Version = version;
        entry.Date = date;
        entry.Content = content;
        await db.SaveChangesAsync();
        _logger.LogInformation("Updated changelog entry {Id}", id);
        return entry;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var entry = await db.ChangelogEntries.FirstOrDefaultAsync(c => c.Id == id);
        if (entry == null) return false;
        db.ChangelogEntries.Remove(entry);
        await db.SaveChangesAsync();
        _logger.LogInformation("Deleted changelog entry {Id}", id);
        return true;
    }
}