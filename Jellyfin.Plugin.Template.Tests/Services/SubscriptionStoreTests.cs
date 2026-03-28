using System;
using Jellyfin.Plugin.Template.Data;
using Jellyfin.Plugin.Template.Models;
using Jellyfin.Plugin.Template.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jellyfin.Plugin.Template.Tests.Services;

public class SubscriptionStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SubscriptionStore _store;

    private static readonly Guid UserA = Guid.NewGuid();
    private static readonly Guid UserB = Guid.NewGuid();

    private static PodcastFeed MakeFeed(string id) => new()
    {
        Id = id,
        FeedUrl = $"https://example.com/{id}.rss",
        Title = $"Podcast {id}",
        Description = string.Empty,
    };

    public SubscriptionStoreTests()
    {
        // A named in-memory database shared across all contexts in this test instance.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _store = CreateStore(_connection);
    }

    public void Dispose() => _connection.Dispose();

    private static SubscriptionStore CreateStore(SqliteConnection connection)
    {
        var factory = new SharedConnectionDbContextFactory(connection);
        return new SubscriptionStore(factory);
    }

    [Fact]
    public void Subscribe_NewFeed_FeedAppearsInGetAllFeeds()
    {
        _store.Subscribe(UserA, MakeFeed("feed1"));
        Assert.Single(_store.GetAllFeeds());
    }

    [Fact]
    public void Subscribe_NewFeed_UserSeesIt()
    {
        _store.Subscribe(UserA, MakeFeed("feed1"));
        Assert.Single(_store.GetFeedsForUser(UserA));
    }

    [Fact]
    public void Subscribe_TwoUsers_SameFeed_FeedStoredOnce()
    {
        var feed = MakeFeed("feed1");
        _store.Subscribe(UserA, feed);
        _store.Subscribe(UserB, feed);

        Assert.Single(_store.GetAllFeeds());
    }

    [Fact]
    public void Subscribe_TwoUsers_SameFeed_BothUsersSeeFeed()
    {
        var feed = MakeFeed("feed1");
        _store.Subscribe(UserA, feed);
        _store.Subscribe(UserB, feed);

        Assert.Single(_store.GetFeedsForUser(UserA));
        Assert.Single(_store.GetFeedsForUser(UserB));
    }

    [Fact]
    public void Subscribe_AlreadySubscribed_DoesNotDuplicate()
    {
        var feed = MakeFeed("feed1");
        _store.Subscribe(UserA, feed);
        _store.Subscribe(UserA, feed);

        Assert.Single(_store.GetFeedsForUser(UserA));
    }

    [Fact]
    public void GetFeedsForUser_OtherUsersFeeds_NotReturned()
    {
        _store.Subscribe(UserA, MakeFeed("feed1"));
        _store.Subscribe(UserB, MakeFeed("feed2"));

        var userAFeeds = _store.GetFeedsForUser(UserA);
        Assert.Single(userAFeeds);
        Assert.Equal("feed1", userAFeeds[0].Id);
    }

    [Fact]
    public void Unsubscribe_LastSubscriber_RemovesFeedFromAllFeeds()
    {
        _store.Subscribe(UserA, MakeFeed("feed1"));
        _store.Unsubscribe(UserA, "feed1");

        Assert.Empty(_store.GetAllFeeds());
    }

    [Fact]
    public void Unsubscribe_LastSubscriber_UserNoLongerSeesFeed()
    {
        _store.Subscribe(UserA, MakeFeed("feed1"));
        _store.Unsubscribe(UserA, "feed1");

        Assert.Empty(_store.GetFeedsForUser(UserA));
    }

    [Fact]
    public void Unsubscribe_NotLastSubscriber_FeedRemainsInAllFeeds()
    {
        var feed = MakeFeed("feed1");
        _store.Subscribe(UserA, feed);
        _store.Subscribe(UserB, feed);

        _store.Unsubscribe(UserA, "feed1");

        Assert.Single(_store.GetAllFeeds());
    }

    [Fact]
    public void Unsubscribe_NotLastSubscriber_RemainingUserStillSeesFeed()
    {
        var feed = MakeFeed("feed1");
        _store.Subscribe(UserA, feed);
        _store.Subscribe(UserB, feed);

        _store.Unsubscribe(UserA, "feed1");

        Assert.Single(_store.GetFeedsForUser(UserB));
    }

    [Fact]
    public void Unsubscribe_NotSubscribed_NoException()
    {
        // Should be a no-op
        _store.Unsubscribe(UserA, "nonexistent");
        Assert.Empty(_store.GetAllFeeds());
    }

    [Fact]
    public void DataIsPersisted_AcrossNewStoreInstance()
    {
        _store.Subscribe(UserA, MakeFeed("feed1"));

        // A second store over the same open connection simulates a server restart
        // (data is already migrated; the new instance should see the existing rows).
        var reloadedStore = CreateStore(_connection);
        Assert.Single(reloadedStore.GetFeedsForUser(UserA));
    }

    /// <summary>
    /// Test-only factory that reuses a single open <see cref="SqliteConnection"/> so that
    /// in-memory databases are shared across all <see cref="PodcastsDbContext"/> instances.
    /// </summary>
    private sealed class SharedConnectionDbContextFactory : IDbContextFactory<PodcastsDbContext>
    {
        private readonly DbContextOptions<PodcastsDbContext> _options;

        public SharedConnectionDbContextFactory(SqliteConnection connection)
        {
            _options = new DbContextOptionsBuilder<PodcastsDbContext>()
                .UseSqlite(connection)
                .Options;
        }

        public PodcastsDbContext CreateDbContext() => new(_options);
    }
}
