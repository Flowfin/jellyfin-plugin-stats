// Which route ended a play, on the row and in the figures a report answers with.
//
// A play the server sent a stop for and a play something gave up waiting for
// were the same row, so a report could say how many plays it had read and not
// how many of them ended cleanly. The watched time on the first is what was
// watched; on the rest it is what had been watched by the last moment the server
// heard from the session, which is a floor.
//
// Nothing here waits and nothing reads a clock. Every moment a row carries is
// set on the session, and the moment a bound is measured back from is handed to
// a fixed clock the case chose, so a play that has been quiet for an hour is a
// case that runs in microseconds.
//
// Issue #222.

using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Capture;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Reports;
using Jellyfin.Plugin.Stats.ScheduledTasks;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ServerPlayMethod = MediaBrowser.Model.Session.PlayMethod;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class HowAPlayWasClosedTests : IDisposable
{
    private static readonly DateTimeOffset Eight = new(2026, 1, 2, 20, 0, 0, TimeSpan.Zero);

    private static readonly Guid Alice = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static readonly DateTime March = new(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _root;

    public HowAPlayWasClosedTests()
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
    /// The first condition of issue #222, over every route that can end a play
    /// inside the capture.
    /// </summary>
    /// <remarks>
    /// One case over the three rather than three cases, because what is being
    /// asserted is that they differ. Three cases each asserting their own value
    /// would all pass over a tracker that recorded one constant, and that
    /// constant is what the row held before this.
    /// </remarks>
    [Fact]
    public void EachRouteInsideTheCaptureRecordsItselfAndNotTheOthers()
    {
        var sessions = new FakeSessionManager();
        var rows = new RecordingPlaySink();
        var tracker = new PlayTracker(rows, NullLogger<PlayTracker>.Instance);

        var stopped = ASession(sessions, "session-1", "play-1");
        tracker.PlaybackStarted(StartOf(sessions, stopped, Eight));
        tracker.PlaybackStopped(StopOf(sessions, stopped, TimeSpan.FromMinutes(30), Eight.AddMinutes(30)));

        var ended = ASession(sessions, "session-2", "play-2", "device-2");
        tracker.PlaybackStarted(StartOf(sessions, ended, Eight));
        tracker.SessionEnded(EndOf(sessions, ended));

        var quiet = ASession(sessions, "session-3", "play-3", "device-3");
        tracker.PlaybackStarted(StartOf(sessions, quiet, Eight));
        Assert.Equal(1, new QuietPlaySweep(tracker, new FixedClock(Eight.AddHours(1)), QuietPlaySweep.DefaultBound).Run());

        Assert.Equal(
            new[] { PlayClosedBy.AStopEvent, PlayClosedBy.TheSessionEnding, PlayClosedBy.GoingQuiet },
            rows.Rows.Select(row => row.ClosedBy));
    }

    /// <summary>
    /// A play that is still running says nothing about what closed it, because
    /// nothing has.
    /// </summary>
    /// <remarks>
    /// The running row is the same shape as a finished one, so it carries this
    /// column too. A running row that named a route would be answering a
    /// question the play has not reached, and the route it named would be
    /// whichever one the code that built the snapshot happened to pass.
    /// </remarks>
    [Fact]
    public void ARunningRowSaysNothingAboutWhatClosedThePlay()
    {
        var sessions = new FakeSessionManager();
        var rows = new RecordingPlaySink();
        var tracker = new PlayTracker(rows, NullLogger<PlayTracker>.Instance);

        var session = ASession(sessions, "session-1", "play-1");
        tracker.PlaybackStarted(StartOf(sessions, session, Eight));

        Assert.Equal(PlayClosedBy.NotSaid, Assert.Single(rows.Running).SoFar.ClosedBy);
    }

    /// <summary>
    /// The second condition of issue #222. A row written before the column
    /// existed reads back as not saying, rather than as closed cleanly.
    /// </summary>
    /// <remarks>
    /// The row goes in through the statement the build at that version ran,
    /// against a store taken up to that version and no further, so what is read
    /// back is what an installation upgrading tonight would have. A case that
    /// wrote the row through today's statement and then blanked the column would
    /// be testing a state no build ever wrote.
    /// <para>
    /// Not saying and closed cleanly are the two answers this is between, and
    /// both are asserted. A default of the clean value would pass an assertion
    /// that only asked whether something came back.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARowWrittenBeforeTheColumnExistedReadsBackAsNotSayingRatherThanCleanly()
    {
        Directory.CreateDirectory(_root);

        using (var connection = OpenTheFile())
        {
            SchemaMigrator.MigrateToLatest(
                connection,
                SchemaMigrations.All.Where(step => step.Version <= 5).ToList());

            using var insert = connection.CreateCommand();
            insert.CommandText =
                @"INSERT INTO plays (
                      SchemaVersion, UserId, ItemId, ItemType, ItemName,
                      StartedUtcTicks, EndedUtcTicks, WatchedDurationTicks, ReachedTheEnd,
                      ClientName, DeviceId, DeviceName, PlayMethodAtStart,
                      TranscodeVideoWasDirect, TranscodeAudioWasDirect, TranscodeReasons
                  ) VALUES (
                      5, $userId, $itemId, 'Movie', 'An older film',
                      $started, $ended, 0, 0,
                      'Web', 'a-device', 'A device', 1,
                      1, 1, ''
                  )";
            insert.Parameters.AddWithValue("$userId", Alice.ToString("N"));
            insert.Parameters.AddWithValue("$itemId", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$started", March.Ticks);
            insert.Parameters.AddWithValue("$ended", March.AddMinutes(41).Ticks);
            insert.ExecuteNonQuery();
        }

        using var reopened = new SqlitePlayStore(_root);

        var read = Assert.Single(reopened.MostRecentPlays(10));

        Assert.Equal("An older film", read.ItemName);
        Assert.Equal(PlayClosedBy.NotSaid, read.ClosedBy);
        Assert.NotEqual(PlayClosedBy.AStopEvent, read.ClosedBy);
    }

    /// <summary>
    /// The fourth condition of issue #222. The column arrives as an appended
    /// step, and a store written by an earlier build still opens and reads every
    /// row it had.
    /// </summary>
    /// <remarks>
    /// The step list is asserted as well as the upgrade. A column added by
    /// editing a step that has already shipped would leave this case green, and
    /// would leave every installation that had already run that step without the
    /// column.
    /// </remarks>
    [Fact]
    public void TheColumnArrivesAsAnAppendedStepAndAnOlderStoreStillReads()
    {
        Assert.Equal(6, SchemaMigrations.Latest);
        Assert.Equal(
            new[] { 1, 2, 3, 4, 5, 6 },
            SchemaMigrations.All.Select(step => step.Version));

        Directory.CreateDirectory(_root);

        using (var connection = OpenTheFile())
        {
            SchemaMigrator.MigrateToLatest(
                connection,
                SchemaMigrations.All.Where(step => step.Version <= 5).ToList());

            using var insert = connection.CreateCommand();
            insert.CommandText =
                @"INSERT INTO plays (
                      SchemaVersion, UserId, ItemId, ItemType, ItemName,
                      StartedUtcTicks, EndedUtcTicks, WatchedDurationTicks, ReachedTheEnd,
                      ClientName, DeviceId, DeviceName, PlayMethodAtStart,
                      TranscodeVideoWasDirect, TranscodeAudioWasDirect, TranscodeReasons
                  ) VALUES (
                      5, $userId, $itemId, 'Movie', 'An older film',
                      $started, $ended, $watched, 0,
                      'Web', 'a-device', 'A device', 1,
                      1, 1, ''
                  )";
            insert.Parameters.AddWithValue("$userId", Alice.ToString("N"));
            insert.Parameters.AddWithValue("$itemId", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$started", March.Ticks);
            insert.Parameters.AddWithValue("$ended", March.AddMinutes(41).Ticks);
            insert.Parameters.AddWithValue("$watched", TimeSpan.FromMinutes(41).Ticks);
            insert.ExecuteNonQuery();
        }

        using (var reopened = new SqlitePlayStore(_root))
        {
            var read = Assert.Single(reopened.AllPlays());

            Assert.Equal(TimeSpan.FromMinutes(41), read.WatchedDuration);
            Assert.Equal(PlayClosedBy.NotSaid, read.ClosedBy);
        }

        // Opening the store is what runs the steps, so the version on the file
        // is read after that and through a connection of this case's own.
        using var afterwards = OpenTheFile();

        Assert.Equal(SchemaMigrations.Latest, SchemaMigrator.CurrentVersion(afterwards));

    }

    /// <summary>
    /// The third condition of issue #222. A report over a set mixing every route
    /// says how many of the plays it read ended cleanly.
    /// </summary>
    /// <remarks>
    /// The set mixes all five answers on purpose, including the one that says
    /// nothing, because the mistake this is against is a fold that divides the
    /// plays into clean and unclean and puts the absence on one side of that
    /// line.
    /// </remarks>
    [Fact]
    public void AReportSaysHowManyOfThePlaysItReadEndedCleanly()
    {
        Store(
            APlay(March, PlayClosedBy.AStopEvent),
            APlay(March.AddHours(1), PlayClosedBy.AStopEvent),
            APlay(March.AddHours(2), PlayClosedBy.AStopEvent),
            APlay(March.AddHours(3), PlayClosedBy.TheSessionEnding),
            APlay(March.AddHours(4), PlayClosedBy.GoingQuiet),
            APlay(March.AddHours(5), PlayClosedBy.GoingQuiet),
            APlay(March.AddHours(6), PlayClosedBy.ARestart),
            APlay(March.AddHours(7), PlayClosedBy.NotSaid));

        var totals = new AggregateQueries(() => new SqlitePlayStore(_root))
            .Total(QueryWindow.Of(March, March.AddDays(1)));

        Assert.Equal(8, totals.Plays);
        Assert.Equal(3, totals.Ending.Cleanly);
        Assert.Equal(1, totals.Ending.OnASessionEnding);
        Assert.Equal(2, totals.Ending.OnSilence);
        Assert.Equal(1, totals.Ending.OnARestart);
        Assert.Equal(1, totals.Ending.NotSaid);
    }

    /// <summary>
    /// The five figures add up to the plays they were folded from.
    /// </summary>
    /// <remarks>
    /// Counted rather than derived on both sides, which is what makes this worth
    /// asserting: a fold that worked the total out by adding the five up would
    /// make this true by definition and would say nothing about a row it lost.
    /// </remarks>
    [Fact]
    public void TheFiveFiguresAddUpToThePlaysTheyWereFoldedFrom()
    {
        var ended = HowPlaysEnded.Over(
        [
            APlay(March, PlayClosedBy.AStopEvent),
            APlay(March.AddHours(1), PlayClosedBy.TheSessionEnding),
            APlay(March.AddHours(2), PlayClosedBy.GoingQuiet),
            APlay(March.AddHours(3), PlayClosedBy.ARestart),
            APlay(March.AddHours(4), PlayClosedBy.NotSaid)
        ]);

        Assert.Equal(5, ended.Plays);
        Assert.Equal(
            ended.Plays,
            ended.Cleanly + ended.OnASessionEnding + ended.OnSilence + ended.OnARestart + ended.NotSaid);
    }

    /// <summary>
    /// A row carrying a route this build has no name for is counted as not
    /// saying, and is never counted as clean.
    /// </summary>
    /// <remarks>
    /// What a row from a later build looks like from here. It is the same
    /// answer the delivery shares give a method they have no name for, and for
    /// the same reason: the plugin does not know how that play ended, dropping
    /// it would take a play out of the answer with nothing saying so, and
    /// counting it as clean would flatter the server on no evidence.
    /// </remarks>
    [Fact]
    public void ARouteThisBuildHasNoNameForIsCountedAsNotSaying()
    {
        var ended = HowPlaysEnded.Over([APlay(March, (PlayClosedBy)99)]);

        Assert.Equal(1, ended.Plays);
        Assert.Equal(1, ended.NotSaid);
        Assert.Equal(0, ended.Cleanly);
    }


    /// <summary>
    /// A row that says it is older than this column and carries it anyway keeps
    /// its answer through an export and an import.
    /// </summary>
    /// <remarks>
    /// NOT A HYPOTHETICAL ROW. The capture stamps every row it writes with the
    /// version the row shape was decided at rather than with the store's, so a
    /// row written today says it is at version one while carrying a column added
    /// at version six. The step that fills this column in on the way back has to
    /// fill an absence and never overwrite an answer, or a store exported and
    /// imported again would come back saying nothing about how any of its plays
    /// ended.
    /// <para>
    /// The step beside it, which came in with the delivery method, does
    /// overwrite. That is a defect of its own rather than one this case is
    /// about, and it is written on issue #222 with what it costs.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARowOlderThanTheColumnThatCarriesItAnywayKeepsItsAnswer()
    {
        var written = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);

        PlayArchive.Export(
            [APlay(March, PlayClosedBy.GoingQuiet) with { SchemaVersion = 1 }],
            written);

        using var store = new SqlitePlayStore(_root);
        using var reading = new StringReader(written.ToString());

        Assert.Equal(1, PlayArchive.Import(reading, store));
        Assert.Equal(PlayClosedBy.GoingQuiet, Assert.Single(store.AllPlays()).ClosedBy);
    }

    /// <summary>
    /// The route survives the file.
    /// </summary>
    /// <remarks>
    /// The column is on both tables, so a running row keeps its answer across a
    /// restart as well, which is what the row a later start-up finishes is read
    /// out of.
    /// </remarks>
    [Fact]
    public void TheRouteGoesToTheFileAndComesBack()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlay(March, PlayClosedBy.GoingQuiet));
            store.NoteOpenPlay(new OpenPlay
            {
                PlayKey = "play-1",
                SoFar = APlay(March.AddHours(1), PlayClosedBy.NotSaid)
            });
        }

        using var reopened = new SqlitePlayStore(_root);

        Assert.Equal(PlayClosedBy.GoingQuiet, Assert.Single(reopened.AllPlays()).ClosedBy);
        Assert.Equal(PlayClosedBy.NotSaid, Assert.Single(reopened.OpenPlays()).SoFar.ClosedBy);
    }

    private Microsoft.Data.Sqlite.SqliteConnection OpenTheFile()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_root, SqlitePlayStore.FileName),
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString());

        connection.Open();

        return connection;
    }

    private void Store(params PlayRecord[] plays)
    {
        using var store = new SqlitePlayStore(_root);

        foreach (var play in plays)
        {
            store.Add(play);
        }
    }

    private static PlayRecord APlay(DateTime startedUtc, PlayClosedBy closedBy)
    {
        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = Alice,
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = "Movie",
            ParentId = null,
            ItemName = "A Film",
            ItemRuntime = TimeSpan.FromMinutes(90),
            StartedUtc = startedUtc,
            EndedUtc = startedUtc.AddMinutes(30),
            WatchedDuration = TimeSpan.FromMinutes(30),
            ReachedTheEnd = false,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethodAtStart = PlayMethod.DirectPlay,
            PlayMethodChangedUtc = null,
            ClosedBy = closedBy,
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
        };
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

    private static PlaybackProgressEventArgs StartOf(FakeSessionManager sessions, SessionInfo session, DateTimeOffset at)
    {
        PlaybackProgressEventArgs? seen = null;

        void Handler(object? sender, PlaybackProgressEventArgs args) => seen = args;

        sessions.PlaybackStart += Handler;
        try
        {
            sessions.RaisePlaybackStart(session, at);
        }
        finally
        {
            sessions.PlaybackStart -= Handler;
        }

        return seen!;
    }

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

    private static SessionEventArgs EndOf(FakeSessionManager sessions, SessionInfo session)
    {
        SessionEventArgs? seen = null;

        void Handler(object? sender, SessionEventArgs args) => seen = args;

        sessions.SessionEnded += Handler;
        try
        {
            sessions.RaiseSessionEnded(session);
        }
        finally
        {
            sessions.SessionEnded -= Handler;
        }

        return seen!;
    }

    /// <summary>
    /// Keeps what the tracker hands over, finished rows and running ones alike.
    /// </summary>
    /// <remarks>
    /// Nested here rather than shared with the cases beside it, which is what
    /// those already do with theirs. This one keeps the running rows as well,
    /// because one case here is about what a running row says.
    /// </remarks>
    private sealed class RecordingPlaySink : IPlaySink
    {
        private readonly System.Collections.Generic.List<PlayRecord> _rows = new();
        private readonly System.Collections.Generic.List<OpenPlay> _running = new();

        /// <summary>
        /// Gets the finished rows, in the order they were handed over.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<PlayRecord> Rows => _rows;

        /// <summary>
        /// Gets the running rows, in the order they were handed over.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<OpenPlay> Running => _running;

        public void Add(PlayRecord play, string playKey) => _rows.Add(play);

        public void NoteOpen(OpenPlay play) => _running.Add(play);

        public void ForgetOpen(string playKey)
        {
        }
    }
}
