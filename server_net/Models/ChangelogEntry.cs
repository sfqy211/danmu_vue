using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Danmu.Server.Models;

[Table("changelog_entries")]
public class ChangelogEntry
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("version")]
    public string Version { get; set; } = string.Empty;

    [Column("date")]
    public DateTime Date { get; set; }

    [Required]
    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}