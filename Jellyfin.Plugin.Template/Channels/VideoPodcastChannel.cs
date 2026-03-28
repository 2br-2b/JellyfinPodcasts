using System.Collections.Generic;
using Jellyfin.Plugin.Podcasts.FeedParser;
using Jellyfin.Plugin.Template.Models;
using Jellyfin.Plugin.Template.Services;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Template.Channels;

/// <summary>
/// Jellyfin channel that will surface per-user video podcast (vodcast) subscriptions.
/// Video playback is not yet implemented; this channel surfaces the correct structure
/// so that vodcast support can be added without architectural changes.
/// </summary>
public class VideoPodcastChannel : PodcastChannelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VideoPodcastChannel"/> class.
    /// </summary>
    /// <param name="subscriptionStore">User subscription store.</param>
    /// <param name="feedParser">RSS feed parser.</param>
    /// <param name="logger">Logger instance.</param>
    public VideoPodcastChannel(
        ISubscriptionStore subscriptionStore,
        RssFeedParser feedParser,
        ILogger<VideoPodcastChannel> logger)
        : base(subscriptionStore, feedParser, logger)
    {
    }

    /// <inheritdoc />
    public override string Name => "Video Podcasts";

    /// <inheritdoc />
    public override string Description => "Per-user video podcast (vodcast) subscriptions.";

    /// <inheritdoc />
    protected override PodcastMediaType MediaType => PodcastMediaType.Video;

    /// <inheritdoc />
    public override InternalChannelFeatures GetChannelFeatures() => new()
    {
        MediaTypes = [ChannelMediaType.Video],
        ContentTypes = [ChannelMediaContentType.Podcast],
        DefaultSortFields = [ChannelItemSortField.DateCreated],
        SupportsSortOrderToggle = true,
    };

    /// <inheritdoc />
    protected override IEnumerable<ChannelItemInfo> MapEpisodes(
        IReadOnlyList<Jellyfin.Plugin.Podcasts.FeedParser.Models.ParsedEpisode> episodes,
        string? fallbackImageUrl)
    {
        // TODO: map video enclosures when vodcast support is implemented.
        // For now this method is unreachable because no video feeds exist in the store.
        foreach (var ep in episodes)
        {
            yield return new ChannelItemInfo
            {
                Id = ep.Guid,
                Name = ep.Title,
                Overview = ep.Description,
                ImageUrl = ep.ImageUrl ?? fallbackImageUrl,
                Type = ChannelItemType.Media,
                MediaType = ChannelMediaType.Video,
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
                        SupportsDirectStream = true,
                        IsInfiniteStream = false,
                    }
                ],
            };
        }
    }
}
