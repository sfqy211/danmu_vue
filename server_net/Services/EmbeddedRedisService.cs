using Garnet;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Danmu.Server.Services;

public class EmbeddedRedisService : IHostedService
{
    private readonly ILogger<EmbeddedRedisService> _logger;
    private GarnetServer? _server;

    public EmbeddedRedisService(ILogger<EmbeddedRedisService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _server = new GarnetServer(new string[] { "--port", "6379", "--bind", "127.0.0.1", "--lua" });
            _server.Start();
            _logger.LogInformation("Embedded Garnet (Redis) server started on 127.0.0.1:6379");
        }
        catch (Exception ex)
        {
            // If port is already in use, likely an external Redis is running.
            // We just log this and let the application connect to the existing server.
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
