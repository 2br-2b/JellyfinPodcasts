using Jellyfin.Plugin.Podcasts.Cache;
using Jellyfin.Plugin.Podcasts.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Podcasts.Library;

/// <summary>
/// Bridges the plugin's SQLite podcast/episode records into Jellyfin's virtual media library.
/// Exposes HTTP endpoints used by the Jellyfin web UI to browse podcasts and stream episodes.
/// </summary>
public class PodcastLibraryManager
{
    private readonly PodcastDbContext _db;
    private readonly CacheManager _cacheManager;
    private readonly OnDemandDownloader _downloader;
    private readonly ILogger<PodcastLibraryManager> _logger;

    public PodcastLibraryManager(
        PodcastDbContext db,
        CacheManager cacheManager,
        OnDemandDownloader downloader,
        ILogger<PodcastLibraryManager> logger)
    {
        _db = db;
        _cacheManager = cacheManager;
        _downloader = downloader;
        _logger = logger;
    }

    /// <summary>
    /// Returns a lightweight DTO list of all podcasts for the library view.
    /// </summary>
    public async Task<IReadOnlyList<PodcastDto>> GetPodcastsAsync(string userId, CancellationToken ct = default)
    {
        var subscriptions = await _db.UserSubscriptions
            .Where(s => s.UserId == userId && s.IsSubscribed && s.DeletedAt == null)
            .Include(s => s.Podcast)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var result = new List<PodcastDto>();
        foreach (var sub in subscriptions)
        {
            if (sub.Podcast is null) continue;

            var unplayed = await _db.Episodes
                .Where(e => e.PodcastId == sub.PodcastId)
                .CountAsync(e => !_db.UserEpisodeStates
                    .Any(s => s.UserId == userId && s.EpisodeId == e.Id && s.IsPlayed), ct)
                .ConfigureAwait(false);

            var latest = await _db.Episodes
                .Where(e => e.PodcastId == sub.PodcastId)
                .MaxAsync(e => (DateTime?)e.PublishedAt, ct)
                .ConfigureAwait(false);

            result.Add(new PodcastDto
            {
                Id = sub.Podcast.Id,
                Title = sub.Podcast.Title ?? sub.Podcast.FeedUrl,
                Description = sub.Podcast.Description,
                ImageUrl = sub.Podcast.ImageUrl,
                Author = sub.Podcast.Author,
                LatestEpisodeDate = latest,
                UnplayedCount = unplayed
            });
        }

        return result;
    }

    /// <summary>
    /// Returns all episodes for a given podcast, newest first.
    /// </summary>
    public async Task<IReadOnlyList<EpisodeDto>> GetEpisodesAsync(Guid podcastId, string userId, CancellationToken ct = default)
    {
        var episodes = await _db.Episodes
            .Where(e => e.PodcastId == podcastId)
            .OrderByDescending(e => e.PublishedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var states = await _db.UserEpisodeStates
            .Where(s => s.UserId == userId && episodes.Select(e => e.Id).Contains(s.EpisodeId))
            .ToDictionaryAsync(s => s.EpisodeId, ct)
            .ConfigureAwait(false);

        return episodes.Select(ep =>
        {
            states.TryGetValue(ep.Id, out var state);
            return new EpisodeDto
            {
                Id = ep.Id,
                PodcastId = podcastId,
                Title = ep.Title ?? "(untitled)",
                Description = ep.Description,
                PublishedAt = ep.PublishedAt,
                DurationSeconds = ep.DurationSeconds,
                ImageUrl = ep.ImageUrl,
                IsCached = ep.IsCached,
                IsPinned = ep.IsPinned,
                PositionSeconds = state?.PositionSeconds ?? 0,
                IsPlayed = state?.IsPlayed ?? false,
                EnclosureUrl = ep.EnclosureUrl
            };
        }).ToList();
    }

    /// <summary>
    /// Streams an episode to the HTTP response. Serves from cache or proxies from origin.
    /// </summary>
    public async Task StreamEpisodeAsync(Guid episodeId, HttpResponse response, CancellationToken ct)
    {
        var (stream, contentType, contentLength) = await _downloader
            .GetStreamAsync(episodeId, ct)
            .ConfigureAwait(false);

        response.ContentType = contentType ?? "audio/mpeg";
        if (contentLength.HasValue)
            response.ContentLength = contentLength.Value;

        await using (stream)
        {
            await stream.CopyToAsync(response.Body, ct).ConfigureAwait(false);
        }
    }
}

public record PodcastDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? ImageUrl { get; init; }
    public string? Author { get; init; }
    public DateTime? LatestEpisodeDate { get; init; }
    public int UnplayedCount { get; init; }
}

public record EpisodeDto
{
    public Guid Id { get; init; }
    public Guid PodcastId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTime? PublishedAt { get; init; }
    public int? DurationSeconds { get; init; }
    public string? ImageUrl { get; init; }
    public bool IsCached { get; init; }
    public bool IsPinned { get; init; }
    public int PositionSeconds { get; init; }
    public bool IsPlayed { get; init; }
    public string EnclosureUrl { get; init; } = string.Empty;
}
