using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jellyfin.Plugin.Podcasts.Model;

[Table("user_subscriptions")]
public class UserSubscription
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Column("podcast_id")]
    public Guid PodcastId { get; set; }

    [Column("is_subscribed")]
    public bool IsSubscribed { get; set; } = true;

    [Column("subscription_changed_at")]
    public DateTime SubscriptionChangedAt { get; set; } = DateTime.UtcNow;

    [Column("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    // Navigation
    [ForeignKey("PodcastId")]
    public Podcast? Podcast { get; set; }
}
