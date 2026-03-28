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
/// Fetches and parses RSS podcast feeds.
/// </summary>
public class RssFeedParser
{
    private static readonly XNamespace ItunesNs = "http://www.itunes.com/dtds/podcast-1.0.dtd";
    private static readonly XNamespace MediaNs = "http://search.yahoo.com/mrss/";

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

        var feedImageUrl =
            channel.Element(ItunesNs + "image")?.Attribute("href")?.Value
            ?? channel.Element("image")?.Element("url")?.Value;

        return new ParsedFeed
        {
            Title = channel.Element("title")?.Value ?? feedUrl,
            Description = channel.Element("description")?.Value ?? string.Empty,
            ImageUrl = feedImageUrl,
            HomePageUrl = channel.Element("link")?.Value,
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
                // Fall back to a hash of the audio URL so every episode still has a stable ID.
                guid = FallbackId(audioUrl);
            }

            var pubDateStr = item.Element("pubDate")?.Value;
            DateTime? pubDate = null;
            if (!string.IsNullOrEmpty(pubDateStr) &&
                DateTime.TryParse(pubDateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                pubDate = parsed;
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
                ImageUrl = item.Element(ItunesNs + "image")?.Attribute("href")?.Value
                    ?? item.Element(MediaNs + "thumbnail")?.Attribute("url")?.Value
                    ?? feedImageUrl,
                PublishedAt = pubDate,
                DurationTicks = ParseDurationTicks(item.Element(ItunesNs + "duration")?.Value),
            });
        }

        return episodes;
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
