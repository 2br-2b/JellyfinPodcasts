using Jellyfin.Plugin.Podcasts.Configuration;
using Jellyfin.Plugin.Podcasts.Database;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Podcasts;

/// <summary>
/// Jellyfin Podcast Plugin entry point.
/// </summary>
public class PodcastPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly IApplicationPaths _applicationPaths;

    public PodcastPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        _applicationPaths = applicationPaths;
        Instance = this;

        // Ensure DB directory exists and run migrations at startup
        var dbPath = GetDatabasePath();
        var dir = Path.GetDirectoryName(dbPath)!;
        Directory.CreateDirectory(dir);
    }

    public static PodcastPlugin? Instance { get; private set; }

    public override string Name => "Podcasts";

    public override Guid Id => new Guid("6c4b8b6f-3f2a-4e1d-9c7b-2a5d8e0f1b3c");

    public override string Description => "Server-side podcast subscription management with AntennaPod sync support.";

    public string GetDatabasePath()
        => Path.Combine(_applicationPaths.DataPath, "plugins", "podcasts", "podcast_plugin.db");

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "PodcastDashboard",
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.dashboard.html",
            EnableInMainMenu = true,
            DisplayName = "Podcasts"
        };
    }
}

/// <summary>
/// Registers plugin services into the Jellyfin DI container.
/// </summary>
public class PluginServiceRegistrar : IPluginServiceRegistrar
{
    public void RegisterServices(IServiceCollection serviceCollection, IApplicationPaths applicationPaths)
    {
        var plugin = PodcastPlugin.Instance;
        var dbPath = plugin?.GetDatabasePath()
            ?? Path.Combine(applicationPaths.DataPath, "plugins", "podcasts", "podcast_plugin.db");

        serviceCollection.AddDbContext<PodcastDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"),
            ServiceLifetime.Transient);

        // Auto-migrate on startup via a hosted service shim
        serviceCollection.AddHostedService<DatabaseMigrationService>();

        // Register named HttpClient for feed fetching
        serviceCollection.AddHttpClient("PodcastFeedClient")
            .ConfigureHttpClient(c => c.DefaultRequestHeaders.UserAgent.ParseAdd("JellyfinPodcastPlugin/1.0"));

        // Core services
        serviceCollection.AddTransient<Feed.FeedManager>();
        serviceCollection.AddTransient<Cache.CacheManager>();
        serviceCollection.AddTransient<Cache.OnDemandDownloader>();
    }
}

/// <summary>
/// Runs EF Core migrations at application startup.
/// </summary>
internal class DatabaseMigrationService : Microsoft.Extensions.Hosting.IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DatabaseMigrationService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PodcastDbContext>();
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
