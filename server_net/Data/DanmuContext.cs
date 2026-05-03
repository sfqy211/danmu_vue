using Danmu.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Data;

public class DanmuContext : DbContext
{
    public DanmuContext(DbContextOptions<DanmuContext> options) : base(options) { }

    public DbSet<Session> Sessions { get; set; } = null!;
    public DbSet<SongRequest> SongRequests { get; set; } = null!;
    public DbSet<Room> Rooms { get; set; } = null!;
    public DbSet<BiliAccount> BiliAccounts { get; set; } = null!;
    public DbSet<ChangelogEntry> ChangelogEntries { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Room>()
            .HasIndex(r => r.Uid)
            .IsUnique();
    }
}
