using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// The shape of a week: what was played in each hour of each weekday, read in
/// one zone.
/// </summary>
/// <remarks>
/// The zone is carried out with the figures rather than left to whoever draws
/// them. A grid of hours is meaningless without knowing whose midnight is at
/// nought, and a view that has to be told the zone separately is a view
/// somebody can forget to tell: it then draws a week whose hours nobody can
/// identify and reads exactly like a correct one. Issue #58 asks the view to
/// state the zone, and this is what makes that a property of the answer instead
/// of a habit of the caller.
/// <para>
/// The zone arrives as an argument and nothing here reads a clock or a setting,
/// which is the same rule <see cref="LocalDay"/> follows and the reason the
/// answer is the same on the runner and on the server. What decides the zone is
/// the configuration, and it stays where the configuration is.
/// </para>
/// <para>
/// A numeric hour offset would be the cheap way to do this and it is wrong
/// twice a year: an offset is a fact about one moment, and a range that spans a
/// summer change contains moments on both sides of it. Converting each play
/// through the zone reads every one of them against the offset that was in
/// force when it happened.
/// </para>
/// <para>
/// Every hour of the week is in the answer, including the ones nothing was
/// played in. A grid that returned only the hours it had figures for would hand
/// a drawing a week with holes in it, and a hole and a quiet hour are different
/// facts that the drawing is written to tell apart.
/// </para>
/// <para>
/// Which of the two a cell is comes back on the cell, and that is what the
/// range beside the plays is for. An hour nobody watched anything in and an
/// hour the range never reached are not the same fact, and until the range
/// arrived here they were the same nought: over three days the second is most
/// of the picture, and over any range longer than the retention window it is
/// the oldest end of it. The difference is knowable from the range and from
/// nothing else, so the range is an argument rather than something worked out
/// of the rows. It is the shape <see cref="YearCoverage"/> already answers the
/// same question in, one step smaller, because a week of cells can say which
/// hours it could not cover instead of stating a window beside them.
/// </para>
/// </remarks>
public sealed record HourAndWeekdayGrid
{
    /// <summary>
    /// How many hours a day has, in the grid rather than on the clock. A day a
    /// zone moves its clocks on is 23 or 25 hours long, and the hour that is
    /// skipped simply has no plays in it while the hour that happens twice has
    /// the plays of both. Neither changes the shape of the week.
    /// </summary>
    private const int HoursInADay = 24;

    /// <summary>
    /// How many days a week has.
    /// </summary>
    private const int DaysInAWeek = 7;

    private HourAndWeekdayGrid(string zone, IReadOnlyList<WeekCell> cells)
    {
        Zone = zone;
        Cells = cells;
    }

    /// <summary>
    /// Gets the identifier of the zone the hours were read in.
    /// </summary>
    public string Zone { get; }

    /// <summary>
    /// Gets every hour of the week, Monday first and midnight first within each
    /// day.
    /// </summary>
    public IReadOnlyList<WeekCell> Cells { get; }

