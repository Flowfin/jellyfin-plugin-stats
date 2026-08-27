// What a live television row says about the channel it was on.
//
// A live programme has no length a completion ratio could be computed from and
// a title that is one broadcast rather than a thing in a library, so the fact
// worth keeping about it is the channel. The name and not the identifier: the
// rows a yearly report is about are old ones, and a channel is renamed and
// taken off the air while they sit there. Issue #40.

using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Stats.Capture;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ServerPlayMethod = MediaBrowser.Model.Session.PlayMethod;

namespace Jellyfin.Plugin.Stats.Tests;

public class TheChannelALivePlayWasOnTests : IDisposable
{
    private static readonly DateTimeOffset Eight = new(2026, 1, 2, 20, 0, 0, TimeSpan.Zero);
    private static readonly Guid TheChannel = Guid.Parse("9b1c7a30-4d5e-4f60-8a71-2c3d4e5f6071");
    private static readonly Guid Viewer = Guid.Parse("3f2a1b0c-9d8e-4f70-b615-243546576879");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "stats-channel-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ALiveProgrammeRecordsTheChannelItWasOn()
    {
        var sessions = new FakeSessionManager();
        var channels = new FakeChannelNames().Called(TheChannel, "BBC One");
        var rows = new RecordingPlaySink();
        var tracker = new PlayTracker(rows, channels, NullLogger<PlayTracker>.Instance);

        sessions.PlaybackStart += (_, args) => tracker.PlaybackStarted(args);
        sessions.PlaybackStopped += (_, args) => tracker.PlaybackStopped(args);

        var session = new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer"))
            .Playing(PlaySessionBuilder.LiveProgramme("The Nine at Nine", TheChannel))
            .Via(ServerPlayMethod.DirectStream)
            .Build();

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(30), playedToCompletion: false, at: Eight.AddMinutes(30));

        var row = Assert.Single(rows.Rows);

        Assert.Equal("BBC One", row.ChannelName);

