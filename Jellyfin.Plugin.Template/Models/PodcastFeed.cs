using System.Collections.Generic;

namespace Jellyfin.Plugin.Template.Models;

/// <summary>
/// Represents a podcast feed (shared metadata, independent of any user subscription).
/// </summary>
public class PodcastFeed
{
    /// <summary>
    /// Gets or sets the stable unique identifier for this feed (derived from the feed URL).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the RSS/Atom feed URL.
    /// </summary>
    public string FeedUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display title of the podcast.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the podcast description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL of the podcast's cover image.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Gets or sets the podcast home page URL.
    /// </summary>
    public string? HomePageUrl { get; set; }

    /// <summary>
    /// Gets or sets whether this is an audio podcast or a video podcast (vodcast).
    /// Defaults to <see cref="PodcastMediaType.Audio"/>.
    /// </summary>
    public PodcastMediaType MediaType { get; set; } = PodcastMediaType.Audio;

    /// <summary>
    /// Gets the subscriptions for this feed.
    /// </summary>
    public ICollection<UserSubscription> Subscriptions { get; init; } = [];
}
