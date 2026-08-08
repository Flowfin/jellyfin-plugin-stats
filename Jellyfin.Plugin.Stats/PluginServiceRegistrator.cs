using Jellyfin.Plugin.Stats.Capture;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Stats;

/// <summary>
/// Registers this plugin's services with the server's container.
/// </summary>
/// <remarks>
/// The server finds this type by scanning the plugin assembly for the
/// interface, so there is nothing to wire up beyond it existing. It is the one
/// place the capture path is assembled, which is what lets the rest of the
/// plugin take its collaborators through a constructor and never reach for the
/// plugin instance.
/// </remarks>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // The sink is registered before the listener that consumes it, so a
        // container built from this method alone resolves the listener. There
        // is no store yet, so the registration is the discarding one and issue
        // #29 is where it changes.
        serviceCollection.AddSingleton<IPlaybackEventSink, DiscardingPlaybackEventSink>();
        serviceCollection.AddHostedService<PlaybackEventListener>();
    }
}
