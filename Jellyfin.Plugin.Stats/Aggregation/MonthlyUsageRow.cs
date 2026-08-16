using System;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// One month of use, read in the zone the answer that carries it names.
/// </summary>
/// <remarks>
/// Nothing here identifies a person, which is the decision
/// <see cref="DailyUsageRow"/> and <see cref="DimensionRow"/> already take: the
/// type has no field for a user, so a series cannot carry one by being written
/// carelessly.
/// <para>
/// A month is added up from the days rather than folded from the plays a second
/// time. Two folds over the same rows are two places a play can be counted on
/// one side of a boundary and not the other, and a reader who finds a month
/// disagreeing with the days inside it has no way to tell which is wrong.
/// </para>
/// </remarks>
/// <param name="Month">The month, from one for January to twelve for December.</param>
/// <param name="Watched">How much was watched over the days in it.</param>
/// <param name="Plays">How many plays fell on those days.</param>
public sealed record MonthlyUsageRow(int Month, TimeSpan Watched, long Plays);
