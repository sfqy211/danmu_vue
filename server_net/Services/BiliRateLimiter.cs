namespace Danmu.Server.Services;

public class BiliRateLimiter
{
    private readonly TimeSpan _minInterval;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly ILogger<BiliRateLimiter> _logger;
    private DateTime _lastAccessUtc = DateTime.MinValue;

    public BiliRateLimiter(ILogger<BiliRateLimiter> logger, TimeSpan? minInterval = null)
    {
        _logger = logger;
        _minInterval = minInterval ?? TimeSpan.FromMilliseconds(500);
    }

    /// <summary>
    /// 等待直到满足最小间隔后放行。支持 CancellationToken。
    ///
    /// 设计意图：控制请求发起频率，而非控制并发数。
    /// WaitForAsync 返回后，请求才会真正发起；此时内部锁已经释放，
    /// 因此多个慢请求可能同时在途——这是预期行为，避免慢请求阻塞全局调度。
    /// </summary>
    public async Task WaitForAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastAccessUtc;
            if (elapsed < _minInterval)
            {
                var wait = _minInterval - elapsed;
                _logger.LogDebug("BiliRateLimiter waiting {DelayMs}ms before next request", (int)wait.TotalMilliseconds);
                await Task.Delay(wait, cancellationToken);
                now = DateTime.UtcNow;
            }

            _lastAccessUtc = now;
        }
        finally
        {
            _sync.Release();
        }
    }
}
