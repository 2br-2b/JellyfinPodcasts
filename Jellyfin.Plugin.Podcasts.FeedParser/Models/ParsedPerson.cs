namespace Jellyfin.Plugin.Podcasts.FeedParser.Models;

/// <summary>
/// A person credited in a <c>podcast:person</c> element.
/// </summary>
public class ParsedPerson
{
    /// <summary>
    /// Gets or sets the person's display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the role (e.g. "host", "guest", "cover art designer"), or null if not specified.
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Gets or sets the group (e.g. "cast", "visuals"), or null if not specified.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Gets or sets the URL of the person's profile page.
    /// </summary>
    public string? Href { get; set; }

    /// <summary>
    /// Gets or sets the URL of the person's avatar image.
    /// </summary>
    public string? Img { get; set; }
}
