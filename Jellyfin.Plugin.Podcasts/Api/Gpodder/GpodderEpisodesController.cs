using Jellyfin.Plugin.Podcasts.Auth;
using Jellyfin.Plugin.Podcasts.Database;
using Jellyfin.Plugin.Podcasts.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.Podcasts.Api.Gpodder;

/// <summary>
/// gpodder v2 episode actions shim.
/// Maps gpodder episode action protocol to <c>user_episode_state</c>.
/// </summary>
[ApiController]
[Route("podcasts/sync/gpodder/api/2/episodes/{username}.json")]
public class GpodderEpisodesController : ControllerBase
{
    private readonly PodcastDbContext _db;

    public GpodderEpisodesController(PodcastDbContext db)
    {
        _db = db;
    }

    // GET /podcasts/sync/gpodder/api/2/episodes/{username}.json?since=TIMESTAMP
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetEpisodeActions(string username, [FromQuery] long? since, CancellationToken ct)
    {
        var userId = await AuthenticateAsync(username, ct).ConfigureAwait(false);
        if (userId is null) return Unauthorized();

        var query = _db.UserEpisodeStates
            .Where(s => s.UserId == userId)
            .Include(s => s.Episode)
                .ThenInclude(e => e!.Podcast)
            .AsQueryable();

        if (since.HasValue)
        {
            var sinceDate = DateTimeOffset.FromUnixTimeSeconds(since.Value).UtcDateTime;
            query = query.Where(s => s.UpdatedAt >= sinceDate);
        }

        var states = await query.ToListAsync(ct).ConfigureAwait(false);

        var actions = states.Select(s => new
        {
            podcast = s.Episode?.Podcast?.FeedUrl,
            episode = s.Episode?.EnclosureUrl,
            action = s.IsPlayed ? "play" : "play",
            position = s.PositionSeconds,
            started = 0,
            total = s.Episode?.DurationSeconds ?? 0,
            timestamp = s.UpdatedAt.ToString("O")
        });

        return Ok(new
        {
            actions,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
    }

    // POST /podcasts/sync/gpodder/api/2/episodes/{username}.json
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> PostEpisodeActions(
        string username,
        [FromBody] List<GpodderEpisodeAction> body,
        CancellationToken ct)
    {
        var userId = await AuthenticateAsync(username, ct).ConfigureAwait(false);
        if (userId is null) return Unauthorized();

        foreach (var action in body)
        {
            if (action.Action?.ToLowerInvariant() != "play") continue;

            // Look up episode by enclosure URL
            var episode = await _db.Episodes
                .FirstOrDefaultAsync(e => e.EnclosureUrl == action.Episode, ct)
                .ConfigureAwait(false);

            if (episode is null) continue;

            var actionTime = !string.IsNullOrEmpty(action.Timestamp) && DateTime.TryParse(action.Timestamp, out var t)
                ? t
                : DateTime.UtcNow;

            var state = await _db.UserEpisodeStates
                .FirstOrDefaultAsync(s => s.UserId == userId && s.EpisodeId == episode.Id, ct)
                .ConfigureAwait(false);

            if (state is null)
            {
                state = new UserEpisodeState
                {
                    UserId = userId,
                    EpisodeId = episode.Id,
                    PositionSeconds = action.Position,
                    IsPlayed = action.Position > 0 && action.Total > 0 && action.Position >= action.Total - 30,
                    UpdatedAt = actionTime
                };
                _db.UserEpisodeStates.Add(state);
            }
            else if (actionTime >= state.UpdatedAt)
            {
                state.PositionSeconds = action.Position;
                state.IsPlayed = action.Position > 0 && action.Total > 0 && action.Position >= action.Total - 30;
                state.UpdatedAt = actionTime;
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

public class GpodderEpisodeAction
{
    public string? Podcast { get; set; }
    public string? Episode { get; set; }
    public string? Action { get; set; }
    public int Position { get; set; }
    public int Started { get; set; }
    public int Total { get; set; }
    public string? Timestamp { get; set; }
}
