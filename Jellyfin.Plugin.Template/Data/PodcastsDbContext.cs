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

    /// <summary>Gets the podcast feeds.</summary>
    public DbSet<PodcastFeed> Feeds => Set<PodcastFeed>();

    /// <summary>Gets the user subscriptions.</summary>
    public DbSet<UserSubscription> Subscriptions => Set<UserSubscription>();

    /// <summary>Gets the app passwords for external clients.</summary>
    public DbSet<AppPassword> AppPasswords => Set<AppPassword>();

    /// <summary>Gets the async deletion request records.</summary>
    public DbSet<DeletionRequest> DeletionRequests => Set<DeletionRequest>();

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
            entity.Property(f => f.MediaType)
                .HasConversion<string>()
                .HasDefaultValue(PodcastMediaType.Audio)
                .IsRequired();
        });

        modelBuilder.Entity<UserSubscription>(entity =>
        {
            entity.HasKey(s => new { s.UserId, s.FeedId });
            entity.HasOne(s => s.Feed)
                .WithMany(f => f.Subscriptions)
                .HasForeignKey(s => s.FeedId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(s => s.IsSubscribed).HasDefaultValue(true).IsRequired();
            entity.Property(s => s.SubscriptionChanged).HasColumnType("TEXT");
            entity.Property(s => s.GuidChanged).HasColumnType("TEXT");
            entity.Property(s => s.Deleted).HasColumnType("TEXT");
        });

        modelBuilder.Entity<AppPassword>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.UserId).IsRequired();
            entity.Property(p => p.Label).IsRequired();
            entity.Property(p => p.TokenHash).IsRequired();
            entity.Property(p => p.CreatedAt).HasColumnType("TEXT").IsRequired();
            entity.Property(p => p.LastUsedAt).HasColumnType("TEXT");
            entity.HasIndex(p => p.TokenHash).IsUnique();
        });

        modelBuilder.Entity<DeletionRequest>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Id).ValueGeneratedOnAdd();
            entity.Property(d => d.UserId).IsRequired();
            entity.Property(d => d.FeedId).IsRequired();
            entity.Property(d => d.Status).IsRequired().HasDefaultValue(DeletionStatuses.Pending);
            entity.Property(d => d.RequestedAt).HasColumnType("TEXT").IsRequired();
            entity.Property(d => d.CompletedAt).HasColumnType("TEXT");
        });
    }
}
