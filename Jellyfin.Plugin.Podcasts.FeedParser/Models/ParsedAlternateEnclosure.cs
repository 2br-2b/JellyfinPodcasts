using System.Collections.Generic;

namespace Jellyfin.Plugin.Podcasts.FeedParser.Models;

/// <summary>
/// An alternative audio/video source from a <c>podcast:alternateEnclosure</c> element.
/// Only HTTP(S) source URIs are retained; IPFS and other non-HTTP schemes are discarded.
/// </summary>
public class ParsedAlternateEnclosure
{
    /// <summary>
    /// Gets or sets the MIME type (e.g. "audio/opus").
    /// </summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bitrate in bits per second, if specified.
    /// </summary>
    public double? Bitrate { get; set; }

    /// <summary>
    /// Gets or sets the file size in bytes, if specified.
    /// </summary>
    public long? Length { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the default/recommended enclosure.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Gets or sets a human-readable title for this quality variant (e.g. "High quality").
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the HTTP(S) source URIs for this enclosure.
    /// </summary>
    public IReadOnlyList<string> HttpSources { get; set; } = [];
}
