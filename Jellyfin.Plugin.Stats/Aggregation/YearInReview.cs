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
/// <para>
/// So does the window it was folded over. Play rows are deleted after ninety
/// days by default, so the ordinary wrap-up on an ordinary server is computed
/// over a quarter of the year it is named after, and the figures cannot say so
/// on their own: they are right about the rows that were there. <see
/// cref="Coverage"/> is the sentence that says which days those were, and no
/// figure here is ever scaled up from it to what a whole year might have held.
/// </para>
/// </remarks>
public sealed record YearInReview
{
    private readonly IReadOnlyList<TitleRow> _rankedItems;
    private readonly IReadOnlyList<TitleRow> _rankedSeries;
    private readonly int _topCount;

    private YearInReview(int year, string zoneId, YearCoverage coverage)
    {
        Year = year;
        ZoneId = zoneId;
        Coverage = coverage;
        AnythingRecorded = false;
        TopItems = Array.Empty<TitleRow>();
        TopSeries = Array.Empty<TitleRow>();
        _rankedItems = Array.Empty<TitleRow>();
        _rankedSeries = Array.Empty<TitleRow>();
        _topCount = 0;
    }

    private YearInReview(
        int year,
        string zoneId,
        YearCoverage coverage,
        long plays,
        TimeSpan watched,
        long distinctItems,
        TimeSpan longestPlay,
        DailyUsageRow busiestDay,
        MonthlyUsageRow busiestMonth,
        long finished,
        long abandoned,
        DeliveryMethodShares delivery,
        IReadOnlyList<TitleRow> rankedItems,
        IReadOnlyList<TitleRow> rankedSeries,
        int topCount)
    {
        Year = year;
        ZoneId = zoneId;
        Coverage = coverage;
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
        _rankedItems = rankedItems;
        _rankedSeries = rankedSeries;
        _topCount = topCount;
        TopItems = FirstOf(rankedItems, topCount);
        TopSeries = FirstOf(rankedSeries, topCount);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="YearInReview"/> class from
    /// an already-folded year, with its two lists taken again.
    /// </summary>
    /// <remarks>
    /// This is what reading a folded year for a particular account produces.
    /// Every figure is copied rather than recomputed. Access decides which rows
    /// a top list carries and it decides nothing else, so a total taken from
    /// this copy and a total taken from the fold it came from are the same
    /// number by construction rather than by two walks agreeing.
    /// </remarks>
    private YearInReview(YearInReview folded, IReadOnlyList<TitleRow> topItems, IReadOnlyList<TitleRow> topSeries)
    {
        Year = folded.Year;
        ZoneId = folded.ZoneId;
        Coverage = folded.Coverage;
        AnythingRecorded = folded.AnythingRecorded;
        Plays = folded.Plays;
        Watched = folded.Watched;
        DistinctItems = folded.DistinctItems;
        LongestPlay = folded.LongestPlay;
        BusiestDay = folded.BusiestDay;
        BusiestMonth = folded.BusiestMonth;
        Finished = folded.Finished;
        Abandoned = folded.Abandoned;
        Delivery = folded.Delivery;
        _rankedItems = folded._rankedItems;
        _rankedSeries = folded._rankedSeries;
        _topCount = folded._topCount;
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
    /// Gets which days of the year the store could still answer for when this
    /// was folded, and the earliest day of it this person has a play on.
    /// </summary>
    /// <remarks>
    /// Read this before the figures. Retention deletes play rows after ninety
    /// days by default, so on an ordinary server the figures below are computed
    /// over the part of the year that survived rather than over the year, and
    /// nothing in them says so: they are correct arithmetic over real plays and
    /// the only thing wrong with them is the heading. Every figure is what the
    /// surviving rows recorded and none of them is scaled up to what a whole
    /// year might have held, which is the second condition of issue #69.
    /// </remarks>
    public YearCoverage Coverage { get; }

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
    /// <para>
    /// Straight out of the fold this is every item, cut to the bound and asked
    /// about nobody. <see cref="SeenBy"/> is what makes it a list for a
    /// particular account, and a caller that serves this to a person without
    /// going through it is serving a list nothing has checked. Issue #54.
    /// </para>
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
    /// <para>
    /// It is cut by <see cref="SeenBy"/> on the same rule as the item list,
    /// although no name is being withheld today. A row here is still a
    /// statement that this account watched something under a parent, and the
    /// day a stored series name arrives on the row this list starts printing
    /// one; a filter written for items alone would leak on the change that
    /// added the label rather than on the change that added the filter.
    /// </para>
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
    /// <param name="oldestPlayStartedUtc">
    /// When the oldest row anywhere in the store started, in UTC, or null where
    /// the store holds none. It is what the answer's window is read from, and
    /// it is the store's reading over every account rather than this person's
    /// earliest play: somebody who first watched in September has a September
    /// row on a store going back to January, and a window read off their own
    /// rows would report a quiet start of a year as a retention cut. A caller
    /// with nothing to say about the store passes null, and the answer then
    /// says it covers no part of the year rather than claiming the whole of it.
    /// </param>
    /// <returns>The year, or an answer saying there was nothing in it.</returns>
    /// <exception cref="ArgumentException">A play carries a start that is not in UTC, or the oldest stored start is not.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The top list bound is not a positive number.</exception>
    public static YearInReview Over(
        IEnumerable<PlayRecord> plays,
        Guid userId,
        int year,
        TimeZoneInfo zone,
        int topCount,
        DateTime? oldestPlayStartedUtc)
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
            return new YearInReview(
                year,
                zone.Id,
                YearCoverage.Of(year, oldestPlayStartedUtc, zone, earliestPlay: null));
        }

        var days = DailyUsage.Over(theirs, zone);

        // The days ascend, so the first of them is the earliest day of the year
        // this person has a row on. Taken from the fold rather than from the
        // sequence, because the sequence arrives in whatever order a store
        // handed it back.
        var earliest = days.Rows[0].Day;

        return new YearInReview(
            year,
            zone.Id,
            YearCoverage.Of(year, oldestPlayStartedUtc, zone, earliest),
            theirs.Count,
            watched,
            items.Count,
            longest,
            BusiestOf(days.Rows, static row => row.Watched),
            BusiestOf(MonthsOf(days.Rows), static row => row.Watched),
            finished,
            abandoned,
            DeliveryMethodShares.Over(theirs),
            RankedOf(items),
            RankedOf(series),
            topCount);
    }

