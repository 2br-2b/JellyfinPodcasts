using Jellyfin.Plugin.Podcasts.Auth;
using Jellyfin.Plugin.Podcasts.Database;
using Jellyfin.Plugin.Podcasts.Feed;
using Jellyfin.Plugin.Podcasts.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.Podcasts.Api.Opa;

/// <summary>
/// Implements the OPA Subscriptions specification.
/// </summary>
[ApiController]
[Route("podcasts/sync/opa/subscriptions")]
[Authorize]
public class OpaSubscriptionsController : ControllerBase
{
    private readonly PodcastDbContext _db;
    private readonly FeedManager _feedManager;

    public OpaSubscriptionsController(PodcastDbContext db, FeedManager feedManager)
    {
        _db = db;
        _feedManager = feedManager;
    }

    private string UserId => User.FindFirst("sub")?.Value
        ?? User.FindFirst("nameid")?.Value
        ?? string.Empty;

    // GET /podcasts/sync/opa/subscriptions[?since=ISO8601]
    [HttpGet]
    public async Task<IActionResult> GetSubscriptions([FromQuery] DateTime? since, CancellationToken ct)
    {
        var query = _db.UserSubscriptions
            .Where(s => s.UserId == UserId)
            .Include(s => s.Podcast)
            .AsQueryable();

        if (since.HasValue)
            query = query.Where(s => s.SubscriptionChangedAt >= since.Value);

        var subs = await query.ToListAsync(ct).ConfigureAwait(false);
        return Ok(subs.Select(ToDto));
    }

    // POST /podcasts/sync/opa/subscriptions
    [HttpPost]
    public async Task<IActionResult> AddSubscription([FromBody] OpaSubscriptionRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.FeedUrl))
            return BadRequest("feed_url is required");

        var podcast = await _feedManager.SubscribeAsync(UserId, body.FeedUrl, ct).ConfigureAwait(false);

        var sub = await _db.UserSubscriptions
            .Include(s => s.Podcast)
            .FirstOrDefaultAsync(s => s.UserId == UserId && s.PodcastId == podcast.Id, ct)
            .ConfigureAwait(false);

        return sub is null ? StatusCode(500) : CreatedAtAction(nameof(GetSubscription), new { guid = podcast.Guid ?? podcast.Id.ToString() }, ToDto(sub));
    }

    // GET /podcasts/sync/opa/subscriptions/{guid}
    [HttpGet("{guid}")]
    public async Task<IActionResult> GetSubscription(string guid, CancellationToken ct)
    {
        var sub = await FindByGuidAsync(guid, ct).ConfigureAwait(false);
        return sub is null ? NotFound() : Ok(ToDto(sub));
    }

    // PATCH /podcasts/sync/opa/subscriptions/{guid}
    [HttpPatch("{guid}")]
    public async Task<IActionResult> UpdateSubscription(string guid, [FromBody] OpaSubscriptionPatch patch, CancellationToken ct)
    {
        var sub = await FindByGuidAsync(guid, ct).ConfigureAwait(false);
        if (sub is null) return NotFound();

        if (patch.IsSubscribed.HasValue)
        {
            sub.IsSubscribed = patch.IsSubscribed.Value;
            sub.SubscriptionChangedAt = DateTime.UtcNow;
            if (!patch.IsSubscribed.Value)
                sub.DeletedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(sub));
    }

    // DELETE /podcasts/sync/opa/subscriptions/{guid}
    [HttpDelete("{guid}")]
    public async Task<IActionResult> DeleteSubscription(string guid, CancellationToken ct)
    {
        var sub = await FindByGuidAsync(guid, ct).ConfigureAwait(false);
        if (sub is null) return NotFound();

        sub.IsSubscribed = false;
        sub.DeletedAt = DateTime.UtcNow;
        sub.SubscriptionChangedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

    // GET /podcasts/sync/opa/subscriptions/deletions?since=ISO8601
    [HttpGet("deletions")]
    public async Task<IActionResult> GetDeletions([FromQuery] DateTime? since, CancellationToken ct)
    {
        var query = _db.UserSubscriptions
            .Where(s => s.UserId == UserId && s.DeletedAt != null)
            .Include(s => s.Podcast)
            .AsQueryable();

        if (since.HasValue)
            query = query.Where(s => s.DeletedAt >= since.Value);

        var subs = await query.ToListAsync(ct).ConfigureAwait(false);
        return Ok(subs.Select(ToDto));
    }

    private async Task<UserSubscription?> FindByGuidAsync(string guid, CancellationToken ct)
    {
        // Try matching by podcast.Guid first, then by podcast.Id
        return await _db.UserSubscriptions
            .Include(s => s.Podcast)
            .FirstOrDefaultAsync(s =>
                s.UserId == UserId &&
                (s.Podcast!.Guid == guid || s.Podcast.Id.ToString() == guid), ct)
            .ConfigureAwait(false);
    }

    private static object ToDto(UserSubscription s) => new
    {
        feed_url = s.Podcast?.FeedUrl,
        guid = s.Podcast?.Guid ?? s.Podcast?.Id.ToString(),
        is_subscribed = s.IsSubscribed,
        subscription_changed = s.SubscriptionChangedAt.ToString("O"),
        guid_changed = (string?)null,
        new_guid = (string?)null,
        deleted = s.DeletedAt?.ToString("O")
    };
}

public class OpaSubscriptionRequest
{
    public string FeedUrl { get; set; } = string.Empty;
}

public class OpaSubscriptionPatch
{
    public bool? IsSubscribed { get; set; }
}
