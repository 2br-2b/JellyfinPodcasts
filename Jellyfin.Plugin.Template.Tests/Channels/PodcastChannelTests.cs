using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Podcasts.FeedParser;
using Jellyfin.Plugin.Podcasts.FeedParser.Models;
using Jellyfin.Plugin.Template.Channels;
using Jellyfin.Plugin.Template.Models;
using Jellyfin.Plugin.Template.Services;
using Jellyfin.Plugin.Template.Tests.FeedParser;
using MediaBrowser.Controller.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Template.Tests.Channels;

public class AudioPodcastChannelTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static PodcastFeed MakeFeed(string id, string feedUrl) => new()
    {
        Id = id,
        FeedUrl = feedUrl,
        Title = $"Podcast {id}",
        Description = "A show.",
        ImageUrl = $"https://example.com/{id}/cover.jpg",
        MediaType = PodcastMediaType.Audio,
    };

    private static RssFeedParser CreateParser(string xml)
    {
        var handler = new MockHttpMessageHandler(xml);
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return new RssFeedParser(factory.Object, NullLogger<RssFeedParser>.Instance);
    }

    private static AudioPodcastChannel CreateChannel(ISubscriptionStore store, RssFeedParser parser)
        => new(store, parser, NullLogger<AudioPodcastChannel>.Instance);

    // ── Root view (no FolderId) ──────────────────────────────────────────────

    [Fact]
    public async Task GetChannelItems_NoFolderId_ReturnsSubscribedFeedsAsFolders()
    {
        var store = new Mock<ISubscriptionStore>();
        store.Setup(s => s.GetFeedsForUser(UserId))
             .Returns([MakeFeed("f1", "https://example.com/f1.rss")]);

        var channel = CreateChannel(store.Object, CreateParser("<rss><channel/></rss>"));
        var result = await channel.GetChannelItems(
            new InternalChannelItemQuery { UserId = UserId },
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(ChannelItemType.Folder, result.Items[0].Type);
        Assert.Equal("f1", result.Items[0].Id);
    }

    [Fact]
    public async Task GetChannelItems_NoFolderId_EmptySubscriptions_ReturnsEmptyResult()
    {
        var store = new Mock<ISubscriptionStore>();
        store.Setup(s => s.GetFeedsForUser(UserId)).Returns([]);

        var channel = CreateChannel(store.Object, CreateParser("<rss><channel/></rss>"));
        var result = await channel.GetChannelItems(
            new InternalChannelItemQuery { UserId = UserId },
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetChannelItems_NoFolderId_VideoFeedsNotShown()
    {
        var videoFeed = MakeFeed("v1", "https://example.com/v1.rss");
        videoFeed.MediaType = PodcastMediaType.Video;
        var store = new Mock<ISubscriptionStore>();
        store.Setup(s => s.GetFeedsForUser(UserId)).Returns([videoFeed]);

        var channel = CreateChannel(store.Object, CreateParser("<rss><channel/></rss>"));
        var result = await channel.GetChannelItems(
            new InternalChannelItemQuery { UserId = UserId },
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    // ── Folder view (FolderId set) ───────────────────────────────────────────

    [Fact]
    public async Task GetChannelItems_WithFolderId_ReturnsEpisodesAsMediaItems()
    {
        var feed = MakeFeed("f1", "https://example.com/f1.rss");
        var store = new Mock<ISubscriptionStore>();
        store.Setup(s => s.GetAllFeeds()).Returns([feed]);

        var channel = CreateChannel(store.Object, CreateParser(Feeds.SimpleRss));
        var result = await channel.GetChannelItems(
            new InternalChannelItemQuery { FolderId = "f1" },
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(ChannelItemType.Media, item.Type));
    }

    [Fact]
    public async Task GetChannelItems_WithFolderId_EachEpisodeHasMediaSource()
    {
        var feed = MakeFeed("f1", "https://example.com/f1.rss");
        var store = new Mock<ISubscriptionStore>();
        store.Setup(s => s.GetAllFeeds()).Returns([feed]);

        var channel = CreateChannel(store.Object, CreateParser(Feeds.SimpleRss));
        var result = await channel.GetChannelItems(
            new InternalChannelItemQuery { FolderId = "f1" },
            CancellationToken.None);

        Assert.All(result.Items, item => Assert.NotEmpty(item.MediaSources));
    }

    [Fact]
    public async Task GetChannelItems_UnknownFolderId_ReturnsEmpty()
    {
        var store = new Mock<ISubscriptionStore>();
        store.Setup(s => s.GetAllFeeds()).Returns([]);

        var channel = CreateChannel(store.Object, CreateParser("<rss><channel/></rss>"));
        var result = await channel.GetChannelItems(
            new InternalChannelItemQuery { FolderId = "nonexistent" },
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    // ── GetLatestMedia ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetLatestMedia_ReturnsEpisodesOrderedByDateDescending()
    {
        var feed = MakeFeed("f1", "https://example.com/f1.rss");
        var store = new Mock<ISubscriptionStore>();
        store.Setup(s => s.GetFeedsForUser(UserId)).Returns([feed]);

        var channel = CreateChannel(store.Object, CreateParser(Feeds.SimpleRss));
        var items = (await channel.GetLatestMedia(
            new ChannelLatestMediaSearch { UserId = UserId.ToString() },
            CancellationToken.None)).ToList();

        Assert.Equal(2, items.Count);
        // SimpleRss ep2 pubDate is later than ep1
        Assert.True(items[0].PremiereDate >= items[1].PremiereDate);
    }

    [Fact]
    public async Task GetLatestMedia_InvalidUserId_ReturnsEmpty()
    {
        var store = new Mock<ISubscriptionStore>();
        var channel = CreateChannel(store.Object, CreateParser("<rss><channel/></rss>"));

        var items = await channel.GetLatestMedia(
            new ChannelLatestMediaSearch { UserId = "not-a-guid" },
            CancellationToken.None);

        Assert.Empty(items);
    }

    // ── Static metadata ──────────────────────────────────────────────────────

    [Fact]
    public void Name_IsCorrect()
    {
        var store = new Mock<ISubscriptionStore>();
        var channel = CreateChannel(store.Object, CreateParser("<rss><channel/></rss>"));
        Assert.Equal("Audio Podcasts", channel.Name);
    }

    [Fact]
    public void IsEnabledFor_AlwaysTrue()
    {
        var store = new Mock<ISubscriptionStore>();
        var channel = CreateChannel(store.Object, CreateParser("<rss><channel/></rss>"));
        Assert.True(channel.IsEnabledFor(UserId.ToString()));
    }
}
