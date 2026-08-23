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
    /// An answer that hit the ceiling is not marked, and that is a gap rather
    /// than a decision taken quietly: a report drawn from a truncated read is a
    /// report that is wrong without saying so. Issue #56 is where the honest
    /// answer to a range too large to fold belongs, and this constant is what
    /// stops the failure being unbounded work in the meantime.
    /// </para>
    /// </remarks>
    public const int MostPlaysAnyShapeReads = 250_000;

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
    /// <exception cref="ArgumentException">A bound is not in UTC, or the window ends before it starts.</exception>
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
