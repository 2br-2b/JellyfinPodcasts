using Jellyfin.Plugin.Podcasts.Model;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.Podcasts.Database;

public class PodcastDbContext : DbContext
{
    public PodcastDbContext(DbContextOptions<PodcastDbContext> options) : base(options)
    {
    }

    public DbSet<Podcast> Podcasts => Set<Podcast>();
    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<UserEpisodeState> UserEpisodeStates => Set<UserEpisodeState>();
    public DbSet<UserSubscription> UserSubscriptions => Set<UserSubscription>();
    public DbSet<AppPassword> AppPasswords => Set<AppPassword>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // podcasts: unique index on feed_url
        modelBuilder.Entity<Podcast>()
            .HasIndex(p => p.FeedUrl)
            .IsUnique();

        // episodes: unique index on (podcast_id, guid)
        modelBuilder.Entity<Episode>()
            .HasIndex(e => new { e.PodcastId, e.Guid })
            .IsUnique();

        // user_episode_state: unique index on (user_id, episode_id)
        modelBuilder.Entity<UserEpisodeState>()
            .HasIndex(s => new { s.UserId, s.EpisodeId })
            .IsUnique();

        // user_subscriptions: unique index on (user_id, podcast_id)
        modelBuilder.Entity<UserSubscription>()
            .HasIndex(s => new { s.UserId, s.PodcastId })
            .IsUnique();

        // Relationships
        modelBuilder.Entity<Episode>()
            .HasOne(e => e.Podcast)
            .WithMany(p => p.Episodes)
            .HasForeignKey(e => e.PodcastId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserEpisodeState>()
            .HasOne(s => s.Episode)
            .WithMany(e => e.UserStates)
            .HasForeignKey(s => s.EpisodeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserSubscription>()
            .HasOne(s => s.Podcast)
            .WithMany(p => p.Subscriptions)
            .HasForeignKey(s => s.PodcastId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
