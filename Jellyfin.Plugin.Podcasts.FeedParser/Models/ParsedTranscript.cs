namespace Jellyfin.Plugin.Podcasts.FeedParser.Models;

/// <summary>
/// A transcript linked from a <c>podcast:transcript</c> element.
/// </summary>
public class ParsedTranscript
{
    /// <summary>
    /// Gets or sets the transcript URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MIME type (e.g. "text/plain", "text/vtt", "application/json").
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the BCP-47 language tag, if specified.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets the relationship (e.g. "captions").
    /// </summary>
    public string? Rel { get; set; }
}
