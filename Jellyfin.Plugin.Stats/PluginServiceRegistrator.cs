using System;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Stats.Capture;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Events;
using Jellyfin.Plugin.Stats.ScheduledTasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
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

        // What the server needs to be able to build the retention sweep task.
        // It builds every scheduled task in the assembly out of this container
        // and fails the whole plugin over an argument it cannot resolve, so a
        // line missing here is a plugin that does not load rather than a task
        // that does not run.
        //
        // The clock is registered rather than read where it is needed. A
        // scheduled task is handed a progress reporter and a cancellation token
        // and no moment at all, and neither supported server line registers a
        // clock of its own, so this is where "ninety days ago" starts.
        serviceCollection.AddSingleton(ServerClock.Machine);

        // The configuration as a function rather than a value, for the same
        // reason the gate above takes one: a retention changed on the settings
        // page has to decide the next sweep, not the first one after a restart.
        serviceCollection.AddSingleton<Func<PluginConfiguration>>(_ => () => Plugin.Instance!.Configuration);

        // The sweep takes the same store-opening function the writer does, and
        // opens the store for the length of one run. Nothing is opened here:
        // this is a constructor call, and the function is only run when a sweep
        // starts.
        serviceCollection.AddSingleton(_ => new RetentionSweep(OpenTheStore, RetentionSweep.DefaultBite));

        // The sweep that catches what the route below cannot: an account
        // deleted while this plugin was not loaded. It takes the user manager
        // rather than a list of accounts, because the question it asks is about
        // one identifier at a time and it asks it while the sweep runs.
        serviceCollection.AddSingleton(provider => new UnknownUserSweep(
            OpenTheStore,
            provider.GetRequiredService<IUserManager>(),
            UnknownUserSweep.DefaultBite));

        // The one route by which this plugin hears that an account is gone. The
        // user manager interface carries an update event and no deletion, so
        // without this line a deleted user's rows sit in the store until their
        // retention window expires, and nothing anywhere says they are there.
        //
        // Registered against the interface rather than the type, because the
        // server asks its container for every IEventConsumer of the event it is
        // publishing and never for this class by name. A registration of the
        // concrete type alone resolves in a test and is never called on a
        // server, which is the failure the container test is written against.
        serviceCollection.AddSingleton<IEventConsumer<UserDeletedEventArgs>>(
            _ => new UserDeletedConsumer(OpenTheStore, UserDeletedConsumer.DefaultBite));
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
