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

        _logger.LogInformation("CoverScheduler started.");

        // Calculate delay until next 04:00
        var now = DateTime.Now;
        var target = now.Date.AddHours(4); // Today 04:00
        if (now > target)
        {
            target = target.AddDays(1); // Next day 04:00
        }
        var delay = target - now;

        _logger.LogInformation($"Next cover update scheduled at {target} (in {delay.TotalHours:F1} hours)");
        await Task.Delay(delay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunUpdateTask();
            // Run every 24 hours
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task RunUpdateTask()
    {
        _logger.LogInformation("[Cover] Starting cover update task...");

        // Combine hardcoded VUPs and DB rooms
        var roomMap = new Dictionary<long, (long RoomId, string Name, string Uid)>();

        foreach (var vup in VupConstants.Vups)
        {
            roomMap[vup.RoomId] = (vup.RoomId, vup.Name, vup.Uid);
        }

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
            var dbRooms = await db.Rooms.ToListAsync();
            foreach (var room in dbRooms)
            {
                if (!roomMap.ContainsKey(room.RoomId))
                {
                    roomMap[room.RoomId] = (room.RoomId, room.Name ?? "Unknown", room.Uid ?? "");
                }
            }
        }

        foreach (var room in roomMap.Values)
        {
            try
            {
                var (title, userName, liveStatus, coverUrl, uid) = await _bilibiliService.GetRoomInfoAsync(room.RoomId);
                if (string.IsNullOrEmpty(coverUrl)) continue;

                // If we got a UID from API and don't have one, update it
                var finalUid = string.IsNullOrEmpty(room.Uid) ? uid : room.Uid;
                
                var imageBytes = await _bilibiliService.DownloadImageAsync(coverUrl);
                if (imageBytes == null) continue;

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

            // Wait 30 seconds
            await Task.Delay(30000);
        }

        _logger.LogInformation("[Cover] Cover update task completed.");
    }
}
