using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _store = CreateStore(_connection);
    }

    public void Dispose() => _connection.Dispose();

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
    public void Subscribe_AlreadySubscribed_DoesNotDuplicate()
    {
        var feed = MakeFeed("feed1");
        _store.Subscribe(UserA, feed);
        _store.Subscribe(UserA, feed);

        Assert.Single(_store.GetFeedsForUser(UserA));
    }

    [Fact]
    public void Unsubscribe_LastSubscriber_RemovesFeedFromAllFeeds()
    {
        _store.Subscribe(UserA, MakeFeed("feed1"));
        _store.Unsubscribe(UserA, "feed1");

        Assert.Empty(_store.GetAllFeeds());
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
    public void DataIsPersisted_AcrossNewStoreInstance()
    {
        _store.Subscribe(UserA, MakeFeed("feed1"));

        var reloadedStore = CreateStore(_connection);
        Assert.Single(reloadedStore.GetFeedsForUser(UserA));
    }

    [Fact]
    public async Task PatchSubscriptionAsync_NewGuid_CreatesGuidChainAndKeepsLatestFeedVisibleAsync()
    {
        await _store.UpsertSubscriptionAsync(UserA, MakeFeed("feed1"));

        var updated = await _store.PatchSubscriptionAsync(
            UserA,
            "feed1",
            null,
            "feed2",
            null,
            CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("feed2", updated!.FeedId);
        Assert.Single(_store.GetFeedsForUser(UserA));
        Assert.Equal("feed2", _store.GetFeedsForUser(UserA)[0].Id);

        var original = await _store.GetUserSubscriptionByGuidAsync(UserA, "feed1");
        Assert.Equal("feed2", original!.NewGuid);
        Assert.NotNull(original.GuidChanged);
    }

    [Fact]
    public async Task GetSubscriptionsPagedAsync_WithoutSince_ReturnsOriginalGuidAndLatestNewGuidAsync()
    {
        await _store.UpsertSubscriptionAsync(UserA, MakeFeed("feed1"));
        await _store.PatchSubscriptionAsync(UserA, "feed1", null, "feed2", null);

        var result = await _store.GetSubscriptionsPagedAsync(UserA, null, 1, 10);

        var subscription = Assert.Single(result.Items);
        Assert.Equal("feed1", subscription.FeedId);
        Assert.Equal("feed2", subscription.NewGuid);
    }

    [Fact]
    public async Task Subscribe_AfterDelete_ReinstatesSubscriptionAsync()
    {
        var feed = MakeFeed("feed1");
        await _store.UpsertSubscriptionAsync(UserA, feed);
        await _store.RequestDeletionAsync(UserA, "feed1");

        var reinstated = await _store.UpsertSubscriptionAsync(UserA, feed);

        Assert.True(reinstated.IsSubscribed);
        Assert.Null(reinstated.Deleted);
        Assert.Single(_store.GetFeedsForUser(UserA));
    }

    private static SubscriptionStore CreateStore(SqliteConnection connection)
    {
        var factory = new SharedConnectionDbContextFactory(connection);
        return new SubscriptionStore(factory);
    }

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

        public ValueTask<PodcastsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(CreateDbContext());
    }
}
