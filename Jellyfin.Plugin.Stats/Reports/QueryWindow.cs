using System;
using System.Globalization;

namespace Jellyfin.Plugin.Stats.Reports;

/// <summary>
/// What every shape in the query surface is asked over: a range of time and a
/// bound on how many plays may be read to answer it.
/// </summary>
/// <remarks>
/// One type rather than three arguments repeated at five call sites, because
/// the refusals below are the point. A range with no bound is a read that grows
/// with how long the server has been recording, and a bound that arrives at some
/// shapes and not others is the shape somebody forgets.
/// <para>
/// The window is half open: a play starting exactly at <see cref="FromUtc"/> is
/// in it and one starting exactly at <see cref="ToUtc"/> is not. Two windows laid
/// end to end therefore read each play once, which a closed window would not, and
/// a caller asking for a calendar month names the first instant of the next one
/// rather than a tick before it. The store's own deletions are half open for the
/// same reason.
/// </para>
/// <para>
/// Both bounds say they are in UTC or they are refused. A local moment read as
/// UTC moves the range by the machine's offset, and a report that quietly covers
/// a different fortnight from the one asked for is wrong in a way no reader can
/// see. Which day a play belongs to is a separate question, answered in a zone
/// named at the call, and this is not it.
/// </para>
/// <para>
/// Issue #51, and issue #56 for the bound.
/// </para>
/// </remarks>
public sealed record QueryWindow
{
    /// <summary>
    /// The most plays any one shape will read, whatever a caller asks for.
    /// </summary>
    /// <remarks>
    /// A ceiling rather than a default, so a caller cannot widen its way past
    /// it. What it protects is the server: every shape here folds the plays it
    /// read in memory, so the bound is what stops one request over a decade of
    /// history from being a way to make the server do arbitrary work.
    /// <para>
    /// A range that holds more than this is refused rather than answered from
    /// the first rows of it, which is what
    /// <see cref="TooManyPlaysToAnswerException"/> is for. So this number bounds
    /// the work a request can make the server do, and nothing downstream of it
    /// ever receives a fold that quietly covered part of what was asked.
    /// </para>
    /// </remarks>
    public const int MostPlaysAnyShapeReads = 250_000;

    /// <summary>
    /// The longest range any shape here answers over, whatever a caller asks
    /// for.
    /// </summary>
    /// <remarks>
    /// The other half of the same sentence as the bound above, and it bites
    /// earlier: a range is refused for its length before a single row is read,
    /// so an eight-year window costs the server one comparison rather than a
    /// quarter of a million rows fetched and thrown away.
    /// <para>
    /// The number follows from the longest report this plugin offers rather
    /// than being picked for roundness. That report is a calendar year, and a
    /// leap year is 366 days, so the cap is the year with a day of slack rather
    /// than the year exactly.
    /// </para>
    /// <para>
    /// WHAT THE SLACK IS FOR IS NOT SUMMER TIME, and that is worth writing down
    /// because it is the first thing a reader assumes. A zone that puts its
    /// clocks forward in the spring puts them back in the autumn, so the two
    /// cancel inside one calendar year and a local year in such a zone is
    /// exactly 366 days, which the case beside this measures rather than
    /// assumes. What does stretch a local year past the calendar count is a zone
    /// changing its standard offset partway through one, which is a political
    /// decision rather than a seasonal rule and has happened more than once in
    /// the last few years. A cap sitting exactly on 366 days would refuse a
    /// calendar year in such a zone, and the report refused would be the one
    /// this plugin exists to offer.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan LongestRangeAnyShapeAnswers = TimeSpan.FromDays(367);

    private QueryWindow(DateTime fromUtc, DateTime toUtc, int mostPlays)
    {
        FromUtc = fromUtc;
        ToUtc = toUtc;
        MostPlays = mostPlays;
    }

    /// <summary>
    /// Gets the first moment in the window, in UTC.
    /// </summary>
    public DateTime FromUtc { get; }

    /// <summary>
    /// Gets the first moment after the window, in UTC.
    /// </summary>
    public DateTime ToUtc { get; }

    /// <summary>
    /// Gets how many plays at most may be read to answer over this window.
    /// </summary>
    public int MostPlays { get; }

    /// <summary>
    /// Builds a window, refusing one that could not be answered honestly.
    /// </summary>
    /// <param name="fromUtc">The first moment in the window, in UTC.</param>
    /// <param name="toUtc">The first moment after the window, in UTC.</param>
    /// <param name="mostPlays">How many plays at most to read. Held down to <see cref="MostPlaysAnyShapeReads"/>.</param>
    /// <returns>The window.</returns>
    /// <exception cref="ArgumentException">A bound is not in UTC, the window ends before it starts, or it is longer than <see cref="LongestRangeAnyShapeAnswers"/>.</exception>
    public static QueryWindow Of(DateTime fromUtc, DateTime toUtc, int mostPlays = MostPlaysAnyShapeReads)
    {
        InUtc(fromUtc, nameof(fromUtc));
        InUtc(toUtc, nameof(toUtc));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mostPlays);

        if (toUtc < fromUtc)
        {
            throw new ArgumentException(
                "A window ends no earlier than it starts. Reversed bounds read as an empty range rather than as the range that was meant, so a report over them would answer nothing and say it had answered.",
                nameof(toUtc));
        }

        var asked = toUtc - fromUtc;
        if (asked > LongestRangeAnyShapeAnswers)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "That range is {0} days and the longest this plugin answers over is {1} days. It is refused rather than shortened, because a report over the part of a range that fitted reads exactly like a report over the whole of it.",
                    asked.TotalDays.ToString("0.##", CultureInfo.InvariantCulture),
                    LongestRangeAnyShapeAnswers.TotalDays.ToString("0.##", CultureInfo.InvariantCulture)),
                nameof(toUtc));
        }

        return new QueryWindow(fromUtc, toUtc, Math.Min(mostPlays, MostPlaysAnyShapeReads));
    }

    private static void InUtc(DateTime value, string name)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} is {1} and every moment this layer takes is in UTC. Read as UTC it moves the range by the caller's offset, and the report covers a period nobody asked for without saying so.",
                    name,
                    value.Kind),
                name);
        }
    }
}
