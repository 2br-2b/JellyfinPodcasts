using Jellyfin.Plugin.Podcasts.Database;
using Jellyfin.Plugin.Podcasts.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.Podcasts.Api.Opa;

/// <summary>
/// Implements the experimental OPA Episodes endpoint.
/// Conflict resolution: last-write-wins on <c>updated_at</c>.
/// </summary>
[ApiController]
[Route("podcasts/sync/opa/episodes")]
[Authorize]
public class OpaEpisodesController : ControllerBase
{
    private readonly PodcastDbContext _db;

    public OpaEpisodesController(PodcastDbContext db)
    {
        _db = db;
    }

    private string UserId => User.FindFirst("sub")?.Value
        ?? User.FindFirst("nameid")?.Value
        ?? string.Empty;

    // GET /podcasts/sync/opa/episodes[?since=ISO8601]
    [HttpGet]
    public async Task<IActionResult> GetEpisodeStates([FromQuery] DateTime? since, CancellationToken ct)
    {
        var query = _db.UserEpisodeStates
            .Where(s => s.UserId == UserId)
            .Include(s => s.Episode)
                .ThenInclude(e => e!.Podcast)
            .AsQueryable();

        if (since.HasValue)
            query = query.Where(s => s.UpdatedAt >= since.Value);

        var states = await query.ToListAsync(ct).ConfigureAwait(false);
        return Ok(states.Select(ToDto));
    }

    // POST /podcasts/sync/opa/episodes — batch upsert
    [HttpPost]
    public async Task<IActionResult> UpsertEpisodeStates([FromBody] List<OpaEpisodeStateRequest> body, CancellationToken ct)
    {
        var results = new List<object>();

        foreach (var req in body)
        {
            var episode = await _db.Episodes
                .Include(e => e.Podcast)
                .FirstOrDefaultAsync(e => e.Guid == req.EpisodeGuid && e.Podcast!.Guid == req.PodcastGuid, ct)
                .ConfigureAwait(false);

            if (episode is null) continue;

            var state = await _db.UserEpisodeStates
                .FirstOrDefaultAsync(s => s.UserId == UserId && s.EpisodeId == episode.Id, ct)
                .ConfigureAwait(false);

            var clientUpdatedAt = req.UpdatedAt ?? DateTime.UtcNow;

            if (state is null)
            {
                state = new UserEpisodeState
                {
                    UserId = UserId,
                    EpisodeId = episode.Id,
                    PositionSeconds = req.PositionSeconds,
                    IsPlayed = req.IsPlayed,
                    UpdatedAt = clientUpdatedAt
                };
                _db.UserEpisodeStates.Add(state);
            }
            else if (clientUpdatedAt >= state.UpdatedAt)
            {
                // last-write-wins
                state.PositionSeconds = req.PositionSeconds;
                state.IsPlayed = req.IsPlayed;
                state.UpdatedAt = clientUpdatedAt;
            }
            // else: server state is newer — discard and return server state

            results.Add(ToDto(state, episode));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(results);
    }

    // GET /podcasts/sync/opa/episodes/{podcast_guid}/{episode_guid}
    [HttpGet("{podcastGuid}/{episodeGuid}")]
    public async Task<IActionResult> GetEpisodeState(string podcastGuid, string episodeGuid, CancellationToken ct)
    {
        var state = await _db.UserEpisodeStates
            .Include(s => s.Episode)
                .ThenInclude(e => e!.Podcast)
            .FirstOrDefaultAsync(s =>
                s.UserId == UserId &&
                s.Episode!.Guid == episodeGuid &&
                s.Episode.Podcast!.Guid == podcastGuid, ct)
            .ConfigureAwait(false);

        return state is null ? NotFound() : Ok(ToDto(state));
    }

    // PATCH /podcasts/sync/opa/episodes/{podcast_guid}/{episode_guid}
    [HttpPatch("{podcastGuid}/{episodeGuid}")]
    public async Task<IActionResult> UpdateEpisodeState(
        string podcastGuid, string episodeGuid,
        [FromBody] OpaEpisodeStatePatch patch, CancellationToken ct)
    {
        var episode = await _db.Episodes
            .Include(e => e.Podcast)
            .FirstOrDefaultAsync(e => e.Guid == episodeGuid && e.Podcast!.Guid == podcastGuid, ct)
            .ConfigureAwait(false);

        if (episode is null) return NotFound();

        var state = await _db.UserEpisodeStates
            .FirstOrDefaultAsync(s => s.UserId == UserId && s.EpisodeId == episode.Id, ct)
            .ConfigureAwait(false);

        var clientUpdatedAt = patch.UpdatedAt ?? DateTime.UtcNow;

        if (state is null)
        {
            state = new UserEpisodeState
            {
                UserId = UserId,
                EpisodeId = episode.Id,
                PositionSeconds = patch.PositionSeconds ?? 0,
                IsPlayed = patch.IsPlayed ?? false,
                UpdatedAt = clientUpdatedAt
            };
            _db.UserEpisodeStates.Add(state);
        }
        else if (clientUpdatedAt >= state.UpdatedAt)
        {
            if (patch.PositionSeconds.HasValue) state.PositionSeconds = patch.PositionSeconds.Value;
            if (patch.IsPlayed.HasValue) state.IsPlayed = patch.IsPlayed.Value;
            state.UpdatedAt = clientUpdatedAt;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Ok(ToDto(state, episode));
    }

    private static object ToDto(UserEpisodeState s, Model.Episode? ep = null)
    {
        var episode = ep ?? s.Episode;
        return new
        {
            podcast_guid = episode?.Podcast?.Guid ?? episode?.PodcastId.ToString(),
            episode_guid = episode?.Guid,
            position_seconds = s.PositionSeconds,
            is_played = s.IsPlayed,
            updated_at = s.UpdatedAt.ToString("O")
        };
    }
}

public class OpaEpisodeStateRequest
{
    public string PodcastGuid { get; set; } = string.Empty;
    public string EpisodeGuid { get; set; } = string.Empty;
    public int PositionSeconds { get; set; }
    public bool IsPlayed { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class OpaEpisodeStatePatch
{
    public int? PositionSeconds { get; set; }
    public bool? IsPlayed { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
