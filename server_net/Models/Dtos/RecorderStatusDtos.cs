using System.Text.Json.Serialization;

namespace Danmu.Server.Models.Dtos;

public class RecorderStatusResponseDto
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "success";

    [JsonPropertyName("processes")]
    public List<RecorderProcessDto> Processes { get; set; } = [];
}

public class RecorderProcessDto
{
    [JsonPropertyName("uid")]
    public string Uid { get; set; } = "";

    [JsonPropertyName("room_id")]
    public long RoomId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "stopped";

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("uptime")]
    public string Uptime { get; set; } = "0s";

    [JsonPropertyName("start_time")]
    public long? StartTime { get; set; }

    [JsonPropertyName("live_status")]
    public int LiveStatus { get; set; }

    [JsonPropertyName("live_start_time")]
    public long? LiveStartTime { get; set; }

    [JsonPropertyName("account_uid")]
    public long? AccountUid { get; set; }
}
