using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Podcasts.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Maximum total disk usage for cached episode audio, in gigabytes.</summary>
    public int MaxCacheSizeGb { get; set; } = 20;

    /// <summary>Optional per-feed cap on cached episodes. Null means no cap.</summary>
    public int? MaxEpisodesPerPodcast { get; set; }

    /// <summary>Automatically download newly discovered episodes.</summary>
    public bool DownloadNewEpisodes { get; set; } = true;

    /// <summary>How many of the most recent episodes to auto-download per feed.</summary>
    public int AutoDownloadCount { get; set; } = 3;

    /// <summary>Feed polling interval in minutes.</summary>
    public int PollIntervalMinutes { get; set; } = 60;
}
