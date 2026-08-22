// A play that is still running, driven through the whole path and read back off
// the file by somebody else.
//
// The reader in every case here is a second store opened over the same
// directory, which is what "on the file" has to mean: the tracker holds the
// play in memory and the writer holds a connection, so anything asserted
// through either of those would pass over a play that never reached the disk.
// Issue #220.
//
// Every wait carries a timeout and every timeout is asserted, because the write
// happens on a thread of the writer's own. A path that stopped writing running
// plays would turn these into tests that hang, and a suite that hangs reports
// nothing.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Capture;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ServerPlayMethod = MediaBrowser.Model.Session.PlayMethod;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class OpenPlaysReachTheFileTests : IDisposable
{
    /// <summary>
    /// How many times a wait looks before it gives up, and how long it sleeps
    /// between two looks. Thirty seconds in total, which is long enough that a
    /// slow runner is not a failure and short enough that a path which stopped
    /// writing running plays is reported rather than waited on.
    /// </summary>
    private const int Attempts = 3000;

    private const int BetweenAttempts = 10;

    /// <summary>
    /// When the server first hears from a session here. Every moment in a row
    /// comes off the session's last check-in, so a case that wants the play to
    /// have run for twenty minutes says so by moving that rather than by
    /// waiting.
    /// </summary>
    private static readonly DateTimeOffset Midday = new(2026, 3, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root;

    private SqlitePlayStore? _reader;

    public OpenPlaysReachTheFileTests()
    {
        _root = Path.Join(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// The first condition of issue #220. A play that has started and has not
    /// stopped is on the file, read by a store this test opens for itself while
    /// the play is still running.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task APlayThatHasNotStoppedIsAlreadyOnTheFile()
    {
        var sessions = new FakeSessionManager();
        using var writer = AWriter();
        var listener = ListenerOver(sessions, writer);
        await listener.StartAsync(CancellationToken.None);

        var session = ASession(sessions, "play-1", "A Film");
        sessions.RaisePlaybackStart(session, Midday);

        var running = Assert.Single(WaitForOpenPlays(1));

        Assert.Equal("play-1", running.PlayKey);
        Assert.Equal("A Film", running.SoFar.ItemName);

        // Nothing has ended, and the row says so in the two fields that can be
        // read as an ending.
        Assert.False(running.SoFar.ReachedTheEnd);
        Assert.Equal(running.SoFar.StartedUtc, running.SoFar.EndedUtc);

        // And no finished row exists for it yet, which is the other half of
        // "still running" and the half a reader would otherwise have to take on
        // trust.
        using var reader = new SqlitePlayStore(_root);
        Assert.Empty(reader.AllPlays());

        await listener.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// The running row follows the play. A progress report moves what the file
    /// says about it, which is what makes the row worth having: a row frozen at
    /// the start would say a three hour film had watched nothing.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheRunningRowMovesWithThePlay()
    {
        var sessions = new FakeSessionManager();
        using var writer = AWriter();
        var listener = ListenerOver(sessions, writer);
        await listener.StartAsync(CancellationToken.None);

        var session = ASession(sessions, "play-1", "A Film");
        sessions.RaisePlaybackStart(session, Midday);

        var atTheStart = Assert.Single(WaitForOpenPlays(1));

        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(20), at: Midday.AddMinutes(20));

        WaitFor(
            () => OpenPlays().Count == 1 && OpenPlays()[0].SoFar.WatchedDuration > atTheStart.SoFar.WatchedDuration,
            "the running row never moved after a progress report.");

        var later = Assert.Single(OpenPlays());

        Assert.True(later.SoFar.EndedUtc > atTheStart.SoFar.EndedUtc);
        Assert.Equal("play-1", later.PlayKey);

        await listener.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// The second condition of issue #220. Once the play stops it is one row
    /// and not two: the finished row is there and the running one is gone, and
    /// both are counted rather than one being taken as evidence of the other.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AStoppedPlayIsOneRowAndNotTwo()
    {
        var sessions = new FakeSessionManager();
        using var writer = AWriter();
        var listener = ListenerOver(sessions, writer);
        await listener.StartAsync(CancellationToken.None);

        var session = ASession(sessions, "play-1", "A Film");
        sessions.RaisePlaybackStart(session, Midday);
        WaitForOpenPlays(1);

        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(20), at: Midday.AddMinutes(20));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(40), at: Midday.AddMinutes(40));

        WaitFor(() => FinishedPlays().Count == 1, "the finished row never reached the file.");
        WaitFor(() => !OpenPlays().Any(), "the running row was left on the file after the play stopped.");

        var finished = Assert.Single(FinishedPlays());

        Assert.Equal("A Film", finished.ItemName);
        Assert.True(finished.WatchedDuration > TimeSpan.Zero);

        await listener.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// The third condition of issue #220. What a play costs the file does not
    /// grow with how often its session reported, driven with one play that
    /// reports two hundred times beside one that reports twice.
    /// </summary>
    /// <remarks>
    /// Rows rather than bytes. A file's size moves in pages and with whatever
    /// space earlier rows freed, so a byte count would be asserting the
    /// storage engine's housekeeping; what this issue is about is whether a
    /// report costs a row, and the row count answers that exactly.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task WhatAPlayCostsDoesNotGrowWithHowOftenItReported()
    {
        var sessions = new FakeSessionManager();
        using var writer = AWriter();
        var listener = ListenerOver(sessions, writer);
        await listener.StartAsync(CancellationToken.None);

        var noisy = ASession(sessions, "play-noisy", "A Long Film");
        var quiet = ASession(sessions, "play-quiet", "A Short Film");

        sessions.RaisePlaybackStart(noisy, Midday);
        sessions.RaisePlaybackStart(quiet, Midday);

        for (var i = 1; i <= 200; i++)
        {
            sessions.RaisePlaybackProgress(noisy, TimeSpan.FromSeconds(i * 10d), at: Midday.AddSeconds(i * 10d));
        }

        sessions.RaisePlaybackProgress(quiet, TimeSpan.FromSeconds(10), at: Midday.AddSeconds(10));

        WaitFor(() => OpenPlays().Count() == 2, "the two running plays are not both on the file.");

        var running = OpenPlays().ToDictionary(play => play.PlayKey, StringComparer.Ordinal);

        // One row each, after two hundred reports and after two. This is the
        // whole of the condition: a hundredfold difference in how often a
        // session checked in makes no difference to what the file holds.
        Assert.Equal(1, running.Count(entry => entry.Key == "play-noisy"));
        Assert.Equal(1, running.Count(entry => entry.Key == "play-quiet"));

        sessions.RaisePlaybackStopped(noisy, TimeSpan.FromSeconds(2000), at: Midday.AddSeconds(2000));
        sessions.RaisePlaybackStopped(quiet, TimeSpan.FromSeconds(20), at: Midday.AddSeconds(20));

        WaitFor(() => FinishedPlays().Count == 2, "the two finished rows never reached the file.");
        WaitFor(() => !OpenPlays().Any(), "a running row was left behind after both plays stopped.");

        await listener.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// A running row survives the process that was holding it. The writer is
    /// disposed of without the play ever stopping, which is what a restart
    /// looks like from the file's side, and the row is still there afterwards.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ARunningRowOutlivesTheProcessThatWroteIt()
    {
        var sessions = new FakeSessionManager();

        // The writer is disposed of by leaving this block, which is what stands
        // in for the process that was holding the play going away. A line after
        // the reads would be a line an assertion above it could skip, and the
        // writer would then still be open while the file is read.
        using (var writer = AWriter())
        {
            var listener = ListenerOver(sessions, writer);
            await listener.StartAsync(CancellationToken.None);

            sessions.RaisePlaybackStart(ASession(sessions, "play-1", "A Film"), Midday);
            WaitForOpenPlays(1);

            await listener.StopAsync(CancellationToken.None);
        }

        using var afterwards = new SqlitePlayStore(_root);

        var left = Assert.Single(afterwards.OpenPlays());

        Assert.Equal("play-1", left.PlayKey);
        Assert.Empty(afterwards.AllPlays());
    }

    public void Dispose()
    {
        _reader?.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static PlaybackEventListener ListenerOver(FakeSessionManager sessions, IPlaySink sink)
        => new(
            sessions,
            new PlayTracker(sink, NullLogger<PlayTracker>.Instance),
            NullLogger<PlaybackEventListener>.Instance);

    private static SessionInfo ASession(FakeSessionManager sessions, string playSessionId, string item)
        => new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer"))
            .Playing(PlaySessionBuilder.Video(item, TimeSpan.FromMinutes(90)))
            .From("Jellyfin Web", "A browser", playSessionId)
            .Via(ServerPlayMethod.DirectPlay)
            .Identified(playSessionId, playSessionId)
            .Build();

    private QueuedPlayWriter AWriter()
        => new(
            () => new SqlitePlayStore(_root),
            QueuedPlayWriter.DefaultBound,
            NullLogger<QueuedPlayWriter>.Instance);

    /// <summary>
    /// Reads the running plays off the file, through a store of this test's own.
    /// </summary>
    /// <returns>What the file holds.</returns>
    private IReadOnlyList<OpenPlay> OpenPlays()
        => Reader() is { } reader ? reader.OpenPlays().ToArray() : Array.Empty<OpenPlay>();

    /// <summary>
    /// Reads the finished plays off the file, through a store of this test's own.
    /// </summary>
    /// <returns>What the file holds.</returns>
    private IReadOnlyList<PlayRecord> FinishedPlays()
        => Reader() is { } reader ? reader.AllPlays().ToArray() : Array.Empty<PlayRecord>();

    /// <summary>
    /// The second store, opened once the writer has made the file and held for
    /// the rest of the case.
    /// </summary>
    /// <remarks>
    /// Held rather than opened per look, because opening one runs the whole
    /// migration list and a wait looks a hundred times a second. It waits for
    /// the file rather than creating it, so a case asserting that nothing was
    /// written is asserting the writer's silence and not this reader's.
    /// </remarks>
    /// <returns>The reader, or null while there is no file yet.</returns>
    private SqlitePlayStore? Reader()
    {
        if (_reader is not null)
        {
            return _reader;
        }

        if (!File.Exists(Path.Join(_root, SqlitePlayStore.FileName)))
        {
            return null;
        }

        _reader = new SqlitePlayStore(_root);

        return _reader;
    }

    private IReadOnlyList<OpenPlay> WaitForOpenPlays(int howMany)
    {
        WaitFor(() => OpenPlays().Count == howMany, "the file never came to hold " + howMany + " running play(s).");

        return OpenPlays();
    }

    /// <summary>
    /// Waits for something the writer's own thread makes true.
    /// </summary>
    /// <remarks>
    /// Counted attempts rather than a deadline read off a clock, because a test
    /// that reads the machine clock is what <c>no-ambient-clock</c> is against
    /// and a count answers the same question. The ceiling is a failure however
    /// slow the runner is: what is being waited for is a write already queued.
    /// </remarks>
    /// <param name="until">What is being waited for.</param>
    /// <param name="whatWentWrong">What to say where it never became true.</param>
    private static void WaitFor(Func<bool> until, string whatWentWrong)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (until())
            {
                return;
            }

            Thread.Sleep(BetweenAttempts);
        }

        Assert.True(false, whatWentWrong);
    }
}
