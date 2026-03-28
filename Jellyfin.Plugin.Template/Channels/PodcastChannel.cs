using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Podcasts.FeedParser;
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
/// Jellyfin channel that surfaces per-user podcast subscriptions.
/// </summary>
public class PodcastChannel : IChannel, ISupportsLatestMedia
{
    private readonly SubscriptionStore _subscriptionStore;
    private readonly RssFeedParser _feedParser;
    private readonly ILogger<PodcastChannel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PodcastChannel"/> class.
    /// </summary>
    /// <param name="subscriptionStore">User subscription store.</param>
    /// <param name="feedParser">RSS feed parser.</param>
    /// <param name="logger">Logger instance.</param>
    public PodcastChannel(
        SubscriptionStore subscriptionStore,
        RssFeedParser feedParser,
        ILogger<PodcastChannel> logger)
    {
        _subscriptionStore = subscriptionStore;
        _feedParser = feedParser;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Podcasts";

    /// <inheritdoc />
    public string Description => "Per-user podcast subscriptions.";

    /// <inheritdoc />
    public string DataVersion => "1";

    /// <inheritdoc />
    public string HomePageUrl => string.Empty;

    /// <inheritdoc />
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    /// <inheritdoc />
    public bool IsEnabledFor(string userId) => true;

    /// <inheritdoc />
    public InternalChannelFeatures GetChannelFeatures() => new()
    {
        MediaTypes = [ChannelMediaType.Audio],
        ContentTypes = [ChannelMediaContentType.Podcast],
        DefaultSortFields = [ChannelItemSortField.DateCreated],
        SupportsSortOrderToggle = true,
    };

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
        // Root view — list the user's subscribed feeds as folders.
        if (string.IsNullOrEmpty(query.FolderId))
        {
            return GetSubscribedFeedsAsItems(query.UserId);
        }

        // Folder view — list episodes from the selected feed.
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

        var feeds = _subscriptionStore.GetFeedsForUser(userId);
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
        var feeds = _subscriptionStore.GetFeedsForUser(userId);

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
            .FirstOrDefault(f => string.Equals(f.Id, feedId, StringComparison.OrdinalIgnoreCase));

        if (feed is null)
        {
            _logger.LogWarning("Feed {FeedId} not found in subscription store.", feedId);
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

    private static IEnumerable<ChannelItemInfo> MapEpisodes(
        IReadOnlyList<Jellyfin.Plugin.Podcasts.FeedParser.Models.ParsedEpisode> episodes,
        string? fallbackImageUrl)
    {
        foreach (var ep in episodes)
        {
            var container = (ep.EnclosureType ?? string.Empty) switch
            {
                var t when t.Contains("mp3", StringComparison.OrdinalIgnoreCase) => "mp3",
                var t when t.Contains("ogg", StringComparison.OrdinalIgnoreCase) => "ogg",
                var t when t.Contains("aac", StringComparison.OrdinalIgnoreCase) => "aac",
                var t when t.Contains("m4a", StringComparison.OrdinalIgnoreCase) => "m4a",
                _ => "mp3",
            };

            yield return new ChannelItemInfo
            {
                Id = ep.Guid,
                Name = ep.Title,
                Overview = ep.Description,
                ImageUrl = ep.ImageUrl ?? fallbackImageUrl,
                Type = ChannelItemType.Media,
                MediaType = ChannelMediaType.Audio,
                ContentType = ChannelMediaContentType.Podcast,
                PremiereDate = ep.PublishedAt,
                RunTimeTicks = ep.DurationTicks,
                MediaSources =
                [
                    new MediaSourceInfo
                    {
                        Id = ep.Guid,
                        Path = ep.AudioUrl,
                        Protocol = MediaProtocol.Http,
                        Container = container,
                        SupportsDirectStream = true,
                        IsInfiniteStream = false,
                    }
                ],
            };
        }
    }
}
