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
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
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
    public void ADirectPlaySaysSoAndInventsNoTranscodeDetail()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out _);
        var session = APlay(sessions);

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(5), at: Eight.AddMinutes(5));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));

        var transcode = Assert.Single(rows.Rows).Transcode;

        // Both streams reached the client as they were. The server was asked on
        // every sample and reported no transcode on any of them, so this is a
        // measurement rather than a field nobody filled in.
        Assert.True(transcode.VideoWasDirect);
        Assert.True(transcode.AudioWasDirect);

        // And nothing else is claimed. A codec, a bitrate or a reason here
        // would be invented: no transcode ran, so the server named none.
        Assert.Null(transcode.VideoCodec);
        Assert.Null(transcode.AudioCodec);
        Assert.Null(transcode.PeakBitrate);
        Assert.Null(transcode.TypicalBitrate);
        Assert.Null(transcode.HardwareAcceleration);
        Assert.Empty(transcode.Reasons);
    }

    [Fact]
    public void ATranscodeThatChangesHalfwayThroughIsOneRowCarryingBothHalves()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out _);
        var session = APlay(sessions);

        session.TranscodingInfo = Transcoding(
            videoCodec: "h264",
            audioCodec: "aac",
            bitrate: 3_000_000,
            reasons: TranscodeReason.VideoCodecNotSupported);

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(5), at: Eight.AddMinutes(5));
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));

        // The client changes what it can take and the server renegotiates: a
        // different codec pair, a higher bitrate, the graphics card doing the
        // work, and a second reason for doing it at all.
        session.TranscodingInfo = Transcoding(
            videoCodec: "hevc",
            audioCodec: "eac3",
            bitrate: 6_000_000,
            acceleration: HardwareAccelerationType.qsv,
            reasons: TranscodeReason.AudioCodecNotSupported);

        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(15), at: Eight.AddMinutes(15));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(20), at: Eight.AddMinutes(20));

        var transcode = Assert.Single(rows.Rows).Transcode;

        // What the play ended up using, and the highest it ever reached.
        Assert.Equal("hevc", transcode.VideoCodec);
        Assert.Equal("eac3", transcode.AudioCodec);
        Assert.Equal(6_000_000, transcode.PeakBitrate);

        // Three samples at the first bitrate against two at the second, so the
        // typical is the first even though the play ended on the second.
        Assert.Equal(3_000_000, transcode.TypicalBitrate);

        Assert.Equal("qsv", transcode.HardwareAcceleration);
        Assert.False(transcode.VideoWasDirect);
        Assert.False(transcode.AudioWasDirect);

        // Both halves, in the order the play showed them.
        Assert.Equal(
            new[] { "VideoCodecNotSupported", "AudioCodecNotSupported" },
            transcode.Reasons);
    }

    [Fact]
    public void APlayThatBeginsDirectAndFallsBackIsNotRecordedAsDirect()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out _);
        var session = APlay(sessions);

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(5), at: Eight.AddMinutes(5));

        session.TranscodingInfo = Transcoding(reasons: TranscodeReason.VideoBitrateNotSupported);

        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(15), at: Eight.AddMinutes(15));

        var transcode = Assert.Single(rows.Rows).Transcode;

        // Direct for the first half is not direct. A row that said otherwise
        // would put this play on the wrong side of the transcode ratio.
        Assert.False(transcode.VideoWasDirect);
        Assert.False(transcode.AudioWasDirect);
        Assert.Equal("h264", transcode.VideoCodec);
        Assert.Equal(new[] { "VideoBitrateNotSupported" }, transcode.Reasons);
    }

    [Fact]
    public void TheNumberOfRowsAPlayProducesDoesNotGrowWithItsLength()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out var tracker);
        var session = APlay(sessions);

        session.TranscodingInfo = Transcoding(
            bitrate: 3_000_000,
            reasons: TranscodeReason.ContainerNotSupported);

        sessions.RaisePlaybackStart(session, Eight);

        // Six hours of progress reports ten seconds apart, which is a long film
        // on a client that checks in as often as the server asks it to.
        const int Reports = 2160;
        for (var i = 1; i <= Reports; i++)
        {
            // Counted as a double rather than multiplied as an int and widened
            // afterwards, which is the shape the analysis reads as an overflow
            // waiting to be given a larger loop.
            var second = 10d * i;
            sessions.RaisePlaybackProgress(session, TimeSpan.FromSeconds(second), at: Eight.AddSeconds(second));
        }

        sessions.RaisePlaybackStopped(session, TimeSpan.FromHours(6), at: Eight.AddHours(6));

        // One row out of two thousand one hundred and sixty two samples, and
        // the summary is the same shape it would be after three.
        var row = Assert.Single(rows.Rows);
        Assert.Equal(TimeSpan.FromHours(6), row.WatchedDuration);
        Assert.Equal(3_000_000, row.Transcode.PeakBitrate);
        Assert.Equal(3_000_000, row.Transcode.TypicalBitrate);

        // The reason arrived on every one of those samples and is listed once.
        Assert.Equal(new[] { "ContainerNotSupported" }, row.Transcode.Reasons);
        Assert.Equal(0, tracker.OpenPlays);
    }

    [Fact]
    public void ARemuxIsDirectVideoAndTranscodedAudio()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out _);
        var session = APlay(sessions);

        session.TranscodingInfo = Transcoding(
            videoCodec: "h264",
            audioCodec: "aac",
            reasons: TranscodeReason.AudioCodecNotSupported,
            videoDirect: true);

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));

        var transcode = Assert.Single(rows.Rows).Transcode;

        // The two streams are answered separately. Folding them into one flag
        // would count a remux as a full transcode.
        Assert.True(transcode.VideoWasDirect);
        Assert.False(transcode.AudioWasDirect);
        Assert.Null(transcode.PeakBitrate);
        Assert.Null(transcode.TypicalBitrate);
    }

    [Fact]
    public void SoftwareTranscodingReportsNoHardwareAcceleration()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out _);
        var session = APlay(sessions);

        // The server names the software path with a member of the same enum
        // rather than by leaving the field empty, and a row that stored that
        // name would report hardware acceleration nobody had.
        session.TranscodingInfo = Transcoding(acceleration: HardwareAccelerationType.none);

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));

        Assert.Null(Assert.Single(rows.Rows).Transcode.HardwareAcceleration);
    }

    [Fact]
    public void ASampleThatNamesNoCodecLeavesTheOneAlreadyReported()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out _);
        var session = APlay(sessions);

        session.TranscodingInfo = Transcoding(videoCodec: "hevc", audioCodec: "eac3");

        sessions.RaisePlaybackStart(session, Eight);

        // The server rewrites the whole state at once, so a sample carrying a
        // bitrate and no codecs is it having nothing to say about the streams
        // rather than the streams having stopped being what they were.
        session.TranscodingInfo = Transcoding(videoCodec: null, audioCodec: string.Empty, bitrate: 5_000_000);

        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));

        var transcode = Assert.Single(rows.Rows).Transcode;
        Assert.Equal("hevc", transcode.VideoCodec);
        Assert.Equal("eac3", transcode.AudioCodec);
        Assert.Equal(5_000_000, transcode.PeakBitrate);
    }

    [Fact]
    public void APlayThatRenegotiatesMoreBitratesThanAreTrackedStillReportsThePeak()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out _);
        var session = APlay(sessions);

        // Two samples at the first value, then sixty three more distinct ones,
        // which is the whole of what the fold keeps a count for.
        session.TranscodingInfo = Transcoding(bitrate: 1_000_000);
        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackProgress(session, TimeSpan.FromSeconds(10), at: Eight.AddSeconds(10));

        var second = 10;
        for (var i = 1; i <= 63; i++)
        {
            second += 10;
            session.TranscodingInfo = Transcoding(bitrate: 1_000_000 + (i * 1_000));
            sessions.RaisePlaybackProgress(session, TimeSpan.FromSeconds(second), at: Eight.AddSeconds(second));
        }

        // One more distinct value, arriving with no room left, and sampled more
        // often than any value that did get room.
        session.TranscodingInfo = Transcoding(bitrate: 1_064_000);
        for (var i = 0; i < 3; i++)
        {
            second += 10;
            sessions.RaisePlaybackProgress(session, TimeSpan.FromSeconds(second), at: Eight.AddSeconds(second));
        }

        sessions.RaisePlaybackStopped(session, TimeSpan.FromSeconds(second + 10), at: Eight.AddSeconds(second + 10));

        var transcode = Assert.Single(rows.Rows).Transcode;

        // The peak is a maximum and needs no table, so a value the table had no
        // room for still moves it.
        Assert.Equal(1_064_000, transcode.PeakBitrate);

        // The typical is drawn from the values the table did hold. The most
        // sampled value of the play is not among them and does not win, which
        // is what the bound costs and is why it is written down.
        Assert.Equal(1_000_000, transcode.TypicalBitrate);
    }

    [Fact]
    public void AVideoReEncodedForPartOfThePlayIsNotDirect()
    {
        var sessions = new FakeSessionManager();
        var rows = Watching(sessions, out _);
        var session = APlay(sessions);

        session.TranscodingInfo = Transcoding(videoCodec: "h264", audioCodec: "aac");

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(5), at: Eight.AddMinutes(5));

        // The server renegotiates to passing the video through and carries on
        // re-encoding the audio. The video was still re-encoded for five
        // minutes, and a summary taken from the last sample would say it never
        // was.
        session.TranscodingInfo = Transcoding(
            videoCodec: "h264",
            audioCodec: "aac",
            reasons: TranscodeReason.AudioCodecNotSupported,
            videoDirect: true);

        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(15), at: Eight.AddMinutes(15));

        var transcode = Assert.Single(rows.Rows).Transcode;
        Assert.False(transcode.VideoWasDirect);
        Assert.False(transcode.AudioWasDirect);
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

    /// <summary>
    /// A transcoding state as the server writes it on the session, with the
    /// fields a test is not about left at what a transcode usually carries.
    /// </summary>
    private static TranscodingInfo Transcoding(
        string? videoCodec = "h264",
        string? audioCodec = "aac",
        int? bitrate = null,
        HardwareAccelerationType? acceleration = null,
        TranscodeReason reasons = default,
        bool videoDirect = false,
        bool audioDirect = false)
    {
        return new TranscodingInfo
        {
            VideoCodec = videoCodec,
            AudioCodec = audioCodec,
            Container = "ts",
            Bitrate = bitrate,
            HardwareAccelerationType = acceleration,
            TranscodeReasons = reasons,
            IsVideoDirect = videoDirect,
            IsAudioDirect = audioDirect
        };
    }

    private sealed class RecordingPlaySink : IFinishedPlaySink
    {
        private readonly List<PlayRecord> _rows = new();

        public IReadOnlyList<PlayRecord> Rows => _rows;

        public void Add(PlayRecord play) => _rows.Add(play);
    }
}
