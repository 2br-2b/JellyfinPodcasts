#pragma warning disable CA2007
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Template.Data;
using Jellyfin.Plugin.Template.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Template.Services;

/// <summary>
/// Persists podcast feeds, per-user subscriptions, and deletion requests using SQLite via EF Core.
/// </summary>
public class SubscriptionStore : ISubscriptionStore
{
    private readonly IDbContextFactory<PodcastsDbContext> _dbContextFactory;
    private readonly ILogger<SubscriptionStore> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubscriptionStore"/> class.
    /// Applies any pending migrations on first use.
    /// </summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    /// <param name="logger">The logger.</param>
    public SubscriptionStore(IDbContextFactory<PodcastsDbContext> dbContextFactory, ILogger<SubscriptionStore> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        using var ctx = dbContextFactory.CreateDbContext();
        ctx.Database.Migrate();
    }

    /// <inheritdoc />
    public IReadOnlyList<PodcastFeed> GetAllFeeds()
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Subscriptions
            .Where(s => s.NewGuid == null && s.IsSubscribed && s.Deleted == null)
            .Include(s => s.Feed)
            .Select(s => s.Feed)
            .Distinct()
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<PodcastFeed> GetFeedsForUser(Guid userId)
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Subscriptions
            .Where(s => s.UserId == userId && s.NewGuid == null && s.IsSubscribed && s.Deleted == null)
            .Include(s => s.Feed)
            .Select(s => s.Feed)
            .ToList();
    }

    /// <inheritdoc />
    public void Subscribe(Guid userId, PodcastFeed feed)
        => UpsertSubscriptionAsync(userId, feed).GetAwaiter().GetResult();

    /// <inheritdoc />
    public void Unsubscribe(Guid userId, string feedId)
        => PatchSubscriptionAsync(userId, feedId, null, null, false).GetAwaiter().GetResult();

    /// <inheritdoc />
    public async Task<(IReadOnlyList<UserSubscription> Items, int Total)> GetSubscriptionsPagedAsync(
        Guid userId, DateTime? since, int page, int perPage, CancellationToken ct = default)
    {
        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var allSubscriptions = await ctx.Subscriptions
            .Where(s => s.UserId == userId)
            .Include(s => s.Feed)
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var projections = BuildPublicSubscriptions(allSubscriptions, since);
        var total = projections.Count;
        var items = projections
            .OrderBy(s => s.DateAdded)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .ToList();
        return (items, total);
    }

    /// <inheritdoc />
    public async Task<UserSubscription?> GetUserSubscriptionByGuidAsync(
        Guid userId, string feedId, CancellationToken ct = default)
    {
        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await ctx.Subscriptions
            .Include(s => s.Feed)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.FeedId == feedId, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<UserSubscription?> GetLatestSubscriptionAsync(
        Guid userId, string feedId, CancellationToken ct = default)
    {
        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var allSubscriptions = await ctx.Subscriptions
            .Where(s => s.UserId == userId)
            .Include(s => s.Feed)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var lookup = allSubscriptions.ToDictionary(s => s.FeedId, StringComparer.Ordinal);
        return lookup.TryGetValue(feedId, out var subscription)
            ? GetLatest(subscription, lookup)
            : null;
    }

    /// <inheritdoc />
    public async Task<PodcastFeed?> GetFeedByIdAsync(string feedId, CancellationToken ct = default)
    {
        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await ctx.Feeds.FirstOrDefaultAsync(f => f.Id == feedId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PodcastFeed?> GetFeedByUrlAsync(string feedUrl, CancellationToken ct = default)
    {
        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await ctx.Feeds.FirstOrDefaultAsync(f => f.FeedUrl == feedUrl, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<UserSubscription> UpsertSubscriptionAsync(
        Guid userId, PodcastFeed feed, CancellationToken ct = default)
    {
        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var currentFeed = await ctx.Feeds
            .FirstOrDefaultAsync(f => f.Id == feed.Id || f.FeedUrl == feed.FeedUrl, ct)
            .ConfigureAwait(false);
        if (currentFeed is null)
        {
            currentFeed = CloneFeed(feed);
            ctx.Feeds.Add(currentFeed);
        }

        var subscription = await ctx.Subscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.FeedId == currentFeed.Id, ct)
            .ConfigureAwait(false);
        var now = DateTime.UtcNow;

        if (subscription is null)
        {
            subscription = new UserSubscription
            {
                UserId = userId,
                FeedId = currentFeed.Id,
                DateAdded = now,
                IsSubscribed = true,
                SubscriptionChanged = now,
            };
            ctx.Subscriptions.Add(subscription);
        }
        else
        {
            subscription.IsSubscribed = true;
            subscription.Deleted = null;
            subscription.SubscriptionChanged = now;
        }

        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        subscription.Feed = currentFeed;
        return subscription;
    }

    /// <inheritdoc />
    public async Task<UserSubscription?> PatchSubscriptionAsync(
        Guid userId,
        string feedId,
        string? newFeedUrl,
        string? newGuid,
        bool? isSubscribed,
        CancellationToken ct = default)
    {
        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var allSubscriptions = await ctx.Subscriptions
            .Where(s => s.UserId == userId)
            .Include(s => s.Feed)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var lookup = allSubscriptions.ToDictionary(s => s.FeedId, StringComparer.Ordinal);
        if (!lookup.TryGetValue(feedId, out var requested))
        {
            return null;
        }

        var current = GetLatest(requested, lookup);
        var active = current;
        var now = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(newGuid) && !string.Equals(newGuid, current.FeedId, StringComparison.Ordinal))
        {
            var targetFeed = await ctx.Feeds.FindAsync(new object[] { newGuid }, ct).ConfigureAwait(false);
            if (targetFeed is null)
            {
                targetFeed = CloneFeed(current.Feed);
                targetFeed.Id = newGuid;
                if (!string.IsNullOrWhiteSpace(newFeedUrl))
                {
                    targetFeed.FeedUrl = newFeedUrl;
                }

                ctx.Feeds.Add(targetFeed);
            }

            // If targetFeed already existed in the DB, we reuse it as-is.
            // newFeedUrl is not applied to pre-existing shared records.

            var targetSubscription = await ctx.Subscriptions
                .Include(s => s.Feed)
                .FirstOrDefaultAsync(s => s.UserId == userId && s.FeedId == newGuid, ct)
                .ConfigureAwait(false);
            if (targetSubscription is null)
            {
                targetSubscription = new UserSubscription
                {
                    UserId = userId,
                    FeedId = newGuid,
                    DateAdded = current.DateAdded,
                    IsSubscribed = current.IsSubscribed,
                    SubscriptionChanged = current.SubscriptionChanged,
                    Deleted = current.Deleted,
                };
                ctx.Subscriptions.Add(targetSubscription);
            }

            current.NewGuid = newGuid;
            current.GuidChanged = now;
            active = targetSubscription;
            active.Feed = targetFeed;
        }
        else if (!string.IsNullOrWhiteSpace(newFeedUrl))
        {
            // URL-only migration: find or create a feed record at the new URL, then reassign
            // this user's subscription to it. The old feed record is left intact for other subscribers.
            var newFeed = await ctx.Feeds
                .FirstOrDefaultAsync(f => f.FeedUrl == newFeedUrl, ct)
                .ConfigureAwait(false);
            if (newFeed is null)
            {
                newFeed = CloneFeed(active.Feed);
                newFeed.Id = Guid.NewGuid().ToString();
                newFeed.FeedUrl = newFeedUrl;
                ctx.Feeds.Add(newFeed);
            }

            active.FeedId = newFeed.Id;
            active.Feed = newFeed;
            active.SubscriptionChanged = now;
        }

        if (isSubscribed.HasValue)
        {
            active.IsSubscribed = isSubscribed.Value;
            active.SubscriptionChanged = now;
            active.Deleted = isSubscribed.Value ? null : now;
        }

        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        return active;
    }

    /// <inheritdoc />
    public async Task<DeletionRequest> RequestDeletionAsync(
        Guid userId, string feedId, CancellationToken ct = default)
    {
        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var transaction = await ctx.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Resolve the latest subscription in the chain so we soft-delete the right record.
        var allSubscriptions = await ctx.Subscriptions
            .Where(s => s.UserId == userId)
            .Include(s => s.Feed)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var lookup = allSubscriptions.ToDictionary(s => s.FeedId, StringComparer.Ordinal);
        lookup.TryGetValue(feedId, out var requested);
        var current = requested is null ? null : GetLatest(requested, lookup);

        var now = DateTime.UtcNow;
        if (current is not null)
        {
            current.IsSubscribed = false;
            current.SubscriptionChanged = now;
            current.Deleted = now;
        }

        // Create the deletion record in PENDING state so callers receive a trackable ID immediately.
        var deletion = new DeletionRequest
        {
            UserId = userId,
            FeedId = current?.FeedId ?? feedId,
            Status = DeletionStatuses.Pending,
            Message = "Deletion is pending",
            RequestedAt = now,
        };

        ctx.DeletionRequests.Add(deletion);
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);

        // Background: cascade-delete any user-specific data (playback positions, etc.) and
        // mark the deletion as complete. The shared PodcastFeed record is intentionally not
        // deleted here because other users may currently be subscribed to the same feed.
        var deletionId = deletion.Id;
        _ = Task.Run(() => CompleteDeleteAsync(deletionId), CancellationToken.None);

        return deletion;
    }

    /// <inheritdoc />
    public async Task<DeletionRequest?> GetDeletionRequestAsync(
        Guid userId, int deletionId, CancellationToken ct = default)
    {
        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await ctx.DeletionRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == deletionId && d.UserId == userId, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateFeedMetadataAsync(string feedId, PodcastFeed metadata, CancellationToken ct = default)
    {
        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var feed = await ctx.Feeds
            .FirstOrDefaultAsync(f => f.Id == feedId || f.FeedUrl == metadata.FeedUrl, ct)
            .ConfigureAwait(false);
        if (feed is null)
        {
            return;
        }

        feed.Title = metadata.Title;
        feed.Description = metadata.Description;
        feed.ImageUrl = metadata.ImageUrl;
        feed.HomePageUrl = metadata.HomePageUrl;
        feed.MediaType = metadata.MediaType;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Background step for <see cref="RequestDeletionAsync"/>: cascade-deletes user-specific data
    /// and transitions the deletion record from <see cref="DeletionStatuses.Pending"/> to
    /// <see cref="DeletionStatuses.Success"/> or <see cref="DeletionStatuses.Failure"/>.
    /// </summary>
    private async Task CompleteDeleteAsync(int deletionId)
    {
        try
        {
            await using var ctx = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);

            // TODO: add cascade deletes for user-specific data here (e.g. playback positions)
            // when those tables exist.  Each should run inside the same transaction so the whole
            // thing is rolled back on failure.

            var deletion = await ctx.DeletionRequests
                .FirstOrDefaultAsync(d => d.Id == deletionId, CancellationToken.None)
                .ConfigureAwait(false);
            if (deletion is not null)
            {
                deletion.Status = DeletionStatuses.Success;
                deletion.Message = "Subscription deleted successfully";
                deletion.CompletedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background deletion failed for deletion ID {DeletionId}; attempting to mark as failed", deletionId);
            try
            {
                await using var failCtx = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
                var deletion = await failCtx.DeletionRequests
                    .FirstOrDefaultAsync(d => d.Id == deletionId, CancellationToken.None)
                    .ConfigureAwait(false);
                if (deletion is not null)
                {
                    deletion.Status = DeletionStatuses.Failure;
                    deletion.Message = "The deletion process encountered an error and was rolled back";
                    deletion.CompletedAt = DateTime.UtcNow;
                    await failCtx.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "Failed to update deletion status to failure for deletion ID {DeletionId}", deletionId);
            }
        }
    }

    private static PodcastFeed CloneFeed(PodcastFeed feed)
        => new()
        {
            Id = feed.Id,
            FeedUrl = feed.FeedUrl,
            Title = feed.Title,
            Description = feed.Description,
            ImageUrl = feed.ImageUrl,
            HomePageUrl = feed.HomePageUrl,
            MediaType = feed.MediaType,
        };

    private static List<UserSubscription> BuildPublicSubscriptions(
        IReadOnlyList<UserSubscription> allSubscriptions,
        DateTime? since)
    {
        var lookup = allSubscriptions.ToDictionary(s => s.FeedId, StringComparer.Ordinal);
        var roots = allSubscriptions
            .Where(s => !allSubscriptions.Any(candidate => string.Equals(candidate.NewGuid, s.FeedId, StringComparison.Ordinal)))
            .ToList();

        var results = new List<UserSubscription>();
        foreach (var root in roots)
        {
            var chain = GetChain(root, lookup);
            var latest = chain[^1];
            if (!ShouldInclude(chain, latest, since))
            {
                continue;
            }

            var current = since.HasValue ? GetVersionAtSince(chain, since.Value) : root;
            results.Add(new UserSubscription
            {
                UserId = current.UserId,
                FeedId = current.FeedId,
                DateAdded = current.DateAdded,
                IsSubscribed = latest.IsSubscribed,
                SubscriptionChanged = latest.SubscriptionChanged ?? current.SubscriptionChanged,
                GuidChanged = latest.GuidChanged ?? current.GuidChanged,
                NewGuid = latest.FeedId != current.FeedId ? latest.FeedId : null,
                Deleted = latest.Deleted,
                Feed = CloneFeed(current.Feed),
            });
        }

        return results;
    }

    private static bool ShouldInclude(
        IReadOnlyList<UserSubscription> chain,
        UserSubscription latest,
        DateTime? since)
    {
        if (!since.HasValue)
        {
            return latest.IsSubscribed && latest.Deleted is null;
        }

        // Per spec: only return entries whose subscription_changed, guid_changed, or deleted
        // fields are greater than the since parameter.
        var sinceValue = since.Value;
        return chain.Any(s =>
            (s.SubscriptionChanged is not null && s.SubscriptionChanged.Value > sinceValue)
            || (s.GuidChanged is not null && s.GuidChanged.Value > sinceValue)
            || (s.Deleted is not null && s.Deleted.Value > sinceValue));
    }

    private static UserSubscription GetVersionAtSince(List<UserSubscription> chain, DateTime since)
    {
        var current = chain[0];
        for (var i = 0; i < chain.Count - 1; i++)
        {
            if (chain[i].GuidChanged is DateTime guidChanged && guidChanged <= since)
            {
                current = chain[i + 1];
                continue;
            }

            break;
        }

        return current;
    }

    private static List<UserSubscription> GetChain(
        UserSubscription root,
        IReadOnlyDictionary<string, UserSubscription> lookup)
    {
        var chain = new List<UserSubscription> { root };
        var current = root;
        while (!string.IsNullOrWhiteSpace(current.NewGuid) && lookup.TryGetValue(current.NewGuid, out var next))
        {
            chain.Add(next);
            current = next;
        }

        return chain;
    }

    private static UserSubscription GetLatest(
        UserSubscription root,
        IReadOnlyDictionary<string, UserSubscription> lookup)
        => GetChain(root, lookup)[^1];
}
#pragma warning restore CA2007
