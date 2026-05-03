using Danmu.Server.Data;
using Danmu.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Serilog;

// Load .env BEFORE CreateBuilder so IConfiguration can pick up the values
var root = Directory.GetCurrentDirectory();
var envPathLocal = Path.GetFullPath(Path.Combine(root, "../server/.env"));
var envPathRoot = Path.GetFullPath(Path.Combine(root, "../.env"));

void LoadEnvFile(string path)
{
    foreach (var line in File.ReadAllLines(path))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
        var parts = line.Split('=', 2);
        if (parts.Length == 2)
        {
            var key = parts[0].Trim();
            var val = parts[1].Trim();
            if ((val.StartsWith("\"") && val.EndsWith("\"")) || (val.StartsWith("'") && val.EndsWith("'")))
            {
                val = val.Substring(1, val.Length - 2);
            }
            Environment.SetEnvironmentVariable(key, val);
        }
    }
}

if (File.Exists(envPathLocal))
    LoadEnvFile(envPathLocal);
else if (File.Exists(envPathRoot))
    LoadEnvFile(envPathRoot);

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog with rolling file sink
var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logDirectory);
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logDirectory, "app-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 20 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        shared: true,
        flushToDiskInterval: TimeSpan.FromSeconds(1),
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}")
    .CreateLogger();
builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database
var connectionString = builder.Configuration["MYSQL_CONNECTION_STRING"] 
                    ?? builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("Connection string 'MYSQL_CONNECTION_STRING' not found.");
}

builder.Services.AddDbContext<DanmuContext>(options =>
{
    var serverVersion = new MySqlServerVersion(new Version(8, 4, 8));
    options.UseMySql(connectionString, serverVersion, mySqlOptions =>
    {
        mySqlOptions.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null);
    });
});

// Services
builder.Services.AddSingleton<ProcessManager>();
builder.Services.AddSingleton<DanmakuService>();
builder.Services.AddSingleton<RedisReadiness>();
// Start embedded Redis (Garnet) before connecting
builder.Services.AddHostedService<EmbeddedRedisService>();
builder.Services.AddSingleton<RedisService>();
builder.Services.AddHostedService<DanmakuProcessor>();
builder.Services.AddHttpClient<BilibiliService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddHttpClient();
builder.Services.AddSingleton<BiliRateLimiter>();
builder.Services.AddSingleton<BiliAccountService>();
builder.Services.AddSingleton<ImageService>();
builder.Services.AddSingleton<CosService>();
builder.Services.AddHostedService<AvatarScheduler>();
builder.Services.AddHostedService<CoverScheduler>();
builder.Services.AddHostedService<VupInfoScheduler>();
builder.Services.AddSingleton<LiveStatusService>();
builder.Services.AddSingleton<ChangelogService>();
builder.Services.AddSingleton<LogService>();
builder.Services.AddSingleton<AlertService>();
builder.Services.AddHostedService<BiliAccountBootstrapService>();
builder.Services.AddHostedService<LiveStatusService>(sp => sp.GetRequiredService<LiveStatusService>());
builder.Services.AddHostedService<HealthCheckService>();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        builder =>
        {
            builder.AllowAnyOrigin()
                   .AllowAnyMethod()
                   .AllowAnyHeader();
        });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");
app.UseAuthorization();

// Serve Static Files
var distPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../dist"));
var publicPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../public"));
var localPublicPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "public"));
var staticPath = Directory.Exists(distPath)
    ? distPath
    : Directory.Exists(publicPath)
        ? publicPath
        : Directory.Exists(localPublicPath)
            ? localPublicPath
            : null;

if (staticPath != null)
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(staticPath),
        RequestPath = ""
    });
}


app.MapControllers();

// SSE endpoint for real-time log streaming (with token auth via query string)
app.MapGet("/api/admin/logs/stream", async (HttpContext context, LogService logService, CancellationToken ct) =>
{
    // Auth: EventSource cannot send custom headers, so validate token from query string
    var token = context.Request.Query["token"].FirstOrDefault();
    var adminToken = Environment.GetEnvironmentVariable("ADMIN_TOKEN");
    if (string.IsNullOrEmpty(adminToken) || token != adminToken)
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("Unauthorized", ct);
        return;
    }

    var file = context.Request.Query["file"].ToString();
    var logDir = logService.GetLogDirectory();

    string filePath;
    if (string.IsNullOrEmpty(file))
    {
        var latest = Directory.GetFiles(logDir, "*.log").OrderByDescending(f => f).FirstOrDefault();
        if (latest == null)
        {
            context.Response.StatusCode = 404;
            return;
        }
        filePath = latest;
    }
    else
    {
        filePath = Path.GetFullPath(Path.Combine(logDir, file));
        if (!filePath.StartsWith(Path.GetFullPath(logDir), StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 400;
            return;
        }
    }

    if (!File.Exists(filePath))
    {
        context.Response.StatusCode = 404;
        return;
    }

    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";

    // Send last 200 lines immediately
    using var initialStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var initialReader = new StreamReader(initialStream);
    var allLines = new List<string>();
    while (await initialReader.ReadLineAsync(ct) is { } line)
    {
        allLines.Add(line);
    }
    var startIdx = Math.Max(0, allLines.Count - 200);
    for (var i = startIdx; i < allLines.Count; i++)
    {
        await context.Response.WriteAsync($"data: {allLines[i]}\n\n", ct);
    }
    await context.Response.Body.FlushAsync(ct);

    // Stream new lines
    var lastPosition = initialStream.Position;
    initialReader.Dispose();
    initialStream.Dispose();

    while (!ct.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(500, ct);
            if (!File.Exists(filePath)) continue;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < lastPosition)
            {
                // File was rotated, reset
                lastPosition = 0;
            }
            fs.Seek(lastPosition, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            string? newLine;
            while ((newLine = await reader.ReadLineAsync(ct)) != null)
            {
                await context.Response.WriteAsync($"data: {newLine}\n\n", ct);
            }
            lastPosition = fs.Position;
            await context.Response.Body.FlushAsync(ct);
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch
        {
            // Ignore read errors, retry
        }
    }
});

// SPA Fallback
if (staticPath != null)
{
    app.MapFallbackToFile("index.html", new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(staticPath)
    });
}

// Init DB
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<DanmuContext>();
    var logger = services.GetRequiredService<ILogger<Program>>();
    try 
    {
        DbInitializer.InitializeAsync(context, logger).Wait();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred creating the DB.");
    }
}

// Restore recorders
var pm = app.Services.GetRequiredService<ProcessManager>();
_ = pm.RestoreRecordersAsync(); // Fire and forget but better to await if possible, but Run() is blocking.

app.Run();
