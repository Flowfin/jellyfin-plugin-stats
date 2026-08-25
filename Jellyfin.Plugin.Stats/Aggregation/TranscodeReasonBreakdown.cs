using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// Why a set of plays was not passed through, as one row per reason the server
/// gave, each carrying the number of plays that recorded it and how much of
/// those plays was watched.
/// </summary>
/// <remarks>
/// This is not a partition and it is not meant to be read as one. A play
/// carries every reason the server gave for it and a server usually gives more
/// than one, so the rows add up to more than the plays they came from. That is
/// the right answer rather than a defect, it is the point
/// <c>docs/transcode-reasons.md</c> exists to make, and it is why the delivery
/// figures beside it are a separate type: those have exactly one answer per
/// play and these do not.
/// <para>
/// The watched time under a row is counted the same way and for the same
/// reason. A play's whole watched time goes under every reason it carries, so
/// four reasons on one play put that play's minutes under four rows rather
/// than a quarter of them under each. Apportioning would put a number nobody
/// watched on the page: the server did not spend a quarter of that play on the
/// container and three quarters on the codecs, it re-encoded one play under all
/// four conditions at once. Issue #242 is where that was decided, and the cost
/// of it is that the minutes total more than the range holds, which is the same
/// cost the play counts already carry and is stated in the same place.
/// </para>
/// <para>
/// A reason counts one play once, however often the stored row repeats it. The
/// capture fold already drops a repeat when it collects the reasons, so on a
/// row this build wrote the check finds nothing; it is here because a stored
/// row outlives the assembly that wrote it, and a row from another build is
/// still read by this one. Counting the repeat would turn one long film into
/// several plays under whichever reason it happened to report most often, which
/// is exactly the misreading the number is meant to survive.
/// </para>
/// <para>
/// A play carrying no reasons is counted in the plays and appears under no row,
/// and no row is invented for it. Most such plays needed no reason, because the
/// server passed them through, and this fold reads the summary rather than the
/// delivery method, so it cannot tell those from a play that was re-encoded and
/// reported nothing. Making that distinction means reading the two together and
/// they can disagree, which is issue #158, so the honest statement here is how
/// many plays recorded any reason at all and no more than that.
/// </para>
/// <para>
/// Nothing here divides, for the reason the delivery figures give: a percentage
/// over a range with no plays in it has no answer, and whoever draws the chart
/// decides what an empty range looks like.
/// </para>
/// </remarks>
public sealed record TranscodeReasonBreakdown
{
    private TranscodeReasonBreakdown(
        IReadOnlyList<TranscodeReasonCount> reasons,
        long plays,
        long playsWithAReason,
        double watchedMinutes,
        double watchedMinutesWithAReason)
    {
        Reasons = reasons;
        Plays = plays;
        PlaysWithAtLeastOneReason = playsWithAReason;
        WatchedMinutes = watchedMinutes;
        WatchedMinutesWithAtLeastOneReason = watchedMinutesWithAReason;
    }

    /// <summary>
    /// Gets the reasons that were recorded, most plays first, and reasons with
    /// equal counts in the order the server's own names sort in. The order is
    /// decided here rather than by the order the plays arrived in, so the same
    /// rows read back the same way whatever a query returned them in.
    /// </summary>
    public IReadOnlyList<TranscodeReasonCount> Reasons { get; }

    /// <summary>
    /// Gets how many plays were folded, counted as they arrived. The rows above
    /// are not a division of this number and will usually add up to more.
    /// </summary>
    public long Plays { get; }

    /// <summary>
    /// Gets how many of those plays recorded at least one reason. This is the
    /// number a single row is a part of; no row can be larger than it, and the
    /// rows together can be.
    /// </summary>
    public long PlaysWithAtLeastOneReason { get; }

    /// <summary>
    /// Gets how many minutes the folded plays were watched for, counted once
    /// each. This is the period the rows above are read against, and they will
    /// usually add up to more than it.
    /// </summary>
    public double WatchedMinutes { get; }

    /// <summary>
    /// Gets how many of those minutes belong to plays that recorded at least
    /// one reason, counted once each. This is the number a single row is a part
    /// of; no row can be larger than it, and the rows together can be.
    /// </summary>
    public double WatchedMinutesWithAtLeastOneReason { get; }

    /// <summary>
    /// Folds a sequence of plays into one row per reason.
    /// </summary>
    /// <remarks>
    /// Reason names are compared as bytes, which is the comparer the capture
    /// fold already dedupes with. Two spellings the server gave are two
    /// observations, and folding them together would be the plugin deciding
    /// they mean the same thing, which is the same move as working a reason out
    /// from the codecs afterwards.
    /// </remarks>
    /// <param name="plays">The plays to fold. The range they belong to is chosen before they get here.</param>
    /// <returns>The rows, the plays they were folded from, and how many of those recorded a reason.</returns>
    public static TranscodeReasonBreakdown Over(IEnumerable<PlayRecord> plays)
    {
        ArgumentNullException.ThrowIfNull(plays);

        var counted = new Dictionary<string, long>(StringComparer.Ordinal);
        var watched = new Dictionary<string, long>(StringComparer.Ordinal);
        var onThisPlay = new HashSet<string>(StringComparer.Ordinal);
        long folded = 0;
        long withAReason = 0;
        long watchedTicks = 0;
        long watchedTicksWithAReason = 0;

        foreach (var play in plays)
        {
            folded++;
            onThisPlay.Clear();

            // Ticks rather than minutes, so the same rows fold to the same
            // figure whatever order a query returned them in. Adding fractions
            // of a minute play by play does not, and a row that moves in its
            // last digits between two runs over one range is a figure nobody
            // can check by hand.
            var ticks = play.WatchedDuration.Ticks;
            watchedTicks += ticks;

            foreach (var reason in play.Transcode.Reasons)
            {
                if (!onThisPlay.Add(reason))
                {
                    continue;
                }

                counted.TryGetValue(reason, out var soFar);
                counted[reason] = soFar + 1;

                // The whole of this play's watched time, under this reason and
                // under every other reason it carries. Nothing is divided.
                watched.TryGetValue(reason, out var watchedSoFar);
                watched[reason] = watchedSoFar + ticks;
            }

            if (onThisPlay.Count > 0)
            {
                withAReason++;
                watchedTicksWithAReason += ticks;
            }
        }

        var rows = new List<TranscodeReasonCount>(counted.Count);
        foreach (var pair in counted)
        {
            rows.Add(new TranscodeReasonCount(pair.Key, pair.Value, MinutesOf(watched[pair.Key])));
        }

        rows.Sort(static (left, right) =>
        {
            var byPlays = right.Plays.CompareTo(left.Plays);

            return byPlays != 0 ? byPlays : string.CompareOrdinal(left.Reason, right.Reason);
        });

        return new TranscodeReasonBreakdown(
            rows,
            folded,
            withAReason,
            MinutesOf(watchedTicks),
            MinutesOf(watchedTicksWithAReason));
    }

    /// <summary>
    /// A summed span of watched time as the minutes a drawing scales.
    /// </summary>
    /// <remarks>
    /// The conversion happens once, at the end of the fold, and never inside
    /// it. Every figure this type hands out therefore comes from the same
    /// arithmetic, so the rows and the two totals beside them can be compared
    /// without the comparison being about where a rounding happened.
    /// </remarks>
    /// <param name="ticks">The summed watched time.</param>
    /// <returns>The same span in minutes.</returns>
    private static double MinutesOf(long ticks) => TimeSpan.FromTicks(ticks).TotalMinutes;
}
