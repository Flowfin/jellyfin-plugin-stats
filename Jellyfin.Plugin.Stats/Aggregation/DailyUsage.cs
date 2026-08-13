using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// How a set of plays divides over the days it fell on, as one row per day that
/// had any, read in one zone.
/// </summary>
/// <remarks>
/// A play counts on the day it started. It is not spread across the days it
/// touched, because the figure being spread would be the watched duration and
/// nothing on the row says when within the play the watching happened: a film
/// started before midnight and paused for an hour has a watched duration that
/// no rule could divide between the two days without inventing one.
/// <para>
/// Days that had no plays are not rows. This fold is handed a sequence and not
/// a range, so it cannot tell a day inside the range that nobody watched
/// anything on from a day the retention sweep emptied or a day the range never
/// covered. Writing nought for all three collapses them before a page can draw
/// them apart, which is the distinction the page module already holds on its
/// side, where a value that is not known is written as null and never as zero.
/// Whoever chose the range knows which days it covers and is the one that can
/// say which of the three a gap is.
/// </para>
/// <para>
/// The zone travels with the answer. Which day a play falls on depends on whose
/// midnight is meant, so a series of days is not readable without the zone that
/// produced it, and a page that states a zone it was not given is quoting a
/// setting rather than saying anything about the numbers it drew.
/// </para>
/// <para>
/// Nothing here divides and nothing here reads a clock or a setting. The zone
/// arrives as an argument, the same way <see cref="LocalDay"/> takes one at
/// every call, so the same sequence gives the same answer on any machine.
/// </para>
/// </remarks>
public sealed record DailyUsage
{
    private DailyUsage(IReadOnlyList<DailyUsageRow> rows, long plays, TimeSpan watched, string zoneId)
    {
        Rows = rows;
        Plays = plays;
        Watched = watched;
        ZoneId = zoneId;
    }

    /// <summary>
    /// Gets the days that had plays, earliest first. The order is decided here
    /// rather than by the order the plays arrived in, so a chart drawn from this
    /// reads left to right along time however a query's plan produced its rows.
    /// </summary>
    public IReadOnlyList<DailyUsageRow> Rows { get; }

    /// <summary>
    /// Gets how many plays were folded, counted as they arrived. The rows above
    /// add up to this, and that they do is the property this type exists to hold
    /// rather than a fact about how it is written.
    /// </summary>
    public long Plays { get; }

    /// <summary>
    /// Gets how much was watched over all of them, accumulated as they arrived.
    /// The rows add up to this for the same reason and with the same value.
    /// </summary>
    public TimeSpan Watched { get; }

    /// <summary>
    /// Gets the identifier of the zone the days were read in, as the zone handed
    /// in calls itself.
    /// </summary>
    public string ZoneId { get; }

    /// <summary>
    /// Folds a sequence of plays into one row per day they fell on.
    /// </summary>
    /// <param name="plays">The plays to fold. The range they belong to is chosen before they get here.</param>
    /// <param name="zone">The zone the days are read in.</param>
    /// <returns>The rows, the figures they add up to, and the zone they were read in.</returns>
    public static DailyUsage Over(IEnumerable<PlayRecord> plays, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(plays);
        ArgumentNullException.ThrowIfNull(zone);

        var grouped = new Dictionary<DateOnly, List<PlayRecord>>();
        long folded = 0;
        var watched = TimeSpan.Zero;

        foreach (var play in plays)
        {
            folded++;
            watched += play.WatchedDuration;

            var day = LocalDay.Of(StartOf(play), zone);

            if (!grouped.TryGetValue(day, out var onThisDay))
            {
                onThisDay = new List<PlayRecord>();
                grouped[day] = onThisDay;
            }

            onThisDay.Add(play);
        }

        var rows = new List<DailyUsageRow>(grouped.Count);
        foreach (var pair in grouped)
        {
            var watchedOnThisDay = TimeSpan.Zero;
            foreach (var play in pair.Value)
            {
                watchedOnThisDay += play.WatchedDuration;
            }

            rows.Add(new DailyUsageRow(pair.Key, watchedOnThisDay, DeliveryMethodShares.Over(pair.Value)));
        }

        rows.Sort(static (left, right) => left.Day.CompareTo(right.Day));

        return new DailyUsage(rows, folded, watched, zone.Id);
    }

    /// <summary>
    /// The moment a play started, as a moment rather than as a reading whose
    /// meaning depends on a flag, and a refusal where the flag says the field is
    /// not what the row promises.
    /// </summary>
    /// <remarks>
    /// A start that is not in UTC would be read as a local time on whichever
    /// machine did the reading, which moves the play by that machine's offset
    /// and puts it on a day it did not fall on. That is the machine zone this
    /// namespace takes an argument to avoid, and it arrives silently: the day is
    /// a real day and the totals still add up, so nothing downstream can tell it
    /// from the right answer.
    /// <para>
    /// It is refused rather than corrected, and the store makes the same choice
    /// at the other end of the same field, where a timestamp that is not UTC is
    /// refused at the write rather than repaired at the read. Correcting it here
    /// would mean deciding that a row saying local means UTC, and a row whose
    /// start really was a local time would then be moved twice.
    /// </para>
    /// </remarks>
    /// <param name="play">The play whose start is being read.</param>
    /// <returns>The moment it started.</returns>
    private static DateTimeOffset StartOf(PlayRecord play)
    {
        if (play.StartedUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "A play carries a start that is {0} and every row keeps its start in UTC. Reading it here would move the play by the reader's offset and put it on a day it did not fall on.",
                    play.StartedUtc.Kind),
                nameof(play));
        }

        return new DateTimeOffset(play.StartedUtc);
    }
}
