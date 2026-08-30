using System;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// Reads the plays that started inside one half-open window of UTC.
/// </summary>
/// <param name="fromUtc">The first moment in the window.</param>
/// <param name="toUtc">The first moment after it.</param>
/// <returns>The rows, or the reason there are too many of them.</returns>
public delegate WindowOfPlays ReadPlaysInAWindow(DateTimeOffset fromUtc, DateTimeOffset toUtc);
