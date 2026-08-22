// What the queue behind the tracker owes: the server's thread never waits for a
// write, a backlog is bounded rather than unbounded, and a store that will not
// take a row says so once instead of once per row.
//
// The first of those is asserted through the whole path rather than on the
// writer alone. The thing issue #29 is about is a playback event the server
// raised, so the test raises one, and the sink under it is the one the
// registrator builds. Asserting it on a direct call to Add would prove the
// queue accepts a row quickly and say nothing about the path the row arrives
// on.
//
// Every wait here carries a timeout and every timeout is asserted. A queue
// removed from between the tracker and the store turns these into tests that
// hang, and a suite that hangs reports nothing; with the timeout it reports a
// failure and names it.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Capture;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ServerPlayMethod = MediaBrowser.Model.Session.PlayMethod;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class QueuedPlayWriterTests
{
    private static readonly TimeSpan LongEnoughToBeAFailure = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task AHeldWriterDoesNotHoldThePlaybackEvent()
    {
        var store = new HoldablePlayStore();
        using var writer = WriterOver(store);
        var sessions = new FakeSessionManager();
        var listener = ListenerOver(sessions, writer);
        await listener.StartAsync(CancellationToken.None);

        store.Hold();

        // The play is raised on a thread of its own, and that is the whole
        // shape of this test. A write path without a queue in it blocks inside
        // the held store, so the raise never returns; on the test's own thread
        // that is a suite that hangs and reports nothing, and on this one it is
        // an assertion that fails and says which.
        using var returned = new ManualResetEventSlim(initialState: false);
        var raising = new Thread(() =>
        {
            RaiseAPlay(sessions, "play-1");
            returned.Set();
        });
        raising.Start();

        Assert.True(
            returned.Wait(LongEnoughToBeAFailure),
            "the playback event did not return while the writer was held.");

        Assert.True(
            store.Entered.Wait(LongEnoughToBeAFailure),
            "the writer never reached the store, so nothing was being held.");

        // Held inside the write, so the row is on its way and has not arrived.
        Assert.Empty(store.Rows);

        store.Release();
        raising.Join();
        await listener.StopAsync(CancellationToken.None);
        writer.Dispose();

        Assert.Single(store.Rows);
        Assert.Equal(1, writer.Written);
    }

    [Fact]
    public void RowsPastTheBoundAreTurnedAwayAndCounted()
    {
        var store = new HoldablePlayStore();
        using var writer = WriterOver(store, bound: 4);
        store.Hold();

        // One row first, and then a wait until the writer is inside the store
        // holding it. Filling the queue straight away would race the writer:
        // whether it had taken a row out yet decides how many of the rest fit,
        // and the count this test asserts would be one of two answers
        // depending on how the two threads were scheduled. Measured, on a
        // runner that gave the other one.
        writer.Add(APlay(), "a-play");

        Assert.True(
            store.Entered.Wait(LongEnoughToBeAFailure),
            "the writer never reached the store.");

        // The queue is empty and cannot drain, so the next four fill it exactly
        // and the five after that have nowhere to go.
        for (var i = 0; i < 9; i++)
        {
            writer.Add(APlay(), "a-play");
        }

        store.Release();
        writer.Dispose();

        Assert.Equal(5, writer.Accepted);
        Assert.Equal(5, writer.Written);
        Assert.Equal(5, writer.Refused);
        Assert.Equal(5, store.Rows.Count);
    }

    [Fact]
    public void EveryRowAlreadyAcceptedIsWrittenBeforeDisposeReturns()
    {
        var store = new HoldablePlayStore();
        using var writer = WriterOver(store, bound: 64);
        store.Hold();

        for (var i = 0; i < 20; i++)
        {
            writer.Add(APlay(), "a-play");
        }

        store.Release();
        writer.Dispose();

        // Not "eventually". Dispose has returned, so the promise made by
        // accepting each of these rows is either kept by now or broken.
        Assert.Equal(20, store.Rows.Count);
        Assert.Equal(20, writer.Accepted);
        Assert.Equal(0, writer.Refused);
        Assert.True(store.Disposed, "the writer left the store open.");
    }

    [Fact]
    public void ARowHandedOverAfterTheWriterStoppedIsCountedRatherThanThrown()
    {
        var store = new HoldablePlayStore();
        using var writer = WriterOver(store);
        writer.Dispose();

        // An event can still be in flight when the server stops the plugin.
        // What must not happen is an exception travelling back into the
        // server's event dispatch from a plugin that has finished.
        writer.Add(APlay(), "a-play");
        writer.Dispose();

        Assert.Equal(1, writer.Refused);
        Assert.Equal(0, writer.Accepted);
        Assert.Empty(store.Rows);
    }

    [Fact]
    public void AStoreThatRefusesEveryRowIsLoggedOncePerClassAndNotOncePerRow()
    {
        var logger = new RecordingLogger<QueuedPlayWriter>();
        var store = new HoldablePlayStore { Throwing = () => new IOException("the disk is full") };
        using var writer = new QueuedPlayWriter(() => store, QueuedPlayWriter.DefaultBound, logger);

        for (var i = 0; i < 12; i++)
        {
            writer.Add(APlay(), "a-play");
        }

        writer.Dispose();

        Assert.Equal(12, writer.Failed);
        Assert.Equal(0, writer.Written);
        Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Error, logger.Lines[0].Level);
        Assert.Contains("System.IO.IOException", logger.Lines[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondKindOfFailureIsItsOwnLine()
    {
        var logger = new RecordingLogger<QueuedPlayWriter>();
        var store = new HoldablePlayStore { Throwing = () => new IOException("the disk is full") };
        using var writer = new QueuedPlayWriter(() => store, bound: 2, logger: logger);
        store.Hold();

        // Held on the first row, then three more at a bound of two, so exactly
        // one is turned away by the queue and the three that got in are refused
        // by the store. The two are different classes, and a reader who only
        // heard about one of them would draw the wrong conclusion about which
        // end is broken.
        writer.Add(APlay(), "a-play");

        Assert.True(
            store.Entered.Wait(LongEnoughToBeAFailure),
            "the writer never reached the store.");

        for (var i = 0; i < 3; i++)
        {
            writer.Add(APlay(), "a-play");
        }

        store.Release();
        writer.Dispose();

        Assert.Equal(1, writer.Refused);
        Assert.Equal(3, writer.Failed);
        Assert.Equal(2, logger.Lines.Count);
        Assert.All(logger.Lines, line => Assert.Equal(LogLevel.Error, line.Level));
        Assert.Contains(logger.Lines, line => line.Message.Contains("System.IO.IOException", StringComparison.Ordinal));
        Assert.Contains(logger.Lines, line => line.Message.Contains("the queue is full", StringComparison.Ordinal));
    }

    [Fact]
    public void AStoreThatCannotBeOpenedCostsRowsAndNothingElse()
    {
        var logger = new RecordingLogger<QueuedPlayWriter>();
        var opens = 0;
        using var writer = new QueuedPlayWriter(
            () =>
            {
                opens++;
                throw new UnauthorizedAccessException("the folder is read only");
            },
            QueuedPlayWriter.DefaultBound,
            logger);

        writer.Add(APlay(), "a-play");
        writer.Add(APlay(), "a-play");
        writer.Dispose();

        // The point of the lazy open: this never reached a constructor the
        // server was waiting on, so nothing here could have stopped the server
        // starting. Issue #31 is where the plugin says so somewhere other than
        // the log.
        Assert.Equal(2, opens);
        Assert.Equal(2, writer.Failed);
        Assert.Equal(0, writer.Written);
        Assert.Single(logger.Lines);
        Assert.Contains("UnauthorizedAccessException", logger.Lines[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStoreIsNotOpenedUntilThereIsARowForIt()
    {
        var opened = false;
        var store = new HoldablePlayStore();
        using var writer = new QueuedPlayWriter(
            () =>
            {
                opened = true;
                return store;
            },
            QueuedPlayWriter.DefaultBound,
            NullLogger<QueuedPlayWriter>.Instance);

        // A plugin installed on a server nobody is watching anything on has no
        // reason to create a database file, and a file that appears without a
        // row in it is a file an administrator has to ask about.
        Assert.False(opened, "the store was opened before a play was finished.");
    }

    private static QueuedPlayWriter WriterOver(IPlayStore store, int bound = QueuedPlayWriter.DefaultBound)
        => new(() => store, bound, NullLogger<QueuedPlayWriter>.Instance);

    private static PlaybackEventListener ListenerOver(FakeSessionManager sessions, IPlaySink sink)
        => new(
            sessions,
            new PlayTracker(sink, NullLogger<PlayTracker>.Instance),
            NullLogger<PlaybackEventListener>.Instance);

    /// <summary>
    /// Runs one whole play through the fake session manager, which is what
    /// produces one finished row.
    /// </summary>
    private static void RaiseAPlay(FakeSessionManager sessions, string playSessionId)
    {
        var session = new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer"))
            .Playing(PlaySessionBuilder.Video("An Item", TimeSpan.FromMinutes(90)))
            .From("Jellyfin Web", "A browser")
            .Via(ServerPlayMethod.DirectPlay)
            .Identified(playSessionId, playSessionId)
            .Build();

        sessions.RaisePlaybackStart(session);
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(5));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(10));
    }

    private static PlayRecord APlay()
    {
        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = "Episode",
            ParentId = null,
            ItemName = "An episode",
            ItemRuntime = TimeSpan.FromMinutes(42),
            StartedUtc = new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc),
            EndedUtc = new DateTime(2026, 3, 14, 9, 41, 0, DateTimeKind.Utc),
            WatchedDuration = TimeSpan.FromMinutes(38),
            ReachedTheEnd = true,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethodAtStart = PlayMethod.DirectPlay,
            PlayMethodChangedUtc = null,
            Transcode = new TranscodeSummary
            {
                VideoCodec = null,
                AudioCodec = null,
                VideoWasDirect = false,
                AudioWasDirect = false,
                PeakBitrate = null,
                TypicalBitrate = null,
                HardwareAcceleration = null,
                Reasons = Array.Empty<string>()
            }
        };
    }
}
