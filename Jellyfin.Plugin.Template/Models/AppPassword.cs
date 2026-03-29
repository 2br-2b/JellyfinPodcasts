using System;

namespace Jellyfin.Plugin.Template.Models;

/// <summary>App password for external podcast clients that don't support Jellyfin SSO.</summary>
public class AppPassword
{
    /// <summary>Gets or sets the unique identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the Jellyfin user this password belongs to.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the human-readable label (e.g. "AntennaPod on Pixel 8").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the app password kind.</summary>
    public string Kind { get; set; } = AppPasswordKinds.OpenPodcastApi;

    /// <summary>Gets or sets the SHA-256 hex digest of the plaintext token.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Gets or sets when this password was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets when this password was last used, or null if never used.</summary>
    public DateTime? LastUsedAt { get; set; }
}
