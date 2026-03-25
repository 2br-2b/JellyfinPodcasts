using System.Text;
using Jellyfin.Plugin.Podcasts.Database;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Podcasts.Auth;

/// <summary>
/// Jellyfin <see cref="IAuthenticationProvider"/> that validates HTTP Basic Auth app-passwords.
/// Only active for requests routed to <c>/podcasts/sync/</c>.
/// </summary>
public class AppPasswordAuthProvider : IAuthenticationProvider
{
    private readonly PodcastDbContext _db;
    private readonly ILogger<AppPasswordAuthProvider> _logger;

    public AppPasswordAuthProvider(PodcastDbContext db, ILogger<AppPasswordAuthProvider> logger)
    {
        _db = db;
        _logger = logger;
    }

    public string Name => "PodcastAppPassword";

    public bool IsEnabled => true;

    public async Task<ProviderAuthenticationResult> Authenticate(string username, string password)
    {
        var candidates = await _db.AppPasswords
            .Where(ap => ap.UserId == username)
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (var ap in candidates)
        {
            if (BCrypt.Net.BCrypt.Verify(password, ap.PasswordHash))
            {
                ap.LastUsedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync().ConfigureAwait(false);

                return new ProviderAuthenticationResult { Username = username };
            }
        }

        throw new AuthenticationException("Invalid app password");
    }

    public bool HasPassword(MediaBrowser.Controller.Entities.User user) => true;

    public Task ChangePassword(MediaBrowser.Controller.Entities.User user, string newPassword)
        => throw new NotSupportedException("App passwords are managed through the podcast plugin management UI");
}
