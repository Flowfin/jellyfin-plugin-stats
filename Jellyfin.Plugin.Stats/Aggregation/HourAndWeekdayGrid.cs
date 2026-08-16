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
    /// <param name="plays">The plays to read. The range they belong to is chosen before they get here.</param>
    /// <param name="zone">The zone the hours are read in.</param>
    /// <returns>Every hour of the week, and the zone they were read in.</returns>
    /// <exception cref="ArgumentException">A play's start is not in UTC.</exception>
    public static HourAndWeekdayGrid Over(IEnumerable<PlayRecord> plays, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(plays);
        ArgumentNullException.ThrowIfNull(zone);

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
                    Plays = counted[index],
                    WatchedMinutes = minutes[index]
                });
            }
        }

        return new HourAndWeekdayGrid(zone.Id, cells);
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
