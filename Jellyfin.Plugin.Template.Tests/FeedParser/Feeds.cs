namespace Jellyfin.Plugin.Template.Tests.FeedParser;

/// <summary>
/// Inline RSS/XML fixtures for parser tests.
/// </summary>
internal static class Feeds
{
    public const string SimpleRss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss xmlns:itunes="http://www.itunes.com/dtds/podcast-1.0.dtd" version="2.0">
          <channel>
            <title>Test Podcast</title>
            <description>A test podcast.</description>
            <link>https://example.com</link>
            <itunes:image href="https://example.com/cover.jpg"/>
            <item>
              <title>Episode 1</title>
              <guid isPermaLink="true">https://example.com/ep1</guid>
              <pubDate>Mon, 15 Jan 2024 10:00:00 GMT</pubDate>
              <enclosure url="https://example.com/ep1.mp3" type="audio/mpeg" length="1000"/>
              <itunes:duration>1:02:03</itunes:duration>
            </item>
            <item>
              <title>Episode 2</title>
              <guid isPermaLink="true">https://example.com/ep2</guid>
              <pubDate>Tue, 16 Jan 2024 10:00:00 GMT</pubDate>
              <enclosure url="https://example.com/ep2.mp3" type="audio/mpeg" length="1000"/>
              <itunes:duration>45:30</itunes:duration>
            </item>
          </channel>
        </rss>
        """;

    public const string EpisodeWithoutGuid = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0">
          <channel>
            <title>No Guid Feed</title>
            <item>
              <title>Episode without guid</title>
              <enclosure url="https://example.com/ep.mp3" type="audio/mpeg" length="1000"/>
            </item>
          </channel>
        </rss>
        """;

    public const string EpisodeWithoutEnclosure = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0">
          <channel>
            <title>No Enclosure Feed</title>
            <item>
              <title>Blog post, not a podcast episode</title>
              <guid>https://example.com/post1</guid>
            </item>
          </channel>
        </rss>
        """;

    /// <summary>
    /// Feed that uses itunes:episode / itunes:season but NOT the podcast namespace,
    /// used to verify iTunes fallback behaviour.
    /// </summary>
    public const string ItunesEpisodeAndSeason = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss xmlns:itunes="http://www.itunes.com/dtds/podcast-1.0.dtd" version="2.0">
          <channel>
            <title>iTunes-only Feed</title>
            <item>
              <title>S2E5</title>
              <guid>https://example.com/s2e5</guid>
              <enclosure url="https://example.com/s2e5.mp3" type="audio/mpeg" length="1000"/>
              <itunes:season>2</itunes:season>
              <itunes:episode>5</itunes:episode>
            </item>
          </channel>
        </rss>
        """;

    public const string WithPodcastNamespace = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss xmlns:podcast="https://podcastindex.org/namespace/1.0"
             xmlns:itunes="http://www.itunes.com/dtds/podcast-1.0.dtd"
             version="2.0">
          <channel>
            <title>Podcasting 2.0 Namespace Example</title>
            <description>A fake show for namespace testing.</description>
            <link>https://example.com/podcast</link>
            <itunes:image href="https://example.com/itunes-cover.jpg"/>
            <podcast:image href="https://example.com/podcast-ns-artwork.png" purpose="artwork"/>
            <podcast:guid>y0ur-gu1d-g035-h3r3</podcast:guid>
            <item>
              <title>Episode 3 - The Future</title>
              <guid isPermaLink="true">https://example.com/ep0003</guid>
              <pubDate>Fri, 09 Oct 2020 04:30:38 GMT</pubDate>
              <itunes:image href="https://example.com/ep3-itunes.jpg"/>
              <podcast:image href="https://example.com/ep3-artwork.png" purpose="artwork"/>
              <podcast:season name="Podcasting 2.0">1</podcast:season>
              <podcast:episode>3</podcast:episode>
              <podcast:chapters url="https://example.com/ep3_chapters.json" type="application/json"/>
              <podcast:transcript url="https://example.com/ep3/transcript.txt" type="text/plain"/>
              <podcast:transcript url="https://example.com/episode1/transcript.vtt" type="text/vtt" language="es" rel="captions"/>
              <podcast:person href="https://www.podchaser.com/creators/adam-curry" img="https://example.com/adamcurry.jpg">Adam Curry</podcast:person>
              <podcast:person role="guest" href="https://github.com/daveajones/" img="https://example.com/davejones.jpg">Dave Jones</podcast:person>
              <podcast:person group="visuals" role="cover art designer" href="https://example.com/beckysmith">Becky Smith</podcast:person>
              <enclosure url="https://example.com/file-03.mp3" length="43200000" type="audio/mpeg"/>
              <podcast:alternateEnclosure type="audio/mpeg" length="43200000" bitrate="128000" default="true" title="Standard">
                <podcast:source uri="https://example.com/file-03.mp3"/>
                <podcast:source uri="ipfs://someRandomMpegFile03"/>
              </podcast:alternateEnclosure>
              <podcast:alternateEnclosure type="audio/opus" length="32400000" bitrate="96000" title="High quality">
                <podcast:source uri="https://example.com/file-high-03.opus"/>
                <podcast:source uri="ipfs://someRandomHighBitrateOpusFile03"/>
              </podcast:alternateEnclosure>
            </item>
          </channel>
        </rss>
        """;
}
