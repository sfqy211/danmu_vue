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

    public AvatarScheduler(ILogger<AvatarScheduler> logger, BilibiliService bilibiliService, ImageService imageService)
    {
        _logger = logger;
        _bilibiliService = bilibiliService;
        _imageService = imageService;

        var root = Directory.GetCurrentDirectory();
        _bgDir = Path.GetFullPath(Path.Combine(root, "../public/vup-bg"));
        _avatarDir = Path.GetFullPath(Path.Combine(root, "../public/vup-avatar"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Directory.Exists(_bgDir)) Directory.CreateDirectory(_bgDir);
        if (!Directory.Exists(_avatarDir)) Directory.CreateDirectory(_avatarDir);

        _logger.LogInformation("AvatarScheduler started.");

        // Delay 10 seconds before first run
        await Task.Delay(10000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunUpdateTask();
            // Run every 24 hours
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task RunUpdateTask()
    {
        _logger.LogInformation("[Avatar] Starting avatar update task...");

        foreach (var vup in VupConstants.Vups)
        {
            try
            {
                var avatarUrl = await _bilibiliService.GetAvatarUrlAsync(vup.Uid);
                if (string.IsNullOrEmpty(avatarUrl)) continue;

                var imageBytes = await _bilibiliService.DownloadImageAsync(avatarUrl);
                if (imageBytes == null) continue;

                // Save original to vup-bg
                var bgPath = Path.Combine(_bgDir, $"{vup.Uid}.png");
                await _imageService.SavePngAsync(imageBytes, bgPath);
                _logger.LogInformation($"[Avatar] Saved original for {vup.Name} to {bgPath}");

                // Save thumbnail to vup-avatar
                var avatarPath = Path.Combine(_avatarDir, $"{vup.Uid}.webp");
                await _imageService.ResizeAndSaveWebpAsync(imageBytes, avatarPath, 120, 120);
                _logger.LogInformation($"[Avatar] Saved thumbnail for {vup.Name} to {avatarPath}");

                // Wait 30 seconds between requests
                await Task.Delay(30000);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[Avatar] Failed to update avatar for {vup.Name}");
            }
        }

        _logger.LogInformation("[Avatar] Avatar update task completed.");
    }
}
