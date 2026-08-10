// A clock that does not move, so a boundary is a value the test chose rather
// than whatever day the suite happened to run on.
//
// The plugin takes a TimeProvider wherever it needs a moment it was not handed,
// and on a server that is the machine clock. Here it is this, which is the whole
// reason the dependency exists.

using System;

namespace Jellyfin.Plugin.Stats.Tests.Fakes;

/// <summary>
/// A <see cref="TimeProvider"/> that always answers the same moment.
/// </summary>
public sealed class FixedClock : TimeProvider
{
    private readonly DateTimeOffset _now;

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedClock"/> class.
    /// </summary>
    /// <param name="now">The moment this clock reports, for as long as it exists.</param>
    public FixedClock(DateTimeOffset now)
    {
        _now = now;
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;
}
