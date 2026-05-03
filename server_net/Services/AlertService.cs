using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Danmu.Server.Services;

public class AlertService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AlertService> _logger;
    private readonly string? _webhookUrl;

    public AlertService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<AlertService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _webhookUrl = configuration["Alert:WebhookUrl"];
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_webhookUrl);

    public async Task SendHealthCheckAlertAsync(HealthCheckReport report)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            var payload = new
            {
                type = "health-check",
                checkedAt = report.CheckedAt,
                recorderCount = report.RecorderCount,
                healthyCount = report.HealthyCount,
                staleHeartbeats = report.StaleHeartbeats,
                driftIssues = report.DriftIssues,
                summary = $"Health check alert: stale={report.StaleHeartbeats.Count}, drift={report.DriftIssues.Count}, recorders={report.RecorderCount}"
            };

            var client = _httpClientFactory.CreateClient();
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(_webhookUrl, content);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send health check alert webhook.");
        }
    }
}
