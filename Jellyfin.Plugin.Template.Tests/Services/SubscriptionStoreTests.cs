using System;
using System.IO;
using Jellyfin.Plugin.Template.Models;
using Jellyfin.Plugin.Template.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Template.Tests.Services;

public class SubscriptionStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SubscriptionStore _store;

    private static readonly Guid UserA = Guid.NewGuid();
    private static readonly Guid UserB = Guid.NewGuid();

    private static PodcastFeed MakeFeed(string id) => new()
    {
        Id = id,
        FeedUrl = $"https://example.com/{id}.rss",
        Title = $"Podcast {id}",
    };

    public SubscriptionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _store = new SubscriptionStore(NullLogger<SubscriptionStore>.Instance, _tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

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

        // New instance over same directory simulates a server restart
        var reloadedStore = new SubscriptionStore(NullLogger<SubscriptionStore>.Instance, _tempDir);
        Assert.Single(reloadedStore.GetFeedsForUser(UserA));
    }
}
