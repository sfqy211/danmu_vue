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

    [Column("is_homepage_display")]
    public int IsHomepageDisplay { get; set; } = 0;

    [Column("group_name")]
    public string? GroupName { get; set; }

    [Column("playlist_url")]
    public string? PlaylistUrl { get; set; }

    [Column("remark")]
    public string? Remark { get; set; }

    [Column("created_at")]
    public string? CreatedAt { get; set; }
}