        // The two halves of this issue together. The channel is on the row
        // exactly where a length is not, so nothing downstream can compute a
        // completion out of a programme that has no end.
        Assert.Null(row.ItemRuntime);
    }

    [Fact]
    public void TheChannelIsResolvedOnceHoweverOftenTheSessionReports()
    {
        var sessions = new FakeSessionManager();
        var channels = new FakeChannelNames().Called(TheChannel, "BBC One");
        var rows = new RecordingPlaySink();
        var tracker = new PlayTracker(rows, channels, NullLogger<PlayTracker>.Instance);

        sessions.PlaybackStart += (_, args) => tracker.PlaybackStarted(args);
        sessions.PlaybackProgress += (_, args) => tracker.PlaybackProgressed(args);
        sessions.PlaybackStopped += (_, args) => tracker.PlaybackStopped(args);

        var session = new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer"))
            .Playing(PlaySessionBuilder.LiveProgramme("An evening of it", TheChannel))
            .Build();

        sessions.RaisePlaybackStart(session, Eight);
        for (var minute = 1; minute <= 20; minute++)
        {
            sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(minute), at: Eight.AddMinutes(minute));
        }

        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(21), playedToCompletion: false, at: Eight.AddMinutes(21));

        // A lookup per progress report would be a library read every few
        // seconds for every live session on the server, and the answer it
        // would produce is the one already on the row.
        Assert.Equal(1, channels.TimesAsked);
        Assert.Equal("BBC One", Assert.Single(rows.Rows).ChannelName);
    }

    [Fact]
    public void AChannelPlayedAsItselfIsItsOwnNameAndNothingIsResolved()
    {
        var sessions = new FakeSessionManager();
        var channels = new FakeChannelNames();
        var rows = new RecordingPlaySink();
        var tracker = new PlayTracker(rows, channels, NullLogger<PlayTracker>.Instance);

        sessions.PlaybackStart += (_, args) => tracker.PlaybackStarted(args);
        sessions.PlaybackStopped += (_, args) => tracker.PlaybackStopped(args);

        var session = new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer"))
            .Playing(PlaySessionBuilder.LiveChannel("Channel Four"))
            .Build();

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(12), playedToCompletion: false, at: Eight.AddMinutes(12));

        Assert.Equal("Channel Four", Assert.Single(rows.Rows).ChannelName);
        Assert.Equal(0, channels.TimesAsked);
    }

    [Fact]
    public void AChannelTheLibraryNoLongerHoldsLeavesTheRowNamingNone()
    {
        var sessions = new FakeSessionManager();
        var channels = new FakeChannelNames();
        var rows = new RecordingPlaySink();
        var tracker = new PlayTracker(rows, channels, NullLogger<PlayTracker>.Instance);

        sessions.PlaybackStart += (_, args) => tracker.PlaybackStarted(args);
        sessions.PlaybackStopped += (_, args) => tracker.PlaybackStopped(args);

        var session = new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer"))
            .Playing(PlaySessionBuilder.LiveProgramme("A programme", TheChannel))
            .Build();

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(5), playedToCompletion: false, at: Eight.AddMinutes(5));

        // Asked and unanswered, which is a different route to the same value
        // than a play that was never live television. A report can say nothing
        // more about either, so the row does not either.
        Assert.Equal(1, channels.TimesAsked);
        Assert.Null(Assert.Single(rows.Rows).ChannelName);
    }

    [Fact]
    public void AFilmNamesNoChannelAndAsksForNone()
    {
        var sessions = new FakeSessionManager();
        var channels = new FakeChannelNames().Called(TheChannel, "BBC One");
        var rows = new RecordingPlaySink();
        var tracker = new PlayTracker(rows, channels, NullLogger<PlayTracker>.Instance);

        sessions.PlaybackStart += (_, args) => tracker.PlaybackStarted(args);
        sessions.PlaybackStopped += (_, args) => tracker.PlaybackStopped(args);

        var session = new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer"))
            .Playing(PlaySessionBuilder.Video("A Film", TimeSpan.FromMinutes(90)))
            .Build();

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(90), playedToCompletion: true, at: Eight.AddMinutes(90));

        Assert.Null(Assert.Single(rows.Rows).ChannelName);
        Assert.Equal(0, channels.TimesAsked);
    }

    [Fact]
    public void TheRunningRowNamesTheChannelFromTheStartEvent()
    {
        var sessions = new FakeSessionManager();
        var channels = new FakeChannelNames().Called(TheChannel, "BBC One");
        var rows = new RecordingPlaySink();
        var tracker = new PlayTracker(rows, channels, NullLogger<PlayTracker>.Instance);

        sessions.PlaybackStart += (_, args) => tracker.PlaybackStarted(args);

        var session = new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer"))
            .Playing(PlaySessionBuilder.LiveProgramme("A programme", TheChannel))
            .Build();

        sessions.RaisePlaybackStart(session, Eight);

        // A live play the server never sends a stop for is closed from what is
        // on the file, so the channel has to be there before the stop rather
        // than added by it.
        Assert.Equal("BBC One", Assert.Single(rows.Running).SoFar.ChannelName);
    }

    [Fact]
    public void AChannelWithNoNameLeavesTheRowNamingNone()
    {
        var sessions = new FakeSessionManager();
        var rows = new RecordingPlaySink();
        var tracker = new PlayTracker(rows, FakeChannelNames.Empty, NullLogger<PlayTracker>.Instance);

        sessions.PlaybackStart += (_, args) => tracker.PlaybackStarted(args);
        sessions.PlaybackStopped += (_, args) => tracker.PlaybackStopped(args);

        var session = new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer"))
            .Playing(PlaySessionBuilder.LiveChannel(string.Empty))
            .Build();

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(3), playedToCompletion: false, at: Eight.AddMinutes(3));

        // An empty name on the row would read as a channel that is called that.
        Assert.Null(Assert.Single(rows.Rows).ChannelName);
    }

    [Fact]
    public void TheLibraryIsAskedForTheChannelAndNothingElse()
    {
        var asked = new System.Collections.Generic.List<Guid>();
        var names = new LibraryChannelNames(channelId =>
        {
            asked.Add(channelId);

            return channelId == TheChannel
                ? PlaySessionBuilder.LiveChannel("BBC One", TheChannel)
                : null;
        });

        Assert.Equal("BBC One", names.NameOf(TheChannel));
        Assert.Null(names.NameOf(Guid.Parse("11112222-3333-4444-5555-666677778888")));

        // An identifier no play carries is not a lookup. The server leaves this
        // field empty on everything that is not a live programme, so a read of
        // it could only fail.
        Assert.Null(names.NameOf(Guid.Empty));
        Assert.Equal(2, asked.Count);
    }

    [Fact]
    public void AChannelTheLibraryHoldsWithoutANameIsNoName()
    {
        var names = new LibraryChannelNames(_ => PlaySessionBuilder.LiveChannel(string.Empty));

        Assert.Null(names.NameOf(TheChannel));
    }

    [Fact]
    public void NamesWithNothingBehindThemAreRefusedWhereTheyAreBuilt()
    {
        // Not at the first live play somebody watches, which is where a null
        // taken on trust here would surface, on a server, hours later.
        Assert.Throws<ArgumentNullException>(() => new LibraryChannelNames(null!));
    }

    [Fact]
    public void TheChannelSurvivesTheStore()
    {
        Directory.CreateDirectory(_root);

        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(ALiveRow("BBC One"));
            store.Add(ALiveRow(null));
        }

        using (var reopened = new SqlitePlayStore(_root))
        {
            var read = reopened.AllPlays().ToList();

            Assert.Equal(2, read.Count);
            Assert.Equal("BBC One", read[0].ChannelName);
            Assert.Null(read[1].ChannelName);
        }
    }

    [Fact]
    public void TheColumnArrivesAsAnAppendedStepAndAnOlderStoreStillReads()
    {
        // Moves each time a step is appended, which is what this case asserts
        // is the only way a column arrives. Ten is the index one account's year
        // is read through, issue #254, and the step this case is about is still
        // the seventh.
        Assert.Equal(10, SchemaMigrations.Latest);

        Directory.CreateDirectory(_root);

        using (var connection = OpenTheFile())
        {
            SchemaMigrator.MigrateToLatest(
                connection,
                SchemaMigrations.All.Where(step => step.Version <= 6).ToList());

            using var insert = connection.CreateCommand();
            insert.CommandText =
                @"INSERT INTO plays (
                      SchemaVersion, UserId, ItemId, ItemType, ItemName,
                      StartedUtcTicks, EndedUtcTicks, WatchedDurationTicks, ReachedTheEnd,
                      ClientName, DeviceId, DeviceName, PlayMethodAtStart,
                      TranscodeVideoWasDirect, TranscodeAudioWasDirect, TranscodeReasons
                  ) VALUES (
                      6, $userId, $itemId, 'Program', 'A programme from before the column',
                      $started, $ended, $watched, 0,
                      'Web', 'a-device', 'A device', 1,
                      1, 1, ''
                  )";
            insert.Parameters.AddWithValue("$userId", Viewer.ToString("N"));
            insert.Parameters.AddWithValue("$itemId", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$started", Eight.UtcDateTime.Ticks);
            insert.Parameters.AddWithValue("$ended", Eight.AddMinutes(30).UtcDateTime.Ticks);
            insert.Parameters.AddWithValue("$watched", TimeSpan.FromMinutes(30).Ticks);
            insert.ExecuteNonQuery();
        }

        using (var reopened = new SqlitePlayStore(_root))
        {
            var read = Assert.Single(reopened.AllPlays());

            // The row is still there and still says what it said. What it does
            // not say is a channel, which is the honest answer: nothing was
            // resolving one when it was written.
            Assert.Equal(TimeSpan.FromMinutes(30), read.WatchedDuration);
            Assert.Null(read.ChannelName);
        }

        using var afterwards = OpenTheFile();

        Assert.Equal(SchemaMigrations.Latest, SchemaMigrator.CurrentVersion(afterwards));
    }

    [Fact]
    public void AnArchiveRowFromBeforeTheColumnImportsNamingNoChannel()
    {
        var before = string.Join(
            "\n",
            "{\"Format\":\"" + PlayArchive.FormatName + "\",\"SchemaVersion\":6}",
            ARowWrittenBeforeTheColumn());

        Directory.CreateDirectory(_root);

        using var into = new SqlitePlayStore(_root);

        Assert.Equal(1, PlayArchive.Import(new StringReader(before), into));
        Assert.Null(Assert.Single(into.AllPlays()).ChannelName);
    }

    [Fact]
    public void AnArchiveKeepsTheChannelARowCarries()
    {
        var written = new StringWriter();

        // Stamped the way the capture stamps it. That number is the version the
        // row SHAPE was decided at rather than the store's, so a row written
        // this morning arrives at the import reading as older than the column
        // it is carrying and every step for that column runs over it. A case
        // that exported at the latest version instead would walk past all of
        // them and prove nothing, which is what this one did until the step was
        // broken to watch it bite.
        PlayArchive.Export([ALiveRow("BBC One") with { SchemaVersion = 1 }], written);

        Directory.CreateDirectory(_root);

        using var into = new SqlitePlayStore(_root);

        Assert.Equal(1, PlayArchive.Import(new StringReader(written.ToString()), into));

        // The step for this column fills an absence and never assigns, so a row
        // the capture wrote today, which says it is at version one while
        // carrying every column added since, comes back naming its channel
        // rather than losing it on the way in.
        Assert.Equal("BBC One", Assert.Single(into.AllPlays()).ChannelName);
    }

    [Fact]
    public void WhatNamesAChannelIsResolvedFromTheContainer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<MediaBrowser.Controller.Session.ISessionManager>(new FakeSessionManager());
        services.AddLogging();

        new PluginServiceRegistrator().RegisterServices(services, applicationHost: null!);

        using var provider = services.BuildServiceProvider();

        // What is proved here is the registration and not the library behind
        // it. The server's library is asked for when a channel is, so nothing
        // in this process resolves it, and there is no stand-in for it: that
        // interface carries over a hundred methods and a fake for it would be a
        // file of refusals proving nothing this case is about.
        Assert.IsType<LibraryChannelNames>(provider.GetRequiredService<IChannelNames>());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Takes the temporary store away.
    /// </summary>
    /// <param name="disposing">Whether managed state is being released.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || !Directory.Exists(_root))
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    /// <summary>
    /// One exported line with this column taken back out of it, so it reads as
    /// a row written before the column existed.
    /// </summary>
    /// <returns>The row line.</returns>
    private static string ARowWrittenBeforeTheColumn()
    {
        var written = new StringWriter();

        PlayArchive.Export([ALiveRow(null) with { SchemaVersion = 6 }], written);

        var line = written.ToString().Split('\n')[1].Trim();

        return line.Replace(",\"ChannelName\":null", string.Empty, StringComparison.Ordinal);
    }

    private static PlayRecord ALiveRow(string? channelName)
    {
        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = Viewer,
            ItemId = Guid.Parse("7c8d9e0f-1a2b-4c3d-8e4f-5a6b7c8d9e0f"),
            ItemType = "Program",
            ParentId = null,
            ItemName = "The Nine at Nine",
            ItemRuntime = null,
            ChannelName = channelName,
            StartedUtc = Eight.UtcDateTime,
            EndedUtc = Eight.AddMinutes(30).UtcDateTime,
            WatchedDuration = TimeSpan.FromMinutes(30),
            ReachedTheEnd = false,
            ClientName = "Jellyfin Web",
            DeviceId = "a-device",
            DeviceName = "A device",
            PlayMethodAtStart = PlayMethod.DirectStream,
            PlayMethodChangedUtc = null,
            ClosedBy = PlayClosedBy.AStopEvent,
            Transcode = new TranscodeSummary
            {
                VideoCodec = null,
                AudioCodec = null,
                VideoWasDirect = true,
                AudioWasDirect = true,
                PeakBitrate = null,
                TypicalBitrate = null,
                HardwareAcceleration = null,
                Reasons = []
            }
        };
    }

    private SqliteConnection OpenTheFile()
    {
        var connection = new SqliteConnection(
            "Data Source=" + Path.Combine(_root, SqlitePlayStore.FileName));

        connection.Open();

        return connection;
    }

    private sealed class RecordingPlaySink : IPlaySink
    {
        private readonly System.Collections.Generic.List<PlayRecord> _rows = [];
        private readonly System.Collections.Generic.List<OpenPlay> _running = [];

        public System.Collections.Generic.IReadOnlyList<PlayRecord> Rows => _rows;

        public System.Collections.Generic.IReadOnlyList<OpenPlay> Running => _running;

        public void Add(PlayRecord play, string playKey) => _rows.Add(play);

        public void NoteOpen(OpenPlay play) => _running.Add(play);

        public void ForgetOpen(string playKey)
        {
        }
    }
}
