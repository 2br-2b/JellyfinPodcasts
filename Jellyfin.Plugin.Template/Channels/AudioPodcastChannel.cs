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
/// Jellyfin channel that surfaces per-user audio podcast subscriptions.
/// </summary>
public class AudioPodcastChannel : PodcastChannelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AudioPodcastChannel"/> class.
    /// </summary>
    /// <param name="subscriptionStore">User subscription store.</param>
    /// <param name="feedParser">RSS feed parser.</param>
    /// <param name="logger">Logger instance.</param>
    public AudioPodcastChannel(
        ISubscriptionStore subscriptionStore,
        RssFeedParser feedParser,
        ILogger<AudioPodcastChannel> logger)
        : base(subscriptionStore, feedParser, logger)
    {
    }

    /// <inheritdoc />
    public override string Name => "Audio Podcasts";

    /// <inheritdoc />
    public override string Description => "Per-user audio podcast subscriptions.";

    /// <inheritdoc />
    protected override PodcastMediaType MediaType => PodcastMediaType.Audio;

    /// <inheritdoc />
    public override InternalChannelFeatures GetChannelFeatures() => new()
    {
        MediaTypes = [ChannelMediaType.Audio],
        ContentTypes = [ChannelMediaContentType.Podcast],
        DefaultSortFields = [ChannelItemSortField.DateCreated],
        SupportsSortOrderToggle = true,
    };

    /// <inheritdoc />
    protected override IEnumerable<ChannelItemInfo> MapEpisodes(
        IReadOnlyList<Jellyfin.Plugin.Podcasts.FeedParser.Models.ParsedEpisode> episodes,
        string? fallbackImageUrl)
    {
        foreach (var ep in episodes)
        {
            var container = (ep.EnclosureType ?? string.Empty) switch
            {
                var t when t.Contains("mp3", System.StringComparison.OrdinalIgnoreCase) => "mp3",
                var t when t.Contains("ogg", System.StringComparison.OrdinalIgnoreCase) => "ogg",
                var t when t.Contains("aac", System.StringComparison.OrdinalIgnoreCase) => "aac",
                var t when t.Contains("m4a", System.StringComparison.OrdinalIgnoreCase) => "m4a",
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
