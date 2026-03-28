using System;

namespace Jellyfin.Plugin.Podcasts.FeedParser.Models;

/// <summary>
/// A single parsed episode from a podcast RSS feed.
/// </summary>
public class ParsedEpisode
{
    /// <summary>
    /// Gets or sets the unique episode identifier from the RSS &lt;guid&gt; element,
    /// or a hash of the audio URL when no guid is present.
    /// </summary>
    public string Guid { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the episode title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the episode description / show notes.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the direct URL to the audio enclosure.
    /// </summary>
    public string AudioUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MIME type of the audio enclosure (e.g. "audio/mpeg").
    /// </summary>
    public string? EnclosureType { get; set; }

    /// <summary>
    /// Gets or sets the episode artwork URL, falling back to the feed image when absent.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Gets or sets the publication date.
    /// </summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>
    /// Gets or sets the episode duration in ticks, or null if not specified.
    /// </summary>
    public long? DurationTicks { get; set; }
}
