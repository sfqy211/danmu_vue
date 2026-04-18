using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Danmu.Server.Models;

[Table("sessions")]
public class Session
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("room_id")]
    public string? RoomId { get; set; }

    [Column("uid")]
    public string? Uid { get; set; }

    [Column("title")]
    public string? Title { get; set; }

    [Column("user_name")]
    public string? UserName { get; set; }

    [Column("start_time")]
    public long? StartTime { get; set; }

    [Column("end_time")]
    public long? EndTime { get; set; }

    [Column("file_path")]
    public string? FilePath { get; set; }

    [Column("summary_json")]
    public string? SummaryJson { get; set; }

    [Column("gift_summary_json")]
    public string? GiftSummaryJson { get; set; }

    [Column("created_at")]
    public string? CreatedAt { get; set; }
}
