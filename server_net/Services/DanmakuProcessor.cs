using Danmu.Server.Data;
using Danmu.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Services;

public class DanmakuProcessor : BackgroundService
{
    private readonly ILogger<DanmakuProcessor> _logger;
    private readonly DanmakuService _service;
    private readonly string _danmakuDir;
    private FileSystemWatcher? _watcher;

    public DanmakuProcessor(ILogger<DanmakuProcessor> logger, DanmakuService service)
    {
        _logger = logger;
        _service = service;
        _danmakuDir = Environment.GetEnvironmentVariable("DANMAKU_DIR") 
                       ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../server/data/danmaku"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Directory.Exists(_danmakuDir))
        {
            Directory.CreateDirectory(_danmakuDir);
        }

        _watcher = new FileSystemWatcher(_danmakuDir, "*.xml")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileChanged;
        _watcher.Changed += OnFileChanged;

        _logger.LogInformation($"Watching directory: {_danmakuDir}");

        // Initial scan
        await ScanDirectoryAsync(_danmakuDir);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce or wait for file to be ready?
        // Simple approach: wait a bit and try to process
        await Task.Delay(3000); 
        await ProcessFileAsync(e.FullPath);
    }

    private async Task ScanDirectoryAsync(string dir)
    {
        try
        {
            var files = Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                await ProcessFileAsync(file);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning directory");
        }
    }

    private async Task ProcessFileAsync(string filePath)
    {
        try
        {
            _logger.LogDebug($"Processing file: {filePath}");
            await _service.ProcessFileAsync(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing file {filePath}");
        }
    }
}
