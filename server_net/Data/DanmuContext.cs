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
    public DbSet<IngestNode> IngestNodes { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Room>()
            .HasIndex(r => r.Uid)
            .IsUnique();

        modelBuilder.Entity<IngestNode>()
            .HasIndex(n => n.NodeId)
            .IsUnique();

        // 同一主播同一开播时间视为同一场直播：多节点上报时据此归并。
        // 不设唯一约束，因为历史数据与单机录制可能产生重复行。
        modelBuilder.Entity<Session>()
            .HasIndex(s => new { s.Uid, s.LiveStartTime });
    }
}
