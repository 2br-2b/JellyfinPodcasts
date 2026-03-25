using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Podcasts.Cache;

/// <summary>
/// Scheduled task that runs LRU cache eviction.
/// Also runs automatically after each feed poll (invoked directly by <see cref="Feed.FeedPollingTask"/>).
/// </summary>
public class CacheEvictionTask : IScheduledTask
{
    private readonly CacheManager _cacheManager;
    private readonly ILogger<CacheEvictionTask> _logger;

    public CacheEvictionTask(CacheManager cacheManager, ILogger<CacheEvictionTask> logger)
    {
        _cacheManager = cacheManager;
        _logger = logger;
    }

    public string Name => "Podcast Cache Eviction";
    public string Key => "PodcastCacheEviction";
    public string Description => "Evicts oldest podcast episode audio files to stay within the configured cache quota.";
    public string Category => "Podcasts";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Running podcast cache eviction");
        progress.Report(0);
        await _cacheManager.EvictAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // No automatic schedule; triggered after feed poll or manually
        yield break;
    }
}
