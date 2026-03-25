using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jellyfin.Plugin.Podcasts.Model;

[Table("user_episode_state")]
public class UserEpisodeState
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Column("episode_id")]
    public Guid EpisodeId { get; set; }

    [Column("position_seconds")]
    public int PositionSeconds { get; set; }

    [Column("is_played")]
    public bool IsPlayed { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey("EpisodeId")]
    public Episode? Episode { get; set; }
}