    /// <summary>
    /// The same year read for one account, with the top lists carrying only the
    /// rows that account may be shown.
    /// </summary>
    /// <remarks>
    /// The cut happens here rather than in the fold, and that is the whole of
    /// why the fold keeps its lists whole. Access is a fact about now and a
    /// folded year is kept between requests, so an answer that had filtered on
    /// the way in would go on handing out an item this account lost access to
    /// months ago and would leave out one they have since been given, with
    /// nothing to make it let go: no row moved, so nothing tells the hold that
    /// the answer is stale. Asking per request costs a bounded number of
    /// library questions and cannot be stale by more than the request.
    /// <para>
    /// Only the lists move. Every total is what it was, so a play of an item
    /// this account may not see is still counted in
    /// <see cref="Plays"/>, <see cref="Watched"/> and
    /// <see cref="DistinctItems"/>, and only its name is withheld. That is the
    /// sentence issue #54 is written on: a report that dropped the play from
    /// its totals as well would be answering a different question, and the
    /// difference between the total and the list is not something an account
    /// can turn back into a name.
    /// </para>
    /// <para>
    /// A year with nothing in it comes back unchanged. There is no list to cut
    /// and no item to ask about, and building a copy of it would be a second
    /// object saying exactly what the first one says.
    /// </para>
    /// </remarks>
    /// <param name="userId">The account this year is being read for.</param>
    /// <param name="access">What the library says about which items that account may see.</param>
    /// <returns>The year as that account may be shown it.</returns>
    public YearInReview SeenBy(Guid userId, IItemAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);

        if (!AnythingRecorded)
        {
            return this;
        }

        return new YearInReview(
            this,
            Shown(_rankedItems, userId, access, _topCount),
            Shown(_rankedSeries, userId, access, _topCount));
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
    /// Everything that was tallied as a ranked list, longest watched first and
    /// equal times in the order the identifiers sort in, cut to nothing.
    /// </summary>
    /// <remarks>
    /// The order is decided here rather than by the order the plays arrived in,
    /// so a list does not move when a query is answered by a different plan. The
    /// tie is broken on the identifier and never left to the dictionary, because
    /// an unstable order between two items with the same watched time is a list
    /// that changes between two readings of the same year.
    /// <para>
    /// It is kept whole rather than cut to the bound, and that is what makes
    /// <see cref="SeenBy"/> able to hand back a full list. A fold that cut
    /// first would leave a caller who may not see two of their own top ten
    /// with eight rows instead of ten with the next two moved up, and the eight
    /// would be a correct answer to a question nobody asked. Issue #54.
    /// </para>
    /// </remarks>
    private static List<TitleRow> RankedOf(Dictionary<Guid, Tally> tallies)
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

        return rows;
    }

    /// <summary>
    /// The head of a ranked list, or the whole of it where it is no longer than
    /// the bound.
    /// </summary>
    private static IReadOnlyList<TitleRow> FirstOf(IReadOnlyList<TitleRow> ranked, int topCount)
    {
        if (ranked.Count <= topCount)
        {
            return ranked;
        }

        var head = new List<TitleRow>(topCount);
        for (var i = 0; i < topCount; i++)
        {
            head.Add(ranked[i]);
        }

        return head;
    }

    /// <summary>
    /// The rows of one ranked list this account may be shown, filled up to the
    /// bound.
    /// </summary>
    /// <remarks>
    /// The walk stops as soon as the list is full, which is what bounds how
    /// often the library is asked. A year of two thousand distinct items costs
    /// the bound plus however many rows above the cut this account may not see,
    /// rather than two thousand questions per request.
    /// </remarks>
    private static List<TitleRow> Shown(
        IReadOnlyList<TitleRow> ranked,
        Guid userId,
        IItemAccess access,
        int topCount)
    {
        var shown = new List<TitleRow>(topCount);

        for (var i = 0; i < ranked.Count && shown.Count < topCount; i++)
        {
            // False is the only answer that drops a row. Null is the library
            // holding no such item, which is a play of something that has since
            // been deleted rather than something this account may not see, and
            // it is named out of the row the way every other label here is.
            if (access.MaySee(userId, ranked[i].Key) != false)
            {
                shown.Add(ranked[i]);
            }
        }

        return shown;
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
