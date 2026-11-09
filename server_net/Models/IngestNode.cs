using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Danmu.Server.Models;

/// <summary>
/// 采集节点凭据。本期不开放自助注册，由管理员在后台手工创建并分发 Token。
/// Token 明文只在创建时返回一次，库中仅保存 SHA256 哈希。
/// </summary>
[Table("ingest_nodes")]
public class IngestNode
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>节点标识（客户端配置里填的 nodeId），全局唯一。</summary>
    [Column("node_id")]
    public string NodeId { get; set; } = "";

    /// <summary>展示用名称，如"家里的台式机"。</summary>
    [Column("name")]
    public string? Name { get; set; }

    /// <summary>Token 的 SHA256 十六进制哈希。</summary>
    [Column("token_hash")]
    public string TokenHash { get; set; } = "";

    [Column("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>最近一次心跳时间（Unix 毫秒），用于判断节点是否在线。</summary>
    [Column("last_seen_at")]
    public long? LastSeenAt { get; set; }

    /// <summary>累计上报条数，便于评估贡献量。</summary>
    [Column("total_events")]
    public long TotalEvents { get; set; }

    [Column("created_at")]
    public string? CreatedAt { get; set; }
}
