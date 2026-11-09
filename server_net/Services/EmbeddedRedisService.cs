using Garnet;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Danmu.Server.Services;

public class EmbeddedRedisService : IHostedService
{
    private readonly ILogger<EmbeddedRedisService> _logger;
    private readonly RedisReadiness _redisReadiness;
    private GarnetServer? _server;

    public EmbeddedRedisService(ILogger<EmbeddedRedisService> logger, RedisReadiness redisReadiness)
    {
        _logger = logger;
        _redisReadiness = redisReadiness;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 多实例部署（蓝绿/水平扩展）时必须关闭嵌入式 Redis：
        // 每个进程各起一份会导致录制状态、会话锁、心跳 key 全部隔离。
        // 设 EMBEDDED_REDIS_ENABLED=false 并配置 REDIS_CONNECTION 指向共享实例。
        var enabledEnv = Environment.GetEnvironmentVariable("EMBEDDED_REDIS_ENABLED");
        if (string.Equals(enabledEnv, "false", StringComparison.OrdinalIgnoreCase))
        {
            _redisReadiness.MarkReady();
            _logger.LogInformation(
                "Embedded Garnet disabled (EMBEDDED_REDIS_ENABLED=false); using external Redis at {Conn}",
                Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379");
            return Task.CompletedTask;
        }

        try
        {
            _server = new GarnetServer(new string[] { "--port", "6379", "--bind", "127.0.0.1", "--lua" });
            _server.Start();
            _redisReadiness.MarkReady();
            _logger.LogInformation("Embedded Garnet (Redis) server started on 127.0.0.1:6379");
        }
        catch (Exception ex)
        {
            // If port is already in use, likely an external Redis is running.
            // We just log this and let the application connect to the existing server.
            _redisReadiness.MarkReady();
            _logger.LogWarning(ex, "Failed to start embedded Garnet server. Is port 6379 already in use? Will attempt to use existing Redis instance.");
            _server = null;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_server != null)
        {
            _logger.LogInformation("Stopping embedded Garnet server...");
            _server.Dispose();
            _server = null;
        }
        return Task.CompletedTask;
    }
}
