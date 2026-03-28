using System;
using System.Collections.Generic;

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
    /// Gets or sets the direct URL to the primary audio enclosure.
    /// </summary>
    public string AudioUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MIME type of the primary audio enclosure (e.g. "audio/mpeg").
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

    // ── Podcast Index namespace fields ──────────────────────────────────────

    /// <summary>
    /// Gets or sets the episode number from <c>podcast:episode</c>.
    /// </summary>
    public double? EpisodeNumber { get; set; }

    /// <summary>
    /// Gets or sets the season number from <c>podcast:season</c>.
    /// </summary>
    public int? SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the season name from the <c>name</c> attribute of <c>podcast:season</c>.
    /// </summary>
    public string? SeasonName { get; set; }

    /// <summary>
    /// Gets or sets the URL of the chapters JSON file from <c>podcast:chapters</c>.
    /// </summary>
    public string? ChaptersUrl { get; set; }

    /// <summary>
    /// Gets or sets the transcripts from <c>podcast:transcript</c> elements.
    /// </summary>
    public IReadOnlyList<ParsedTranscript> Transcripts { get; set; } = [];

    /// <summary>
    /// Gets or sets the alternate enclosures from <c>podcast:alternateEnclosure</c> elements.
    /// </summary>
    public IReadOnlyList<ParsedAlternateEnclosure> AlternateEnclosures { get; set; } = [];

    /// <summary>
    /// Gets or sets the people credited in <c>podcast:person</c> elements.
    /// </summary>
    public IReadOnlyList<ParsedPerson> People { get; set; } = [];
}
