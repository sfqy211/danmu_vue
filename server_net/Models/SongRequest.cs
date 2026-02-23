using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Danmu.Server.Models;

[Table("song_requests")]
public class SongRequest
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("session_id")]
    public int? SessionId { get; set; }

    [Column("room_id")]
    public string? RoomId { get; set; }

    [Column("user_name")]
    public string? UserName { get; set; }

    [Column("uid")]
    public string? Uid { get; set; }

    [Column("song_name")]
    public string? SongName { get; set; }

    [Column("singer")]
    public string? Singer { get; set; }

    [Column("created_at")]
    public long? CreatedAt { get; set; }
}
