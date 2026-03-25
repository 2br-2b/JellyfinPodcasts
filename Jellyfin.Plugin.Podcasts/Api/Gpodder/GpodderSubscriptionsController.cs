using Jellyfin.Plugin.Podcasts.Auth;
using Jellyfin.Plugin.Podcasts.Database;
using Jellyfin.Plugin.Podcasts.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.Podcasts.Api.Gpodder;

/// <summary>
/// gpodder v2 subscriptions shim.
/// Maps gpodder protocol calls to <c>user_subscriptions</c>.
/// </summary>
[ApiController]
[Route("podcasts/sync/gpodder/api/2/subscriptions/{username}")]
public class GpodderSubscriptionsController : ControllerBase
{
    private readonly PodcastDbContext _db;
    private readonly FeedManager _feedManager;

    public GpodderSubscriptionsController(PodcastDbContext db, FeedManager feedManager)
    {
        _db = db;
        _feedManager = feedManager;
    }

    // GET /podcasts/sync/gpodder/api/2/subscriptions/{username}/{deviceid}.json
    [HttpGet("{deviceid}.json")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSubscriptions(string username, string deviceid, CancellationToken ct)
    {
        var userId = await AuthenticateAsync(username, ct).ConfigureAwait(false);
        if (userId is null) return Unauthorized();

        var subs = await _db.UserSubscriptions
            .Where(s => s.UserId == userId && s.IsSubscribed && s.DeletedAt == null)
            .Include(s => s.Podcast)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var urls = subs.Select(s => s.Podcast?.FeedUrl).Where(u => u != null).ToList();
        return Ok(urls);
    }

    // POST /podcasts/sync/gpodder/api/2/subscriptions/{username}/{deviceid}.json
    [HttpPost("{deviceid}.json")]
    [AllowAnonymous]
    public async Task<IActionResult> UpdateSubscriptions(
        string username, string deviceid,
        [FromBody] GpodderSubscriptionUpdate body,
        CancellationToken ct)
    {
        var userId = await AuthenticateAsync(username, ct).ConfigureAwait(false);
        if (userId is null) return Unauthorized();

        foreach (var url in body.Add ?? Enumerable.Empty<string>())
        {
            await _feedManager.SubscribeAsync(userId, url, ct).ConfigureAwait(false);
        }

        foreach (var url in body.Remove ?? Enumerable.Empty<string>())
        {
            var podcast = await _db.Podcasts.FirstOrDefaultAsync(p => p.FeedUrl == url, ct).ConfigureAwait(false);
            if (podcast is null) continue;

            var sub = await _db.UserSubscriptions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.PodcastId == podcast.Id, ct)
                .ConfigureAwait(false);

            if (sub is not null)
            {
                sub.IsSubscribed = false;
                sub.DeletedAt = DateTime.UtcNow;
                sub.SubscriptionChangedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Ok(new
        {
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            update_urls = new object[] { }
        });
    }

    private async Task<string?> AuthenticateAsync(string username, CancellationToken ct)
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return null;

        var (authenticatedUser, userId) = await AppPasswordAuthenticator
            .AuthenticateBasicAsync(_db, authHeader.ToString(), ct)
            .ConfigureAwait(false);

        return authenticatedUser == username ? userId : null;
    }
}

public class GpodderSubscriptionUpdate
{
    public List<string>? Add { get; set; }
    public List<string>? Remove { get; set; }
}