    /// <summary>
    /// Reads a set of plays into the hours of the week they started in.
    /// </summary>
    /// <remarks>
    /// A play is placed by the moment it started. That is the moment the row
    /// records as a fact about the play rather than about the report, and it is
    /// what makes the answer the same however the range was cut.
    /// <para>
    /// The stored moment is in UTC and is refused here where it is not. The
    /// store refuses to write a timestamp of any other kind, so a row arriving
    /// in one is a reader that assembled it wrongly, and reading it as UTC
    /// anyway would place the play by the offset of whichever machine built it.
    /// The refusal is written out rather than left to the conversion, because
    /// the conversion accepts a local moment on a machine that happens to be at
    /// UTC and rejects the same row anywhere else, which is a check whose answer
    /// depends on the runner.
    /// </para>
    /// </remarks>
    /// <param name="plays">The plays to read. The range they belong to is chosen before they get here and is declared by the two arguments below.</param>
    /// <param name="zone">The zone the hours are read in.</param>
    /// <param name="coveredFromUtc">The first moment the figures could have come from, in UTC.</param>
    /// <param name="coveredUntilUtc">The moment after the last one they could have come from, in UTC.</param>
    /// <returns>Every hour of the week, and the zone they were read in.</returns>
    /// <exception cref="ArgumentException">A play's start is not in UTC, a bound of the range is not in UTC, the range ends where it starts or earlier, or a play started outside it.</exception>
    public static HourAndWeekdayGrid Over(
        IEnumerable<PlayRecord> plays,
        TimeZoneInfo zone,
        DateTime coveredFromUtc,
        DateTime coveredUntilUtc)
    {
        ArgumentNullException.ThrowIfNull(plays);
        ArgumentNullException.ThrowIfNull(zone);

        RefuseAMomentThatIsNotInUtc(coveredFromUtc, nameof(coveredFromUtc));
        RefuseAMomentThatIsNotInUtc(coveredUntilUtc, nameof(coveredUntilUtc));

        if (coveredUntilUtc <= coveredFromUtc)
        {
            throw new ArgumentException(
                "A range that ends where it starts or earlier holds no moment at all, and every hour of the week would come back absent. That is a plausible looking week and it answers a caller's mistake rather than reporting it.",
                nameof(coveredUntilUtc));
        }

        var covered = HoursOfTheWeekTheRangeReaches(zone, coveredFromUtc, coveredUntilUtc);
        var counted = new long[DaysInAWeek * HoursInADay];
        var minutes = new double[DaysInAWeek * HoursInADay];

        foreach (var play in plays)
        {
            if (play.StartedUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "A play starts at a moment that is {0} rather than UTC, and the hour of the week it belongs to would then be read off the offset of whichever machine built the row.",
                        play.StartedUtc.Kind),
                    nameof(plays));
            }

            if (play.StartedUtc < coveredFromUtc || play.StartedUtc >= coveredUntilUtc)
            {
                throw new ArgumentException(
                    "A play started outside the range these figures are read over. Counting it would put plays in an hour the answer goes on to call absent, which is a week no reading of it can make sense of, and the two arguments are one fact rather than two.",
                    nameof(plays));
            }

            var local = TimeZoneInfo.ConvertTime(new DateTimeOffset(play.StartedUtc, TimeSpan.Zero), zone);
            var index = (WeekdayOf(local.DayOfWeek) * HoursInADay) + local.Hour;

