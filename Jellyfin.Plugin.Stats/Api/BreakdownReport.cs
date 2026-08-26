using System.Collections.Generic;
using Jellyfin.Plugin.Stats.Aggregation;

namespace Jellyfin.Plugin.Stats.Api;

/// <summary>
/// What a breakdown request is answered with.
/// </summary>
/// <remarks>
/// Three answers and not two, which is what the shape exists for. A range in
/// which nothing was watched, a range whose breakdown may not be shown at all,
/// and a range some of whose members were folded together are different facts,
/// and a response carrying rows alone would report the first two identically and
/// leave the third looking like a server with fewer clients than it has.
/// <para>
/// The group carries no key and no name, exactly as the layer hands it over. A
/// response that gave it one would put it among the rows as though somebody used
/// it, which is the reading issue #41's third condition refuses.
/// </para>
/// </remarks>
/// <param name="Withheld">Whether the breakdown was withheld because too few accounts stood behind what would have been shown.</param>
/// <param name="Rows">One row per member that may be shown under its own name.</param>
/// <param name="Combined">The members too few accounts stand behind, together, and nothing where none had to be folded.</param>
/// <param name="Plays">How many plays the rows and the group together were folded from.</param>
public sealed record BreakdownReport(
    bool Withheld,
    IReadOnlyList<DimensionRow> Rows,
    DeliveryMethodShares? Combined,
    long Plays)
{
    /// <summary>
    /// Gets the answer for a breakdown that may not be shown.
    /// </summary>
    /// <remarks>
    /// It carries no play count. The total over the same range is a separate
    /// question with an answer of its own, and putting a figure here would make
    /// a withheld breakdown a place to read one from.
    /// </remarks>
    public static BreakdownReport NotShown { get; } = new(true, [], null, 0);

    /// <summary>
    /// The answer for a breakdown that may be shown.
    /// </summary>
    /// <param name="breakdown">What the layer folded.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="breakdown"/> is <c>null</c>.</exception>
    public static BreakdownReport Of(DimensionBreakdown breakdown)
    {
        System.ArgumentNullException.ThrowIfNull(breakdown);

        return new BreakdownReport(false, breakdown.Rows, breakdown.Combined, breakdown.Plays);
    }
}
