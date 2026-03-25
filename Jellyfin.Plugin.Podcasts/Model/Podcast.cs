using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jellyfin.Plugin.Podcasts.Model;

[Table("podcasts")]
public class Podcast
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [Column("feed_url")]
    public string FeedUrl { get; set; } = string.Empty;

    [Column("guid")]
    public string? Guid { get; set; }

    [Column("title")]
    public string? Title { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("image_url")]
    public string? ImageUrl { get; set; }

    [Column("author")]
    public string? Author { get; set; }

    [Column("language")]
    public string? Language { get; set; }

    [Column("last_fetched_at")]
    public DateTime? LastFetchedAt { get; set; }

    [Column("fetch_error")]
    public string? FetchError { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Episode> Episodes { get; set; } = new List<Episode>();
    public ICollection<UserSubscription> Subscriptions { get; set; } = new List<UserSubscription>();
}
