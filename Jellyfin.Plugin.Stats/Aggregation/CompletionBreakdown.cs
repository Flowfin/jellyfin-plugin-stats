using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// How far a set of plays got through the items they were of, over the rows
/// that have such a figure, and how many rows that leaves out.
/// </summary>
/// <remarks>
/// The count of what was left out is not a footnote on this fold, it is half of
/// what it answers. A share over the rows that have one, printed without it,
/// reads as a statement about every play in the range, and on a server with a
/// live television tuner most of the range can be missing from it. Issue #40.
/// <para>
/// Two reasons are counted apart, because they are two different things to a
/// reader. A row of a kind with no length to get through is a play this plugin
/// will never have a share for, however the server behaves. A row of a kind
/// that usually has one, arriving with no runtime, is a play the server said
/// nothing about the length of, and a lot of those is a thing to go and look
/// at. A kind this build has never seen is counted with the first, because it
/// has no classification here and no figure is invented for it.
/// </para>
/// <para>
/// The average is absent rather than nought where no row carried a share. A
/// nought there is a statement that everybody stopped immediately, which is the
/// same reading this fold exists to refuse one line up.
/// </para>
/// <para>
/// Nothing here divides by a range or a day. It folds what it is handed, and
/// whoever chose those rows is where a claim about a period lives, which is the
/// same bound every fold beside this one carries.
/// </para>
/// </remarks>
public sealed record CompletionBreakdown
{
    private CompletionBreakdown(
        long plays,
        long playsWithACompletion,
        long playsOfAKindWithNoLength,
        long playsWithNoRuntime,
        double? average)
    {
        Plays = plays;
        PlaysWithACompletion = playsWithACompletion;
        PlaysOfAKindWithNoLength = playsOfAKindWithNoLength;
        PlaysWithNoRuntime = playsWithNoRuntime;
        AverageCompletion = average;
    }

    /// <summary>
    /// Gets how many plays were folded, counted as they arrived.
    /// </summary>
    public long Plays { get; }

    /// <summary>
    /// Gets how many of those plays had a share to compute, which is the number
    /// the average below is over.
    /// </summary>
    public long PlaysWithACompletion { get; }

    /// <summary>
    /// Gets how many plays were of a kind of item with no length to get
    /// through, live television and a photograph among them, and kinds this
    /// build has never seen.
    /// </summary>
    public long PlaysOfAKindWithNoLength { get; }

    /// <summary>
    /// Gets how many plays were of a kind that usually has a length and
    /// arrived with no runtime on them.
    /// </summary>
    public long PlaysWithNoRuntime { get; }

    /// <summary>
    /// Gets how many plays this fold left out of the average, which is the
    /// sentence a report carries beside the figure.
    /// </summary>
    public long PlaysLeftOut => PlaysOfAKindWithNoLength + PlaysWithNoRuntime;

    /// <summary>
    /// Gets the mean share over the plays that had one, or null where none of
    /// them did.
    /// </summary>
    public double? AverageCompletion { get; }

    /// <summary>
    /// Folds a sequence of plays into one statement about how far they got.
    /// </summary>
    /// <param name="plays">The plays to fold.</param>
    /// <returns>The figure and what it left out.</returns>
    public static CompletionBreakdown Over(IEnumerable<PlayRecord> plays)
    {
        ArgumentNullException.ThrowIfNull(plays);

        long counted = 0;
        long withACompletion = 0;
        long noLength = 0;
        long noRuntime = 0;
        double total = 0;

        foreach (var play in plays)
        {
            counted++;

            var share = Completion.Of(play);
            if (share is double value)
            {
                withACompletion++;
                total += value;
                continue;
            }

            // The kind is asked first, so a live television row that arrived
            // carrying a scheduled programme length is left out as live
            // television rather than as a row the server said nothing about.
            if (Completion.CanBeComputedFor(play.ItemType))
            {
                noRuntime++;
            }
            else
            {
                noLength++;
            }
        }

        return new CompletionBreakdown(
            counted,
            withACompletion,
            noLength,
            noRuntime,
            withACompletion == 0 ? null : total / withACompletion);
    }
}
