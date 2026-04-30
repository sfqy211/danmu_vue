using System.Collections.Concurrent;
using Danmu.Server.Data;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Danmu.Server.Services;

public class AvatarScheduler : BackgroundService
{
    private readonly ILogger<AvatarScheduler> _logger;
    private readonly BilibiliService _bilibiliService;
    private readonly ImageService _imageService;
    private readonly CosService _cosService;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _bgDir;
    private readonly string _avatarDir;
    private readonly ConcurrentDictionary<string, DateTime> _lastUpdateMap = new();
    private readonly TimeSpan _updateInterval = TimeSpan.FromDays(1);
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(30);

    public AvatarScheduler(ILogger<AvatarScheduler> logger, BilibiliService bilibiliService, ImageService imageService, CosService cosService, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _bilibiliService = bilibiliService;
        _imageService = imageService;
        _cosService = cosService;
        _serviceProvider = serviceProvider;

        var root = Directory.GetCurrentDirectory();
        _bgDir = Path.GetFullPath(Path.Combine(root, "../server/data/vup-bg"));
        _avatarDir = Path.GetFullPath(Path.Combine(root, "../server/data/vup-avatar"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Directory.Exists(_bgDir)) Directory.CreateDirectory(_bgDir);
        if (!Directory.Exists(_avatarDir)) Directory.CreateDirectory(_avatarDir);

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

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
            var vups = await db.Rooms
                .Where(r => !string.IsNullOrEmpty(r.Uid))
                .OrderBy(r => r.Name ?? string.Empty)
                .Select(r => new { Uid = r.Uid!, Name = r.Name ?? "Unknown", r.RoomId })
                .ToListAsync(stoppingToken);

            foreach (var vup in vups)
            {
                if (_lastUpdateMap.TryGetValue(vup.Uid, out var lastUpdate) && (now - lastUpdate) <= _updateInterval)
                {
                    continue;
                }

                var bgPath = Path.Combine(_bgDir, $"{vup.Uid}.png");
                if (File.Exists(bgPath))
                {
                    var lastWrite = File.GetLastWriteTimeUtc(bgPath);
                    if ((now - lastWrite) <= _updateInterval)
                    {
                        _lastUpdateMap[vup.Uid] = lastWrite;
                        continue;
                    }
                }

                await UpdateSingleAvatarAsync((vup.Uid, vup.Name, vup.RoomId), stoppingToken);
                _lastUpdateMap[vup.Uid] = DateTime.UtcNow;
                break;
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
            await _cosService.UploadAsync(bgPath, $"vup-bg/{vup.Uid}.png", "image/png");

            // Save thumbnail to vup-avatar
            var avatarPath = Path.Combine(_avatarDir, $"{vup.Uid}.webp");
            await _imageService.ResizeAndSaveWebpAsync(imageBytes, avatarPath, 120, 120);
            _logger.LogInformation($"[Avatar] Saved thumbnail for {vup.Name} to {avatarPath}");
            await _cosService.UploadAsync(avatarPath, $"vup-avatar/{vup.Uid}.webp", "image/webp");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[Avatar] Failed to update avatar for {vup.Name}");
        }
    }
}
