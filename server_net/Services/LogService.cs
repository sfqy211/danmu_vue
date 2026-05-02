namespace Danmu.Server.Services;

public class LogService
{
    private readonly string _logDirectory;
    private readonly ILogger<LogService> _logger;

    public LogService(ILogger<LogService> logger)
    {
        _logger = logger;
        // Serilog writes to "logs/" relative to the app base directory
        _logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }
    }

    /// <summary>
    /// List all log files, newest first.
    /// </summary>
    public List<LogFileEntry> GetLogFiles()
    {
        if (!Directory.Exists(_logDirectory))
            return [];

        return Directory.GetFiles(_logDirectory, "*.log")
            .Select(f =>
            {
                var info = new FileInfo(f);
                return new LogFileEntry
                {
                    Name = info.Name,
                    Size = info.Length,
                    LastModified = info.LastWriteTimeUtc
                };
            })
            .OrderByDescending(f => f.Name)
            .ToList();
    }

    /// <summary>
    /// Read the last N lines of a log file.
    /// </summary>
    public LogContentResult? GetLogContent(string? fileName, int tail = 500)
    {
        var filePath = ResolveFilePath(fileName);
        if (filePath == null || !File.Exists(filePath))
            return null;

        var info = new FileInfo(filePath);
        var lines = ReadLastLines(filePath, tail);

        return new LogContentResult
        {
            FileName = info.Name,
            Size = info.Length,
            Lines = lines,
            Content = string.Join('\n', lines)
        };
    }

    /// <summary>
    /// Get the full file path for a log file (for PhysicalFile download).
    /// Returns null if the file doesn't exist or path traversal is detected.
    /// </summary>
    public string? GetLogFilePath(string? fileName)
    {
        var filePath = ResolveFilePath(fileName);
        if (filePath == null || !File.Exists(filePath))
            return null;
        return filePath;
    }

    /// <summary>
    /// Get the log directory path (for SSE streaming).
    /// </summary>
    public string GetLogDirectory() => _logDirectory;

    private string? ResolveFilePath(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            // Default to the latest log file
            if (!Directory.Exists(_logDirectory))
                return null;
            var latest = Directory.GetFiles(_logDirectory, "*.log")
                .OrderByDescending(f => f)
                .FirstOrDefault();
            return latest;
        }

        // Prevent path traversal
        var fullPath = Path.GetFullPath(Path.Combine(_logDirectory, fileName));
        if (!fullPath.StartsWith(Path.GetFullPath(_logDirectory), StringComparison.OrdinalIgnoreCase))
            return null;

        return fullPath;
    }

    private static List<string> ReadLastLines(string filePath, int count)
    {
        var lines = new List<string>(count);
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            // Read all lines and take the last N
            // For very large files, we could optimize with seeking from the end,
            // but for typical log files this is fine
            var allLines = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                allLines.Add(line);
            }

            var start = Math.Max(0, allLines.Count - count);
            for (var i = start; i < allLines.Count; i++)
            {
                lines.Add(allLines[i]);
            }
        }
        catch (Exception ex)
        {
            lines.Add($"[Error reading log file: {ex.Message}]");
        }

        return lines;
    }
}

public class LogFileEntry
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
}

public class LogContentResult
{
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public List<string> Lines { get; set; } = [];
    public string Content { get; set; } = "";
}