// Watch time against wall-clock time, in the four shapes that make them differ:
// ordinary playback, a pause, a forward seek, and a client that keeps moving the
// position it reports while paused.
//
// No clock is involved. Every moment here is a value, so an eight hour pause
// costs the run nothing and the same sequence produces the same figures on every
// machine that runs it.

using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Stats.Capture;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class WatchedTimeTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OrdinaryPlaybackCountsAsWatched()
    {
        var watched = new WatchedTime(Start, TimeSpan.Zero);

        // Ten reports ten seconds apart, the position moving with them, which is
        // what a session sends while somebody is watching.
        for (var step = 1; step <= 10; step++)
        {
            watched.Observe(Start + Seconds(10 * step), Seconds(10 * step), isPaused: false);
        }

        Assert.Equal(Seconds(100), watched.Watched);
        Assert.Equal(Seconds(100), watched.WallClock);
    }

    [Fact]
    public void APauseIsWallClockTimeAndNotWatchedTime()
    {
        var watched = new WatchedTime(Start, TimeSpan.Zero);

        watched.Observe(Start + Seconds(10), Seconds(10), isPaused: true);
        watched.Observe(Start + Seconds(10) + TimeSpan.FromHours(1), Seconds(10), isPaused: false);
        watched.Observe(Start + Seconds(20) + TimeSpan.FromHours(1), Seconds(20), isPaused: false);

        Assert.Equal(Seconds(20), watched.Watched);
        Assert.Equal(Seconds(20) + TimeSpan.FromHours(1), watched.WallClock);
    }

    [Fact]
    public void APausedSessionThatKeepsMovingItsPositionStillWatchesNothing()
    {
        var watched = new WatchedTime(Start, TimeSpan.Zero);

        watched.Observe(Start + Seconds(10), Seconds(10), isPaused: true);

        // A client that advances the position it reports while paused. Taking
        // its word for it is how a paused film becomes a night of viewing, and
        // the position moving in step with the clock is exactly the case the
        // seek clamp cannot tell from real playback.
        for (var step = 2; step <= 360; step++)
        {
            watched.Observe(Start + Seconds(10 * step), Seconds(10 * step), isPaused: true);
        }

        Assert.Equal(Seconds(10), watched.Watched);
        Assert.Equal(Seconds(3600), watched.WallClock);
    }

    [Fact]
    public void ASeekForwardDoesNotBuyWatchedTime()
    {
        var watched = new WatchedTime(Start, TimeSpan.Zero);

        watched.Observe(Start + Seconds(10), Seconds(10), isPaused: false);

        // Twenty minutes further into the item, ten seconds later.
        watched.Observe(Start + Seconds(20), Seconds(10) + TimeSpan.FromMinutes(20), isPaused: false);

        Assert.Equal(Seconds(20), watched.Watched);
        Assert.Equal(Seconds(20), watched.WallClock);
    }

    [Fact]
    public void ASeekBackwardsDoesNotSubtractWatchedTime()
    {
        var watched = new WatchedTime(Start, TimeSpan.Zero);

        watched.Observe(Start + Seconds(600), Seconds(600), isPaused: false);
        watched.Observe(Start + Seconds(610), Seconds(60), isPaused: false);

        // The ten minutes already watched stay watched. The interval the rewind
        // happened in contributes nothing rather than a negative number, which
        // is a known undercount bounded by one reporting interval and is stated
        // rather than hidden.
        Assert.Equal(Seconds(600), watched.Watched);
        Assert.Equal(Seconds(610), watched.WallClock);
    }

    [Fact]
    public void APlayResumedPartWayThroughCountsFromWhereItResumed()
    {
        var watched = new WatchedTime(Start, TimeSpan.FromMinutes(30));

        watched.Observe(Start + Seconds(10), TimeSpan.FromMinutes(30) + Seconds(10), isPaused: false);

        Assert.Equal(Seconds(10), watched.Watched);
    }

    [Fact]
    public void AReportOutOfOrderChangesNothing()
    {
        var watched = new WatchedTime(Start, TimeSpan.Zero);

        watched.Observe(Start + Seconds(20), Seconds(20), isPaused: false);
        watched.Observe(Start + Seconds(10), Seconds(10), isPaused: false);
        watched.Observe(Start + Seconds(20), Seconds(20), isPaused: false);

        // The two later reports are behind or level with what has been seen, so
        // neither the figures nor the position they are measured from move.
        Assert.Equal(Seconds(20), watched.Watched);
        Assert.Equal(Seconds(20), watched.WallClock);
    }

    [Fact]
    public void WatchedTimeNeverExceedsWallClockTime()
    {
        // A property over generated sequences rather than over one example. The
        // generator is a seeded linear congruential one written here rather than
        // taken from a package: it has to produce the same sequences on every
        // machine and on both frameworks, and a failure has to be reproducible
        // from the seed printed beside it.
        var failures = new List<string>();

        for (var seed = 1; seed <= 500; seed++)
        {
            var random = new Lcg(seed);
            var watched = new WatchedTime(Start, Seconds(random.Next(0, 600)));
            var at = Start;

            for (var report = 0; report < 40; report++)
            {
                // Intervals include zero and a step backwards, so out of order
                // and duplicate reports are inside what the property covers.
                at += Seconds(random.Next(-5, 30));

                var position = Seconds(random.Next(0, 7200));
                var paused = random.Next(0, 4) == 0;

                watched.Observe(at, position, paused);

                if (watched.Watched > watched.WallClock || watched.Watched < TimeSpan.Zero)
                {
                    failures.Add(
                        $"seed {seed} report {report}: watched {watched.Watched}, wall clock {watched.WallClock}");
                    break;
                }
            }
        }

        Assert.Empty(failures);
    }

    private static TimeSpan Seconds(int count) => TimeSpan.FromSeconds(count);

    /// <summary>
    /// A linear congruential generator, so the sequences are the same everywhere
    /// and a seed is enough to reproduce one.
    /// </summary>
    private sealed class Lcg
    {
        private uint _state;

        public Lcg(int seed) => _state = (uint)seed;

        public int Next(int fromInclusive, int toExclusive)
        {
            _state = (_state * 1664525u) + 1013904223u;
            return fromInclusive + (int)(_state % (uint)(toExclusive - fromInclusive));
        }
    }
}
