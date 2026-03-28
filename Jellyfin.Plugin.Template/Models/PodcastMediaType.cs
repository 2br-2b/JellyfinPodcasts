namespace Jellyfin.Plugin.Template.Models;

/// <summary>
/// Distinguishes between audio-only podcasts and video podcasts (vodcasts).
/// </summary>
public enum PodcastMediaType
{
    /// <summary>
    /// An audio podcast.
    /// </summary>
    Audio,

    /// <summary>
    /// A video podcast (vodcast).
    /// </summary>
    Video,
}
