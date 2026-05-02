using Danmu.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Danmu.Server.Data;

public class DanmuContext : DbContext
{
    public DanmuContext(DbContextOptions<DanmuContext> options) : base(options) { }

    public required DbSet<Session> Sessions { get; set; }
    public required DbSet<SongRequest> SongRequests { get; set; }
    public required DbSet<Room> Rooms { get; set; }
    public required DbSet<BiliAccount> BiliAccounts { get; set; }
    public required DbSet<ChangelogEntry> ChangelogEntries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Room>()
            .HasIndex(r => r.Uid)
            .IsUnique();
    }
}
