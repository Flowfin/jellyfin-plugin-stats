// The two accounts a row carries of how a play was delivered, and the moment
// they parted company.
//
// A row says how the server was delivering the item when the play began and
// what the transcoding state came to over the whole play. Both are true and
// they are about different moments, so a reader who takes them for one answer
// finds a disagreement that is not one. Issue #158 is that finding, and what it
// asked for is the change recorded as its own fact under names that say which
// moment each field speaks about.

using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Capture;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ServerPlayMethod = MediaBrowser.Model.Session.PlayMethod;
using StoredPlayMethod = Jellyfin.Plugin.Stats.Data.PlayMethod;

namespace Jellyfin.Plugin.Stats.Tests;

public class PlayMethodChangeTests
{
    private static readonly DateTimeOffset Eight = new(2026, 1, 2, 20, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The first condition of issue #158. A play that begins as a direct play
    /// and is re-encoded from its second minute says both, and says when the
    /// two parted company.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task APlayThatBeginsDirectAndIsReEncodedSaysBothAndWhen()
    {
        var sessions = new FakeSessionManager();
        var rows = new RecordingPlaySink();
        var listener = ListenerOver(sessions, rows);
        await listener.StartAsync(CancellationToken.None);

        var session = ASession(sessions);

        sessions.RaisePlaybackStart(session, Eight);

        ReEncodingFrom(session);
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(2), at: Eight.AddMinutes(2));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(30), at: Eight.AddMinutes(30));

        var row = Assert.Single(rows.Rows);

        // The three fields together are the whole of what this issue asked for.
        // The start value is what it was, the summary is what the play came to,
        // and the moment says the first is not a statement about the second.
        Assert.Equal(StoredPlayMethod.DirectPlay, row.PlayMethodAtStart);
        Assert.False(row.Transcode.VideoWasDirect);
        Assert.Equal(Eight.AddMinutes(2).UtcDateTime, row.PlayMethodChangedUtc);

