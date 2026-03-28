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
}
