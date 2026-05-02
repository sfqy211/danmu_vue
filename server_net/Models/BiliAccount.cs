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

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
