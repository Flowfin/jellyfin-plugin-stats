using System;
using Jellyfin.Plugin.Stats.Capture;

namespace Jellyfin.Plugin.Stats.ScheduledTasks;

/// <summary>
/// Closes the plays whose sessions have said nothing for longer than the bound.
/// </summary>
/// <remarks>
/// A play ends in one of three ways here. The server sends a stop, or the
/// session ends and the tracker closes what it held, or nothing further arrives
/// at all. The third is what this is for: a client that lost its network, a
/// device switched off, a tab closed hard. None of those produces an event, so
/// the only thing that can tell such a play from one being watched is how long
/// it has been since the server heard from it.
/// <para>
/// This is where the moment comes from, and it is the only reason this class
/// exists beside the tracker. The tracker takes the moment as an argument, which
/// is what lets a test choose a play an hour old without waiting an hour and
/// what <c>no-ambient-clock</c> in <c>tools/invariants/rules</c> refuses the
/// other way round. On a server the clock is the registered
/// <see cref="TimeProvider"/>, which is the machine clock read in the one file
/// allowed to read it.
/// </para>
/// <para>
/// Issue #221.
/// </para>
/// </remarks>
public sealed class QuietPlaySweep
{
    /// <summary>
    /// How long a play may hear nothing from its session before it is closed.
    /// </summary>
    /// <remarks>
    /// A constant rather than a setting, because the number that matters is not
    /// a preference: it is a bound below which plays that are still being
    /// watched get closed. A client reports while it plays and while it is
    /// paused, at intervals the server decides, so a value chosen on a settings
    /// page by somebody with no way to measure that interval buys nothing and
    /// costs a closed play on whichever client reports least often.
    /// <para>
    /// Half an hour is longer than any reporting interval either supported
    /// server line is configured with and short enough that a play lost to a
    /// dead client is in the reports the same day. A play closed here is not
    /// lost time: its end is the last moment the server heard from the session,
    /// so the row says how much was watched and not how long the row waited to
    /// be written.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan DefaultBound = TimeSpan.FromMinutes(30);

    private readonly PlayTracker _tracker;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _bound;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuietPlaySweep"/> class.
    /// </summary>
    /// <param name="tracker">The tracker holding the plays that have started and not stopped.</param>
    /// <param name="clock">Where the moment the bound is measured back from comes from.</param>
    /// <param name="bound">How long a play may hear nothing before it is closed.</param>
    public QuietPlaySweep(PlayTracker tracker, TimeProvider clock, TimeSpan bound)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bound, TimeSpan.Zero);

        _tracker = tracker;
        _clock = clock;
        _bound = bound;
    }

    /// <summary>
    /// Closes whatever has gone quiet, as of now.
    /// </summary>
    /// <returns>How many plays were closed.</returns>
    public int Run() => _tracker.CloseWhatHasGoneQuiet(_clock.GetUtcNow(), _bound);
}
