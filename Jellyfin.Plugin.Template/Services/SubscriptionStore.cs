using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Template.Data;
using Jellyfin.Plugin.Template.Models;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.Template.Services;

/// <summary>
/// Persists podcast feeds and per-user subscriptions using SQLite via EF Core.
/// </summary>
public class SubscriptionStore : ISubscriptionStore
{
    private readonly IDbContextFactory<PodcastsDbContext> _dbContextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubscriptionStore"/> class.
    /// Applies any pending migrations on first use.
    /// </summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public SubscriptionStore(IDbContextFactory<PodcastsDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
        using var ctx = dbContextFactory.CreateDbContext();
        ctx.Database.Migrate();
    }

    /// <inheritdoc />
    public IReadOnlyList<PodcastFeed> GetAllFeeds()
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Feeds.ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<PodcastFeed> GetFeedsForUser(Guid userId)
    {
        using var ctx = _dbContextFactory.CreateDbContext();
        return ctx.Subscriptions
            .Where(s => s.UserId == userId)
            .Include(s => s.Feed)
            .Select(s => s.Feed)
            .ToList();
    }

    /// <inheritdoc />
    public void Subscribe(Guid userId, PodcastFeed feed)
    {
        using var ctx = _dbContextFactory.CreateDbContext();

        if (!ctx.Feeds.Any(f => f.Id == feed.Id))
        {
            ctx.Feeds.Add(feed);
        }

        if (!ctx.Subscriptions.Any(s => s.UserId == userId && s.FeedId == feed.Id))
        {
            ctx.Subscriptions.Add(new UserSubscription
            {
                UserId = userId,
                FeedId = feed.Id,
                DateAdded = DateTime.UtcNow,
            });
        }

        ctx.SaveChanges();
    }

    /// <inheritdoc />
    public void Unsubscribe(Guid userId, string feedId)
    {
        using var ctx = _dbContextFactory.CreateDbContext();

        var subscription = ctx.Subscriptions
            .FirstOrDefault(s => s.UserId == userId && s.FeedId == feedId);

        if (subscription is null)
        {
            return;
        }

        ctx.Subscriptions.Remove(subscription);
        ctx.SaveChanges();

        // Prune the feed if no subscribers remain.
        if (!ctx.Subscriptions.Any(s => s.FeedId == feedId))
        {
            var feed = ctx.Feeds.Find(feedId);
            if (feed is not null)
            {
                ctx.Feeds.Remove(feed);
                ctx.SaveChanges();
            }
        }
    }
}
