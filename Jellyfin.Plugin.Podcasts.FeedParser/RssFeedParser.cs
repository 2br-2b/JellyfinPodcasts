using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Jellyfin.Plugin.Podcasts.FeedParser.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Podcasts.FeedParser;

/// <summary>
/// Fetches and parses RSS podcast feeds, including iTunes and Podcast Index namespace extensions.
/// </summary>
public class RssFeedParser
{
    private static readonly XNamespace ItunesNs = "http://www.itunes.com/dtds/podcast-1.0.dtd";
    private static readonly XNamespace MediaNs = "http://search.yahoo.com/mrss/";
    private static readonly XNamespace PodcastNs = "https://podcastindex.org/namespace/1.0";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RssFeedParser> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RssFeedParser"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger instance.</param>
    public RssFeedParser(IHttpClientFactory httpClientFactory, ILogger<RssFeedParser> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Fetches and parses a podcast RSS feed.
    /// </summary>
    /// <param name="feedUrl">The RSS feed URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parsed feed, or null if the feed could not be fetched or parsed.</returns>
    public async Task<ParsedFeed?> ParseAsync(string feedUrl, CancellationToken cancellationToken)
    {
        var xml = await FetchXmlAsync(feedUrl, cancellationToken).ConfigureAwait(false);
        if (xml is null)
        {
            return null;
        }

        var channel = xml.Descendants("channel").FirstOrDefault();
        if (channel is null)
        {
            _logger.LogWarning("No <channel> element found in feed {Url}", feedUrl);
            return null;
        }

        // Image priority: podcast:image (artwork) > itunes:image > rss image
        var feedImageUrl =
            channel.Elements(PodcastNs + "image")
                .FirstOrDefault(e => string.Equals(
                    e.Attribute("purpose")?.Value, "artwork", StringComparison.OrdinalIgnoreCase))
                ?.Attribute("href")?.Value
            ?? channel.Element(ItunesNs + "image")?.Attribute("href")?.Value
            ?? channel.Element("image")?.Element("url")?.Value;

        return new ParsedFeed
        {
            Title = channel.Element("title")?.Value ?? feedUrl,
            Description = channel.Element("description")?.Value ?? string.Empty,
            ImageUrl = feedImageUrl,
            HomePageUrl = channel.Element("link")?.Value,
            Guid = channel.Element(PodcastNs + "guid")?.Value,
            Episodes = ParseEpisodes(channel, feedImageUrl),
        };
    }

    private static IReadOnlyList<ParsedEpisode> ParseEpisodes(XElement channel, string? feedImageUrl)
    {
        var episodes = new List<ParsedEpisode>();

        foreach (var item in channel.Elements("item"))
        {
            var enclosure = item.Element("enclosure");
            var audioUrl = enclosure?.Attribute("url")?.Value;
            if (string.IsNullOrWhiteSpace(audioUrl))
            {
                continue;
            }

            var guid = item.Element("guid")?.Value;
            if (string.IsNullOrWhiteSpace(guid))
            {
                guid = FallbackId(audioUrl);
            }

            var pubDateStr = item.Element("pubDate")?.Value;
            DateTime? pubDate = null;
            if (!string.IsNullOrEmpty(pubDateStr) &&
                DateTime.TryParse(pubDateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                pubDate = parsed;
            }

            // Image priority: podcast:image (artwork) > itunes:image > media:thumbnail > feed fallback
            var episodeImageUrl =
                item.Elements(PodcastNs + "image")
                    .FirstOrDefault(e => string.Equals(
                        e.Attribute("purpose")?.Value, "artwork", StringComparison.OrdinalIgnoreCase))
                    ?.Attribute("href")?.Value
                ?? item.Element(ItunesNs + "image")?.Attribute("href")?.Value
                ?? item.Element(MediaNs + "thumbnail")?.Attribute("url")?.Value
                ?? feedImageUrl;

            // Season: podcast:season preferred, fall back to itunes:season
            var seasonEl = item.Element(PodcastNs + "season");
            var seasonValueStr = seasonEl?.Value ?? item.Element(ItunesNs + "season")?.Value;
            int? seasonNumber = null;
            if (!string.IsNullOrEmpty(seasonValueStr) && int.TryParse(seasonValueStr, out var s))
            {
                seasonNumber = s;
            }

            // Episode number: podcast:episode preferred, fall back to itunes:episode
            var episodeEl = item.Element(PodcastNs + "episode");
            var episodeValueStr = episodeEl?.Value ?? item.Element(ItunesNs + "episode")?.Value;
            double? episodeNumber = null;
            if (!string.IsNullOrEmpty(episodeValueStr) &&
                double.TryParse(episodeValueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var epNum))
            {
                episodeNumber = epNum;
            }

            episodes.Add(new ParsedEpisode
            {
                Guid = guid,
                Title = item.Element("title")?.Value ?? "Untitled Episode",
                Description = item.Element("description")?.Value
                    ?? item.Element(ItunesNs + "summary")?.Value
                    ?? string.Empty,
                AudioUrl = audioUrl,
                EnclosureType = enclosure?.Attribute("type")?.Value,
                ImageUrl = episodeImageUrl,
                PublishedAt = pubDate,
                DurationTicks = ParseDurationTicks(item.Element(ItunesNs + "duration")?.Value),
                EpisodeNumber = episodeNumber,
                SeasonNumber = seasonNumber,
                SeasonName = item.Element(PodcastNs + "season")?.Attribute("name")?.Value,
                ChaptersUrl = item.Element(PodcastNs + "chapters")?.Attribute("url")?.Value,
                Transcripts = ParseTranscripts(item),
                AlternateEnclosures = ParseAlternateEnclosures(item),
                People = ParsePeople(item),
            });
        }

        return episodes;
    }

    private static IReadOnlyList<ParsedTranscript> ParseTranscripts(XElement item)
    {
        var transcripts = new List<ParsedTranscript>();

        foreach (var el in item.Elements(PodcastNs + "transcript"))
        {
            var url = el.Attribute("url")?.Value;
            var type = el.Attribute("type")?.Value;
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            transcripts.Add(new ParsedTranscript
            {
                Url = url,
                Type = type,
                Language = el.Attribute("language")?.Value,
                Rel = el.Attribute("rel")?.Value,
            });
        }

        return transcripts;
    }

    private static IReadOnlyList<ParsedAlternateEnclosure> ParseAlternateEnclosures(XElement item)
    {
        var enclosures = new List<ParsedAlternateEnclosure>();

        foreach (var el in item.Elements(PodcastNs + "alternateEnclosure"))
        {
            var mimeType = el.Attribute("type")?.Value;
            if (string.IsNullOrWhiteSpace(mimeType))
            {
                continue;
            }

            var httpSources = el.Elements(PodcastNs + "source")
                .Select(s => s.Attribute("uri")?.Value)
                .Where(uri => !string.IsNullOrWhiteSpace(uri) &&
                              (uri!.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                               uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (httpSources.Count == 0)
            {
                continue;
            }

            double? bitrate = null;
            var bitrateStr = el.Attribute("bitrate")?.Value;
            if (!string.IsNullOrEmpty(bitrateStr) &&
                double.TryParse(bitrateStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var br))
            {
                bitrate = br;
            }

            long? length = null;
            var lengthStr = el.Attribute("length")?.Value;
            if (!string.IsNullOrEmpty(lengthStr) &&
                long.TryParse(lengthStr, out var len))
            {
                length = len;
            }

            enclosures.Add(new ParsedAlternateEnclosure
            {
                MimeType = mimeType,
                Bitrate = bitrate,
                Length = length,
                IsDefault = string.Equals(el.Attribute("default")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                Title = el.Attribute("title")?.Value,
                HttpSources = httpSources!,
            });
        }

        return enclosures;
    }

    private static IReadOnlyList<ParsedPerson> ParsePeople(XElement item)
    {
        return item.Elements(PodcastNs + "person")
            .Select(el => new ParsedPerson
            {
                Name = el.Value,
                Role = el.Attribute("role")?.Value,
                Group = el.Attribute("group")?.Value,
                Href = el.Attribute("href")?.Value,
                Img = el.Attribute("img")?.Value,
            })
            .ToList();
    }

    private async Task<XDocument?> FetchXmlAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var xml = await client.GetStringAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
            return XDocument.Parse(xml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch feed {Url}", url);
            return null;
        }
    }

    private static string FallbackId(string url)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(bytes)[..16].ToUpperInvariant();
    }

    private static long? ParseDurationTicks(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
        {
            return null;
        }

        // Formats: "HH:MM:SS", "MM:SS", or plain seconds
        var parts = duration.Split(':');
        if (parts.Length == 3 &&
            int.TryParse(parts[0], out var h) &&
            int.TryParse(parts[1], out var m) &&
            int.TryParse(parts[2], out var s))
        {
            return TimeSpan.FromSeconds((h * 3600) + (m * 60) + s).Ticks;
        }

        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var mm) &&
            int.TryParse(parts[1], out var ss))
        {
            return TimeSpan.FromSeconds((mm * 60) + ss).Ticks;
        }

        if (double.TryParse(duration, NumberStyles.Any, CultureInfo.InvariantCulture, out var totalSeconds))
        {
            return TimeSpan.FromSeconds(totalSeconds).Ticks;
        }

        return null;
    }
}
