using Microsoft.Extensions.Hosting;

namespace Danmu.Server.Services;

public class BiliAccountBootstrapService : IHostedService
{
    private readonly RedisReadiness _redisReadiness;
    private readonly BiliAccountService _biliAccountService;
    private readonly ILogger<BiliAccountBootstrapService> _logger;

    public BiliAccountBootstrapService(
        RedisReadiness redisReadiness,
        BiliAccountService biliAccountService,
        ILogger<BiliAccountBootstrapService> logger)
    {
        _redisReadiness = redisReadiness;
        _biliAccountService = biliAccountService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _redisReadiness.WaitUntilReadyAsync(cancellationToken);
            await _biliAccountService.PreloadCacheAsync();
            _biliAccountService.StartAutoRefreshLoop();
            _logger.LogInformation("BiliAccount bootstrap completed after Redis became ready.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bootstrap BiliAccountService after Redis readiness.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _biliAccountService.StopAutoRefreshLoop();
        return Task.CompletedTask;
    }
}
