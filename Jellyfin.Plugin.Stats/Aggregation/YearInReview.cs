using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// One person's calendar year, folded from their own plays and read in one
/// zone.
/// </summary>
/// <remarks>
/// This is the report a person is most likely to open and the one where a
/// mistake is most visible, because people remember their own year. Everything
/// here is therefore derived from the rows and never from a clock, a setting or
/// a second definition of a figure that already exists: the days come from
/// <see cref="DailyUsage"/>, the delivery figures from
/// <see cref="DeliveryMethodShares"/>, and the month is the days added up. A
/// wrap-up that disagreed with the daily chart beside it would be two answers
/// about one year with nothing saying which to believe.
/// <para>
/// The user is an argument and the fold does the filtering itself. A sequence
/// carrying somebody else's rows is the ordinary case, because the reads that
/// exist walk a store rather than answering a question, and a computation that
/// trusted its caller to have filtered would be one careless call away from
/// telling a person about a year that was not theirs.
/// </para>
/// <para>
/// A year with nothing in it says so rather than answering with noughts.
/// <see cref="AnythingRecorded"/> is false and every figure is absent, because
/// a wrap-up showing nought plays, nought minutes and no busiest day reads as a
/// year somebody spent not watching anything, and the year a retention sweep
/// emptied, a year before the plugin was installed and a genuinely quiet year
/// are three different statements. This is the same decision the page module
/// takes for a single figure, where a value that is not known is written as
/// null and never as zero.
/// </para>
/// <para>
/// The zone travels with the answer. Which year a play falls in depends on
/// whose midnight is meant at both ends of it, so a wrap-up is not readable
/// without the zone that produced it, and a page that states a zone it was not
/// given is quoting a setting rather than saying anything about the numbers it
/// drew.
/// </para>
/// </remarks>
public sealed record YearInReview
{
    private YearInReview(int year, string zoneId)
    {
        Year = year;
        ZoneId = zoneId;
        AnythingRecorded = false;
        TopItems = Array.Empty<TitleRow>();
        TopSeries = Array.Empty<TitleRow>();
    }

    private YearInReview(
        int year,
        string zoneId,
        long plays,
        TimeSpan watched,
        long distinctItems,
        TimeSpan longestPlay,
        DailyUsageRow busiestDay,
        MonthlyUsageRow busiestMonth,
        long finished,
        long abandoned,
        DeliveryMethodShares delivery,
        IReadOnlyList<TitleRow> topItems,
        IReadOnlyList<TitleRow> topSeries)
    {
        Year = year;
        ZoneId = zoneId;
        AnythingRecorded = true;
        Plays = plays;
        Watched = watched;
        DistinctItems = distinctItems;
        LongestPlay = longestPlay;
        BusiestDay = busiestDay;
        BusiestMonth = busiestMonth;
        Finished = finished;
        Abandoned = abandoned;
        Delivery = delivery;
        TopItems = topItems;
        TopSeries = topSeries;
    }

    /// <summary>
    /// Gets the calendar year this is about.
    /// </summary>
    public int Year { get; }

    /// <summary>
    /// Gets the identifier of the zone the year's boundaries and its days were
    /// read in, as the zone handed in calls itself.
    /// </summary>
    public string ZoneId { get; }

    /// <summary>
    /// Gets a value indicating whether that person had any plays in that year.
    /// Where it is false every figure below is absent and both lists are empty,
    /// and that is what separates a year with nothing in it from a year of
    /// noughts.
    /// </summary>
    public bool AnythingRecorded { get; }

    /// <summary>
    /// Gets how many of their plays fell in the year, counted as they arrived.
    /// </summary>
    public long? Plays { get; }

    /// <summary>
    /// Gets how much they actually watched over the year. It is what the rows
    /// recorded as watched and not the time their sessions were open: a play
    /// that is paused runs on the clock and not on the item.
    /// </summary>
    public TimeSpan? Watched { get; }

    /// <summary>
    /// Gets how many different items they played, counted by identifier rather
    /// than by name so that two items sharing a name are two and one item
    /// renamed mid-year is one.
    /// </summary>
    public long? DistinctItems { get; }

    /// <summary>
    /// Gets the longest single play of the year, measured by what was watched
    /// rather than by how long the session was open.
    /// </summary>
    public TimeSpan? LongestPlay { get; }

    /// <summary>
    /// Gets the day they watched most on, and the earliest of them where two
    /// days tie. It is a row of the same fold the daily chart is drawn from, so
    /// the wrap-up and that chart cannot disagree about where a day ends.
    /// </summary>
    public DailyUsageRow? BusiestDay { get; }

    /// <summary>
    /// Gets the month they watched most in, and the earliest of them where two
    /// months tie. It is the days added up rather than a second fold over the
    /// plays, for the reason <see cref="MonthlyUsageRow"/> gives.
    /// </summary>
    public MonthlyUsageRow? BusiestMonth { get; }

    /// <summary>
    /// Gets how many of the year's plays reached the end of the item.
    /// </summary>
    public long? Finished { get; }