        await listener.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// The third condition of issue #158. A play whose method never changes
    /// produces the row it produced before, with nothing recorded for the
    /// change.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task APlayWhoseMethodNeverChangesRecordsNoChange()
    {
        var sessions = new FakeSessionManager();
        var rows = new RecordingPlaySink();
        var listener = ListenerOver(sessions, rows);
        await listener.StartAsync(CancellationToken.None);

        var session = ASession(sessions);

        sessions.RaisePlaybackStart(session, Eight);
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(5), at: Eight.AddMinutes(5));
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(15), at: Eight.AddMinutes(15));

        var row = Assert.Single(rows.Rows);

        Assert.Equal(StoredPlayMethod.DirectPlay, row.PlayMethodAtStart);
        Assert.Null(row.PlayMethodChangedUtc);

        await listener.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// The first change is the one recorded. A play that moves and moves back
    /// would otherwise say nothing changed, which is the case a reader
    /// comparing the two fields would most need told about.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheFirstChangeIsTheOneRecorded()
    {
        var sessions = new FakeSessionManager();
        var rows = new RecordingPlaySink();
        var listener = ListenerOver(sessions, rows);
        await listener.StartAsync(CancellationToken.None);

        var session = ASession(sessions);

        sessions.RaisePlaybackStart(session, Eight);

        ReEncodingFrom(session);
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));

        session.PlayState.PlayMethod = ServerPlayMethod.DirectPlay;
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(20), at: Eight.AddMinutes(20));

        session.PlayState.PlayMethod = ServerPlayMethod.Transcode;
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(30), at: Eight.AddMinutes(30));

        Assert.Equal(Eight.AddMinutes(10).UtcDateTime, Assert.Single(rows.Rows).PlayMethodChangedUtc);

        await listener.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// A sample the server gave no method for is not a change. A client that
    /// goes quiet for one report would otherwise be recorded as a play whose
    /// delivery moved, which is the server having nothing to say rather than
    /// the session having done anything.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ASampleWithNoMethodIsNotAChange()
    {
        var sessions = new FakeSessionManager();
        var rows = new RecordingPlaySink();
        var listener = ListenerOver(sessions, rows);
        await listener.StartAsync(CancellationToken.None);

        var session = ASession(sessions);

        sessions.RaisePlaybackStart(session, Eight);

        session.PlayState.PlayMethod = null;
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(5), at: Eight.AddMinutes(5));

        session.PlayState.PlayMethod = ServerPlayMethod.DirectPlay;
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));

        var row = Assert.Single(rows.Rows);

        Assert.Equal(StoredPlayMethod.DirectPlay, row.PlayMethodAtStart);
        Assert.Null(row.PlayMethodChangedUtc);

        await listener.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// A play that began before the server had decided records the moment it
    /// did. The start value is unknown, which is not what the play turned out
    /// to be, and a reader comparing it against the transcode summary has to
    /// know that.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task APlayThatBeganBeforeTheServerDecidedRecordsWhenItDid()
    {
        var sessions = new FakeSessionManager();
        var rows = new RecordingPlaySink();
        var listener = ListenerOver(sessions, rows);
        await listener.StartAsync(CancellationToken.None);

        var session = ASession(sessions);
        session.PlayState.PlayMethod = null;

        sessions.RaisePlaybackStart(session, Eight);

        session.PlayState.PlayMethod = ServerPlayMethod.Transcode;
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(1), at: Eight.AddMinutes(1));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(10), at: Eight.AddMinutes(10));

        var row = Assert.Single(rows.Rows);

        Assert.Equal(StoredPlayMethod.Unknown, row.PlayMethodAtStart);
        Assert.Equal(Eight.AddMinutes(1).UtcDateTime, row.PlayMethodChangedUtc);

        await listener.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// The moment survives the store, and a moment that does not say it is in
    /// UTC is refused rather than kept as one that is.
    /// </summary>
    [Fact]
    public void TheMomentGoesToTheFileAndComesBack()
    {
        var root = System.IO.Path.Join(
            System.IO.Path.GetTempPath(),
            "jellyfin-plugin-stats-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var changed = new DateTime(2026, 1, 2, 20, 10, 0, DateTimeKind.Utc);

            using (var store = new SqlitePlayStore(root))
            {
                store.Add(APlay() with { PlayMethodChangedUtc = changed });

                Assert.Throws<ArgumentException>(
                    () => store.Add(APlay() with { PlayMethodChangedUtc = DateTime.SpecifyKind(changed, DateTimeKind.Local) }));
            }

            using var reopened = new SqlitePlayStore(root);

            Assert.Equal(changed, Assert.Single(reopened.MostRecentPlays(10)).PlayMethodChangedUtc);
        }
        finally
        {
            if (System.IO.Directory.Exists(root))
            {
                System.IO.Directory.Delete(root, true);
            }
        }
    }

    private static PlaybackEventListener ListenerOver(FakeSessionManager sessions, IPlaySink sink)
        => new(
            sessions,
            new PlayTracker(sink, NullLogger<PlayTracker>.Instance),
            NullLogger<PlaybackEventListener>.Instance);

    private static SessionInfo ASession(FakeSessionManager sessions)
        => new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer"))
            .Playing(PlaySessionBuilder.Video("A Film", TimeSpan.FromMinutes(90)))
            .From("Jellyfin Web", "A browser")
            .Via(ServerPlayMethod.DirectPlay)
            .Identified("session-1", "play-1")
            .Build();

    /// <summary>
    /// Puts the session into the state the server leaves it in once it has
    /// started re-encoding: the method it reports moves, and so does the
    /// transcoding state the summary is folded from.
    /// </summary>
    /// <param name="session">The session.</param>
    private static void ReEncodingFrom(SessionInfo session)
    {
        session.PlayState.PlayMethod = ServerPlayMethod.Transcode;
        session.TranscodingInfo = new TranscodingInfo
        {
            VideoCodec = "h264",
            AudioCodec = "aac",
            Container = "ts",
            TranscodeReasons = TranscodeReason.VideoCodecNotSupported,
            IsVideoDirect = false,
            IsAudioDirect = true
        };
    }

    private static PlayRecord APlay()
    {
        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = "Movie",
            ParentId = null,
            ItemName = "A Film",
            ItemRuntime = TimeSpan.FromMinutes(90),
            StartedUtc = Eight.UtcDateTime,
            EndedUtc = Eight.AddMinutes(30).UtcDateTime,
            WatchedDuration = TimeSpan.FromMinutes(30),
            ReachedTheEnd = false,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethodAtStart = StoredPlayMethod.DirectPlay,
            PlayMethodChangedUtc = null,
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

    private sealed class RecordingPlaySink : IPlaySink
    {
        private readonly System.Collections.Generic.List<PlayRecord> _rows = new();

        public System.Collections.Generic.IReadOnlyList<PlayRecord> Rows => _rows;

        public void Add(PlayRecord play, string playKey) => _rows.Add(play);

        public void NoteOpen(OpenPlay play)
        {
        }

        public void ForgetOpen(string playKey)
        {
        }
    }
}
