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
var dbPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../server/data/danmaku_data.db"));
builder.Services.AddDbContext<DanmuContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Services
builder.Services.AddSingleton<ProcessManager>();
builder.Services.AddSingleton<DanmakuService>();
builder.Services.AddHostedService<DanmakuProcessor>();
builder.Services.AddHttpClient<BilibiliService>();
builder.Services.AddSingleton<ImageService>();
builder.Services.AddHostedService<AvatarScheduler>();
builder.Services.AddHostedService<CoverScheduler>();

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
var staticPath = Directory.Exists(distPath) ? distPath : publicPath;

if (Directory.Exists(staticPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(staticPath),
        RequestPath = ""
    });
}

var dataRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../server/data"));
var bgPath = Path.GetFullPath(Path.Combine(dataRoot, "vup-bg"));
var avatarPath = Path.GetFullPath(Path.Combine(dataRoot, "vup-avatar"));
var coverPath = Path.GetFullPath(Path.Combine(dataRoot, "vup-cover"));

if (!Directory.Exists(bgPath)) Directory.CreateDirectory(bgPath);
if (!Directory.Exists(avatarPath)) Directory.CreateDirectory(avatarPath);
if (!Directory.Exists(coverPath)) Directory.CreateDirectory(coverPath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(bgPath),
    RequestPath = "/vup-bg"
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(avatarPath),
    RequestPath = "/vup-avatar"
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(coverPath),
    RequestPath = "/vup-cover"
});


app.MapControllers();

// SPA Fallback
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(staticPath)
});

// Init DB
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DanmuContext>();
    db.Database.EnsureCreated();
}

// Restore recorders
var pm = app.Services.GetRequiredService<ProcessManager>();
_ = pm.RestoreRecordersAsync(); // Fire and forget but better to await if possible, but Run() is blocking.

app.Run();
