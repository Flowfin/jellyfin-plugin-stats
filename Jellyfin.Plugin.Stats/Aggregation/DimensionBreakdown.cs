using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// How a set of plays divides between the members of one dimension, as one row
/// per member carrying the delivery figures for the plays under it.
/// </summary>
/// <remarks>
/// This is a partition, unlike the reason breakdown beside it. Every play falls
/// into exactly one row, so the rows add up to the plays they came from, and
/// that is the property the fold is held to.
/// <para>
/// A play the server named neither a client nor a device for is a row and not a
/// silence. Dropping it would take a play out of the answer with nothing saying
/// so, and a reader who adds the rows up and meets a larger play count beside
/// them concludes the plugin is counting wrong. The row that holds those plays
/// carries no name rather than an invented one, for the reason
/// <see cref="DimensionRow"/> gives.
/// </para>
/// <para>
/// Nothing here divides, and nothing here is a share of anything drawn as a
/// percentage. That is the same decision <see cref="DeliveryMethodShares"/>
/// takes and for the same reason: a percentage over a range with no plays in it
/// has no answer, and whoever draws the chart decides what an empty range looks
/// like.
/// </para>
/// </remarks>
public sealed record DimensionBreakdown
{
    private DimensionBreakdown(IReadOnlyList<DimensionRow> rows, long plays)
    {
        Rows = rows;
        Plays = plays;
    }

    /// <summary>
    /// Gets the members that were seen, most plays first, and members with equal
    /// counts in the order their keys sort in. The order is decided here rather
    /// than by the order the plays arrived in, so a chart's bars do not move
    /// when a query is answered by a different plan.
    /// </summary>
    public IReadOnlyList<DimensionRow> Rows { get; }

    /// <summary>
    /// Gets how many plays were folded, counted as they arrived. The rows above
    /// add up to this, and that they do is the property this type exists to
    /// hold rather than a fact about how it is written.
    /// </summary>
    public long Plays { get; }

    /// <summary>
    /// Folds a sequence of plays into one row per member of a dimension.
    /// </summary>
    /// <remarks>
    /// Keys are compared as bytes, which is the comparer the rest of this
    /// namespace already groups with. Two spellings the server gave are two
    /// observations, and folding them together would be the plugin deciding
    /// they mean the same thing.
    /// </remarks>
    /// <param name="plays">The plays to fold. The range they belong to is chosen before they get here.</param>
    /// <param name="dimension">What to group them by.</param>
    /// <returns>The rows and the number of plays they were folded from.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The dimension is not one this build knows.</exception>
    public static DimensionBreakdown Over(IEnumerable<PlayRecord> plays, PlayDimension dimension)
    {
        ArgumentNullException.ThrowIfNull(plays);

        var grouped = new Dictionary<string, List<PlayRecord>>(StringComparer.Ordinal);
        var labels = new Dictionary<string, Label>(StringComparer.Ordinal);
        long folded = 0;

        foreach (var play in plays)
        {
            folded++;

            var (key, name) = Group(play, dimension);

            if (!grouped.TryGetValue(key, out var underThisKey))
            {
                underThisKey = new List<PlayRecord>();
                grouped[key] = underThisKey;
                labels[key] = new Label(name, play.StartedUtc);
            }
            else if (play.StartedUtc >= labels[key].ReportedAt)
            {
                // The latest name wins. A device renamed halfway through a range
                // is one device with two names in the rows, and showing the
                // older one names it something an administrator no longer sees
                // anywhere on their server.
                labels[key] = new Label(name, play.StartedUtc);
            }

            underThisKey.Add(play);
        }

        var rows = new List<DimensionRow>(grouped.Count);
        foreach (var pair in grouped)
        {
            rows.Add(new DimensionRow(pair.Key, labels[pair.Key].Name, DeliveryMethodShares.Over(pair.Value)));
        }

        rows.Sort(static (left, right) =>
        {
            var byPlays = right.Delivery.Plays.CompareTo(left.Delivery.Plays);

            return byPlays != 0 ? byPlays : string.CompareOrdinal(left.Key, right.Key);
        });

        return new DimensionBreakdown(rows, folded);
    }

    /// <summary>
    /// What one play contributes to: the key it is counted under and what that
    /// key is called on the strength of this play.
    /// </summary>
    /// <remarks>
    /// The two are separate for the device, and only for the device. A client
    /// names itself and that name is both the grouping and the label, so the two
    /// move together. A device has an identifier the server assigned and a name
    /// somebody typed, the second of which changes without the device changing,
    /// which is why plays are grouped on the first and labelled with the second.
    /// The row comment on <c>PlayRecord.DeviceId</c> already says that field is
    /// what a breakdown by device groups on.
    /// </remarks>
    private static (string Key, string? Name) Group(PlayRecord play, PlayDimension dimension)
    {
        switch (dimension)
        {
            case PlayDimension.Client:
                var client = Reported(play.ClientName);

                return (client ?? string.Empty, client);

            case PlayDimension.Device:
                return (Reported(play.DeviceId) ?? string.Empty, Reported(play.DeviceName));

            default:
                // A stored row outlives the assembly that wrote it, and so does
                // a caller. A dimension this build has no column for is refused
                // rather than folded into one of the two, because a breakdown
                // answering the wrong question is worse than one that answers
                // none.
                throw new ArgumentOutOfRangeException(
                    nameof(dimension),
                    dimension,
                    "The plays cannot be grouped by a dimension this build has no column for.");
        }
    }

    /// <summary>
    /// What the server actually reported, or nothing where it reported nothing.
    /// Whitespace counts as nothing: a name made only of spaces is a label a
    /// reader cannot see and cannot tell from an absent one.
    /// </summary>
    private static string? Reported(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private readonly record struct Label(string? Name, DateTime ReportedAt);
}
