using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Podcasts.FeedParser;
using Jellyfin.Plugin.Template.Models;
using Jellyfin.Plugin.Template.Services;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Template.Channels;

/// <summary>
/// Shared base for audio and video podcast channels.
/// Subclasses declare which <see cref="PodcastMediaType"/> they surface and supply the
/// matching Jellyfin <see cref="InternalChannelFeatures"/>.
/// </summary>
public abstract class PodcastChannelBase : IChannel, ISupportsLatestMedia
{
    private readonly ISubscriptionStore _subscriptionStore;
    private readonly RssFeedParser _feedParser;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PodcastChannelBase"/> class.
    /// </summary>
    /// <param name="subscriptionStore">User subscription store.</param>
    /// <param name="feedParser">RSS feed parser.</param>
    /// <param name="logger">Logger instance.</param>
    protected PodcastChannelBase(
        ISubscriptionStore subscriptionStore,
        RssFeedParser feedParser,
        ILogger logger)
    {
        _subscriptionStore = subscriptionStore;
        _feedParser = feedParser;
        _logger = logger;
    }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <summary>
    /// Gets the media type this channel surfaces (Audio or Video).
    /// Used to filter feeds from the shared subscription store.
    /// </summary>
    protected abstract PodcastMediaType MediaType { get; }

    /// <inheritdoc />
    public string DataVersion => "1";

    /// <inheritdoc />
    public string HomePageUrl => string.Empty;

    /// <inheritdoc />
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    /// <inheritdoc />
    public bool IsEnabledFor(string userId) => true;

    /// <inheritdoc />
    public abstract InternalChannelFeatures GetChannelFeatures();

    /// <inheritdoc />
    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult(new DynamicImageResponse { HasImage = false });

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedChannelImages() => [];

    /// <inheritdoc />
    public async Task<ChannelItemResult> GetChannelItems(
        InternalChannelItemQuery query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(query.FolderId))
        {
            return GetSubscribedFeedsAsItems(query.UserId);
        }

        return await GetEpisodesForFeedAsync(query.FolderId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ChannelItemInfo>> GetLatestMedia(
        ChannelLatestMediaSearch request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
        {
            return [];
        }

        var feeds = GetFeedsForUser(userId);
        var allEpisodes = new List<ChannelItemInfo>();

        foreach (var feed in feeds)
        {
            var parsed = await _feedParser.ParseAsync(feed.FeedUrl, cancellationToken).ConfigureAwait(false);
            if (parsed is null)
            {
                continue;
            }

            allEpisodes.AddRange(MapEpisodes(parsed.Episodes, feed.ImageUrl));
        }

        return allEpisodes
            .OrderByDescending(e => e.PremiereDate ?? DateTime.MinValue)
            .Take(10);
    }

    private ChannelItemResult GetSubscribedFeedsAsItems(Guid userId)
    {
        var feeds = GetFeedsForUser(userId);

        var items = feeds.Select(f => new ChannelItemInfo
        {
            Id = f.Id,
            Name = f.Title,
            Overview = f.Description,
            ImageUrl = f.ImageUrl,
            HomePageUrl = f.HomePageUrl,
            Type = ChannelItemType.Folder,
        }).ToList();

        return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
    }

    private async Task<ChannelItemResult> GetEpisodesForFeedAsync(
        string feedId,
        CancellationToken cancellationToken)
    {
        var feed = _subscriptionStore.GetAllFeeds()
            .FirstOrDefault(f => string.Equals(f.Id, feedId, StringComparison.OrdinalIgnoreCase)
                              && f.MediaType == MediaType);

        if (feed is null)
        {
            _logger.LogWarning("Feed {FeedId} not found for media type {MediaType}.", feedId, MediaType);
            return new ChannelItemResult();
        }

        var parsed = await _feedParser.ParseAsync(feed.FeedUrl, cancellationToken).ConfigureAwait(false);
        if (parsed is null)
        {
            return new ChannelItemResult();
        }

        var items = MapEpisodes(parsed.Episodes, feed.ImageUrl).ToList();
        return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
    }

    /// <summary>
    /// Returns the feeds the user is subscribed to that match this channel's <see cref="MediaType"/>.
    /// </summary>
    private List<PodcastFeed> GetFeedsForUser(Guid userId)
        => _subscriptionStore.GetFeedsForUser(userId)
            .Where(f => f.MediaType == MediaType)
            .ToList();

    /// <summary>
    /// Maps parsed episodes to <see cref="ChannelItemInfo"/> objects.
    /// Subclasses that need different mapping (e.g. video media sources) can override this.
    /// </summary>
    /// <param name="episodes">The parsed episodes to map.</param>
    /// <param name="fallbackImageUrl">Image URL to use when the episode has no artwork.</param>
    /// <returns>Sequence of channel item info objects ready for Jellyfin.</returns>
    protected abstract IEnumerable<ChannelItemInfo> MapEpisodes(
        IReadOnlyList<Jellyfin.Plugin.Podcasts.FeedParser.Models.ParsedEpisode> episodes,
        string? fallbackImageUrl);
}
