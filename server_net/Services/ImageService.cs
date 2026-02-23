using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Danmu.Server.Services;

public class ImageService
{
    private readonly ILogger<ImageService> _logger;

    public ImageService(ILogger<ImageService> logger)
    {
        _logger = logger;
    }

    public async Task ResizeAndSaveWebpAsync(byte[] imageBytes, string outputPath, int width, int height)
    {
        try
        {
            using var image = Image.Load(imageBytes);
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(width, height),
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center
            }));
            await image.SaveAsWebpAsync(outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to resize and save image to {outputPath}");
        }
    }

    public async Task SavePngAsync(byte[] imageBytes, string outputPath)
    {
        try
        {
            // Node version re-encodes to PNG, ensuring format consistency.
            // But just saving buffer might be enough if we trust the source.
            // However, to be safe and consistent with Node version (which uses sharp().png()), we re-encode.
            using var image = Image.Load(imageBytes);
            await image.SaveAsPngAsync(outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to save image to {outputPath}");
        }
    }
}
