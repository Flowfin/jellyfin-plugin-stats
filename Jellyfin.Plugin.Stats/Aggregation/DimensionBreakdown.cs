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
    private DimensionBreakdown(IReadOnlyList<DimensionRow> rows, DeliveryMethodShares? combined, long plays)
    {
        Rows = rows;
        Combined = combined;
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
    /// Gets the plays whose members were folded together because too few
    /// accounts stood behind each of them, and nothing at all where no member
    /// had to be.
    /// </summary>
    /// <remarks>
    /// It is not a row and it is deliberately not shaped like one. A member that
    /// only one account used cannot be shown under its own name without naming
    /// that account to anybody who knows who was watching, so those members are
    /// folded into this one figure, which has no key and no name because it is
    /// not a member of anything. Issue #41 decided the fold on 2026-08-24, in
    /// place of the whole breakdown being withheld.
    /// <para>
    /// A reader who wants it as a row has to make one, and in making it has to
    /// choose a label. That is the point of the shape: a combined group with a
    /// key would sit in <see cref="Rows"/> and be drawn as though it were a
    /// client somebody uses, which is the reading issue #41's third condition
    /// refuses in as many words.
    /// </para>
    /// <para>
    /// It never stands on fewer accounts than a row would have needed. Where
    /// the members that would have folded into it come to fewer than that
    /// between them, there is no breakdown at all rather than a thin group
    /// under another name, and the layer that applies the rule is where that is
    /// decided.
    /// </para>
    /// </remarks>
    public DeliveryMethodShares? Combined { get; }

    /// <summary>
    /// Gets how many plays were folded, counted as they arrived.
    /// </summary>
    /// <remarks>
    /// THE ROWS ALONE NO LONGER ADD UP TO THIS, and that is the one thing to
    /// read carefully about this type. The rows and <see cref="Combined"/>
    /// together do, which is the same property in the presence of a group that
    /// has no name. A reader who adds the rows up and meets a larger count is
    /// looking at a breakdown some of whose members were folded, and the figure
    /// that says so is beside them rather than missing.
    /// </remarks>
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
    /// <param name="foldedTogether">The keys that may not be shown under their own names, which become one group with no name. Empty where every member may be shown.</param>
    /// <returns>The rows, the group the rest were folded into, and the number of plays both were folded from.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The dimension is not one this build knows.</exception>
    public static DimensionBreakdown Over(
        IEnumerable<PlayRecord> plays,
        PlayDimension dimension,
        IReadOnlyCollection<string> foldedTogether)
    {
        ArgumentNullException.ThrowIfNull(plays);
        ArgumentNullException.ThrowIfNull(foldedTogether);

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

        // Which keys fold is decided before this and handed in, because the
        // rule that decides it counts accounts and this fold has never seen an
        // account. Passing the keys rather than the accounts keeps it that way:
        // there is nothing here to count people with.
        var folding = new HashSet<string>(foldedTogether, StringComparer.Ordinal);

        var rows = new List<DimensionRow>(grouped.Count);
        var combined = new List<PlayRecord>();

        foreach (var pair in grouped)
        {
            if (folding.Contains(pair.Key))
            {
                combined.AddRange(pair.Value);
                continue;
            }

            rows.Add(new DimensionRow(pair.Key, labels[pair.Key].Name, DeliveryMethodShares.Over(pair.Value)));
        }

        rows.Sort(static (left, right) =>
        {
            var byPlays = right.Delivery.Plays.CompareTo(left.Delivery.Plays);

            return byPlays != 0 ? byPlays : string.CompareOrdinal(left.Key, right.Key);
        });

        // Nothing folded is nothing to say. A group carrying no plays and a
        // breakdown that had no member to fold are the same fact, and answering
        // the second with an empty group would tell a reader that something was
        // withheld from them when nothing was.
        return new DimensionBreakdown(
            rows,
            combined.Count == 0 ? null : DeliveryMethodShares.Over(combined),
            folded);
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
