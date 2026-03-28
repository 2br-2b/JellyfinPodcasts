using System.IO;
using Jellyfin.Plugin.Podcasts.FeedParser;
using Jellyfin.Plugin.Template.Channels;
using Jellyfin.Plugin.Template.Data;
using Jellyfin.Plugin.Template.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Template;

/// <summary>
/// Registers the plugin's services with the Jellyfin DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient();
        serviceCollection.AddSingleton<RssFeedParser>();

        serviceCollection.AddDbContextFactory<PodcastsDbContext>((sp, options) =>
        {
            var appPaths = sp.GetRequiredService<IApplicationPaths>();
            var dataDir = Path.Combine(appPaths.DataPath, "podcasts");
            Directory.CreateDirectory(dataDir);
            options.UseSqlite($"Data Source={Path.Combine(dataDir, "podcasts.db")}");
        });

        serviceCollection.AddSingleton<ISubscriptionStore, SubscriptionStore>();
        serviceCollection.AddSingleton<IChannel, PodcastChannel>();
    }
}
