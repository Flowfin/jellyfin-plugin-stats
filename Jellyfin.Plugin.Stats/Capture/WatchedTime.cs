using System;

namespace Jellyfin.Plugin.Stats.Capture;

/// <summary>
/// Accumulates how much of a play was actually watched, from the progress
/// reports a session sends.
/// </summary>
/// <remarks>
/// Wall-clock time between start and stop is not watch time. A paused film left
/// open overnight would count as eight hours, and a viewer who skips through an
/// episode would count as having watched it whole. Both are wrong in the
/// direction that flatters the numbers, so this type keeps the two apart and a
/// report can show either.
/// <para>
/// It takes no clock. Every moment it works from arrives on the observation,
/// which is what makes a play that is an hour long a test that runs in
/// microseconds, and what keeps the accumulation identical whether it is fed
/// live or replayed from stored events.
/// </para>
/// <para>
/// Whether the play reached the end is not decided here. That comes from the
/// server's own flag on the stop event, not from a ratio over these two
/// figures, because a ratio would call a film watched that was skipped through
/// and unwatched that was joined late.
/// </para>
/// </remarks>
public sealed class WatchedTime
{
    private readonly DateTimeOffset _startedAt;
    private DateTimeOffset _lastAt;
    private TimeSpan _lastPosition;
    private bool _lastWasPaused;
    private TimeSpan _watched;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchedTime"/> class at the
    /// moment a play started.
    /// </summary>
    /// <param name="startedAt">When the play started.</param>
    /// <param name="startPosition">How far into the item the play started, which is not zero for a resumed item.</param>
    public WatchedTime(DateTimeOffset startedAt, TimeSpan startPosition)
    {
        _startedAt = startedAt;
        _lastAt = startedAt;
        _lastPosition = startPosition;
    }

    /// <summary>
    /// Gets the time actually spent watching, with pauses and seeks taken out.
    /// </summary>
    public TimeSpan Watched => _watched;

    /// <summary>
    /// Gets the time between the start of the play and the last thing heard
    /// about it, pauses included.
    /// </summary>
    public TimeSpan WallClock => _lastAt - _startedAt;

    /// <summary>
    /// Takes one progress report into account.
    /// </summary>
    /// <remarks>
    /// The interval since the previous report contributes the distance the
    /// position moved over it, and never more than the time that passed. That
    /// one clamp is what refuses a seek: jumping twenty minutes forward during a
    /// ten second interval moves the position by twenty minutes and the viewer
    /// watched ten seconds, so ten seconds is what it adds. It is also what
    /// makes the wall-clock figure an upper bound on the watched one by
    /// construction rather than by a check somewhere else.
    /// <para>
    /// A report that arrives out of order, or one at the same moment as the
    /// previous, contributes nothing and does not move the state backwards. The
    /// server sends these on a timer over a network, so out of order is a thing
    /// that happens rather than a thing to assume away.
    /// </para>
    /// </remarks>
    /// <param name="at">When the report was made.</param>
    /// <param name="position">How far into the item the session now is.</param>
    /// <param name="isPaused">Whether the session reports itself paused at that position.</param>
    public void Observe(DateTimeOffset at, TimeSpan position, bool isPaused)
    {
        var elapsed = at - _lastAt;
        if (elapsed <= TimeSpan.Zero)
        {
            return;
        }

        // A session that was paused over this interval watched none of it, even
        // where the position it reports has moved. Some clients keep advancing
        // the position they report while paused, and taking their word for it
        // is how a paused film becomes eight hours of viewing.
        if (!_lastWasPaused)
        {
            var advance = position - _lastPosition;
            _watched += Clamp(advance, TimeSpan.Zero, elapsed);
        }

        _lastAt = at;
        _lastPosition = position;
        _lastWasPaused = isPaused;
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan low, TimeSpan high)
    {
        if (value < low)
        {
            return low;
        }

        return value > high ? high : value;
    }
}
