using Jellyfin.Plugin.Podcasts.FeedParser;
using Jellyfin.Plugin.Template.Channels;
using Jellyfin.Plugin.Template.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Plugins;
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
        serviceCollection.AddSingleton<SubscriptionStore>();
        serviceCollection.AddSingleton<IChannel, PodcastChannel>();
    }
}
