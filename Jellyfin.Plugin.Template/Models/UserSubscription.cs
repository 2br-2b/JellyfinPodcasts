using System;

namespace Jellyfin.Plugin.Template.Models;

/// <summary>
/// Represents a single user's subscription to a podcast feed.
/// </summary>
public class UserSubscription
{
    /// <summary>
    /// Gets or sets the Jellyfin user ID this subscription belongs to.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the ID of the subscribed <see cref="PodcastFeed"/>.
    /// </summary>
    public string FeedId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the user added this subscription.
    /// </summary>
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets a value indicating whether the user is currently subscribed.</summary>
    public bool IsSubscribed { get; set; } = true;

    /// <summary>Gets or sets when IsSubscribed was last changed.</summary>
    public DateTime? SubscriptionChanged { get; set; }

    /// <summary>Gets or sets when the podcast GUID changed for this subscription.</summary>
    public DateTime? GuidChanged { get; set; }

    /// <summary>Gets or sets the new GUID if this subscription was migrated to a new podcast GUID.</summary>
    public string? NewGuid { get; set; }

    /// <summary>Gets or sets the tombstone timestamp, or null if not deleted.</summary>
    public DateTime? Deleted { get; set; }

    /// <summary>
    /// Gets or sets the associated feed.
    /// </summary>
    public PodcastFeed Feed { get; set; } = null!;
}
