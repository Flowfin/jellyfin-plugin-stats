using Jellyfin.Plugin.Stats.Capture;
using Jellyfin.Plugin.Stats.Data;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    /// <remarks>
    /// The order is the order the container is read in, so a container built
    /// from this method alone resolves the listener and everything under it.
    /// The whole write path is assembled here: the subscription hands events to
    /// the tracker, the tracker hands a finished row to the gate, the gate
    /// decides whether it is recorded, and the queue's own thread opens the
    /// store and writes it.
    /// </remarks>
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton(provider => new QueuedPlayWriter(
            OpenTheStore,
            QueuedPlayWriter.DefaultBound,
            provider.GetRequiredService<ILogger<QueuedPlayWriter>>()));

        // The gate reads the configuration off the plugin instance every time
        // it judges a play, rather than being handed a value once. That is what
        // makes a change on the settings page take effect on the next event.
        serviceCollection.AddSingleton<IFinishedPlaySink>(provider => new CaptureGate(
            provider.GetRequiredService<QueuedPlayWriter>(),
            () => Plugin.Instance!.Configuration));

        serviceCollection.AddSingleton<IPlaybackEventSink, PlayTracker>();
        serviceCollection.AddHostedService<PlaybackEventListener>();
    }

    /// <summary>
    /// Opens the store in the folder the server gave this plugin.
    /// </summary>
    /// <remarks>
    /// This is the one place the plugin instance is reached for, and it is
    /// deliberately a function passed on rather than a call made here. The
    /// writer runs it on its own thread when the first row arrives, so a server
    /// building its container never waits for a file to open and never sees
    /// this throw; the folder is also the one the plugin reports rather than a
    /// second opinion about where the server put it.
    /// </remarks>
    /// <returns>The store.</returns>
    private static SqlitePlayStore OpenTheStore()
        => new(Plugin.Instance!.DataFolderPath);
}