    /// <summary>
    /// Gets how many did not. The two add up to <see cref="Plays"/>, and that
    /// they do is the property this split is held to rather than a fact about
    /// how it is written.
    /// </summary>
    public long? Abandoned { get; }

    /// <summary>
    /// Gets how the year's plays divide between the ways the server delivered
    /// them, which is where the share it had to transcode is read. It is the
    /// same four figures every other view answers with, so a person's
    /// transcoded share and the server's are read the same way rather than
    /// being two definitions that drift.
    /// </summary>
    public DeliveryMethodShares? Delivery { get; }

    /// <summary>
    /// Gets the items they watched most of, most watched time first and items
    /// with equal time in the order their identifiers sort in, at most as many
    /// as were asked for.
    /// </summary>
    /// <remarks>
    /// Ordered by watched time and carrying the play count as well, because the
    /// two orderings disagree and a reader that wants the other one should not
    /// need a second fold to get it. Offering both as lists of their own is a
    /// condition of the top lists issue and not of this one.
    /// </remarks>
    public IReadOnlyList<TitleRow> TopItems { get; }

    /// <summary>
    /// Gets the series they watched most of, in the same order and under the
    /// same bound, counting every play of an episode under the series it
    /// belongs to.
    /// </summary>
    /// <remarks>
    /// Every row here carries no name. A play keeps the name the item had at
    /// the time and no name for its parent, and this plugin holds no route to
    /// ask the library for one, so a series can be counted and cannot be
    /// labelled. It is left absent rather than filled with the name of one of
    /// the episodes under it, which would read as a series called after
    /// whichever episode happened to be folded first.
    /// </remarks>
    public IReadOnlyList<TitleRow> TopSeries { get; }

    /// <summary>
    /// Folds one person's plays for one calendar year into their wrap-up.
    /// </summary>
    /// <param name="plays">
    /// The plays to fold. They may belong to anybody and to any year; the ones
    /// counted are chosen here rather than by whoever produced the sequence.
    /// </param>
    /// <param name="userId">Whose year this is.</param>
    /// <param name="year">The calendar year, read in the zone below.</param>
    /// <param name="zone">The zone the year's days and its boundaries are read in.</param>
    /// <param name="topCount">How many rows each top list may hold.</param>
    /// <returns>The year, or an answer saying there was nothing in it.</returns>
    /// <exception cref="ArgumentException">A play carries a start that is not in UTC.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The top list bound is not a positive number.</exception>
    public static YearInReview Over(
        IEnumerable<PlayRecord> plays,
        Guid userId,
        int year,
        TimeZoneInfo zone,
        int topCount)
    {
        ArgumentNullException.ThrowIfNull(plays);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topCount);

        var theirs = new List<PlayRecord>();
        var items = new Dictionary<Guid, Tally>();
        var series = new Dictionary<Guid, Tally>();
        var watched = TimeSpan.Zero;
        var longest = TimeSpan.Zero;
        long finished = 0;
        long abandoned = 0;

        foreach (var play in plays)
        {
            if (play.UserId != userId)
            {
                continue;
            }

            if (LocalDay.Of(StartOf(play), zone).Year != year)
            {
                continue;
            }

            theirs.Add(play);
            watched += play.WatchedDuration;

            if (play.WatchedDuration > longest)
            {
                longest = play.WatchedDuration;
            }

            if (play.ReachedTheEnd)
            {
                finished++;
            }
            else
            {
                abandoned++;
            }

            var item = Under(items, play.ItemId);
            item.Add(play.WatchedDuration);
            item.NameItAs(Reported(play.ItemName), play.StartedUtc);

            if (play.ParentId is Guid parent)
            {
                Under(series, parent).Add(play.WatchedDuration);
            }
        }

        if (theirs.Count == 0)
        {
            return new YearInReview(year, zone.Id);
        }

        var days = DailyUsage.Over(theirs, zone);

