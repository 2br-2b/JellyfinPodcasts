using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Template.Models;

namespace Jellyfin.Plugin.Template.Services;

/// <summary>
/// Stores and retrieves per-user podcast feed subscriptions.
/// </summary>
public interface ISubscriptionStore
{
    /// <summary>
    /// Gets all known podcast feeds regardless of subscriber.
    /// </summary>
    /// <returns>All feeds.</returns>
    IReadOnlyList<PodcastFeed> GetAllFeeds();

    /// <summary>
    /// Gets the feeds a specific user is subscribed to.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <returns>Feeds the user subscribes to.</returns>
    IReadOnlyList<PodcastFeed> GetFeedsForUser(Guid userId);

    /// <summary>
    /// Subscribes a user to a feed, adding the feed record if it does not already exist.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="feed">The feed to subscribe to.</param>
    void Subscribe(Guid userId, PodcastFeed feed);

    /// <summary>
    /// Removes a user's subscription. The feed record is pruned when no subscribers remain.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="feedId">The ID of the feed to unsubscribe from.</param>
    void Unsubscribe(Guid userId, string feedId);
}
