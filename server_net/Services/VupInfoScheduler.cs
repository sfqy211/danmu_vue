using System.Collections.Concurrent;
using Danmu.Server.Data;
using Danmu.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Services;

public class VupInfoScheduler : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<VupInfoScheduler> _logger;
    private readonly ConcurrentDictionary<long, DateTime> _lastUpdateMap = new();
    private readonly TimeSpan _updateInterval = TimeSpan.FromHours(1);
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

    public VupInfoScheduler(IServiceProvider services, ILogger<VupInfoScheduler> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_checkInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessNextVupAsync(stoppingToken);
        }
    }

    private async Task ProcessNextVupAsync(CancellationToken stoppingToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
        var biliService = scope.ServiceProvider.GetRequiredService<BilibiliService>();

        try 
        {
            var rooms = await db.Rooms.ToListAsync(stoppingToken);
            
            // Find one VUP that needs update
            Room? targetRoom = null;
            var now = DateTime.UtcNow;

            foreach (var room in rooms)
            {
                if (string.IsNullOrEmpty(room.Uid)) continue;

                // Check if it's time to update this VUP
                // 1. Check in-memory cache
                if (_lastUpdateMap.TryGetValue(room.RoomId, out var lastUpdate) && (now - lastUpdate) <= _updateInterval)
                {
                    continue;
                }

                // 2. Check persistence field UpdatedAt
                if (!string.IsNullOrEmpty(room.UpdatedAt) && DateTime.TryParse(room.UpdatedAt, out var persistentUpdate))
                {
                    if ((now - persistentUpdate.ToUniversalTime()) <= _updateInterval)
                    {
                        // Cache it to avoid parsing next time
                        _lastUpdateMap[room.RoomId] = persistentUpdate.ToUniversalTime();
                        continue;
                    }
                }

                targetRoom = room;
                break; // Process only one per tick
            }

            if (targetRoom != null)
            {
                await UpdateSingleVupStatsAsync(targetRoom, db, biliService, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ProcessNextVupAsync");
        }
    }

    private async Task UpdateSingleVupStatsAsync(Room room, DanmuContext db, BilibiliService biliService, CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation($"Updating stats for {room.Name} (RoomId: {room.RoomId})...");

            var (followers, guardNum, videoCount) = await biliService.GetVupStatsAsync(room.RoomId, room.Uid!);
            var (liveStatus, liveStartTime) = await biliService.GetRoomLiveStatusAsync(room.RoomId);
            
            room.Followers = followers;
            room.GuardNum = guardNum;
            room.VideoCount = videoCount;
            
            // Priority:
            // 1. Current live status (if live)
            // 2. Local session history (if offline)
            
            if (liveStatus == 1 && liveStartTime.HasValue)
            {
                room.LastLiveTime = liveStartTime.Value;
            }
            else 
            {
                // Try to find last session time from DB if offline
                try 
                {
                    var roomIdStr = room.RoomId.ToString();
                    var lastSession = await db.Sessions
                        .Where(s => s.RoomId == roomIdStr)
                        .OrderByDescending(s => s.StartTime)
                        .FirstOrDefaultAsync(stoppingToken);
                        
                    if (lastSession != null && lastSession.StartTime > room.LastLiveTime)
                    {
                        room.LastLiveTime = lastSession.StartTime ?? 0;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to get last session time for {room.Name}");
                }
            }
            
            room.UpdatedAt = DateTime.UtcNow.ToString("O");
            
            await db.SaveChangesAsync(stoppingToken);
            
            // Update the map
            _lastUpdateMap[room.RoomId] = DateTime.UtcNow;
            
            _logger.LogInformation($"Updated stats for {room.Name}: Followers={followers}, Guards={guardNum}, Videos={videoCount}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating stats for room {room.RoomId}");
        }
    }
}
