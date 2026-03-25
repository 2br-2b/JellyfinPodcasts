using System.Text;
using Jellyfin.Plugin.Podcasts.Database;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.Podcasts.Auth;

/// <summary>
/// Helper that validates HTTP Basic Auth credentials against the <c>app_passwords</c> table.
/// </summary>
public static class AppPasswordAuthenticator
{
    /// <summary>
    /// Parses an HTTP Basic Authorization header and validates the password against stored bcrypt hashes.
    /// </summary>
    /// <returns>
    /// A tuple of (username, userId) if authentication succeeds, or (null, null) on failure.
    /// </returns>
    public static async Task<(string? Username, string? UserId)> AuthenticateBasicAsync(
        PodcastDbContext db,
        string authorizationHeader,
        CancellationToken ct = default)
    {
        if (!authorizationHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return (null, null);

        string credentials;
        try
        {
            credentials = Encoding.UTF8.GetString(Convert.FromBase64String(authorizationHeader[6..].Trim()));
        }
        catch
        {
            return (null, null);
        }

        var colonIndex = credentials.IndexOf(':');
        if (colonIndex < 0) return (null, null);

        var username = credentials[..colonIndex];
        var password = credentials[(colonIndex + 1)..];

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return (null, null);

        // Retrieve all app passwords for this username (there may be multiple devices)
        var candidates = await db.AppPasswords
            .Where(ap => ap.UserId == username)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var ap in candidates)
        {
            if (BCrypt.Net.BCrypt.Verify(password, ap.PasswordHash))
            {
                // Update last_used_at
                ap.LastUsedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                return (username, ap.UserId);
            }
        }

        return (null, null);
    }
}
