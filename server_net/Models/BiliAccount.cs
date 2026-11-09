using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Danmu.Server.Models;

[Table("bili_accounts")]
public class BiliAccount
{
    [Key]
    [Column("uid")]
    public long Uid { get; set; }

    [Column("name")]
    public string? Name { get; set; }

    [Column("face")]
    public string? Face { get; set; }

    [Column("access_token")]
    public string? AccessToken { get; set; }

    [Column("refresh_token")]
    public string? RefreshToken { get; set; }

    /// <summary>
    /// JSON object of cookie key-value pairs, e.g. {"SESSDATA":"...","bili_jct":"..."}
    /// </summary>
    [Column("cookie_json")]
    public string? CookieJson { get; set; }

    [Column("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// 是否为「全局默认账号」。同一时刻只有一个，用于非房间绑定的 API 调用
    /// （抓取房间信息、账号相关接口等）。与 <see cref="IsEnabled"/> 语义独立。
    /// </summary>
    [Column("is_active")]
    public bool IsActive { get; set; }

    /// <summary>
    /// 是否启用。停用的账号不参与房间录制分配，也不会被选为全局默认账号。
    /// 可多个账号同时启用。
    /// </summary>
    [Column("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
