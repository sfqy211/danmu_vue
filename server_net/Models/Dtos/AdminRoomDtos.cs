using System.Text.Json.Serialization;

namespace Danmu.Server.Models.Dtos;

public class AdminRoomListItemDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("room_id")]
    public long RoomId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("uid")]
    public string? Uid { get; set; }

    [JsonPropertyName("auto_record")]
    public int AutoRecord { get; set; }

    [JsonPropertyName("process_status")]
    public string ProcessStatus { get; set; } = "stopped";

    [JsonPropertyName("process_uptime")]
    public string ProcessUptime { get; set; } = "0s";

    [JsonPropertyName("process_start_time")]
    public long? ProcessStartTime { get; set; }

    [JsonPropertyName("live_status")]
    public int LiveStatus { get; set; }

    [JsonPropertyName("live_start_time")]
    public long? LiveStartTime { get; set; }

    [JsonPropertyName("pid")]
    public int? Pid { get; set; }

    [JsonPropertyName("account_uid")]
    public long? AccountUid { get; set; }

    [JsonPropertyName("remark")]
    public string? Remark { get; set; }

    [JsonPropertyName("playlist_url")]
    public string? PlaylistUrl { get; set; }
}
