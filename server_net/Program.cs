using Danmu.Server.Data;
using Danmu.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Load .env
var root = Directory.GetCurrentDirectory();
var envPathLocal = Path.GetFullPath(Path.Combine(root, "../server/.env"));
var envPathRoot = Path.GetFullPath(Path.Combine(root, "../.env"));

if (File.Exists(envPathLocal))
{
    foreach (var line in File.ReadAllLines(envPathLocal))
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
else if (File.Exists(envPathRoot))
{
    foreach (var line in File.ReadAllLines(envPathRoot))
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
// Start embedded Redis (Garnet) before connecting
builder.Services.AddHostedService<EmbeddedRedisService>();
builder.Services.AddSingleton<RedisService>();
builder.Services.AddHostedService<DanmakuProcessor>();
builder.Services.AddHttpClient<BilibiliService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<ImageService>();
builder.Services.AddSingleton<CosService>();
builder.Services.AddHostedService<AvatarScheduler>();
builder.Services.AddHostedService<CoverScheduler>();
builder.Services.AddHostedService<VupInfoScheduler>();

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
