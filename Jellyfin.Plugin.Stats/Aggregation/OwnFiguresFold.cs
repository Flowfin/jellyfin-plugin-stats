using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// Folds one account's own figures over one window, from the day-by-day rollups
/// where they can be used and from the play rows where they cannot.
/// </summary>
/// <remarks>
/// The rollups are preferred and are not required. A rollup carries the plays,
/// the watched time and how many of them reached the end, all keyed by day, so
/// the headline figures and the series fold from a read bounded by DAYS rather
/// than by plays - a heavy watcher and a light one with the same habits cost the
/// same read. A rollup carries nothing that names an item, because it holds only
/// what a rebuild can produce again from the rows, so what this account watched
/// most is the one figure that always goes back to the rows.
/// <para>
/// WHERE THE ROLLUPS CANNOT BE USED THE ROWS ANSWER INSTEAD, and they cost
/// nothing extra because they were already read for the top items. A store that
/// has never keyed a rollup, or keyed them in another zone, therefore answers a
/// window in full rather than answering that it could not. A figure is degraded
/// only where BOTH sources failed, which is a much narrower condition than the
/// one this fold started out with.
/// </para>
/// <para>
/// ISSUE #274 SAYS COMPLETION HAS NO ROLLUP COLUMN AND IT HAS ONE.
/// <see cref="DailyRollup.Completed"/> is the count that reached the end and the
/// abandoned figure is the plays less that count, so the completion split folds
/// from the aggregates here rather than from the rows. The correction is written
/// on that issue.
/// </para>
/// <para>
/// A figure that could not be taken is absent with its reason rather than
/// nought, which is the rule issue #66 settled for a year and which holds here
/// for the same reason: a window this page offers is not one a reader can
/// shorten.
/// </para>
/// </remarks>
public static class OwnFiguresFold
{
    /// <summary>
    /// Folds the window.
    /// </summary>
    /// <param name="window">Which window, in the words the request named it with.</param>
    /// <param name="grouping">How the window is divided.</param>
    /// <param name="zone">The zone the window's days are read in.</param>
    /// <param name="firstDay">The first day of the window.</param>
    /// <param name="dayAfter">The first day after the window.</param>
    /// <param name="rollups">This account's rollups for the window, or null where none may be used.</param>
    /// <param name="rows">This account's play rows for the window, or null where they could not be read.</param>
    /// <param name="rowsRefusedBecause">Why the rows could not be read, where they could not.</param>
    /// <param name="topCount">How many rows the top list may hold.</param>
    /// <returns>The figures.</returns>
    /// <exception cref="ArgumentNullException">No window name or no zone was given.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The top list bound is not a positive number.</exception>
    public static OwnFigures Over(
        string window,
        PersonalWindow grouping,
        TimeZoneInfo zone,
        DateOnly firstDay,
        DateOnly dayAfter,
        IReadOnlyList<DailyRollup>? rollups,
        IReadOnlyList<PlayRecord>? rows,
        string? rowsRefusedBecause,
        int topCount)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topCount);

        var degraded = new Dictionary<string, string>(StringComparer.Ordinal);

        if (rows is null)
        {
            degraded[OwnFigures.TopItemsFigure] =
                rowsRefusedBecause ?? "What you watched most over this window could not be read.";
        }

        var totals = rollups is not null
            ? FromTheRollups(rollups, firstDay, dayAfter)
            : rows is not null
                ? FromTheRows(rows, zone, firstDay, dayAfter)
                : null;

        if (totals is null)
        {
            var because = rowsRefusedBecause
                ?? "The figures for this window could not be read from either the stored days or the rows.";

            degraded[OwnFigures.PlaysFigure] = because;
            degraded[OwnFigures.WatchedFigure] = because;
            degraded[OwnFigures.CompletionFigure] = because;

            return new OwnFigures
            {
                Window = window,
                ZoneId = zone.Id,
                Degraded = degraded,
            };
        }

        return new OwnFigures
        {
            Window = window,
            ZoneId = zone.Id,
            Plays = totals.Plays,
            Watched = totals.Watched,
            Finished = totals.Finished,
            Abandoned = totals.Plays - totals.Finished,
            Points = PointsOf(grouping, firstDay, dayAfter, totals.PerDay),
            TopItems = rows is null ? Array.Empty<TitleRow>() : TopOf(rows, topCount),
            Degraded = degraded,
        };
    }

    /// <summary>
    /// The window's totals off the day-by-day rollups.
    /// </summary>
    /// <remarks>
    /// The window is filtered here rather than trusted to have been filtered,
    /// for the reason the year's fold filters its own: a caller handing over a
    /// wider read must not widen the answer silently.
    /// </remarks>
    /// <param name="rollups">The rollups.</param>
    /// <param name="firstDay">The first day of the window.</param>
    /// <param name="dayAfter">The first day after it.</param>
    /// <returns>The totals.</returns>
    private static Totals FromTheRollups(
        IReadOnlyList<DailyRollup> rollups,
        DateOnly firstDay,
        DateOnly dayAfter)
    {
        var totals = new Totals();

        foreach (var rollup in rollups)
        {
            if (rollup.Day < firstDay || rollup.Day >= dayAfter)
            {
                continue;
            }

            totals.Plays += rollup.Plays;
            totals.Finished += rollup.Completed;
            totals.Watched += rollup.Watched;
            totals.OnDay(rollup.Day, rollup.Watched);
        }

        return totals;
    }

    /// <summary>
    /// The same totals off the play rows, for a store whose rollups cannot be
    /// used.
    /// </summary>
    /// <remarks>
    /// The day a row belongs to is read in the zone the window is read in, which
    /// is what makes this fold and the rollup fold answer the same question. A
    /// row read in UTC would land on the wrong side of a local midnight and the
    /// series would come out a day out of step with the one beside it.
    /// </remarks>
    /// <param name="rows">The rows.</param>
    /// <param name="zone">The zone the window is read in.</param>
    /// <param name="firstDay">The first day of the window.</param>
    /// <param name="dayAfter">The first day after it.</param>
    /// <returns>The totals.</returns>
    private static Totals FromTheRows(
        IReadOnlyList<PlayRecord> rows,
        TimeZoneInfo zone,
        DateOnly firstDay,
        DateOnly dayAfter)
    {
        var totals = new Totals();

        foreach (var play in rows)
        {
            var day = LocalDay.Of(
                new DateTimeOffset(DateTime.SpecifyKind(play.StartedUtc, DateTimeKind.Utc)),
                zone);

            if (day < firstDay || day >= dayAfter)
            {
                continue;
            }

            totals.Plays++;
            totals.Watched += play.WatchedDuration;
            totals.OnDay(day, play.WatchedDuration);

            if (play.ReachedTheEnd)
            {
                totals.Finished++;
            }
        }

        return totals;
    }

    /// <summary>
    /// What this account watched most over the window.
    /// </summary>
    /// <remarks>
    /// The identifier breaks a tie, so two items watched for the same time come
    /// back in the same order on every run and on every machine rather than in
    /// the order the store happened to hand them over.
    /// </remarks>
    /// <param name="rows">The rows.</param>
    /// <param name="topCount">How many rows the list may hold.</param>
    /// <returns>The list.</returns>
    private static List<TitleRow> TopOf(IReadOnlyList<PlayRecord> rows, int topCount)
    {
        var tallies = new Dictionary<Guid, Tally>();

        foreach (var play in rows)
        {
            if (!tallies.TryGetValue(play.ItemId, out var tally))
            {
                tally = new Tally(play.ItemName);
                tallies[play.ItemId] = tally;
            }

            tally.Add(play.WatchedDuration);
        }

        var ranked = new List<TitleRow>(tallies.Count);

        foreach (var pair in tallies)
        {
            ranked.Add(new TitleRow(pair.Key, pair.Value.Name, pair.Value.Plays, pair.Value.Watched));
        }

        ranked.Sort(static (left, right) =>
        {
            var byWatched = right.Watched.CompareTo(left.Watched);

            return byWatched != 0 ? byWatched : left.Key.CompareTo(right.Key);
        });

        return ranked.Count > topCount ? ranked.GetRange(0, topCount) : ranked;
    }

    /// <summary>
    /// The window divided into the parts it is grouped by, every part present.
    /// </summary>
    /// <remarks>
    /// The parts are walked from the window's own boundaries rather than read
    /// off what was recorded, so a stretch nothing happened in is a part reading
    /// nought instead of a gap. A series built from the parts that exist draws a
    /// shorter window than the one that was asked for, and a reader has no way
    /// to see that it did.
    /// </remarks>
    /// <param name="grouping">How the window is divided.</param>
    /// <param name="firstDay">The first day of the window.</param>
    /// <param name="dayAfter">The first day after the window.</param>
    /// <param name="perDay">What was watched on each day that has any.</param>
    /// <returns>The parts.</returns>
    private static IReadOnlyList<UsagePoint> PointsOf(
        PersonalWindow grouping,
        DateOnly firstDay,
        DateOnly dayAfter,
        Dictionary<DateOnly, TimeSpan> perDay)
    {
        if (grouping == PersonalWindow.AllTime)
        {
            return Array.Empty<UsagePoint>();
        }

        var points = new List<UsagePoint>();

        if (grouping == PersonalWindow.Last30Days)
        {
            for (var day = firstDay; day < dayAfter; day = day.AddDays(1))
            {
                perDay.TryGetValue(day, out var watched);
                points.Add(new UsagePoint(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), watched));
            }

            return points;
        }

        var month = new DateOnly(firstDay.Year, firstDay.Month, 1);

        // The last part is the month the window's LAST DAY falls in, and that
        // day is the one before dayAfter. Walking to the month dayAfter itself
        // falls in drops the current month whenever the window ends mid-month,
        // which is every day but one.
        var lastDay = dayAfter.AddDays(-1);
        var monthAfter = new DateOnly(lastDay.Year, lastDay.Month, 1).AddMonths(1);

        while (month < monthAfter)
        {
            var next = month.AddMonths(1);
            var watched = TimeSpan.Zero;

            foreach (var pair in perDay)
            {
                if (pair.Key >= month && pair.Key < next)
                {
                    watched += pair.Value;
                }
            }

            points.Add(new UsagePoint(month.ToString("yyyy-MM", CultureInfo.InvariantCulture), watched));
            month = next;
        }

        return points;
    }

    /// <summary>
    /// What a window comes to, whichever source it was folded from.
    /// </summary>
    private sealed class Totals
    {
        public long Plays { get; set; }

        public long Finished { get; set; }

        public TimeSpan Watched { get; set; }

        public Dictionary<DateOnly, TimeSpan> PerDay { get; } = new();

        public void OnDay(DateOnly day, TimeSpan watched)
        {
            PerDay.TryGetValue(day, out var already);
            PerDay[day] = already + watched;
        }
    }

    /// <summary>
    /// One item's running total while the top list is folded.
    /// </summary>
    private sealed class Tally
    {
        public Tally(string? name) => Name = Reported(name);

        public string? Name { get; }

        public long Plays { get; private set; }

        public TimeSpan Watched { get; private set; }

        public void Add(TimeSpan watched)
        {
            Plays++;
            Watched += watched;
        }

        private static string? Reported(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
