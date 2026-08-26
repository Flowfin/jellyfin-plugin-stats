// The plays that reach a row without a stop event, and the two routes that get
// them there.
//
// A play arrives as a start, some progress reports and a stop, and the stop is
// the one event a server cannot promise. A session ends, a client loses its
// network, a process is restarted in the middle of a film. Each of those leaves
// a play that happened and that nothing would ever write down, and each is
// answered here.
//
// Nothing in this file waits and nothing reads a clock. Every moment a row
// carries is set on the session, which is the field the server writes when a
// client checks in, and the moment a bound is measured back from is handed to a
// fixed clock the test chose. A play that has been quiet for an hour is
// therefore a case that runs in microseconds.
//
// Issue #221.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Capture;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.ScheduledTasks;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ServerPlayMethod = MediaBrowser.Model.Session.PlayMethod;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class PlaysNobodyStoppedTests : IDisposable
{
    private static readonly DateTimeOffset Eight = new(2026, 1, 2, 20, 0, 0, TimeSpan.Zero);

    private static readonly Guid Alice = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private readonly string _root;

    public PlaysNobodyStoppedTests()
    {
        _root = Path.Join(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// A session that ends while a play is running produces the row that play
    /// had reached, ending at the last moment the server heard from it.
    /// </summary>
    /// <remarks>
    /// The alternative is discarding it, and that is what this case is really
    /// against: a browser tab closed on a film that ran for twenty minutes is
    /// twenty minutes somebody watched, and a plugin that dropped it would
    /// report a quiet evening on a server that was busy.
    /// </remarks>
    [Fact]
    public void ASessionThatEndsWithoutAStopStillProducesItsRow()
    {
        var sessions = new FakeSessionManager();
        var rows = new RecordingPlaySink();
        var tracker = new PlayTracker(rows, FakeChannelNames.Empty, NullLogger<PlayTracker>.Instance);
        var session = ASession(sessions, "session-1", "play-1");

        tracker.PlaybackStarted(StartOf(sessions, session, Eight));
        tracker.PlaybackProgressed(ProgressOf(sessions, session, TimeSpan.FromMinutes(20), Eight.AddMinutes(20)));

        tracker.SessionEnded(new SessionEventArgs { SessionInfo = session });

        var row = Assert.Single(rows.Rows);

        Assert.Equal(Eight.UtcDateTime, row.StartedUtc);
        Assert.Equal(Eight.AddMinutes(20).UtcDateTime, row.EndedUtc);
        Assert.Equal(TimeSpan.FromMinutes(20), row.WatchedDuration);
        Assert.False(row.ReachedTheEnd, "Nothing said the item was played through, so the row may not say so.");
        Assert.Equal(0, tracker.OpenPlays);
    }

    /// <summary>
    /// A session ending closes the plays it held and nothing anybody else was
    /// watching.
    /// </summary>
    /// <remarks>
    /// The plays are matched on the session they arrived on rather than on the
    /// key their events are joined on, and those are different identifiers. A
    /// match on the wrong one closes every play on the server the moment one
    /// client disconnects.
    /// </remarks>
    [Fact]
    public void ASessionEndingClosesItsOwnPlaysAndNobodyElsesRow()
    {
        var sessions = new FakeSessionManager();
        var rows = new RecordingPlaySink();
        var tracker = new PlayTracker(rows, FakeChannelNames.Empty, NullLogger<PlayTracker>.Instance);

        var leaving = ASession(sessions, "session-1", "play-1");
        var staying = ASession(sessions, "session-2", "play-2", device: "device-2");

        tracker.PlaybackStarted(StartOf(sessions, leaving, Eight));
        tracker.PlaybackStarted(StartOf(sessions, staying, Eight));

        tracker.SessionEnded(new SessionEventArgs { SessionInfo = leaving });

        Assert.Single(rows.Rows);
        Assert.Equal("play-1", Assert.Single(rows.Keys));
        Assert.Equal(1, tracker.OpenPlays);
    }

    /// <summary>
    /// A play whose session has said nothing for longer than the bound is
    /// closed, and one inside the bound is left alone.
    /// </summary>
    /// <remarks>
    /// Both halves in one case, because the bound is only worth anything if it
    /// separates the two. A sweep that closed everything would pass a case that
    /// asserted only the first half, and it would end every play on the server
    /// ten minutes after it started.
    /// <para>
    /// The clock is fixed and the play is old because the test said so, not
    /// because anything waited. That is the whole reason the moment is an
    /// argument.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(31, 1)]
    [InlineData(29, 0)]
    public void APlayIsClosedOnceItHasBeenQuietForLongerThanTheBound(int quietForMinutes, int expectedRows)
    {
        var sessions = new FakeSessionManager();
        var rows = new RecordingPlaySink();
        var tracker = new PlayTracker(rows, FakeChannelNames.Empty, NullLogger<PlayTracker>.Instance);
        var session = ASession(sessions, "session-1", "play-1");

        tracker.PlaybackStarted(StartOf(sessions, session, Eight));

        var sweep = new QuietPlaySweep(
            tracker,
            new FixedClock(Eight.AddMinutes(quietForMinutes)),
            QuietPlaySweep.DefaultBound);

        Assert.Equal(expectedRows, sweep.Run());
        Assert.Equal(expectedRows, rows.Rows.Count);
    }

    /// <summary>
    /// The bound is measured from the last moment the server heard from the
    /// session and not from when the play started.
    /// </summary>
    /// <remarks>
    /// A film is longer than the bound. Measured from the start, every play
    /// over half an hour would be closed while somebody was still watching it,
    /// and the stop arriving afterwards would write it a second time.
    /// </remarks>
    [Fact]
    public void APlayThatIsStillReportingIsNotClosedHoweverLongItHasRun()
    {
        var sessions = new FakeSessionManager();
        var rows = new RecordingPlaySink();
        var tracker = new PlayTracker(rows, FakeChannelNames.Empty, NullLogger<PlayTracker>.Instance);
        var session = ASession(sessions, "session-1", "play-1");

        tracker.PlaybackStarted(StartOf(sessions, session, Eight));
        tracker.PlaybackProgressed(ProgressOf(sessions, session, TimeSpan.FromMinutes(100), Eight.AddMinutes(100)));

        var sweep = new QuietPlaySweep(
            tracker,
            new FixedClock(Eight.AddMinutes(110)),
            QuietPlaySweep.DefaultBound);

        Assert.Equal(0, sweep.Run());
        Assert.Empty(rows.Rows);
        Assert.Equal(1, tracker.OpenPlays);
    }

    /// <summary>
    /// A stop that arrives after the play was closed does not write it again.
    /// </summary>
    /// <remarks>
    /// This is the property the three pieces of issue #36 share, on the route
    /// most likely to break it: a client that went quiet for an hour and then
    /// came back to send its stop. Closing a play takes it out of the tracker in
    /// the same act that hands its row over, so the late stop finds nothing open
    /// and is counted instead of written.
    /// </remarks>
    [Fact]
    public void AStopThatArrivesAfterTheCloseDoesNotWriteThePlayAgain()
    {
        var sessions = new FakeSessionManager();
        var rows = new RecordingPlaySink();
        var tracker = new PlayTracker(rows, FakeChannelNames.Empty, NullLogger<PlayTracker>.Instance);
        var session = ASession(sessions, "session-1", "play-1");

        tracker.PlaybackStarted(StartOf(sessions, session, Eight));

        var sweep = new QuietPlaySweep(tracker, new FixedClock(Eight.AddHours(1)), QuietPlaySweep.DefaultBound);

        Assert.Equal(1, sweep.Run());

        tracker.PlaybackStopped(StopOf(sessions, session, TimeSpan.FromMinutes(5), Eight.AddHours(1)));

        Assert.Single(rows.Rows);
        Assert.Equal(1, tracker.EventsWithNoOpenPlay);
    }

    /// <summary>
    /// A session ending twice writes the play once.
    /// </summary>
    /// <remarks>
    /// Nothing in the server promises one session end per session, and the
    /// second one arrives at a tracker that has already handed the row over.
    /// </remarks>
    [Fact]
    public void ASecondSessionEndWritesNothingFurther()
    {
        var sessions = new FakeSessionManager();
        var rows = new RecordingPlaySink();
        var tracker = new PlayTracker(rows, FakeChannelNames.Empty, NullLogger<PlayTracker>.Instance);
        var session = ASession(sessions, "session-1", "play-1");

        tracker.PlaybackStarted(StartOf(sessions, session, Eight));

        tracker.SessionEnded(new SessionEventArgs { SessionInfo = session });
        tracker.SessionEnded(new SessionEventArgs { SessionInfo = session });

        Assert.Single(rows.Rows);
    }

    /// <summary>
    /// A play a previous process left running on the file is finished, with the
    /// position and the moment that process last recorded, and it stops being
    /// an open row.
    /// </summary>
    /// <remarks>
    /// Driven over a real store rather than a fake, because what the condition
    /// is about is a file that outlived a process. The open row is written
    /// through one store and read back through another opened over the same
    /// folder, which is what a restart is.
    /// <para>
    /// One field is not as it stood and the comparison says which. The open row
    /// said nothing about what closed it, because nothing had; the finished row
    /// says a restart did. Everything the previous process knew about the play
    /// is carried across untouched, and what is added is the one thing that
    /// process could not have known.
    /// </para>
    /// </remarks>
    [Fact]
    public void APlayLeftRunningByAPreviousProcessIsFinishedAsItStood()
    {
        var left = ARunningPlay("play-1", startedUtc: Eight.UtcDateTime, watched: TimeSpan.FromMinutes(37));

        using (var before = new SqlitePlayStore(_root))
        {
            before.NoteOpenPlay(left);
        }

        Assert.Equal(1, new FinishWhatARestartLeftOpen(() => new SqlitePlayStore(_root)).Run());

        using var after = new SqlitePlayStore(_root);

        var row = Assert.Single(after.AllPlays());

        Assert.Equal(left.SoFar with { ClosedBy = PlayClosedBy.ARestart }, row);
        Assert.Equal(PlayClosedBy.NotSaid, left.SoFar.ClosedBy);
        Assert.Empty(after.OpenPlays());
    }

    /// <summary>
    /// The play appears exactly once, however many times the pass runs.
    /// </summary>
    /// <remarks>
    /// A restart is not a single event in the life of a file, and a pass that
    /// added a row each time it ran would turn one interrupted film into one row
    /// per restart. What holds it is the store's own write, which adds the
    /// finished row and removes the open one in one transaction.
    /// </remarks>
    [Fact]
    public void APlayLeftRunningIsFinishedOnceHoweverOftenThePassRuns()
    {
        using (var before = new SqlitePlayStore(_root))
        {
            before.NoteOpenPlay(ARunningPlay("play-1", startedUtc: Eight.UtcDateTime, watched: TimeSpan.FromMinutes(37)));
        }

        var pass = new FinishWhatARestartLeftOpen(() => new SqlitePlayStore(_root));

        Assert.Equal(1, pass.Run());
        Assert.Equal(0, pass.Run());
        Assert.Equal(0, pass.Run());

        using var after = new SqlitePlayStore(_root);

        Assert.Single(after.AllPlays());
    }

    /// <summary>
    /// A file holding no open plays costs nothing and writes nothing.
    /// </summary>
    [Fact]
    public void AFileWithNothingLeftOpenProducesNoRow()
    {
        using (var before = new SqlitePlayStore(_root))
        {
            before.Dispose();
        }

        Assert.Equal(0, new FinishWhatARestartLeftOpen(() => new SqlitePlayStore(_root)).Run());

        using var after = new SqlitePlayStore(_root);

        Assert.Empty(after.AllPlays());
    }

    /// <summary>
    /// The listener finishes what was left open before it subscribes to
    /// anything.
    /// </summary>
    /// <remarks>
    /// The ordering is the whole of what makes the pass safe, so it is asserted
    /// at the instant it matters rather than inferred from the source. The store
    /// raises a playback start on the server the moment its open rows are read,
    /// and the tracker behind the listener is holding nothing when it does,
    /// which is only true if no handler had been added yet.
    /// <para>
    /// A pass made after the subscriptions were live would meet the open rows of
    /// plays being watched right now, close them, and write each of them a
    /// second time when its stop arrived.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheListenerFinishesWhatWasLeftOpenBeforeItSubscribes()
    {
        var sessions = new FakeSessionManager();
        var rows = new RecordingPlaySink();
        var tracker = new PlayTracker(rows, FakeChannelNames.Empty, NullLogger<PlayTracker>.Instance);
        var session = ASession(sessions, "session-1", "play-1");

        var subscribedAlready = true;
        var store = new HoldablePlayStore
        {
            WhenOpenPlaysAreRead = () =>
            {
                sessions.RaisePlaybackStart(session, Eight);
                subscribedAlready = tracker.OpenPlays > 0;
            }
        };

        var listener = new PlaybackEventListener(
            sessions,
            tracker,
            new FinishWhatARestartLeftOpen(() => store),
            NullLogger<PlaybackEventListener>.Instance);

        await listener.StartAsync(CancellationToken.None);

        Assert.False(
            subscribedAlready,
            "A playback start raised while the open rows were being read reached the tracker, so the listener had already subscribed when the pass ran.");
    }

    /// <summary>
    /// A store that cannot be opened costs the pass and nothing else: the host
    /// still starts and the events are still subscribed to.
    /// </summary>
    /// <remarks>
    /// The rule is issue #31's. A plugin whose file is corrupt or locked is this
    /// plugin's failure and nobody else's, and the pass runs inside the host's
    /// own start, which is the one path where letting it out would take the
    /// server with it.
    /// </remarks>
    [Fact]
    public async Task AStoreThatCannotBeOpenedCostsThePassAndNothingElse()
    {
        var sessions = new FakeSessionManager();
        var rows = new RecordingPlaySink();
        var tracker = new PlayTracker(rows, FakeChannelNames.Empty, NullLogger<PlayTracker>.Instance);

        var listener = new PlaybackEventListener(
            sessions,
            tracker,
            new FinishWhatARestartLeftOpen(() => throw new IOException("The file is not a database.")),
            NullLogger<PlaybackEventListener>.Instance);

        await listener.StartAsync(CancellationToken.None);

        var session = ASession(sessions, "session-1", "play-1");
        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(5), at: Eight.AddMinutes(5));

        Assert.Single(rows.Rows);
    }

    /// <summary>
    /// The sweep and the capture path work on one tracker.
    /// </summary>
    /// <remarks>
    /// The plays this sweep closes are held in memory by the tracker the
    /// server's events reach, so two instances is a sweep walking an empty
    /// dictionary while the plays sit in the other one. Nothing about that
    /// failure is visible: the task runs, reports itself finished, and closes
    /// nothing, forever.
    /// <para>
    /// Resolved out of a container carrying this plugin's own registrations,
    /// because what decides it is a registration rather than anything in either
    /// class.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSweepAndTheCaptureWorkOnOneTracker()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IUserManager>(new FakeUserManager());

        new PluginServiceRegistrator().RegisterServices(services, applicationHost: null!);

        using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<PlayTracker>(),
            provider.GetRequiredService<IPlaybackEventSink>());
    }

    private static SessionInfo ASession(
        FakeSessionManager sessions,
        string sessionId,
        string playSessionId,
        string device = "device-1")
        => new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer"))
            .Playing(PlaySessionBuilder.Video("A Film", TimeSpan.FromMinutes(90)))
            .From("Jellyfin Web", "A browser", device)
            .Identified(sessionId, playSessionId)
            .Via(ServerPlayMethod.DirectPlay)
            .Build();

    /// <summary>
    /// The start event for a session, taken from the fake so the moment lands
    /// on the session's own check-in field, which is where every time in a row
    /// comes from.
    /// </summary>
    private static PlaybackProgressEventArgs StartOf(FakeSessionManager sessions, SessionInfo session, DateTimeOffset at)
        => CapturedBy(sessions, capture => capture.RaisePlaybackStart(session, at));

    private static PlaybackProgressEventArgs ProgressOf(
        FakeSessionManager sessions,
        SessionInfo session,
        TimeSpan position,
        DateTimeOffset at)
        => CapturedBy(sessions, capture => capture.RaisePlaybackProgress(session, position, at: at));

    private static PlaybackStopEventArgs StopOf(
        FakeSessionManager sessions,
        SessionInfo session,
        TimeSpan position,
        DateTimeOffset at)
    {
        PlaybackStopEventArgs? seen = null;

        void Handler(object? sender, PlaybackStopEventArgs args) => seen = args;

        sessions.PlaybackStopped += Handler;
        try
        {
            sessions.RaisePlaybackStopped(session, position, at: at);
        }
        finally
        {
            sessions.PlaybackStopped -= Handler;
        }

        return seen!;
    }

    /// <summary>
    /// Raises one event on the fake and hands back the arguments the server
    /// would have carried, so a case can drive the tracker directly and still
    /// work from the shape a real event has.
    /// </summary>
    private static PlaybackProgressEventArgs CapturedBy(FakeSessionManager sessions, Action<FakeSessionManager> raise)
    {
        PlaybackProgressEventArgs? seen = null;

        void OnStart(object? sender, PlaybackProgressEventArgs args) => seen = args;

        sessions.PlaybackStart += OnStart;
        sessions.PlaybackProgress += OnStart;
        try
        {
            raise(sessions);
        }
        finally
        {
            sessions.PlaybackStart -= OnStart;
            sessions.PlaybackProgress -= OnStart;
        }

        return seen!;
    }

    /// <summary>
    /// A sink that keeps what it is handed, so a case can read the rows a close
    /// produced without a store under it.
    /// </summary>
    /// <remarks>
    /// Nested here rather than shared, which is what the three cases beside it
    /// in this suite already do. What each one keeps is what its own file
    /// asserts on, and a shared one grows a member per caller until it is a
    /// fake nobody can read.
    /// </remarks>
    private sealed class RecordingPlaySink : IPlaySink
    {
        private readonly List<PlayRecord> _rows = new();
        private readonly List<string> _keys = new();

        /// <summary>
        /// Gets the finished rows, in the order they were handed over.
        /// </summary>
        public IReadOnlyList<PlayRecord> Rows => _rows;

        /// <summary>
        /// Gets the keys of the finished rows, in the same order.
        /// </summary>
        public IReadOnlyList<string> Keys => _keys;

        public void Add(PlayRecord play, string playKey)
        {
            _rows.Add(play);
            _keys.Add(playKey);
        }

        public void NoteOpen(OpenPlay play)
        {
        }

        public void ForgetOpen(string playKey)
        {
        }
    }

    private static OpenPlay ARunningPlay(string key, DateTime startedUtc, TimeSpan watched)
    {
        return new OpenPlay
        {
            PlayKey = key,
            SoFar = new PlayRecord
            {
                SchemaVersion = SqlitePlayStore.SchemaVersion,
                UserId = Alice,
                ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                ItemType = "Movie",
                ParentId = null,
                ItemName = "An item",
                ItemRuntime = TimeSpan.FromMinutes(90),
                ChannelName = null,
                StartedUtc = startedUtc,
                EndedUtc = startedUtc + watched,
                WatchedDuration = watched,
                ReachedTheEnd = false,
                ClientName = "Jellyfin Web",
                DeviceId = "device-1",
                DeviceName = "A browser",
                PlayMethodAtStart = PlayMethod.DirectPlay,
                PlayMethodChangedUtc = null,

                // A running row has not been closed, so nothing has ended it
                // and the row says so. This is what the tracker writes for one.
                ClosedBy = PlayClosedBy.NotSaid,
                Transcode = new TranscodeSummary
                {
                    VideoCodec = null,
                    AudioCodec = null,
                    VideoWasDirect = true,
                    AudioWasDirect = true,
                    PeakBitrate = null,
                    TypicalBitrate = null,
                    HardwareAcceleration = null,
                    Reasons = Array.Empty<string>()
                }
            }
        };
    }
}
