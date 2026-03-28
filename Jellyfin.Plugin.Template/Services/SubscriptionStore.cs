using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.Template.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Template.Services;

/// <summary>
/// Persists podcast feeds and per-user subscriptions to JSON files in the plugin data folder.
/// </summary>
public class SubscriptionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<SubscriptionStore> _logger;
    private readonly string _feedsFilePath;
    private readonly string _subscriptionsFilePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubscriptionStore"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public SubscriptionStore(ILogger<SubscriptionStore> logger)
    {
        _logger = logger;

        var dataDir = Plugin.Instance?.DataFolderPath
            ?? throw new InvalidOperationException("Plugin instance is not initialized.");

        _feedsFilePath = Path.Combine(dataDir, "feeds.json");
        _subscriptionsFilePath = Path.Combine(dataDir, "subscriptions.json");
    }

    /// <summary>
    /// Gets all known podcast feeds.
    /// </summary>
    /// <returns>All feeds.</returns>
    public IReadOnlyList<PodcastFeed> GetAllFeeds()
    {
        return Load<List<PodcastFeed>>(_feedsFilePath) ?? [];
    }

    /// <summary>
    /// Gets the feeds a specific user is subscribed to.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <returns>Feeds the user subscribes to.</returns>
    public IReadOnlyList<PodcastFeed> GetFeedsForUser(Guid userId)
    {
        var allFeeds = GetAllFeeds();
        var subscriptions = GetAllSubscriptions();
        var subscribedIds = subscriptions
            .Where(s => s.UserId == userId)
            .Select(s => s.FeedId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return allFeeds.Where(f => subscribedIds.Contains(f.Id)).ToList();
    }

    /// <summary>
    /// Adds a feed and subscribes the given user to it. If the feed already exists, only the subscription is added.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="feed">The feed to add.</param>
    public void Subscribe(Guid userId, PodcastFeed feed)
    {
        var feeds = LoadFeedsMutable();
        if (!feeds.Any(f => string.Equals(f.Id, feed.Id, StringComparison.OrdinalIgnoreCase)))
        {
            feeds.Add(feed);
            Save(_feedsFilePath, feeds);
        }

        var subscriptions = GetAllSubscriptions();
        bool alreadySubscribed = subscriptions.Any(s =>
            s.UserId == userId &&
            string.Equals(s.FeedId, feed.Id, StringComparison.OrdinalIgnoreCase));

        if (!alreadySubscribed)
        {
            subscriptions.Add(new UserSubscription { UserId = userId, FeedId = feed.Id });
            Save(_subscriptionsFilePath, subscriptions);
        }
    }

    /// <summary>
    /// Removes a user's subscription to a feed. The feed record is also removed if no users remain subscribed.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="feedId">The feed ID to unsubscribe from.</param>
    public void Unsubscribe(Guid userId, string feedId)
    {
        var subscriptions = GetAllSubscriptions();
        int removed = subscriptions.RemoveAll(s =>
            s.UserId == userId &&
            string.Equals(s.FeedId, feedId, StringComparison.OrdinalIgnoreCase));

        if (removed > 0)
        {
            Save(_subscriptionsFilePath, subscriptions);

            bool anyoneStillSubscribed = subscriptions.Any(s =>
                string.Equals(s.FeedId, feedId, StringComparison.OrdinalIgnoreCase));

            if (!anyoneStillSubscribed)
            {
                var feeds = LoadFeedsMutable();
                feeds.RemoveAll(f => string.Equals(f.Id, feedId, StringComparison.OrdinalIgnoreCase));
                Save(_feedsFilePath, feeds);
            }
        }
    }

    private List<PodcastFeed> LoadFeedsMutable()
    {
        return Load<List<PodcastFeed>>(_feedsFilePath) ?? [];
    }

    private List<UserSubscription> GetAllSubscriptions()
    {
        return Load<List<UserSubscription>>(_subscriptionsFilePath) ?? [];
    }

    private T? Load<T>(string path)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load {Path}", path);
            return default;
        }
    }

    private void Save<T>(string path, T data)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save {Path}", path);
        }
    }
}
