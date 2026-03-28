using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Podcasts.FeedParser;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Template.Tests.FeedParser;

public class RssFeedParserTests
{
    private static RssFeedParser CreateParser(string xml)
    {
        var handler = new MockHttpMessageHandler(xml);
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return new RssFeedParser(factory.Object, NullLogger<RssFeedParser>.Instance);
    }

    // ── Feed-level metadata ──────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_StandardRss_ParsesFeedTitle()
    {
        var parser = CreateParser(Feeds.SimpleRss);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.NotNull(feed);
        Assert.Equal("Test Podcast", feed.Title);
    }

    [Fact]
    public async Task ParseAsync_StandardRss_ParsesFeedDescription()
    {
        var parser = CreateParser(Feeds.SimpleRss);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Equal("A test podcast.", feed!.Description);
    }

    [Fact]
    public async Task ParseAsync_StandardRss_ParsesFeedHomePageUrl()
    {
        var parser = CreateParser(Feeds.SimpleRss);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Equal("https://example.com", feed!.HomePageUrl);
    }

    [Fact]
    public async Task ParseAsync_ItunesImage_UsedAsFeedImage()
    {
        var parser = CreateParser(Feeds.SimpleRss);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Equal("https://example.com/cover.jpg", feed!.ImageUrl);
    }

    [Fact]
    public async Task ParseAsync_PodcastNamespace_ParsesFeedGuid()
    {
        var parser = CreateParser(Feeds.WithPodcastNamespace);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Equal("y0ur-gu1d-g035-h3r3", feed!.Guid);
    }

    [Fact]
    public async Task ParseAsync_PodcastNamespaceArtworkImage_TakesPriorityOverItunes()
    {
        var parser = CreateParser(Feeds.WithPodcastNamespace);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Equal("https://example.com/podcast-ns-artwork.png", feed!.ImageUrl);
    }

    // ── Episode parsing ──────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_StandardRss_ParsesEpisodeCount()
    {
        var parser = CreateParser(Feeds.SimpleRss);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Equal(2, feed!.Episodes.Count);
    }

    [Fact]
    public async Task ParseAsync_StandardRss_ParsesEpisodeTitle()
    {
        var parser = CreateParser(Feeds.SimpleRss);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Equal("Episode 1", feed!.Episodes[0].Title);
    }

    [Fact]
    public async Task ParseAsync_StandardRss_ParsesEpisodeGuid()
    {
        var parser = CreateParser(Feeds.SimpleRss);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Equal("https://example.com/ep1", feed!.Episodes[0].Guid);
    }

    [Fact]
    public async Task ParseAsync_MissingGuid_FallsBackToUrlHash()
    {
        var parser = CreateParser(Feeds.EpisodeWithoutGuid);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.NotNull(feed);
        Assert.NotEmpty(feed!.Episodes[0].Guid);
        // Hash should be stable and not equal to the URL itself
        Assert.NotEqual("https://example.com/ep.mp3", feed.Episodes[0].Guid);
    }

    [Fact]
    public async Task ParseAsync_ItemWithoutEnclosure_IsSkipped()
    {
        var parser = CreateParser(Feeds.EpisodeWithoutEnclosure);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Empty(feed!.Episodes);
    }

    [Fact]
    public async Task ParseAsync_ItunesDuration_ParsedToTicks()
    {
        var parser = CreateParser(Feeds.SimpleRss);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        // Episode 1 has duration "1:02:03" = 3723 seconds
        var expected = TimeSpan.FromSeconds(3723).Ticks;
        Assert.Equal(expected, feed!.Episodes[0].DurationTicks);
    }

    [Fact]
    public async Task ParseAsync_ItunesDurationMmSs_ParsedToTicks()
    {
        var parser = CreateParser(Feeds.SimpleRss);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        // Episode 2 has duration "45:30" = 2730 seconds
        var expected = TimeSpan.FromSeconds(2730).Ticks;
        Assert.Equal(expected, feed!.Episodes[1].DurationTicks);
    }

    [Fact]
    public async Task ParseAsync_PublishDate_ParsedCorrectly()
    {
        var parser = CreateParser(Feeds.SimpleRss);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Equal(new DateTime(2024, 1, 15), feed!.Episodes[0].PublishedAt!.Value.Date);
    }

    // ── Podcast Index namespace — episode fields ─────────────────────────────

    [Fact]
    public async Task ParseAsync_PodcastNamespace_ParsesEpisodeNumber()
    {
        var parser = CreateParser(Feeds.WithPodcastNamespace);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Equal(3.0, feed!.Episodes[0].EpisodeNumber);
    }

