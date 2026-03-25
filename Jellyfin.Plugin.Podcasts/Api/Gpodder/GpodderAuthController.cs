using Jellyfin.Plugin.Podcasts.Auth;
using Jellyfin.Plugin.Podcasts.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.Podcasts.Api.Gpodder;

/// <summary>
/// gpodder v2 auth shim. Authenticates via app passwords stored in the plugin database.
/// </summary>
[ApiController]
[Route("podcasts/sync/gpodder/api/2/auth/{username}")]
public class GpodderAuthController : ControllerBase
{
    private readonly PodcastDbContext _db;

    public GpodderAuthController(PodcastDbContext db)
    {
        _db = db;
    }

    // POST /podcasts/sync/gpodder/api/2/auth/{username}/login.json
    [HttpPost("login.json")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(string username, CancellationToken ct)
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return Unauthorized();

        var (authenticatedUser, _) = await AppPasswordAuthenticator
            .AuthenticateBasicAsync(_db, authHeader.ToString(), ct)
            .ConfigureAwait(false);

        if (authenticatedUser is null || authenticatedUser != username)
            return Unauthorized();

        // gpodder login simply returns an empty JSON object on success
        return Ok(new { });
    }

    // POST /podcasts/sync/gpodder/api/2/auth/{username}/logout.json
    [HttpPost("logout.json")]
    [AllowAnonymous]
    public IActionResult Logout(string username)
    {
        // Stateless — nothing to invalidate
        return Ok(new { });
    }
}
