using Jellyfin.Plugin.Podcasts.Database;
using Jellyfin.Plugin.Podcasts.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Podcasts.Library;

/// <summary>
/// Writes Jellyfin playback progress back to <c>user_episode_state</c>,
/// keeping sync state up to date so AntennaPod picks up progress on next sync.
/// Called by the playback API controller when a progress report is received.
/// </summary>
public class PlaybackProgressHook
{
    private readonly PodcastDbContext _db;
    private readonly ILogger<PlaybackProgressHook> _logger;

    public PlaybackProgressHook(PodcastDbContext db, ILogger<PlaybackProgressHook> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Records a playback progress update for a user/episode pair.
    /// </summary>
    /// <param name="userId">Jellyfin user ID.</param>
    /// <param name="episodeId">Plugin episode ID.</param>
    /// <param name="positionSeconds">Current playback position in seconds.</param>
    /// <param name="isPlayed">True if the episode has been fully played.</param>
    public async Task RecordProgressAsync(
        string userId,
        Guid episodeId,
        int positionSeconds,
        bool isPlayed,
        CancellationToken ct = default)
    {
        var state = await _db.UserEpisodeStates
            .FirstOrDefaultAsync(s => s.UserId == userId && s.EpisodeId == episodeId, ct)
            .ConfigureAwait(false);

        if (state is null)
        {
            state = new UserEpisodeState
            {
                UserId = userId,
                EpisodeId = episodeId
            };
            _db.UserEpisodeStates.Add(state);
        }

        state.PositionSeconds = positionSeconds;
        state.IsPlayed = isPlayed;
        state.UpdatedAt = DateTime.UtcNow;

        // Also refresh episode cached_at to update LRU position
        var episode = await _db.Episodes.FindAsync(new object[] { episodeId }, ct).ConfigureAwait(false);
        if (episode is { IsCached: true })
            episode.CachedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        _logger.LogDebug("Recorded progress for user {UserId} episode {EpisodeId}: {Position}s played={IsPlayed}",
            userId, episodeId, positionSeconds, isPlayed);
    }
}