        return new YearInReview(
            year,
            zone.Id,
            theirs.Count,
            watched,
            items.Count,
            longest,
            BusiestOf(days.Rows, static row => row.Watched),
            BusiestOf(MonthsOf(days.Rows), static row => row.Watched),
            finished,
            abandoned,
            DeliveryMethodShares.Over(theirs),
            TopOf(items, topCount),
            TopOf(series, topCount));
    }

    /// <summary>
    /// The days added up into the months they fell in, earliest month first.
    /// </summary>
    private static List<MonthlyUsageRow> MonthsOf(IReadOnlyList<DailyUsageRow> days)
    {
        var tallies = new Dictionary<int, Tally>();
        foreach (var day in days)
        {
            Under(tallies, day.Day.Month).Add(day.Watched, day.Delivery.Plays);
        }

        var months = new List<MonthlyUsageRow>(tallies.Count);
        foreach (var pair in tallies)
        {
            months.Add(new MonthlyUsageRow(pair.Key, pair.Value.Watched, pair.Value.Plays));
        }

        months.Sort(static (left, right) => left.Month.CompareTo(right.Month));

        return months;
    }

    /// <summary>
    /// The row with the most watched time, and the first of them where two tie.
    /// The sequences handed in ascend through the year, so the first of a tie is
    /// the earlier one, which is the reading a person expects of "the day I
    /// watched most" when two of them are level.
    /// </summary>
    private static TRow BusiestOf<TRow>(IReadOnlyList<TRow> rows, Func<TRow, TimeSpan> watchedOn)
    {
        var busiest = rows[0];
        for (var i = 1; i < rows.Count; i++)
        {
            if (watchedOn(rows[i]) > watchedOn(busiest))
            {
                busiest = rows[i];
            }
        }

        return busiest;
    }

    /// <summary>
    /// A top list out of what was tallied, longest watched first and equal
    /// times in the order the identifiers sort in, cut to the bound asked for.
    /// </summary>
    /// <remarks>
    /// The order is decided here rather than by the order the plays arrived in,
    /// so a list does not move when a query is answered by a different plan. The
    /// tie is broken on the identifier and never left to the dictionary, because
    /// an unstable order between two items with the same watched time is a list
    /// that changes between two readings of the same year.
    /// </remarks>
    private static List<TitleRow> TopOf(Dictionary<Guid, Tally> tallies, int topCount)
    {
        var rows = new List<TitleRow>(tallies.Count);
        foreach (var pair in tallies)
        {
            rows.Add(new TitleRow(pair.Key, pair.Value.Name, pair.Value.Plays, pair.Value.Watched));
        }

        rows.Sort(static (left, right) =>
        {
            var byWatched = right.Watched.CompareTo(left.Watched);

            return byWatched != 0 ? byWatched : left.Key.CompareTo(right.Key);
        });

        return rows.Count <= topCount ? rows : rows.GetRange(0, topCount);
    }

    /// <summary>
    /// The tally for a key, created on first sight of it.
    /// </summary>
    private static Tally Under<TKey>(Dictionary<TKey, Tally> tallies, TKey key)
        where TKey : notnull
    {
        if (!tallies.TryGetValue(key, out var tally))
        {
            tally = new Tally();
            tallies[key] = tally;
        }

        return tally;
    }

    /// <summary>
    /// What the row actually named, or nothing where it named nothing.
    /// Whitespace counts as nothing, the same way a breakdown reads a client
    /// name: a label made only of spaces is one a reader cannot see and cannot
    /// tell from an absent one.
    /// </summary>
    private static string? Reported(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// The moment a play started, and a refusal where the row says that field is
    /// not what every row promises.
    /// </summary>
    /// <remarks>
    /// A start that is not in UTC would be read as a local time on whichever
    /// machine did the reading, which moves the play by that machine's offset.
    /// On a year that is a play falling out of the wrap-up at one end and
    /// another falling into it at the other, and nothing downstream can tell
    /// that from the right answer, because both years are real years and both
    /// still add up. It is refused rather than corrected for the reason the
    /// daily fold gives at the same field: correcting it would mean deciding
    /// that a row saying local means UTC, and a row whose start really was a
    /// local time would then be moved twice.
    /// </remarks>
    private static DateTimeOffset StartOf(PlayRecord play)
    {
        if (play.StartedUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "A play carries a start that is {0} and every row keeps its start in UTC. Reading it here would move the play by the reader's offset and put it in a year it did not fall in.",
                    play.StartedUtc.Kind),
                nameof(play));
        }

        return new DateTimeOffset(play.StartedUtc);
    }

    /// <summary>
    /// What is being accumulated under one key while the plays are walked. It is
    /// mutable and private, and nothing mutable leaves this class: every figure
    /// above is copied out of one of these into a record once the walk is over.
    /// </summary>
    private sealed class Tally
    {
        /// <summary>
        /// Gets the label the latest row under this key gave it, and null where
        /// no row gave one.
        /// </summary>
        public string? Name { get; private set; }

        /// <summary>
        /// Gets how many plays fell under this key.
        /// </summary>
        public long Plays { get; private set; }

        /// <summary>
        /// Gets how much was watched under it.
        /// </summary>
        public TimeSpan Watched { get; private set; }

        private DateTime NamedAt { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Counts one play, or one already-folded day.
        /// </summary>
        public void Add(TimeSpan watched, long plays = 1)
        {
            Plays += plays;
            Watched += watched;
        }

        /// <summary>
        /// Takes the label from this row if no later row has given one.
        /// </summary>
        /// <remarks>
        /// The latest name wins, which is the rule a breakdown by device already
        /// follows: an item renamed halfway through the year is one item, and
        /// showing the older name calls it something nobody sees on their server
        /// any more. The comparison is on the row's own start and never on the
        /// order the rows arrived in, so a sequence a store handed back in a
        /// different order gives the same label.
        /// </remarks>
        public void NameItAs(string? name, DateTime reportedAt)
        {
            if (reportedAt >= NamedAt)
            {
                Name = name;
                NamedAt = reportedAt;
            }
        }
    }
}
