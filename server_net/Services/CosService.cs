using COSXML;
using COSXML.Auth;
using COSXML.Model.Object;

namespace Danmu.Server.Services;

public class CosService
{
    private readonly CosXml? _cosXml;
    private readonly string _bucket;
    private readonly ILogger<CosService> _logger;

    public CosService(IConfiguration configuration, ILogger<CosService> logger)
    {
        _logger = logger;
        _bucket = configuration["COS_BUCKET"] ?? "";
        var region = configuration["COS_REGION"] ?? "";
        var secretId = configuration["TENCENT_SECRET_ID"] ?? "";
        var secretKey = configuration["TENCENT_SECRET_KEY"] ?? "";

        if (string.IsNullOrEmpty(_bucket) || string.IsNullOrEmpty(region)
            || string.IsNullOrEmpty(secretId) || string.IsNullOrEmpty(secretKey))
        {
            _logger.LogWarning("COS configuration is incomplete (COS_BUCKET/COS_REGION/TENCENT_SECRET_ID/TENCENT_SECRET_KEY). COS upload will be disabled.");
            return;
        }

        var config = new CosXmlConfig.Builder()
            .SetRegion(region)
            .Build();

        var credentialProvider = new DefaultQCloudCredentialProvider(
            secretId, secretKey, 600);

        _cosXml = new CosXmlServer(config, credentialProvider);
        _logger.LogInformation("COS service initialized: bucket={Bucket}, region={Region}", _bucket, region);
    }

    /// <summary>
    /// 上传文件到 COS。
    /// </summary>
    /// <param name="localFilePath">本地文件绝对路径</param>
    /// <param name="remotePath">COS 远程路径，如 "vup-avatar/12345.webp"</param>
    /// <param name="contentType">MIME 类型，如 "image/webp"</param>
    public async Task UploadAsync(string localFilePath, string remotePath, string contentType)
    {
        if (_cosXml == null)
        {
            _logger.LogDebug("COS not configured, skipping upload of {RemotePath}", remotePath);
            return;
        }

        if (!File.Exists(localFilePath))
        {
            _logger.LogWarning("Local file not found, skipping COS upload: {FilePath}", localFilePath);
            return;
        }

        try
        {
            var result = await Task.Run(() =>
            {
                var request = new PutObjectRequest(_bucket, remotePath, localFilePath);
                request.SetRequestHeader("Content-Type", contentType);
                return _cosXml.PutObject(request);
            });

            if (result.httpCode >= 200 && result.httpCode < 300)
            {
                _logger.LogInformation("Uploaded {RemotePath} to COS (HTTP {Code})", remotePath, result.httpCode);
            }
            else
            {
                _logger.LogWarning("Failed to upload {RemotePath} to COS: HTTP {Code}", remotePath, result.httpCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading {RemotePath} to COS", remotePath);
        }
    }
}
