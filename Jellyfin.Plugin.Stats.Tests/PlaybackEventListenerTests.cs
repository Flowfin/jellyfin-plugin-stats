// What the subscription owes, checked three ways: it leaves nothing behind, it
// keeps a fault out of the server, and it comes out of the container.
//
// Handler counts are read off the fake with reflection rather than through a
// counter the fake exposes. A field-like event compiles to a private field of
// the same name holding the invocation list, so this reads the real
// subscription state; a counter the fake maintained would be a second account
// of it that a test could agree with while the events themselves were wrong.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Capture;
using StatsData = Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class PlaybackEventListenerTests
{
    private static readonly string[] SubscribedEvents =
    [
        "PlaybackStart",
        "PlaybackProgress",
        "PlaybackStopped",
        "SessionEnded"
    ];

    [Fact]
    public async Task StartSubscribesToEveryEventItReads()
    {
        var sessions = new FakeSessionManager();
        var listener = ListenerOver(sessions, new RecordingSink());

        Assert.All(SubscribedEvents, name => Assert.Equal(0, HandlerCount(sessions, name)));

        await listener.StartAsync(CancellationToken.None);

        Assert.All(SubscribedEvents, name => Assert.Equal(1, HandlerCount(sessions, name)));
    }

    [Fact]
    public async Task StartAndStopTwiceLeavesNoSubscriptionBehind()
    {
        var sessions = new FakeSessionManager();
        var listener = ListenerOver(sessions, new RecordingSink());

        for (var round = 0; round < 2; round++)
        {
            await listener.StartAsync(CancellationToken.None);
            await listener.StopAsync(CancellationToken.None);
        }

        Assert.All(SubscribedEvents, name => Assert.Equal(0, HandlerCount(sessions, name)));
    }

    [Fact]
    public async Task StartingTwiceDoesNotDeliverAnEventTwice()
    {
        var sessions = new FakeSessionManager();
        var sink = new RecordingSink();
        var listener = ListenerOver(sessions, sink);

        await listener.StartAsync(CancellationToken.None);
        await listener.StartAsync(CancellationToken.None);

        RaiseAPlay(sessions);

        // One start or two, a play is one play. A second subscription would
        // report every number in every report at twice its real value.
        Assert.Equal(1, sink.Started);
        Assert.Equal(1, sink.Stopped);
    }

    [Fact]
    public async Task StopWithoutStartRemovesNothing()
    {
        var sessions = new FakeSessionManager();
        var other = new RecordingSink();
        var otherListener = ListenerOver(sessions, other);
        await otherListener.StartAsync(CancellationToken.None);

        var neverStarted = ListenerOver(sessions, new RecordingSink());
        await neverStarted.StopAsync(CancellationToken.None);

        Assert.All(SubscribedEvents, name => Assert.Equal(1, HandlerCount(sessions, name)));
    }

    [Fact]
    public async Task AfterStopTheSinkHearsNothing()
    {
        var sessions = new FakeSessionManager();
        var sink = new RecordingSink();
        var listener = ListenerOver(sessions, sink);

        await listener.StartAsync(CancellationToken.None);
        await listener.StopAsync(CancellationToken.None);

        RaiseAPlay(sessions);

        Assert.Equal(0, sink.Started);
        Assert.Equal(0, sink.Progressed);
        Assert.Equal(0, sink.Stopped);
        Assert.Equal(0, sink.SessionsEnded);
    }

    [Fact]
    public async Task EveryEventReachesTheSink()
    {
        var sessions = new FakeSessionManager();
        var sink = new RecordingSink();
        var listener = ListenerOver(sessions, sink);

        await listener.StartAsync(CancellationToken.None);
        RaiseAPlay(sessions);

        Assert.Equal(1, sink.Started);
        Assert.Equal(1, sink.Progressed);
        Assert.Equal(1, sink.Stopped);
        Assert.Equal(1, sink.SessionsEnded);
    }

    [Fact]
    public async Task AnExceptionInAHandlerDoesNotReachTheServer()
    {
        var sessions = new FakeSessionManager();
        var listener = ListenerOver(sessions, new ThrowingSink());

        await listener.StartAsync(CancellationToken.None);

        // Raising is what the server's event dispatch does. Nothing comes back
        // out of it, so a fault in this plugin cannot stop the dispatch reaching
        // whatever else is listening on the same event.
        RaiseAPlay(sessions);
    }

    [Fact]
    public async Task AFaultingHandlerDoesNotUnsubscribeTheOnesAfterIt()
    {
        var sessions = new FakeSessionManager();
        var listener = ListenerOver(sessions, new ThrowingSink());

        await listener.StartAsync(CancellationToken.None);
        RaiseAPlay(sessions);

        // A plugin that fell off the session manager the first time something
        // went wrong would report nothing afterwards and say so nowhere.
        Assert.All(SubscribedEvents, name => Assert.Equal(1, HandlerCount(sessions, name)));
    }

    [Fact]
    public void TheListenerIsResolvedFromTheContainer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISessionManager>(new FakeSessionManager());
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddLogging();

        // The registrator is called exactly as the server calls it, so a
        // registration that stops working is caught here rather than on a
        // server. The application host is not read by it; a fake for that
        // interface would be a hundred refusing members proving nothing.
        new PluginServiceRegistrator().RegisterServices(services, applicationHost: null!);

        using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();

        Assert.Single(hosted);
        Assert.IsType<PlaybackEventListener>(hosted[0]);
    }

    [Fact]
    public void TheSinkIsResolvedFromTheContainer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISessionManager>(new FakeSessionManager());
        services.AddLogging();

        new PluginServiceRegistrator().RegisterServices(services, applicationHost: null!);

        using var provider = services.BuildServiceProvider();

        // The whole write path, read out of the container in the order it is
        // used. A reader who wants to know whether the plugin is writing
        // anything can see it here, and a registration that stopped resolving
        // is caught here rather than on a server.
        Assert.IsType<PlayTracker>(provider.GetRequiredService<IPlaybackEventSink>());
        Assert.IsType<CaptureGate>(provider.GetRequiredService<IFinishedPlaySink>());
        Assert.NotNull(provider.GetRequiredService<StatsData.QueuedPlayWriter>());
    }

    [Fact]
    public void TheStoreIsNotOpenedWhileTheContainerIsBeingBuilt()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISessionManager>(new FakeSessionManager());
        services.AddLogging();

        new PluginServiceRegistrator().RegisterServices(services, applicationHost: null!);

        using var provider = services.BuildServiceProvider();

        // There is no plugin instance here and therefore no data folder, so a
        // registration that opened the store while resolving would throw on
        // this line. It does not, because the writer opens it on its own thread
        // when the first row arrives. That is what keeps a store which cannot
        // be opened from being a server that will not start.
        Assert.NotNull(provider.GetRequiredService<IFinishedPlaySink>());
    }

    private static PlaybackEventListener ListenerOver(ISessionManager sessions, IPlaybackEventSink sink)
        => new(sessions, sink, NullLogger<PlaybackEventListener>.Instance);

    /// <summary>
    /// Runs one play through the fake, start to stop, and ends the session.
    /// </summary>
    private static void RaiseAPlay(FakeSessionManager sessions)
    {
        var session = new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer"))
            .Playing(PlaySessionBuilder.Video("An Item", TimeSpan.FromMinutes(90)))
            .From("Jellyfin Web", "A browser")
            .Via(PlayMethod.DirectPlay)
            .Build();

        sessions.RaisePlaybackStart(session);
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(5));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(10));
        sessions.RaiseSessionEnded(session);
    }

    /// <summary>
    /// Reads how many handlers are attached to one of the fake's events.
    /// </summary>
    private static int HandlerCount(FakeSessionManager sessions, string eventName)
    {
        var field = typeof(FakeSessionManager).GetField(
            eventName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);

        var handler = (Delegate?)field!.GetValue(sessions);
        return handler is null ? 0 : handler.GetInvocationList().Length;
    }

    private sealed class RecordingSink : IPlaybackEventSink
    {
        public int Started { get; private set; }

        public int Progressed { get; private set; }

        public int Stopped { get; private set; }

        public int SessionsEnded { get; private set; }

        public void PlaybackStarted(PlaybackProgressEventArgs args) => Started++;

        public void PlaybackProgressed(PlaybackProgressEventArgs args) => Progressed++;

        public void PlaybackStopped(PlaybackStopEventArgs args) => Stopped++;

        public void SessionEnded(SessionEventArgs args) => SessionsEnded++;
    }

    private sealed class ThrowingSink : IPlaybackEventSink
    {
        public void PlaybackStarted(PlaybackProgressEventArgs args) => throw new InvalidOperationException("start");

        public void PlaybackProgressed(PlaybackProgressEventArgs args) => throw new InvalidOperationException("progress");

        public void PlaybackStopped(PlaybackStopEventArgs args) => throw new InvalidOperationException("stop");

        public void SessionEnded(SessionEventArgs args) => throw new InvalidOperationException("session end");
    }
}
