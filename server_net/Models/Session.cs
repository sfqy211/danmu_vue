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

    /// <summary>
    /// 采集节点标识。为空表示由中心节点本机录制；非空表示由外部采集端上报。
    /// </summary>
    [Column("node_id")]
    public string? NodeId { get; set; }

    /// <summary>
    /// 直播开始时间（B 站下发，毫秒）。与 Uid 一起构成"同一场直播"的判定键，
    /// 多台机器录制同一场时据此归并到同一个 Session。
    /// </summary>
    [Column("live_start_time")]
    public long? LiveStartTime { get; set; }

    /// <summary>
    /// 各采集节点贡献的时段与条数，JSON 数组：
    /// [{"nodeId":"node-a","from":..,"to":..,"count":123}, ...]
    /// 用于展示"这场由哪些节点拼接而成"。
    /// </summary>
    [Column("provider_nodes")]
    public string? ProviderNodes { get; set; }
}
