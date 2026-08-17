using System;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// Which days of a calendar year the store could still answer for when the
/// wrap-up over it was folded.
/// </summary>
/// <remarks>
/// A yearly wrap-up computed from rows that are deleted after ninety days is a
/// ninety-day wrap-up wearing a yearly title, and nothing in the figures says
/// so: they are real figures over real plays, they add up, and the only thing
/// wrong with them is the word on the heading. This is the sentence that stops
/// that reading, and it travels with the answer rather than being worked out by
/// whoever draws it.
/// <para>
/// It is a statement about what the store has LOST and never about how much of
/// the year has happened. A day inside the covered window with nothing on it is
/// a day nobody watched anything; a day outside it is a day whose rows may have
/// existed and are gone. Those are different facts and the figures cannot tell
/// them apart, which is why this is stated separately from them.
/// </para>
/// <para>
/// The window comes from the store and never from the person's own rows.
/// Somebody whose first play of the year was in September has a September row
/// on a store that goes back to January, and reading the window off their plays
/// would report every quiet start of a year as a retention cut. What the store
/// holds is one moment for the whole file, and it is the same moment whoever
/// asks.
/// </para>
/// <para>
/// The window is stated on every wrap-up and not only on a short one. A reader
/// given a statement only where the year is incomplete has to read an absence
/// as an assurance, which is the shape this plugin refuses everywhere else: a
/// figure that is not known is written down as not known rather than left out.
/// </para>
/// <para>
/// Nothing here is scaled, and nothing downstream may scale from it. Four
/// months of a year are four months of figures, and a projection of what the
/// missing eight would have held is a number about plays that were deleted,
/// invented by the thing whose job is to report what was kept. That is the
/// second condition of issue #69 and the reason this type carries a window
/// rather than a factor.
/// </para>
/// <para>
/// The window is a floor and not an exact account of what one person lost, and
/// it errs on the pessimistic side on purpose. A deletion of somebody else's
/// older rows moves the store's oldest row forward without taking anything of
/// this person's, so their window narrows for a reason that was not about them.
/// The opposite error is the one that matters: a window claiming days the store
/// no longer answers for is a partial year presented as a whole one, which is
/// what this type exists against. A per-account reading would not repair it
/// either, because that reading is the person's own earliest play, which is the
/// number the paragraph above says not to use.
/// </para>
/// <para>
/// A window held with an answer is the window that was true when it was folded.
/// The retention sweep is the only route that narrows what the store can answer
/// for by removing days, and it lets go of every held answer where it deleted
/// anything, so a held wrap-up cannot outlive its own window that way. What can
/// outlive it is the paragraph above, one account's deletion narrowing the
/// server-wide reading, and a held answer is then wider than a fresh fold would
/// be and closer to what that person actually kept.
/// </para>
/// </remarks>
public sealed record YearCoverage
{
    private YearCoverage(int year, DateOnly? firstDayCovered, DateOnly? earliestPlay)
    {
        Year = year;
        FirstDayCovered = firstDayCovered;
        LastDayCovered = new DateOnly(year, 12, 31);
        EarliestPlay = earliestPlay;
    }

    /// <summary>
    /// Gets the calendar year this is about, read in the same zone as the
    /// wrap-up carrying it.
    /// </summary>
    public int Year { get; }

    /// <summary>
    /// Gets the first day of the year the store could still answer for, and
    /// null where it could answer for none of it because it holds no row at
    /// all.
    /// </summary>
    /// <remarks>
    /// It is the later of the first of January and the day the oldest row in
    /// the store started on. A store swept back to September is a store that
    /// answers for September onwards whoever asks, and a store whose oldest row
    /// is from a previous year answers for the whole of this one.
    /// </remarks>
    public DateOnly? FirstDayCovered { get; }

