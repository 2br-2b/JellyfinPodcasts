using Jellyfin.Plugin.Podcasts.Cache;
using Jellyfin.Plugin.Podcasts.Configuration;
using Jellyfin.Plugin.Podcasts.Database;
using Jellyfin.Plugin.Podcasts.Feed;
using Jellyfin.Plugin.Podcasts.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.Podcasts.Api.Management;

/// <summary>
/// Internal management API used exclusively by the plugin dashboard UI.
/// </summary>
[ApiController]
[Route("podcasts/management")]
[Authorize]
public class ManagementController : ControllerBase
{
    private readonly PodcastDbContext _db;
    private readonly FeedManager _feedManager;
    private readonly CacheManager _cacheManager;

    public ManagementController(PodcastDbContext db, FeedManager feedManager, CacheManager cacheManager)
    {
        _db = db;
        _feedManager = feedManager;
        _cacheManager = cacheManager;
    }

    private string UserId => User.FindFirst("sub")?.Value
        ?? User.FindFirst("nameid")?.Value
        ?? string.Empty;

    // ── Subscriptions ─────────────────────────────────────────────────────────

    [HttpGet("subscriptions")]
    public async Task<IActionResult> GetSubscriptions(CancellationToken ct)
    {
        var mgr = new PodcastLibraryManager(_db, _cacheManager,
            HttpContext.RequestServices.GetRequiredService<Cache.OnDemandDownloader>(),
            HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PodcastLibraryManager>>());

        var podcasts = await mgr.GetPodcastsAsync(UserId, ct).ConfigureAwait(false);
        return Ok(podcasts.Select(p => new
        {
            id = p.Id,
            title = p.Title,
            author = p.Author,
            image_url = p.ImageUrl,
            feed_url = _db.Podcasts.Where(pod => pod.Id == p.Id).Select(pod => pod.FeedUrl).FirstOrDefault(),
            latest_episode_date = p.LatestEpisodeDate,
            unplayed_count = p.UnplayedCount
        }));
    }

    [HttpPost("subscriptions")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.FeedUrl)) return BadRequest();
        var podcast = await _feedManager.SubscribeAsync(UserId, body.FeedUrl, ct).ConfigureAwait(false);
        return Ok(new { id = podcast.Id, title = podcast.Title, feed_url = podcast.FeedUrl });
    }

    [HttpPost("subscriptions/{id:guid}/refresh")]
    public async Task<IActionResult> RefreshFeed(Guid id, CancellationToken ct)
    {
        var podcast = await _db.Podcasts.FindAsync(new object[] { id }, ct).ConfigureAwait(false);
        if (podcast is null) return NotFound();
        await _feedManager.FetchPodcastAsync(podcast, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpPost("refresh-all")]
    public async Task<IActionResult> RefreshAll(CancellationToken ct)
    {
        await _feedManager.PollAllAsync(ct).ConfigureAwait(false);
        return Ok();
    }

    // ── Cache ─────────────────────────────────────────────────────────────────

    [HttpGet("cache")]
    public async Task<IActionResult> GetCache(CancellationToken ct)
    {
        var config = PodcastPlugin.Instance?.Configuration ?? new PluginConfiguration();
        long quotaBytes = (long)config.MaxCacheSizeGb * 1024 * 1024 * 1024;

        var cached = await _db.Episodes
            .Where(e => e.IsCached)
            .Include(e => e.Podcast)
            .OrderBy(e => e.CachedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        long usedBytes = cached.Sum(e => e.CachedSizeBytes ?? 0);

        return Ok(new
        {
            used_bytes = usedBytes,
            quota_bytes = quotaBytes,
            episodes = cached.Select(e => new
            {
                id = e.Id,
                title = e.Title,
                podcast_title = e.Podcast?.Title,
                cached_size_bytes = e.CachedSizeBytes,
                cached_at = e.CachedAt,
                is_pinned = e.IsPinned
            })
        });
    }

    [HttpPost("cache/{id:guid}/pin")]
    public async Task<IActionResult> PinEpisode(Guid id, CancellationToken ct)
    {
        await _cacheManager.PinEpisodeAsync(id, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpPost("cache/{id:guid}/unpin")]
    public async Task<IActionResult> UnpinEpisode(Guid id, CancellationToken ct)
    {
        await _cacheManager.UnpinEpisodeAsync(id, ct).ConfigureAwait(false);
        return Ok();
    }

    [HttpPost("cache/evict")]
    public async Task<IActionResult> Evict(CancellationToken ct)
    {
        await _cacheManager.EvictAsync(ct).ConfigureAwait(false);
        return Ok();
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    [HttpGet("settings")]
    public IActionResult GetSettings()
    {
        var cfg = PodcastPlugin.Instance?.Configuration ?? new PluginConfiguration();
        return Ok(new
        {
            max_cache_size_gb = cfg.MaxCacheSizeGb,
            max_episodes_per_podcast = cfg.MaxEpisodesPerPodcast,
            poll_interval_minutes = cfg.PollIntervalMinutes,
            auto_download_count = cfg.AutoDownloadCount,
            download_new_episodes = cfg.DownloadNewEpisodes
        });
    }

    [HttpPut("settings")]
    public IActionResult SaveSettings([FromBody] SettingsRequest body)
    {
        var plugin = PodcastPlugin.Instance;
        if (plugin is null) return StatusCode(500, "Plugin not initialized");

        var cfg = plugin.Configuration;
        cfg.MaxCacheSizeGb = body.MaxCacheSizeGb;
        cfg.MaxEpisodesPerPodcast = body.MaxEpisodesPerPodcast;
        cfg.PollIntervalMinutes = body.PollIntervalMinutes;
        cfg.AutoDownloadCount = body.AutoDownloadCount;
        cfg.DownloadNewEpisodes = body.DownloadNewEpisodes;
        plugin.SaveConfiguration();

        return Ok();
    }
}

public class SubscribeRequest
{
    public string FeedUrl { get; set; } = string.Empty;
}

public class SettingsRequest
{
    public int MaxCacheSizeGb { get; set; } = 20;
    public int? MaxEpisodesPerPodcast { get; set; }
    public int PollIntervalMinutes { get; set; } = 60;
    public int AutoDownloadCount { get; set; } = 3;
    public bool DownloadNewEpisodes { get; set; } = true;
}
