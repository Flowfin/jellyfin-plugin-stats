using System;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// One part of a window: the label it is drawn under and what was watched in it.
/// </summary>
/// <remarks>
/// The label is the part's own name in the zone the window was read in - a date
/// for a day and a year and month for a month - and never words a page would
/// have to parse back into an instant. What a page does with it is print it.
/// <para>
/// The watched time is nought rather than absent for a part nothing was recorded
/// in, because a part with no rows IS a part in which nothing was watched. That
/// is the one place in this shape where a nought is the honest answer, and it is
/// honest because the part was read: a part that could not be read takes the
/// whole series with it, through <see cref="OwnFigures.Degraded"/>.
/// </para>
/// </remarks>
/// <param name="Label">The part's own name, in the zone the window was read in.</param>
/// <param name="Watched">What was watched in it.</param>
public sealed record UsagePoint(string Label, TimeSpan Watched);
