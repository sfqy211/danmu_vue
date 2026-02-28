using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Danmu.Server.Models;

[Table("rooms")]
public class Room
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("room_id")]
    public long RoomId { get; set; }

    [Column("uid")]
    public string? Uid { get; set; }

    [Column("name")]
    public string? Name { get; set; }

    [Column("is_active")]
    public int IsActive { get; set; } = 1;

    [Column("auto_record")]
    public int AutoRecord { get; set; } = 1;

    [Column("group_name")]
    public string? GroupName { get; set; }

    [Column("playlist_url")]
    public string? PlaylistUrl { get; set; }

    [Column("sort_order")]
    public int SortOrder { get; set; } = 0;

    [Column("remark")]
    public string? Remark { get; set; }

    [Column("followers")]
    public int Followers { get; set; }

    [Column("guard_num")]
    public int GuardNum { get; set; }

    [Column("video_count")]
    public int VideoCount { get; set; }

    [Column("last_live_time")]
    public long LastLiveTime { get; set; }

    [Column("updated_at")]
    public string? UpdatedAt { get; set; }
}
