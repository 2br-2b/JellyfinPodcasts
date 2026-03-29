using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

    // ── OpenPodcastAPI async methods ─────────────────────────────────────────

    /// <summary>Returns a paged list of subscriptions for the user, optionally filtered by a since timestamp.</summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="since">Optional lower bound for returned changes.</param>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="perPage">The number of items per page.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The page of subscriptions plus the total count.</returns>
    Task<(IReadOnlyList<UserSubscription> Items, int Total)> GetSubscriptionsPagedAsync(
        Guid userId, DateTime? since, int page, int perPage, CancellationToken ct = default);

    /// <summary>Gets a single subscription by user and feed GUID.</summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="feedId">The subscription GUID.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The matching subscription, if one exists.</returns>
    Task<UserSubscription?> GetUserSubscriptionByGuidAsync(
        Guid userId, string feedId, CancellationToken ct = default);

    /// <summary>Looks up a feed record by its primary key GUID.</summary>
    /// <param name="feedId">The feed GUID.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The feed when found; otherwise <c>null</c>.</returns>
    Task<PodcastFeed?> GetFeedByIdAsync(string feedId, CancellationToken ct = default);

    /// <summary>Looks up a feed record by its RSS URL.</summary>
    /// <param name="feedUrl">The RSS feed URL.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The feed when found; otherwise <c>null</c>.</returns>
    Task<PodcastFeed?> GetFeedByUrlAsync(string feedUrl, CancellationToken ct = default);

    /// <summary>Creates or updates a subscription, upserting the feed record as needed.</summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="feed">The feed metadata to persist.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The created or updated subscription.</returns>
    Task<UserSubscription> UpsertSubscriptionAsync(
        Guid userId, PodcastFeed feed, CancellationToken ct = default);

    /// <summary>Applies a partial update to a subscription (feed URL, GUID, or subscribed status).</summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="feedId">The subscription GUID being updated.</param>
    /// <param name="newFeedUrl">The new RSS feed URL, if one was supplied.</param>
    /// <param name="newGuid">The new subscription GUID, if one was supplied.</param>
    /// <param name="isSubscribed">The new subscribed state, if one was supplied.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The updated subscription when found; otherwise <c>null</c>.</returns>
    Task<UserSubscription?> PatchSubscriptionAsync(
        Guid userId,
        string feedId,
        string? newFeedUrl,
        string? newGuid,
        bool? isSubscribed,
        CancellationToken ct = default);

    /// <summary>Records an async deletion request and soft-deletes the subscription.</summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="feedId">The subscription GUID being deleted.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The created deletion request.</returns>
    Task<DeletionRequest> RequestDeletionAsync(
        Guid userId, string feedId, CancellationToken ct = default);

    /// <summary>Retrieves the status of a prior deletion request.</summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="deletionId">The deletion request identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The deletion request when found; otherwise <c>null</c>.</returns>
    Task<DeletionRequest?> GetDeletionRequestAsync(
        Guid userId, int deletionId, CancellationToken ct = default);

    /// <summary>Resolves the latest subscription in a guid migration chain for the user.</summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="feedId">The starting subscription GUID.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The latest subscription in the chain when found; otherwise <c>null</c>.</returns>
    Task<UserSubscription?> GetLatestSubscriptionAsync(
        Guid userId, string feedId, CancellationToken ct = default);

    /// <summary>
    /// Updates the metadata of a feed record from a trusted server-side source (e.g. a background RSS fetch).
    /// Unlike user-initiated upserts, this path is allowed to overwrite shared feed metadata.
    /// </summary>
    /// <param name="feedId">The ID of the feed record to update.</param>
    /// <param name="metadata">The fresh metadata parsed from the RSS feed.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the feed metadata update finishes.</returns>
    Task UpdateFeedMetadataAsync(string feedId, PodcastFeed metadata, CancellationToken ct = default);
}
