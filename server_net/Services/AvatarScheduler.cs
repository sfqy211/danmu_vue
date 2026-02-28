using System.Collections.Concurrent;
using Danmu.Server.Constants;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Danmu.Server.Services;

public class AvatarScheduler : BackgroundService
{
    private readonly ILogger<AvatarScheduler> _logger;
    private readonly BilibiliService _bilibiliService;
    private readonly ImageService _imageService;
    private readonly string _bgDir;
    private readonly string _avatarDir;
    private readonly ConcurrentDictionary<string, DateTime> _lastUpdateMap = new();
    private readonly TimeSpan _updateInterval = TimeSpan.FromHours(24);
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(2); // Check every 2 mins to spread out

    public AvatarScheduler(ILogger<AvatarScheduler> logger, BilibiliService bilibiliService, ImageService imageService)
    {
        _logger = logger;
        _bilibiliService = bilibiliService;
        _imageService = imageService;

        var root = Directory.GetCurrentDirectory();
        _bgDir = Path.GetFullPath(Path.Combine(root, "../server/data/vup-bg"));
        _avatarDir = Path.GetFullPath(Path.Combine(root, "../server/data/vup-avatar"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Directory.Exists(_bgDir)) Directory.CreateDirectory(_bgDir);
        if (!Directory.Exists(_avatarDir)) Directory.CreateDirectory(_avatarDir);

        _logger.LogInformation("AvatarScheduler started.");

        using var timer = new PeriodicTimer(_checkInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessNextAvatarAsync(stoppingToken);
        }
    }

    private async Task ProcessNextAvatarAsync(CancellationToken stoppingToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            
            // Find one VUP that needs update
            foreach (var vup in VupConstants.Vups)
            {
                if (!_lastUpdateMap.TryGetValue(vup.Uid, out var lastUpdate) || (now - lastUpdate) > _updateInterval)
                {
                    await UpdateSingleAvatarAsync(vup, stoppingToken);
                    _lastUpdateMap[vup.Uid] = DateTime.UtcNow;
                    break; // Process only one per tick
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ProcessNextAvatarAsync");
        }
    }

    private async Task UpdateSingleAvatarAsync((string Uid, string Name, long RoomId) vup, CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation($"[Avatar] Checking avatar for {vup.Name}...");
            
            var avatarUrl = await _bilibiliService.GetAvatarUrlAsync(vup.Uid);
            if (string.IsNullOrEmpty(avatarUrl)) return;

            var imageBytes = await _bilibiliService.DownloadImageAsync(avatarUrl);
            if (imageBytes == null) return;

            // Save original to vup-bg
            var bgPath = Path.Combine(_bgDir, $"{vup.Uid}.png");
            await _imageService.SavePngAsync(imageBytes, bgPath);
            _logger.LogInformation($"[Avatar] Saved original for {vup.Name} to {bgPath}");

            // Save thumbnail to vup-avatar
            var avatarPath = Path.Combine(_avatarDir, $"{vup.Uid}.webp");
            await _imageService.ResizeAndSaveWebpAsync(imageBytes, avatarPath, 120, 120);
            _logger.LogInformation($"[Avatar] Saved thumbnail for {vup.Name} to {avatarPath}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[Avatar] Failed to update avatar for {vup.Name}");
        }
    }
}
