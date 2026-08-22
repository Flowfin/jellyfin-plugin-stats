using System;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Stats.Aggregation;
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

        // What carries the plugin's own state to the settings page. It is a
        // hosted service rather than a line here because it publishes a seam
        // that has to be withdrawn again when the host stops, and because the
        // writer it reads is resolved from this container rather than built
        // here. Nothing is opened while the container is being built: the
        // function below runs when a page asks, and the writer answers from a
        // field.
        serviceCollection.AddHostedService(provider =>
        {
            var writer = provider.GetRequiredService<QueuedPlayWriter>();

            return new PluginStateReport(() => writer.WhyTheStoreCouldNotBeOpened, OldestStoredPlay);
        });

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

        // What keeps a folded year between the first time somebody opens it and
        // the moment the rows underneath it move. The fold is handed in as a
        // function, so this is the only place the store is read for one, and
        // the store is opened when a year is asked for rather than here.
        //
        // Nothing calls this yet. There is no route from the plugin to a page,
        // so no year is folded on a server today and nothing is held; what this
        // line does is make the three deletions below reach whatever holds one,
        // so that a reader arriving later cannot be the change that has to
        // remember to wire them.
        serviceCollection.AddSingleton(provider => new HeldYears(
            (userId, year, zone, topCount) => ReadFromTheStore.Answering(OpenTheStore, store =>

                // The oldest row comes from the same store and the same open as
                // the plays, so what the answer says it covers and what it was
                // folded from are one reading rather than two that a sweep
                // running in between could put out of step. It is asked over
                // every account rather than over this one, because what a
                // window is about is the days the store has lost and not the
                // day this person started watching.
                YearInReview.Over(
                    store.PlaysFor(userId),
                    userId,
                    year,
                    zone,
                    topCount,
                    store.OldestPlayStartedUtc())),
            provider.GetRequiredService<TimeProvider>()));

        // The sweep takes the same store-opening function the writer does, and
        // opens the store for the length of one run. Nothing is opened here:
        // this is a constructor call, and the function is only run when a sweep
        // starts.
        serviceCollection.AddSingleton(provider => new RetentionSweep(
            OpenTheStore,
            RetentionSweep.DefaultBite,
            provider.GetRequiredService<HeldYears>()));

        // The sweep that catches what the route below cannot: an account
        // deleted while this plugin was not loaded. It takes the user manager
        // rather than a list of accounts, because the question it asks is about
        // one identifier at a time and it asks it while the sweep runs.
        serviceCollection.AddSingleton(provider => new UnknownUserSweep(
            OpenTheStore,
            provider.GetRequiredService<IUserManager>(),
            UnknownUserSweep.DefaultBite,
            provider.GetRequiredService<HeldYears>()));

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
            provider => new UserDeletedConsumer(
                OpenTheStore,
                UserDeletedConsumer.DefaultBite,
                provider.GetRequiredService<HeldYears>()));
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

    /// <summary>
    /// Reads the oldest play the store holds.
    /// </summary>
    /// <remarks>
    /// Here rather than in the report that publishes it, because this is the
    /// one file in the plugin that knows how a store is opened, and a second
    /// one that did would be a second answer about where the data folder is.
    /// The store is opened for the length of the question and closed again, the
    /// way a folded year is read.
    /// <para>
    /// It opens the store directly rather than through
    /// <see cref="ReadFromTheStore"/>, and that is the difference between the
    /// two readers rather than an oversight. What catches this one reports the
    /// exception's type to the settings page, so wrapping the open would put
    /// this plugin's own type in front of an operator in place of the one the
    /// file system or the migration actually raised, which is the sentence that
    /// tells them what to do next.
    /// </para>
    /// </remarks>
    /// <returns>When the oldest stored play started, or null where there is none.</returns>
    private static DateTime? OldestStoredPlay()
    {
        using var store = OpenTheStore();

        return store.OldestPlayStartedUtc();
    }
}
