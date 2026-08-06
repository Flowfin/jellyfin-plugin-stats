// What a play is, checked against the events a server actually raises.
//
// The times are chosen by the test and set on the session, because that is the
// field the server writes when a client checks in and the only place the plugin
// reads a moment from. A play of twenty minutes is therefore a test that runs in
// microseconds, and the same events replayed produce the same row.
//
// Most tests subscribe the tracker to the fake directly rather than through
// PlaybackEventListener. The listener swallows whatever a handler throws, which
// is right on a server and wrong in a test: it would turn a tracker that faulted
// into a tracker that wrote no row, and the two deserve different failures. The
// first test below goes through the real listener so the whole chain is
// exercised once.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Capture;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ServerPlayMethod = MediaBrowser.Model.Session.PlayMethod;
using StoredPlayMethod = Jellyfin.Plugin.Stats.Data.PlayMethod;

namespace Jellyfin.Plugin.Stats.Tests;

public class PlayTrackerTests
{
    private static readonly DateTimeOffset Eight = new(2026, 1, 2, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AStartThreeProgressReportsAndAStopAreOnePlay()
    {
        var sessions = new FakeSessionManager();
        var rows = new RecordingPlaySink();
        var tracker = new PlayTracker(rows, NullLogger<PlayTracker>.Instance);
        var listener = new PlaybackEventListener(sessions, tracker, NullLogger<PlaybackEventListener>.Instance);
        await listener.StartAsync(CancellationToken.None);

        var session = new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer"))
            .Playing(PlaySessionBuilder.Video("A Film", TimeSpan.FromMinutes(90)))
            .From("Jellyfin Web", "A browser", "device-1")
            .Via(ServerPlayMethod.DirectPlay)
            .Build();

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(5), at: Eight.AddMinutes(5));
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(15), at: Eight.AddMinutes(15));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(20), playedToCompletion: true, at: Eight.AddMinutes(20));

        var row = Assert.Single(rows.Rows);
        Assert.Equal(TimeSpan.FromMinutes(20), row.WatchedDuration);
        Assert.Equal(Eight.UtcDateTime, row.StartedUtc);
        Assert.Equal(Eight.AddMinutes(20).UtcDateTime, row.EndedUtc);
        Assert.True(row.ReachedTheEnd);
        Assert.Equal("A Film", row.ItemName);
        Assert.Equal(TimeSpan.FromMinutes(90), row.ItemRuntime);
        Assert.Equal("Movie", row.ItemType);
        Assert.Equal("Jellyfin Web", row.ClientName);
        Assert.Equal("device-1", row.DeviceId);
        Assert.Equal("A browser", row.DeviceName);
        Assert.Equal(StoredPlayMethod.DirectPlay, row.PlayMethod);
        Assert.Null(row.ParentId);
        Assert.Equal(1, row.SchemaVersion);
        Assert.Equal(0, tracker.EventsWithNoOpenPlay);
        Assert.Equal(0, tracker.OpenPlays);

        await listener.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void ThePauseAndTheSeekAreOutOfTheDurationTheRowCarries()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out var tracker);

        var session = APlay(sessions);

        sessions.RaisePlaybackStart(session, Eight);

        // Ten minutes watched, then paused for an hour, then a jump forward.
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(10), isPaused: true, at: Eight.AddMinutes(11));
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(71));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(40), at: Eight.AddMinutes(72));

        var row = Assert.Single(rows.Rows);

        // Ten from the first stretch and one from the last minute, which is all
        // the time there was to watch in it however far the position jumped.
        Assert.Equal(TimeSpan.FromMinutes(11), row.WatchedDuration);

        // The row still says the play spanned seventy two minutes, so a report
        // can show either and they do not become the same number.
        Assert.Equal(Eight.AddMinutes(72).UtcDateTime, row.EndedUtc);
        Assert.False(row.ReachedTheEnd);
    }

    [Fact]
    public void TwoDevicesPlayingOneItemAtOnceAreTwoPlays()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out _);

        var user = FakeUserManager.NewUser("viewer");
        var item = PlaySessionBuilder.Video("A Film", TimeSpan.FromMinutes(90));

        var living = new PlaySessionBuilder(sessions)
            .ForUser(user).Playing(item)
            .From("Jellyfin Web", "The television", "device-1")
            .Identified("session-1", "play-1")
            .Build();

        var phone = new PlaySessionBuilder(sessions)
            .ForUser(user).Playing(item)
            .From("Jellyfin Android", "A phone", "device-2")
            .Identified("session-2", "play-2")
            .Build();

        sessions.RaisePlaybackStart(living, Eight);
        sessions.RaisePlaybackStart(phone, Eight.AddMinutes(1));
        sessions.RaisePlaybackStopped(living, TimeSpan.FromMinutes(30), at: Eight.AddMinutes(30));
        sessions.RaisePlaybackStopped(phone, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(11));

        Assert.Equal(2, rows.Rows.Count);
        Assert.Equal(new[] { "device-1", "device-2" }, rows.Rows.Select(r => r.DeviceId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(rows.Rows, r => Assert.Equal(item.Id, r.ItemId));
        Assert.Equal(TimeSpan.FromMinutes(30), rows.Rows.Single(r => r.DeviceId == "device-1").WatchedDuration);
        Assert.Equal(TimeSpan.FromMinutes(10), rows.Rows.Single(r => r.DeviceId == "device-2").WatchedDuration);
    }

    [Fact]
    public void AStartForAPlayThatIsAlreadyOpenBeginsItAgain()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out var tracker);
        var session = APlay(sessions);

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackStart(session, Eight.AddMinutes(5));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(15));

        // One row, and it belongs to the second start. A client that reopens an
        // item without the first play ever stopping has begun a new viewing, and
        // keeping the first would report ten minutes of watching as fifteen.
        var row = Assert.Single(rows.Rows);
        Assert.Equal(Eight.AddMinutes(5).UtcDateTime, row.StartedUtc);
        Assert.Equal(TimeSpan.FromMinutes(10), row.WatchedDuration);
        Assert.Equal(0, tracker.OpenPlays);
    }

    [Fact]
    public void AProgressReportWithNoStartIsCountedAndWritesNothing()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out var tracker);
        var session = APlay(sessions);

        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(5), at: Eight.AddMinutes(5));

        Assert.Empty(rows.Rows);
        Assert.Equal(1, tracker.EventsWithNoOpenPlay);
        Assert.Equal(0, tracker.OpenPlays);
    }

    [Fact]
    public void AStopWithNoStartIsCountedAndWritesNothing()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out var tracker);
        var session = APlay(sessions);

        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(5), at: Eight.AddMinutes(5));

        // No half filled row. A stop on its own carries a position and an item
        // and nothing about when the play began, so a row made from it would
        // report a duration nobody measured.
        Assert.Empty(rows.Rows);
        Assert.Equal(1, tracker.EventsWithNoOpenPlay);
    }

    [Fact]
    public void ASecondStopForOnePlayWritesOneRow()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out var tracker);
        var session = APlay(sessions);

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));

        Assert.Single(rows.Rows);
        Assert.Equal(1, tracker.EventsWithNoOpenPlay);
    }

    [Fact]
    public void APlayTheServerGaveNoIdentifierIsJoinedOnTheDeviceAndTheItem()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out var tracker);

        var session = new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer"))
            .Playing(PlaySessionBuilder.Video("A Film", TimeSpan.FromMinutes(90)))
            .From("Jellyfin Web", "A browser", "device-1")
            .Identified("session-1", string.Empty)
            .Build();

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(5), at: Eight.AddMinutes(5));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(20), at: Eight.AddMinutes(20));

        var row = Assert.Single(rows.Rows);
        Assert.Equal(TimeSpan.FromMinutes(20), row.WatchedDuration);
        Assert.Equal(0, tracker.EventsWithNoOpenPlay);
    }

    [Fact]
    public void TwoDevicesWithNoIdentifierOnOneItemAreStillTwoPlays()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out _);

        var user = FakeUserManager.NewUser("viewer");
        var item = PlaySessionBuilder.Video("A Film", TimeSpan.FromMinutes(90));

        var first = new PlaySessionBuilder(sessions)
            .ForUser(user).Playing(item)
            .From("Jellyfin Web", "The television", "device-1")
            .Identified("session-1", string.Empty)
            .Build();

        var second = new PlaySessionBuilder(sessions)
            .ForUser(user).Playing(item)
            .From("Jellyfin Android", "A phone", "device-2")
            .Identified("session-2", string.Empty)
            .Build();

        sessions.RaisePlaybackStart(first, Eight);
        sessions.RaisePlaybackStart(second, Eight);
        sessions.RaisePlaybackStopped(first, TimeSpan.FromMinutes(30), at: Eight.AddMinutes(30));
        sessions.RaisePlaybackStopped(second, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));

        Assert.Equal(2, rows.Rows.Count);
    }

    [Fact]
    public void AnEpisodeCarriesTheSeriesItIsUnder()
    {
        var series = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var row = ARowFor(new Episode
        {
            Id = Guid.NewGuid(),
            Name = "An Episode",
            RunTimeTicks = TimeSpan.FromMinutes(42).Ticks,
            SeriesId = series
        });

        Assert.Equal(series, row.ParentId);
        Assert.Equal("Episode", row.ItemType);
    }

    [Fact]
    public void AnEpisodeWhoseSeriesTheServerDidNotFillInCarriesNoParent()
    {
        var row = ARowFor(new Episode
        {
            Id = Guid.NewGuid(),
            Name = "An Episode",
            RunTimeTicks = TimeSpan.FromMinutes(42).Ticks
        });

        // Not the empty identifier. A report grouping on the parent would read
        // that as a series of its own that every parentless play belongs to.
        Assert.Null(row.ParentId);
    }

    [Fact]
    public void AnItemWithNoRuntimeCarriesNoRuntime()
    {
        var row = ARowFor(new MediaBrowser.Controller.Entities.Movies.Movie
        {
            Id = Guid.NewGuid(),
            Name = "Something live"
        });

        Assert.Null(row.ItemRuntime);
    }

    [Theory]
    [InlineData(ServerPlayMethod.DirectPlay, StoredPlayMethod.DirectPlay)]
    [InlineData(ServerPlayMethod.DirectStream, StoredPlayMethod.DirectStream)]
    [InlineData(ServerPlayMethod.Transcode, StoredPlayMethod.Transcode)]
    public void TheDeliveryMethodIsStoredAsThisPluginsOwnValue(ServerPlayMethod reported, StoredPlayMethod stored)
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out _);

        var session = new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer"))
            .Playing(PlaySessionBuilder.Video("A Film", TimeSpan.FromMinutes(90)))
            .Via(reported)
            .Build();

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));

        Assert.Equal(stored, Assert.Single(rows.Rows).PlayMethod);
    }

    [Fact]
    public void ASessionThatReportedNoDeliveryMethodIsStoredAsUnknown()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out _);
        var session = APlay(sessions);
        session.PlayState.PlayMethod = null;

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));

        // Unknown rather than one of the three. A row that names a delivery
        // method nobody reported is the transcode ratio quietly gaining a
        // denominator it did not earn.
        Assert.Equal(StoredPlayMethod.Unknown, Assert.Single(rows.Rows).PlayMethod);
    }

    [Fact]
    public void ARowInventsNoTranscodeDetail()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out _);
        var session = APlay(sessions);

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));

        // Sampling the transcoding state is issue #37. Until it lands the
        // summary says nothing rather than saying something plausible.
        var transcode = Assert.Single(rows.Rows).Transcode;
        Assert.Null(transcode.VideoCodec);
        Assert.Null(transcode.AudioCodec);
        Assert.Null(transcode.PeakBitrate);
        Assert.Null(transcode.TypicalBitrate);
        Assert.Null(transcode.HardwareAcceleration);
        Assert.Empty(transcode.Reasons);
    }

    [Fact]
    public void ASessionEndingWritesNoRowYet()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out var tracker);
        var session = APlay(sessions);

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaiseSessionEnded(session);

        // A play left open by a session that went away is issue #36, and until
        // that lands this writes nothing rather than writing a row whose end
        // nobody observed. The play is still open, which is what that issue
        // will find.
        Assert.Empty(rows.Rows);
        Assert.Equal(1, tracker.OpenPlays);
    }

    /// <summary>
    /// Subscribes a tracker to the fake's events and hands back where its rows
    /// land.
    /// </summary>
    private static RecordingPlaySink Watching(FakeSessionManager sessions, out PlayTracker tracker)
    {
        var rows = new RecordingPlaySink();
        var built = new PlayTracker(rows, NullLogger<PlayTracker>.Instance);

        sessions.PlaybackStart += (_, e) => built.PlaybackStarted(e);
        sessions.PlaybackProgress += (_, e) => built.PlaybackProgressed(e);
        sessions.PlaybackStopped += (_, e) => built.PlaybackStopped(e);
        sessions.SessionEnded += (_, e) => built.SessionEnded(e);

        tracker = built;
        return rows;
    }

    private static SessionInfo APlay(FakeSessionManager sessions)
    {
        return new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer"))
            .Playing(PlaySessionBuilder.Video("A Film", TimeSpan.FromMinutes(90)))
            .Build();
    }

    /// <summary>
    /// One whole play of one item, for a test that is about what the row says
    /// of the item rather than about the events.
    /// </summary>
    private static PlayRecord ARowFor(BaseItem item)
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out _);

        var session = new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer"))
            .Playing(item)
            .Build();

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));

        return Assert.Single(rows.Rows);
    }

    private sealed class RecordingPlaySink : IFinishedPlaySink
    {
        private readonly List<PlayRecord> _rows = new();

        public IReadOnlyList<PlayRecord> Rows => _rows;

        public void Add(PlayRecord play) => _rows.Add(play);
    }
}
