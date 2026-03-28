using Jellyfin.Plugin.Template.Models;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.Template.Data;

/// <summary>
/// EF Core database context for podcast plugin data.
/// </summary>
public class PodcastsDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PodcastsDbContext"/> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    public PodcastsDbContext(DbContextOptions<PodcastsDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets the podcast feeds.
    /// </summary>
    public DbSet<PodcastFeed> Feeds => Set<PodcastFeed>();

    /// <summary>
    /// Gets the user subscriptions.
    /// </summary>
    public DbSet<UserSubscription> Subscriptions => Set<UserSubscription>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PodcastFeed>(entity =>
        {
            entity.HasKey(f => f.Id);
            entity.Property(f => f.Id).IsRequired();
            entity.Property(f => f.FeedUrl).IsRequired();
            entity.Property(f => f.Title).IsRequired();
            entity.Property(f => f.Description).IsRequired();
        });

        modelBuilder.Entity<UserSubscription>(entity =>
        {
            entity.HasKey(s => new { s.UserId, s.FeedId });
            entity.HasOne(s => s.Feed)
                .WithMany(f => f.Subscriptions)
                .HasForeignKey(s => s.FeedId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
