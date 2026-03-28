using System.Collections.Generic;

namespace Jellyfin.Plugin.Podcasts.FeedParser.Models;

/// <summary>
/// Feed-level metadata returned from a parsed RSS/Atom document.
/// </summary>
public class ParsedFeed
{
    /// <summary>
    /// Gets or sets the feed title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the feed description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the cover image URL.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Gets or sets the podcast home page URL.
    /// </summary>
    public string? HomePageUrl { get; set; }

    /// <summary>
    /// Gets or sets the episodes contained in this feed.
    /// </summary>
    public IReadOnlyList<ParsedEpisode> Episodes { get; set; } = [];
}
