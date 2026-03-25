using CodeHollow.FeedReader;
using Jellyfin.Plugin.Podcasts.Cache;
using Jellyfin.Plugin.Podcasts.Configuration;
using Jellyfin.Plugin.Podcasts.Database;
using Jellyfin.Plugin.Podcasts.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Podcasts.Feed;

/// <summary>
/// Fetches RSS feeds and upserts podcast/episode metadata into the database.
/// </summary>
public class FeedManager
{
    private readonly PodcastDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CacheManager _cacheManager;
    private readonly ILogger<FeedManager> _logger;

    public FeedManager(
        PodcastDbContext db,
        IHttpClientFactory httpClientFactory,
        CacheManager cacheManager,
        ILogger<FeedManager> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _cacheManager = cacheManager;
        _logger = logger;
    }

    /// <summary>
    /// Subscribes a user to a podcast feed. Creates the podcast record if unknown,
    /// triggers an immediate out-of-cycle fetch, and creates a subscription row.
    /// </summary>
    public async Task<Podcast> SubscribeAsync(string userId, string feedUrl, CancellationToken ct = default)
    {
        feedUrl = feedUrl.Trim();

        var podcast = await _db.Podcasts
            .FirstOrDefaultAsync(p => p.FeedUrl == feedUrl, ct)
            .ConfigureAwait(false);

        if (podcast is null)
        {
            podcast = new Podcast { FeedUrl = feedUrl };
            _db.Podcasts.Add(podcast);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // Upsert subscription
        var sub = await _db.UserSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.PodcastId == podcast.Id, ct)
            .ConfigureAwait(false);

        if (sub is null)
        {
            sub = new UserSubscription { UserId = userId, PodcastId = podcast.Id };
            _db.UserSubscriptions.Add(sub);
        }
        else
        {
            sub.IsSubscribed = true;
            sub.DeletedAt = null;
            sub.SubscriptionChangedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Trigger immediate fetch
        await FetchPodcastAsync(podcast, ct).ConfigureAwait(false);

        return podcast;
    }

    /// <summary>
    /// Polls all podcasts and upserts episode metadata. Called by <see cref="FeedPollingTask"/>.
    /// </summary>
    public async Task PollAllAsync(CancellationToken ct = default)
    {
        var podcasts = await _db.Podcasts.ToListAsync(ct).ConfigureAwait(false);
        foreach (var podcast in podcasts)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await FetchPodcastAsync(podcast, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch feed for podcast {PodcastId} ({Url})", podcast.Id, podcast.FeedUrl);
            }
        }
    }