    /// <summary>
    /// Gets the last day of the year the store could still answer for, which is
    /// the last day of the year.
    /// </summary>
    /// <remarks>
    /// Retention deletes from the old end, so the new end of a year is never
    /// cut by it and this is a constant rather than a reading. On the year the
    /// server is in now that includes days that have not happened, and it is
    /// still the right answer to the question this type asks: no row for those
    /// days has been lost, because none has been written. How much of a year
    /// has passed is a question for a clock, and nothing in this fold reads
    /// one.
    /// </remarks>
    public DateOnly LastDayCovered { get; }

    /// <summary>
    /// Gets a value indicating whether the store could still answer for every
    /// day of the year.
    /// </summary>
    public bool WholeYear => FirstDayCovered == new DateOnly(Year, 1, 1);

    /// <summary>
    /// Gets how many days of the year the store could still answer for.
    /// </summary>
    /// <remarks>
    /// The number a wrap-up says out loud when it says it covers four months
    /// rather than a year. It is derived from the two days above rather than
    /// counted anywhere, so a reader comparing it against them cannot be told
    /// two different things.
    /// </remarks>
    public int DaysCovered =>
        FirstDayCovered is DateOnly first ? LastDayCovered.DayNumber - first.DayNumber + 1 : 0;

    /// <summary>
    /// Gets the earliest day of the year this person actually has a play on,
    /// and null where they have none.
    /// </summary>
    /// <remarks>
    /// The earliest row the wrap-up had, which issue #69 asks it to name. It is
    /// a different statement from <see cref="FirstDayCovered"/> and the two are
    /// worth reading together: a window opening in September with a first play
    /// in September says nothing about when that person started watching, and a
    /// window opening on the first of January with a first play in September
    /// says they watched nothing until then.
    /// </remarks>
    public DateOnly? EarliestPlay { get; }

    /// <summary>
    /// States what part of a year a store could still answer for.
    /// </summary>
    /// <param name="year">The calendar year, read in the zone below.</param>
    /// <param name="oldestPlayStartedUtc">
    /// When the oldest row anywhere in the store started, in UTC, or null where
    /// the store holds no row. It is the store's own reading over every account
    /// and not this person's earliest play.
    /// </param>
    /// <param name="zone">The zone the year's days and its boundaries are read in.</param>
    /// <param name="earliestPlay">
    /// The earliest day of the year this person has a play on, or null where
    /// they have none.
    /// </param>
    /// <returns>The window.</returns>
    /// <exception cref="ArgumentException">The oldest start is not in UTC.</exception>
    public static YearCoverage Of(
        int year,
        DateTime? oldestPlayStartedUtc,
        TimeZoneInfo zone,
        DateOnly? earliestPlay)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (oldestPlayStartedUtc is not DateTime oldest)
        {
            return new YearCoverage(year, firstDayCovered: null, earliestPlay);
        }

        if (oldest.Kind != DateTimeKind.Utc)
        {
            // The same refusal the fold makes at a row's own start, at the one
            // other moment that decides a boundary. Read in the reader's zone
            // instead, this moves the edge of the window by that machine's
            // offset, and the wrap-up then reports a coverage that is a real
            // window over the wrong days on a runner in another zone.
            throw new ArgumentException(
                "The oldest stored start is not in UTC. Reading it here would move the edge of the covered window by the reader's offset.",
                nameof(oldestPlayStartedUtc));
        }

        var oldestDay = LocalDay.Of(new DateTimeOffset(oldest), zone);
        var firstOfTheYear = new DateOnly(year, 1, 1);

        if (oldestDay > new DateOnly(year, 12, 31))
        {
            // Every row the store still holds started after this year ended, so
            // it can answer for none of it. Reported as no window rather than
            // as a window that begins after it finishes, which would make the
            // day count negative and read as a year covered backwards.
            return new YearCoverage(year, firstDayCovered: null, earliestPlay);
        }

        return new YearCoverage(
            year,
            oldestDay > firstOfTheYear ? oldestDay : firstOfTheYear,
            earliestPlay);
    }
}
