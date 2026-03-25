using Jellyfin.Plugin.Podcasts.Cache;
using Jellyfin.Plugin.Podcasts.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Podcasts.Feed;

/// <summary>
/// Scheduled task that polls all podcast RSS feeds on a configurable interval.
/// </summary>
public class FeedPollingTask : IScheduledTask
{
    private readonly FeedManager _feedManager;
    private readonly CacheEvictionTask _evictionTask;
    private readonly ILogger<FeedPollingTask> _logger;

    public FeedPollingTask(
        FeedManager feedManager,
        CacheEvictionTask evictionTask,
        ILogger<FeedPollingTask> logger)
    {
        _feedManager = feedManager;
        _evictionTask = evictionTask;
        _logger = logger;
    }

    public string Name => "Poll Podcast Feeds";
    public string Key => "PodcastFeedPolling";
    public string Description => "Fetches all subscribed podcast RSS feeds and updates episode metadata.";
    public string Category => "Podcasts";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting podcast feed poll");
        progress.Report(0);

        await _feedManager.PollAllAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(80);

        // Run eviction after poll in case new downloads pushed over quota
        await _evictionTask.ExecuteAsync(progress, cancellationToken).ConfigureAwait(false);
        progress.Report(100);

        _logger.LogInformation("Podcast feed poll complete");
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var config = PodcastPlugin.Instance?.Configuration ?? new PluginConfiguration();
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerInterval,
            IntervalTicks = TimeSpan.FromMinutes(config.PollIntervalMinutes).Ticks
        };
    }
}
