using System.IO;
using Microsoft.Extensions.Logging;

namespace Danmu.Server.Utils;

public static class FileUtils
{
    /// <summary>
    /// Moves a file with cross-device fallback (Copy+Delete).
    /// Use when source and destination may reside on different volumes.
    /// </summary>
    public static void MoveFileWithFallback(string source, string dest, ILogger logger)
    {
        try
        {
            File.Move(source, dest, overwrite: true);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "File.Move failed (likely cross-device), falling back to Copy+Delete from {Source} to {Dest}", source, dest);
            File.Copy(source, dest, overwrite: true);
            File.Delete(source);
        }
    }
}
