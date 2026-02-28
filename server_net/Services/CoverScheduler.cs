using System.Collections.Concurrent;
using Danmu.Server.Constants;
using Danmu.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Services;

public class CoverScheduler : BackgroundService
{
    private readonly ILogger<CoverScheduler> _logger;
    private readonly BilibiliService _bilibiliService;
    private readonly ImageService _imageService;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _coverDir;
    private readonly ConcurrentDictionary<long, DateTime> _lastUpdateMap = new();
    private readonly TimeSpan _updateInterval = TimeSpan.FromHours(24);
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(2); // Check every 2 mins

    public CoverScheduler(ILogger<CoverScheduler> logger, BilibiliService bilibiliService, ImageService imageService, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _bilibiliService = bilibiliService;
        _imageService = imageService;
        _serviceProvider = serviceProvider;

        var root = Directory.GetCurrentDirectory();
        _coverDir = Path.GetFullPath(Path.Combine(root, "../server/data/vup-cover"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Directory.Exists(_coverDir)) Directory.CreateDirectory(_coverDir);

        using var timer = new PeriodicTimer(_checkInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessNextCoverAsync(stoppingToken);
        }
    }

    private async Task ProcessNextCoverAsync(CancellationToken stoppingToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            
            // 1. Build room list
            var roomMap = new Dictionary<long, (long RoomId, string Name, string Uid)>();
            foreach (var vup in VupConstants.Vups)
            {
                roomMap[vup.RoomId] = (vup.RoomId, vup.Name, vup.Uid);
            }

            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
                var dbRooms = await db.Rooms.ToListAsync(stoppingToken);
                foreach (var room in dbRooms)
                {
                    if (!roomMap.ContainsKey(room.RoomId))
                    {
                        roomMap[room.RoomId] = (room.RoomId, room.Name ?? "Unknown", room.Uid ?? "");
                    }
                }
            }

            // 2. Find one room to update
            foreach (var room in roomMap.Values)
            {
                if (!_lastUpdateMap.TryGetValue(room.RoomId, out var lastUpdate) || (now - lastUpdate) > _updateInterval)
                {
                    await UpdateSingleCoverAsync(room, stoppingToken);
                    _lastUpdateMap[room.RoomId] = DateTime.UtcNow;
                    break; // Process only one per tick
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ProcessNextCoverAsync");
        }
    }

    private async Task UpdateSingleCoverAsync((long RoomId, string Name, string Uid) room, CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation($"[Cover] Checking cover for {room.Name}...");
            
            var (title, userName, liveStatus, coverUrl, uid) = await _bilibiliService.GetRoomInfoAsync(room.RoomId);
            if (string.IsNullOrEmpty(coverUrl)) return;

            // If we got a UID from API and don't have one, update it
            var finalUid = string.IsNullOrEmpty(room.Uid) ? uid : room.Uid;
            
            var imageBytes = await _bilibiliService.DownloadImageAsync(coverUrl);
            if (imageBytes == null) return;

            var filenameBase = !string.IsNullOrEmpty(finalUid) ? finalUid : room.RoomId.ToString();
            var filename = $"{filenameBase}.png";

            var coverPath = Path.Combine(_coverDir, filename);

            await _imageService.SavePngAsync(imageBytes, coverPath);
            _logger.LogInformation($"[Cover] Saved cover for {room.Name} to {coverPath}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[Cover] Failed to update cover for {room.Name}");
        }
    }
}