            counted[index]++;
            minutes[index] += play.WatchedDuration.TotalMinutes;
        }

        var cells = new List<WeekCell>(DaysInAWeek * HoursInADay);
        for (var weekday = 0; weekday < DaysInAWeek; weekday++)
        {
            for (var hour = 0; hour < HoursInADay; hour++)
            {
                var index = (weekday * HoursInADay) + hour;
                cells.Add(new WeekCell
                {
                    Weekday = weekday,
                    Hour = hour,
                    Plays = covered[index] ? counted[index] : null,
                    WatchedMinutes = covered[index] ? minutes[index] : null
                });
            }
        }

        return new HourAndWeekdayGrid(zone.Id, cells);
    }

    /// <summary>
    /// Which hours of the week a range of moments reaches, read in one zone.
    /// </summary>
    /// <remarks>
    /// The range is walked rather than divided, because the hours of the week
    /// are local hours and the arithmetic that would divide a span of UTC into
    /// them is the numeric offset this whole type exists to avoid. A zone that
    /// moves its clocks makes one local hour of one day happen twice and
    /// another not happen at all, and both fall out of a walk without being
    /// written down: an hour that never occurs is never sampled and is
    /// therefore never called quiet.
    /// <para>
    /// A step of one hour reaches every hour of the week the range holds a
    /// moment of. Consecutive samples are an hour apart and an hour of the week
    /// is an hour wide, so no whole one fits between two of them; the one at
    /// the start is sampled by the first step, and the one at the end by the
    /// last moment of the range, which is why that moment is taken separately
    /// rather than being assumed to fall on a step. The walk stops as soon as
    /// all of them are reached, so a range of years costs about as much as a
    /// range of a fortnight rather than a step per hour in it.
    /// </para>
    /// <para>
    /// The steps are counted off the length of the range and each is measured
    /// from its start, rather than an hour being added to a moving cursor. Both
    /// read the same, and the second walks off the end of the calendar on a
    /// range that ends at the last moment there is, which is a legal range and
    /// would be an exception rather than an answer.
    /// </para>
    /// </remarks>
    /// <param name="zone">The zone the hours are read in.</param>
    /// <param name="fromUtc">The first moment of the range.</param>
    /// <param name="untilUtc">The moment after its last.</param>
    /// <returns>One flag per hour of the week, in the order the cells are laid out.</returns>
    private static bool[] HoursOfTheWeekTheRangeReaches(TimeZoneInfo zone, DateTime fromUtc, DateTime untilUtc)
    {
        var reached = new bool[DaysInAWeek * HoursInADay];
        var reachedSoFar = 0;
        var wholeHoursInTheRange = (long)(untilUtc - fromUtc).TotalHours;

        for (var step = 0L; step <= wholeHoursInTheRange && reachedSoFar < reached.Length; step++)
        {
            var moment = fromUtc.AddHours(step);

            if (moment >= untilUtc)
            {
                break;
            }

            Reach(zone, moment, reached, ref reachedSoFar);
        }

        if (reachedSoFar < reached.Length)
        {
            Reach(zone, untilUtc.AddTicks(-1), reached, ref reachedSoFar);
        }

        return reached;
    }

    /// <summary>
    /// Marks the hour of the week one moment falls in.
    /// </summary>
    /// <param name="zone">The zone the hour is read in.</param>
    /// <param name="momentUtc">The moment.</param>
    /// <param name="reached">The flags to mark.</param>
    /// <param name="reachedSoFar">How many have been marked, so the walk can stop.</param>
    private static void Reach(TimeZoneInfo zone, DateTime momentUtc, bool[] reached, ref int reachedSoFar)
    {
        var local = TimeZoneInfo.ConvertTime(new DateTimeOffset(momentUtc, TimeSpan.Zero), zone);
        var index = (WeekdayOf(local.DayOfWeek) * HoursInADay) + local.Hour;

        if (!reached[index])
        {
            reached[index] = true;
            reachedSoFar++;
        }
    }

    /// <summary>
    /// Refuses a bound of the range that does not say it is in UTC.
    /// </summary>
    /// <remarks>
    /// For the reason a row's moment is refused, one step further on. A local
    /// moment taken for UTC moves the range by the offset of the machine that
    /// built it, and what that changes here is which hours the answer calls
    /// absent, so the mistake arrives as a week that reads perfectly well and
    /// is an hour out at both ends.
    /// </remarks>
    /// <param name="moment">The bound.</param>
    /// <param name="named">Which argument it is.</param>
    private static void RefuseAMomentThatIsNotInUtc(DateTime moment, string named)
    {
        if (moment.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "A bound of the range is a moment that is {0} rather than UTC, and the hours it reaches would then be read off the offset of whichever machine built it.",
                    moment.Kind),
                named);
        }
    }

    /// <summary>
    /// The day of the week, counted from Monday.
    /// </summary>
    /// <remarks>
    /// The framework counts from Sunday and the drawing lays its rows out from
    /// Monday, so one of the two has to be translated and this is the one place
    /// it happens. A week starting on Sunday is a week whose two quiet days sit
    /// at opposite ends of the picture, which is the shape a reader looking for
    /// the weekend has to work around.
    /// </remarks>
    /// <param name="day">The day the framework reported.</param>
    /// <returns>Nought for Monday through six for Sunday.</returns>
    private static int WeekdayOf(DayOfWeek day) => ((int)day + 6) % 7;
}
