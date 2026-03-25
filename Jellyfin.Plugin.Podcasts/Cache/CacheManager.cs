using Jellyfin.Plugin.Podcasts.Configuration;
using Jellyfin.Plugin.Podcasts.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Podcasts.Cache;

/// <summary>
/// Manages downloading, pinning, and evicting podcast episode audio files.
/// </summary>
public class CacheManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CacheManager> _logger;

    // Semaphore to prevent concurrent downloads of the same episode
    private static readonly SemaphoreSlim _downloadSemaphore = new(4, 4);

    public CacheManager(
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<CacheManager> logger)
    {
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string CacheRoot
    {
        get
        {
            var plugin = PodcastPlugin.Instance;
            if (plugin is null) throw new InvalidOperationException("Plugin not initialized");
            return Path.Combine(Path.GetDirectoryName(plugin.GetDatabasePath())!, "cache");
        }
    }

    /// <summary>
    /// Downloads an episode audio file to the local cache.
    /// </summary>
    public async Task DownloadEpisodeAsync(Guid episodeId, CancellationToken ct)
    {
        await _downloadSemaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PodcastDbContext>();

            var episode = await db.Episodes.FindAsync(new object[] { episodeId }, ct).ConfigureAwait(false);
            if (episode is null || episode.IsCached) return;

            var config = PodcastPlugin.Instance?.Configuration ?? new PluginConfiguration();

            // Check quota before downloading
            var currentSize = await db.Episodes
                .Where(e => e.IsCached)
                .SumAsync(e => (long)(e.CachedSizeBytes ?? 0), ct)
                .ConfigureAwait(false);

            long quotaBytes = (long)config.MaxCacheSizeGb * 1024 * 1024 * 1024;
            var estimatedSize = episode.EnclosureLength ?? 50L * 1024 * 1024; // 50 MB default estimate

            if (currentSize + estimatedSize > quotaBytes)
            {
                _logger.LogWarning("Cache quota exceeded; skipping auto-download of episode {EpisodeId}", episodeId);
                return;
            }

            Directory.CreateDirectory(CacheRoot);

            var ext = GetExtension(episode.EnclosureMime, episode.EnclosureUrl);
            var relativePath = Path.Combine(episode.PodcastId.ToString(), $"{episode.Id}{ext}");
            var fullPath = Path.Combine(CacheRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            var client = _httpClientFactory.CreateClient("PodcastFeedClient");
            using var response = await client.GetAsync(episode.EnclosureUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var file = File.Create(fullPath);
            await stream.CopyToAsync(file, ct).ConfigureAwait(false);

            var info = new FileInfo(fullPath);
            episode.IsCached = true;
            episode.CachedAt = DateTime.UtcNow;
            episode.CachedSizeBytes = info.Length;
            episode.LocalPath = relativePath;

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Cached episode {EpisodeId} → {Path}", episodeId, fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download episode {EpisodeId}", episodeId);
        }
        finally
        {
            _downloadSemaphore.Release();
        }
    }

    /// <summary>
    /// Pins an episode, exempting it from LRU eviction.
    /// </summary>
    public async Task PinEpisodeAsync(Guid episodeId, CancellationToken ct = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PodcastDbContext>();
        var episode = await db.Episodes.FindAsync(new object[] { episodeId }, ct).ConfigureAwait(false);
        if (episode is null) return;
        episode.IsPinned = true;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Unpins an episode; it becomes eligible for future eviction but is not evicted immediately.
    /// </summary>
    public async Task UnpinEpisodeAsync(Guid episodeId, CancellationToken ct = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PodcastDbContext>();
        var episode = await db.Episodes.FindAsync(new object[] { episodeId }, ct).ConfigureAwait(false);
        if (episode is null) return;
        episode.IsPinned = false;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs LRU eviction until total cache size is within quota.
    /// Pinned episodes are never evicted.
    /// </summary>
    public async Task EvictAsync(CancellationToken ct = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PodcastDbContext>();
        var config = PodcastPlugin.Instance?.Configuration ?? new PluginConfiguration();

        long quotaBytes = (long)config.MaxCacheSizeGb * 1024 * 1024 * 1024;

        var currentSize = await db.Episodes
            .Where(e => e.IsCached)
            .SumAsync(e => (long)(e.CachedSizeBytes ?? 0), ct)
            .ConfigureAwait(false);

        if (currentSize <= quotaBytes) return;

        // Eviction candidates: cached, not pinned, ordered oldest-cached-first (LRU)
        var candidates = await db.Episodes
            .Where(e => e.IsCached && !e.IsPinned)
            .OrderBy(e => e.CachedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var episode in candidates)
        {
            if (currentSize <= quotaBytes) break;

            var fullPath = episode.LocalPath is not null
                ? Path.Combine(CacheRoot, episode.LocalPath)
                : null;

            if (fullPath is not null && File.Exists(fullPath))
            {
                try { File.Delete(fullPath); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete cache file {Path}", fullPath);
                }
            }

            currentSize -= episode.CachedSizeBytes ?? 0;
            episode.IsCached = false;
            episode.CachedAt = null;
            episode.CachedSizeBytes = null;
            episode.LocalPath = null;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Cache eviction complete. Current size: {Size} bytes", currentSize);
    }

    /// <summary>
    /// Returns the absolute path to a cached episode file, or null if not cached.
    /// </summary>
    public string? GetCachedPath(string relativePath)
    {
        var full = Path.Combine(CacheRoot, relativePath);
        return File.Exists(full) ? full : null;
    }

    private static string GetExtension(string? mime, string url)
    {
        return mime switch
        {
            "audio/mpeg" => ".mp3",
            "audio/mp4" => ".m4a",
            "audio/ogg" => ".ogg",
            "audio/opus" => ".opus",
            "audio/flac" => ".flac",
            "audio/aac" => ".aac",
            "audio/wav" => ".wav",
            _ => Path.GetExtension(new Uri(url).AbsolutePath) is { Length: > 0 } ext ? ext : ".mp3"
        };
    }
}