    /// <summary>
    /// Fetches a single podcast feed, upserts episodes, and auto-downloads if configured.
    /// </summary>
    public async Task FetchPodcastAsync(Podcast podcast, CancellationToken ct = default)
    {
        var config = PodcastPlugin.Instance?.Configuration ?? new PluginConfiguration();

        try
        {
            var client = _httpClientFactory.CreateClient("PodcastFeedClient");

            // Conditional GET
            var request = new HttpRequestMessage(HttpMethod.Get, podcast.FeedUrl);
            if (podcast.LastFetchedAt.HasValue)
                request.Headers.IfModifiedSince = podcast.LastFetchedAt.Value;

            var response = await client.SendAsync(request, ct).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                _logger.LogDebug("Feed not modified: {Url}", podcast.FeedUrl);
                return;
            }

            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            var feed = FeedReader.ReadFromString(content);

            // Update podcast metadata
            podcast.Title = feed.Title ?? podcast.Title;
            podcast.Description = feed.Description ?? podcast.Description;
            podcast.ImageUrl = feed.ImageUrl ?? podcast.ImageUrl;
            podcast.Language = feed.Language ?? podcast.Language;
            podcast.LastFetchedAt = DateTime.UtcNow;
            podcast.FetchError = null;

            // Extract author from specific fields if available
            if (feed.SpecificFeed is CodeHollow.FeedReader.Feeds.Rss20Feed rss20)
                podcast.Author = rss20.ManagingEditor ?? rss20.WebMaster ?? podcast.Author;

            // Upsert episodes (keyed on guid → enclosure URL fallback)
            var newEpisodes = new List<Episode>();
            foreach (var item in feed.Items)
            {
                var guid = !string.IsNullOrWhiteSpace(item.Id) ? item.Id : item.SpecificItem?.Element?.Element("enclosure")?.Attribute("url")?.Value;
                if (string.IsNullOrWhiteSpace(guid)) continue;

                var enclosureUrl = string.Empty;
                int? durationSeconds = null;
                string? enclosureMime = null;
                long? enclosureLength = null;
                string? episodeImageUrl = null;

                if (item.SpecificItem is CodeHollow.FeedReader.Feeds.MediaRssFeedItem mediaItem)
                {
                    // handled below
                }

                // Extract enclosure from raw XML
                var enclosureEl = item.SpecificItem?.Element?.Element("enclosure");
                if (enclosureEl is not null)
                {
                    enclosureUrl = enclosureEl.Attribute("url")?.Value ?? string.Empty;
                    enclosureMime = enclosureEl.Attribute("type")?.Value;
                    if (long.TryParse(enclosureEl.Attribute("length")?.Value, out var len))
                        enclosureLength = len;
                }

                // iTunes namespace
                var ns = System.Xml.Linq.XNamespace.Get("http://www.itunes.com/dtds/podcast-1.0.dtd");
                var itunesDuration = item.SpecificItem?.Element?.Element(ns + "duration")?.Value;
                if (!string.IsNullOrEmpty(itunesDuration))
                    durationSeconds = ParseItunesDuration(itunesDuration);

                var itunesImage = item.SpecificItem?.Element?.Element(ns + "image")?.Attribute("href")?.Value;
                if (!string.IsNullOrEmpty(itunesImage))
                    episodeImageUrl = itunesImage;

                if (string.IsNullOrWhiteSpace(enclosureUrl)) continue;

                var existing = await _db.Episodes
                    .FirstOrDefaultAsync(e => e.PodcastId == podcast.Id && e.Guid == guid, ct)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    var ep = new Episode
                    {
                        PodcastId = podcast.Id,
                        Guid = guid,
                        Title = item.Title,
                        Description = item.Description,
                        PublishedAt = item.PublishingDate,
                        EnclosureUrl = enclosureUrl,
                        EnclosureMime = enclosureMime,
                        EnclosureLength = enclosureLength,
                        DurationSeconds = durationSeconds,
                        ImageUrl = episodeImageUrl
                    };
                    _db.Episodes.Add(ep);
                    newEpisodes.Add(ep);
                }
                else
                {
                    // Update mutable metadata
                    existing.Title = item.Title ?? existing.Title;
                    existing.Description = item.Description ?? existing.Description;
                    existing.DurationSeconds = durationSeconds ?? existing.DurationSeconds;
                    existing.ImageUrl = episodeImageUrl ?? existing.ImageUrl;
                }
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            // Auto-download most-recent episodes if headroom allows
            if (config.DownloadNewEpisodes && newEpisodes.Count > 0)
            {
                var toDownload = newEpisodes
                    .OrderByDescending(e => e.PublishedAt)
                    .Take(config.AutoDownloadCount);

                foreach (var ep in toDownload)
                {
                    _ = _cacheManager.DownloadEpisodeAsync(ep.Id, CancellationToken.None);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching feed {Url}", podcast.FeedUrl);
            podcast.FetchError = ex.Message;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    private static int ParseItunesDuration(string value)
    {
        // Formats: "HH:MM:SS", "MM:SS", or plain seconds
        var parts = value.Split(':');
        return parts.Length switch
        {
            3 => int.Parse(parts[0]) * 3600 + int.Parse(parts[1]) * 60 + int.Parse(parts[2]),
            2 => int.Parse(parts[0]) * 60 + int.Parse(parts[1]),
            _ => int.TryParse(value, out var s) ? s : 0
        };
    }
}