    [Fact]
    public async Task ParseAsync_PodcastNamespace_ParsesSeasonNumber()
    {
        var parser = CreateParser(Feeds.WithPodcastNamespace);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Equal(1, feed!.Episodes[0].SeasonNumber);
    }

    [Fact]
    public async Task ParseAsync_PodcastNamespace_ParsesSeasonName()
    {
        var parser = CreateParser(Feeds.WithPodcastNamespace);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Equal("Podcasting 2.0", feed!.Episodes[0].SeasonName);
    }

    [Fact]
    public async Task ParseAsync_PodcastNamespace_ParsesChaptersUrl()
    {
        var parser = CreateParser(Feeds.WithPodcastNamespace);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Equal("https://example.com/ep3_chapters.json", feed!.Episodes[0].ChaptersUrl);
    }

    [Fact]
    public async Task ParseAsync_PodcastNamespace_ParsesTranscripts()
    {
        var parser = CreateParser(Feeds.WithPodcastNamespace);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        var transcripts = feed!.Episodes[0].Transcripts;
        Assert.Equal(2, transcripts.Count);
        Assert.Equal("https://example.com/ep3/transcript.txt", transcripts[0].Url);
        Assert.Equal("text/plain", transcripts[0].Type);
        Assert.Equal("text/vtt", transcripts[1].Type);
        Assert.Equal("es", transcripts[1].Language);
        Assert.Equal("captions", transcripts[1].Rel);
    }

    [Fact]
    public async Task ParseAsync_PodcastNamespace_ParsesAlternateEnclosures()
    {
        var parser = CreateParser(Feeds.WithPodcastNamespace);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        var enclosures = feed!.Episodes[0].AlternateEnclosures;
        Assert.True(enclosures.Count > 0);
    }

    [Fact]
    public async Task ParseAsync_PodcastNamespace_DefaultEnclosureMarked()
    {
        var parser = CreateParser(Feeds.WithPodcastNamespace);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        var defaultEnc = feed!.Episodes[0].AlternateEnclosures.FirstOrDefault(e => e.IsDefault);
        Assert.NotNull(defaultEnc);
        Assert.Equal("audio/mpeg", defaultEnc!.MimeType);
    }

    [Fact]
    public async Task ParseAsync_PodcastNamespace_IpfsSourcesExcluded()
    {
        var parser = CreateParser(Feeds.WithPodcastNamespace);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        var allSources = feed!.Episodes[0].AlternateEnclosures.SelectMany(e => e.HttpSources);
        Assert.DoesNotContain(allSources, s => s.StartsWith("ipfs://", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ParseAsync_PodcastNamespace_ParsesPeople()
    {
        var parser = CreateParser(Feeds.WithPodcastNamespace);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        var people = feed!.Episodes[0].People;
        Assert.Equal(3, people.Count);
        Assert.Equal("Adam Curry", people[0].Name);
        Assert.Null(people[0].Role);   // no role attr on first person
        Assert.Equal("guest", people[1].Role);
        Assert.Equal("visuals", people[2].Group);
    }

    [Fact]
    public async Task ParseAsync_PodcastNamespaceArtworkImage_UsedForEpisode()
    {
        var parser = CreateParser(Feeds.WithPodcastNamespace);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Equal("https://example.com/ep3-artwork.png", feed!.Episodes[0].ImageUrl);
    }

    // ── iTunes fallbacks ─────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_ItunesEpisode_UsedWhenNoPodcastNamespace()
    {
        var parser = CreateParser(Feeds.ItunesEpisodeAndSeason);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Equal(5.0, feed!.Episodes[0].EpisodeNumber);
    }

    [Fact]
    public async Task ParseAsync_ItunesSeason_UsedWhenNoPodcastNamespace()
    {
        var parser = CreateParser(Feeds.ItunesEpisodeAndSeason);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Equal(2, feed!.Episodes[0].SeasonNumber);
    }

    [Fact]
    public async Task ParseAsync_ItunesSeason_SeasonNameIsNullWithoutPodcastElement()
    {
        var parser = CreateParser(Feeds.ItunesEpisodeAndSeason);
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Null(feed!.Episodes[0].SeasonName);
    }

    // ── Error handling ───────────────────────────────────────────────────────

    [Fact]
    public async Task ParseAsync_NoChannel_ReturnsNull()
    {
        var parser = CreateParser("<rss version=\"2.0\"></rss>");
        var feed = await parser.ParseAsync("https://example.com/feed", CancellationToken.None);
        Assert.Null(feed);
    }
}
