using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jellyfin.Plugin.Podcasts.Model;

[Table("episodes")]
public class Episode
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("podcast_id")]
    public Guid PodcastId { get; set; }

    [Required]
    [Column("guid")]
    public string Guid { get; set; } = string.Empty;

    [Column("title")]
    public string? Title { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("published_at")]
    public DateTime? PublishedAt { get; set; }

    [Column("duration_seconds")]
    public int? DurationSeconds { get; set; }

    [Required]
    [Column("enclosure_url")]
    public string EnclosureUrl { get; set; } = string.Empty;

    [Column("enclosure_mime")]
    public string? EnclosureMime { get; set; }

    [Column("enclosure_length")]
    public long? EnclosureLength { get; set; }

    [Column("image_url")]
    public string? ImageUrl { get; set; }

    [Column("is_cached")]
    public bool IsCached { get; set; }

    [Column("cached_at")]
    public DateTime? CachedAt { get; set; }

    [Column("cached_size_bytes")]
    public long? CachedSizeBytes { get; set; }

    [Column("is_pinned")]
    public bool IsPinned { get; set; }

    [Column("local_path")]
    public string? LocalPath { get; set; }

    [Column("jellyfin_item_id")]
    public string? JellyfinItemId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey("PodcastId")]
    public Podcast? Podcast { get; set; }

    public ICollection<UserEpisodeState> UserStates { get; set; } = new List<UserEpisodeState>();
}
