using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Reports;

/// <summary>
/// The five shapes every aggregate report in this plugin is built out of, and
/// the only route a report has to the plays.
/// </summary>
/// <remarks>
/// A total over a range, a series over days, a distribution over the hours of a
/// week, a breakdown by a named dimension, and a top list. Every report in the
/// dashboard milestone is one or more of those, which was settled on issue #51
/// against the open issues of that milestone rather than assumed here.
/// <para>
/// THE POINT IS THAT THE RULES ARE APPLIED ONCE. A report that reached the store
/// itself would have to re-establish the bound, the range and the privacy rule,
/// and the report that forgot one of them would look exactly like the ones that
/// did not. So the rules live here, and reaching past this layer is refused by
/// <c>no-store-write-outside-the-write-path</c> and
/// <c>no-concrete-store-outside-the-one-function-that-opens-it</c> in
/// <c>tools/invariants/rules</c> rather than being remembered. This file is
/// spared by name in the first of those, which is what a report is not.
/// </para>
/// <para>
/// WHAT THE PRIVACY RULE COMES TO HERE HAS TWO HALVES AND THE FIRST IS BY
/// CONSTRUCTION. No shape below can name an account: none of them returns a user
/// identifier or a user name, and the set of things a breakdown may group by is
/// a closed enumeration the user is deliberately absent from, which
/// <see cref="PlayDimension"/> carries the argument for. So the answer to an
/// aggregate is the same whether an account has agreed to be named, refused, or
/// never been asked, and consent widens nothing here. That is issue #41's first
/// condition, and it is held by there being nothing to widen rather than by a
/// branch somebody has to remember.
/// </para>
/// <para>
/// THE SECOND HALF IS THE ARITHMETIC ONE AND IT IS A RULE THAT RUNS. A row
/// backed by one account names that account to anybody who knows who was
/// watching, and suppressing the row alone moves the subtraction to the total
/// rather than preventing it. So every member standing on fewer than
/// <see cref="FewestAccountsBehindARow"/> accounts is folded into one group that
/// has no name and no key, that group is held to the same threshold, and where
/// it cannot meet it the breakdown is withheld entirely. The total beside it
/// stays available either way, because a total on its own is not half of a
/// subtraction. Issue #41 is where both were decided and what each costs is
/// written there.
/// </para>
/// <para>
/// Every shape opens the store, reads what its window allows, folds it, and
/// closes the store again. Nothing here holds a connection between calls and
/// nothing caches: a sweep that ran between two reports is a report drawn from
/// what the file holds now rather than from what it held when the process
/// started.
/// </para>
/// <para>
/// WHAT A RANGE TOO LARGE TO ANSWER GETS IS A REFUSAL AND NEVER A SHORT ANSWER.
/// Two caps sit in front of every shape, both of them the plugin's and neither
/// of them the caller's: the length of the range, refused by
/// <see cref="QueryWindow"/> before a row is read, and the number of plays the
/// range holds, refused by <see cref="TooManyPlaysToAnswerException"/> once the
/// read shows there are more than the bound allows. A shape that folded what
/// fitted would hand back a report that is wrong by whatever it did not read,
/// with nothing on it saying so, and every reader downstream would take it for
/// a complete one. Issue #56.
/// </para>
/// </remarks>
public sealed class AggregateQueries
{
    /// <summary>
    /// How many distinct accounts have to stand behind a breakdown before it is
    /// answered at all.
    /// </summary>
    /// <remarks>
    /// A constant and not a setting. A setting would let an operator turn this
    /// plugin's own promise down, every case would have to pin the value before
    /// it could assert anything, and a promise that reads differently on each
    /// installation is not a promise.
    /// <para>
    /// Two is the weakest threshold that stops the subtraction at all. It does
    /// nothing against somebody who already knows who was watching, which on a
    /// household server is the administrator. Anyone wanting more pays for it in
    /// breakdowns that do not appear on small servers, and that trade is one
    /// number in one place. Issue #41.
    /// </para>
    /// </remarks>
    public const int FewestAccountsBehindARow = 2;

    /// <summary>
    /// How many rollup rows one account's year may bring back.
    /// </summary>
    /// <remarks>
    /// A rollup is one day, one kind of item and one client, so a year for one
    /// account is at most its days times the kinds it played times the clients
    /// it played them on. Three hundred and sixty-six days against a generous
    /// allowance of both is what this number is, and it is a bound rather than
    /// an expectation: a year over it is answered from the play rows instead,
    /// with the answer saying so, and never truncated to whatever fitted.
    /// </remarks>
    public const int MostRollupRowsAYearMayHold = 366 * 64;

    /// <summary>
    /// How many windows of play rows one personal figure may be read over.
    /// </summary>
    /// <remarks>
    /// The top items are the one figure a rollup cannot carry, so they read the
    /// rows, and the rows are read a month at a time under the cap every other
    /// shape reads under. Bounded reads with no bound on how many of them there
    /// are is not a bounded answer, so a window reaching further back than this
    /// says the figure was not taken rather than issuing the reads.
    /// </remarks>
    public const int MostWindowsAPersonalFigureIsReadOver = 24;

    /// <summary>
    /// How many rollup rows one personal window may bring back.
    /// </summary>
    /// <remarks>
    /// A rollup is one day, one kind of item and one client, so a window costs
    /// its days times the kinds times the clients. A window over the bound is
    /// answered as no rollups rather than as the ones that fitted: a truncated
    /// fold is a figure wrong by whatever it did not read with nothing on it
    /// saying so.
    /// </remarks>
    public const int MostRollupRowsAPersonalWindowMayHold = 366 * 2 * 64;

    private readonly Func<IPlayStore> _openStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="AggregateQueries"/> class.
    /// </summary>
    /// <param name="openStore">Opens the store. Called once per shape, and what it returns is closed before the answer comes back.</param>
    public AggregateQueries(Func<IPlayStore> openStore)
    {
        ArgumentNullException.ThrowIfNull(openStore);

        _openStore = openStore;
    }

    /// <summary>
    /// The first shape: what a range comes to.
    /// </summary>
    /// <param name="window">The range and the bound.</param>
    /// <returns>The totals, which name nobody.</returns>
    /// <exception cref="StoreCouldNotBeOpenedException">The store could not be opened.</exception>
    /// <exception cref="TooManyPlaysToAnswerException">The range holds more plays than the bound allows.</exception>
    public ServerTotals Total(QueryWindow window)
    {
        var plays = Read(window);
        var watched = TimeSpan.Zero;

        foreach (var play in plays)
        {
            watched += play.WatchedDuration;
        }

        return new ServerTotals(
            plays.Count,
            watched,
            DeliveryMethodShares.Over(plays),
            HowPlaysEnded.Over(plays));
    }

    /// <summary>
    /// The second shape: one row per day the range covers.
    /// </summary>
    /// <remarks>
    /// A day and several figures rather than a day and a number, because the
    /// dashboard's usage view draws plays, watched time and the delivery split
    /// in one picture and its own condition is that the page renders from one
    /// request. A shape answering with one figure per day would make that view
    /// three requests.
    /// <para>
    /// The zone arrives as an argument and is returned beside the answer. A
    /// calendar day has a local midnight at each end, so which day a play falls
    /// on is not a fact about the play, and a page stating a zone it was not
    /// given would be quoting a setting rather than saying anything about the
    /// numbers it drew.
    /// </para>
    /// </remarks>
    /// <param name="window">The range and the bound.</param>
    /// <param name="zone">The zone the days are read in.</param>
    /// <returns>The rows, the figures they add up to, and the zone.</returns>
    /// <exception cref="StoreCouldNotBeOpenedException">The store could not be opened.</exception>
    /// <exception cref="TooManyPlaysToAnswerException">The range holds more plays than the bound allows.</exception>
    public DailyUsage Series(QueryWindow window, TimeZoneInfo zone)
        => DailyUsage.Over(Read(window), zone);

    /// <summary>
    /// The third shape: every hour of a week, with what fell in it.
    /// </summary>
    /// <remarks>
    /// Two cyclic buckets crossed rather than two distributions side by side.
    /// The crossing is where the rows are, so an hour distribution and a weekday
    /// distribution answered separately cannot be put back together into the
    /// grid the drawing expects.
    /// </remarks>
    /// <param name="window">The range and the bound.</param>
    /// <param name="zone">The zone the hours are read in.</param>
    /// <returns>Every hour of the week, and the zone they were read in.</returns>
    /// <exception cref="StoreCouldNotBeOpenedException">The store could not be opened.</exception>
    /// <exception cref="TooManyPlaysToAnswerException">The range holds more plays than the bound allows.</exception>
    public HourAndWeekdayGrid Distribution(QueryWindow window, TimeZoneInfo zone)
        => HourAndWeekdayGrid.Over(Read(window), zone, window.FromUtc, window.ToUtc);

    /// <summary>
    /// The fourth shape: one row per member of a dimension, with the members too
    /// few accounts stand behind folded into one group that has no name, or
    /// nothing at all where even that group would name somebody.
    /// </summary>
    /// <remarks>
    /// The shape the privacy rule actually bites on. A client or a device that
    /// one account alone used is that account under another name, and the rows
    /// beside it plus the total are enough to recover what that account watched
    /// whether or not the thin row is shown. So no member standing on fewer than
    /// <see cref="FewestAccountsBehindARow"/> accounts is ever a row.
    /// <para>
    /// WHAT HAPPENS TO THOSE MEMBERS WAS DECIDED ON ISSUE #41 ON 2026-08-24 AND
    /// IS NOT WHAT THIS SHAPE USED TO DO. They fold into one group rather than
    /// the whole breakdown being withheld. Withholding it whole is safe by
    /// refusing to answer at all, which on a small server is every breakdown:
    /// three clients used by two accounts and two used by one each answered
    /// nothing, on exactly the servers a breakdown is worth having on. The fold
    /// answers the three and puts the other two together.
    /// </para>
    /// <para>
    /// THE THRESHOLD REACHES THE FOLD TOO. A group standing on one account is
    /// that account under a shorter name, so where the members that would fold
    /// come to fewer than the threshold between them there is no breakdown at
    /// all. One thin member on its own always lands there, because the accounts
    /// behind one thin member are what made it thin.
    /// </para>
    /// <para>
    /// WITHHELD IS NOT EMPTY AND THE DIFFERENCE IS THE WHOLE ANSWER. A range
    /// with no plays in it and a range whose breakdown may not be shown are
    /// different facts, and a shape returning no rows for both has destroyed the
    /// distinction before a page can draw it. Null is the second of those.
    /// </para>
    /// <para>
    /// What is counted is distinct accounts, not plays and not rows. Four
    /// hundred plays from one person are a row standing on one account.
    /// </para>
    /// </remarks>
    /// <param name="window">The range and the bound.</param>
    /// <param name="dimension">What to group by, from a closed set the user is not in.</param>
    /// <returns>The rows, the group the rest folded into, and what they add up to together, or null where the breakdown is withheld.</returns>
    /// <exception cref="StoreCouldNotBeOpenedException">The store could not be opened.</exception>
    /// <exception cref="TooManyPlaysToAnswerException">The range holds more plays than the bound allows.</exception>
    public DimensionBreakdown? Breakdown(QueryWindow window, PlayDimension dimension)
    {
        var plays = Read(window);
        var accounts = AccountsBehindEachGroup(plays, dimension);

        var folding = new List<string>();
        var behindTheFold = new HashSet<Guid>();

        foreach (var (key, behind) in accounts)
        {
            if (behind.Count >= FewestAccountsBehindARow)
            {
                continue;
            }

            folding.Add(key);
            behindTheFold.UnionWith(behind);
        }

        // The threshold reaches the group the thin members fold into, and it is
        // the same number. A group standing on one account is that account
        // under a different name, and giving it no name does not change who it
        // is about; on a server where every member is used by one person, the
        // fold would otherwise publish that person as "the rest". So where what
        // would fold comes to fewer accounts than a row needs, there is no
        // breakdown at all.
        //
        // One thin member on its own always lands here, which is why the case
        // reads as a whole withhold rather than as a group of one: a member is
        // thin because fewer than the threshold stood behind it, so the union
        // of exactly one thin member is thin by the same count.
        if (folding.Count > 0 && behindTheFold.Count < FewestAccountsBehindARow)
        {
            return null;
        }

        return DimensionBreakdown.Over(plays, dimension, folding);
    }

    /// <summary>
    /// The fourth shape again, over a dimension one play can hold several of.
    /// </summary>
    /// <remarks>
    /// A widening of the breakdown rather than a sixth shape. The plays a server
    /// transcoded carry a list of reasons rather than one, so the rows do not
    /// divide the plays and will usually add up to more than there were, and a
    /// reader adding them up against a play count beside them would conclude the
    /// plugin is wrong. The answer therefore carries both numbers: how many plays
    /// were folded, and how many of them recorded any reason at all. A shape
    /// answering with rows alone drops the difference,
    /// <c>docs/transcode-reasons.md</c> is written against exactly that reading,
    /// and it would stop being provable from a response.
    /// <para>
    /// It is a method of its own rather than a member of
    /// <see cref="PlayDimension"/>, because the answer has a different shape and
    /// not because the dimension is a different kind of thing. A row here is a
    /// reason and never an account, so the rule that withholds a breakdown has
    /// nothing to bite on: what would name somebody is a row standing on one
    /// account, and a reason is not a row about who was watching.
    /// </para>
    /// </remarks>
    /// <param name="window">The range and the bound.</param>
    /// <returns>The reasons, how many plays were folded, and how many of them recorded one.</returns>
    /// <exception cref="StoreCouldNotBeOpenedException">The store could not be opened.</exception>
    /// <exception cref="TooManyPlaysToAnswerException">The range holds more plays than the bound allows.</exception>
    public TranscodeReasonBreakdown ReasonBreakdown(QueryWindow window)
        => TranscodeReasonBreakdown.Over(Read(window));

    /// <summary>
    /// The fifth shape: what was watched most over a range, grouped as the
    /// caller asks and ordered by whichever of the two figures they asked for.
    /// </summary>
    /// <remarks>
    /// THIS PARAGRAPH SAID A TOP LIST CARRIED NO PRIVACY RULE OF ITS OWN,
    /// BECAUSE AN ITEM IS NOT AN ACCOUNT. It is the wrong half of the question,
    /// and issue #52 opens by naming it: a top list is where an aggregate view
    /// most easily stops being aggregate, because on a server with three people
    /// the most-watched item is usually a statement about one of them. What is
    /// published is not who watched, it is that somebody did, and on a small
    /// enough server those are the same sentence.
    /// <para>
    /// So the same rule the breakdown carries applies here, and it is the rule
    /// decided for the whole plugin on issue #41 rather than one invented for
    /// this shape: the list is answered only where every row it would return
    /// stands on at least <see cref="FewestAccountsBehindARow"/> distinct
    /// accounts, and is withheld whole otherwise. Reporting the group size
    /// beside the row was the option that was refused, for the reason a row
    /// reading "1 person" beside a published total names that person by
    /// arithmetic.
    /// </para>
    /// <para>
    /// WITHHELD IS NOT EMPTY, the same way it is not for a breakdown. A range
    /// with nothing in it and a range whose list may not be shown are different
    /// facts, and null is the second of them.
    /// </para>
    /// <para>
    /// Ties break on the row's identifier whichever figure was ordered on, so a
    /// list does not move between two readings of one range. Every row carries
    /// both figures either way; what the order decides is which rows survive
    /// the cut.
    /// </para>
    /// </remarks>
    /// <param name="window">The range and the bound.</param>
    /// <param name="howMany">How many rows at most.</param>
    /// <param name="grouping">Whether a row is an item or the series an episode belongs to.</param>
    /// <param name="order">Which figure decides the order and therefore the cut.</param>
    /// <returns>The rows, and null where the list is withheld.</returns>
    /// <exception cref="StoreCouldNotBeOpenedException">The store could not be opened.</exception>
    /// <exception cref="TooManyPlaysToAnswerException">The range holds more plays than the bound allows.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The bound is not a positive number, or the grouping or the order is not one this build knows.</exception>
    public IReadOnlyList<TitleRow>? Top(
        QueryWindow window,
        int howMany,
        TopListGrouping grouping = TopListGrouping.Item,
        TopListOrder order = TopListOrder.WatchedTime)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(howMany);

        // Both are read before a row is, so a value this build has no name for
        // is refused whatever the range holds. Reading them where they are used
        // would make the refusal depend on the rows: a grouping nobody knows
        // would pass over an empty range, and an order nobody knows would pass
        // over a single row, because a sort of one element never asks its
        // comparison anything. A guard that fires on some ranges and not others
        // is not a guard.
        var keyOf = KeyReaderFor(grouping);
        var comparison = ComparisonFor(order);

        var plays = Read(window);

        var tallies = new Dictionary<Guid, Tally>();
        var accounts = new Dictionary<Guid, HashSet<Guid>>();

        foreach (var play in plays)
        {
            if (keyOf(play) is not Guid key)
            {
                continue;
            }

            if (!tallies.TryGetValue(key, out var tally))
            {
                tally = new Tally(NameOf(play, grouping));
                tallies[key] = tally;
                accounts[key] = [];
            }

            tally.Add(play.WatchedDuration);
            accounts[key].Add(play.UserId);
        }

        // Every row, and not only the ones that would have survived the cut. A
        // row standing on one account is recoverable from the rows beside it
        // and the total whether or not it is shown, which is why the breakdown
        // withholds the whole answer rather than dropping the thin row, and it
        // is why the count is taken before anything is cut here.
        foreach (var behind in accounts.Values)
        {
            if (behind.Count < FewestAccountsBehindARow)
            {
                return null;
            }
        }

        var rows = new List<TitleRow>(tallies.Count);
        foreach (var (key, tally) in tallies)
        {
            rows.Add(new TitleRow(key, tally.Name, tally.Plays, tally.Watched));
        }

        rows.Sort(comparison);

        return rows.Count <= howMany ? rows : rows.GetRange(0, howMany);
    }

    /// <summary>
    /// How many distinct accounts stand behind each member of a dimension in
    /// this set of plays.
    /// </summary>
    /// <remarks>
    /// A set with no plays in it produces no groups, and that is deliberate
    /// rather than an edge nobody thought about: an empty range has no member
    /// standing on anybody, nothing folds, and answering it with an empty
    /// breakdown tells a reader the range is empty, which is true and names
    /// nobody.
    /// <para>
    /// The keys are spelled the way <see cref="DimensionBreakdown"/> spells
    /// them. They have to be, or the accounts are counted over one grouping and
    /// the rule applied to another.
    /// </para>
    /// </remarks>
    /// <param name="plays">The plays the breakdown would be folded from.</param>
    /// <param name="dimension">What the breakdown would group by.</param>
    /// <returns>The accounts behind each member.</returns>
    private static Dictionary<string, HashSet<Guid>> AccountsBehindEachGroup(
        IReadOnlyList<PlayRecord> plays,
        PlayDimension dimension)
    {
        var accounts = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);

        foreach (var play in plays)
        {
            var key = KeyOf(play, dimension);

            if (!accounts.TryGetValue(key, out var behind))
            {
                behind = new HashSet<Guid>();
                accounts[key] = behind;
            }

            behind.Add(play.UserId);
        }

        return accounts;
    }

    /// <summary>
    /// The value a play contributes to a dimension, spelled the way the fold
    /// spells it.
    /// </summary>
    /// <remarks>
    /// It has to agree with <see cref="DimensionBreakdown"/> or the count is
    /// taken over one grouping and the rule applied to another. A device is its
    /// identifier and not its name, because a device that was renamed is one
    /// device.
    /// </remarks>
    /// <param name="play">The play.</param>
    /// <param name="dimension">The dimension.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The dimension is not one this build knows.</exception>
    private static string KeyOf(PlayRecord play, PlayDimension dimension)
    {
        return dimension switch
        {
            PlayDimension.Client => play.ClientName,
            PlayDimension.Device => play.DeviceId,
            _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "This build has no name for that dimension.")
        };
    }

    /// <summary>
    /// How a play's key is read for one grouping, and null from it where the
    /// play counts under nothing.
    /// </summary>
    /// <remarks>
    /// A play with no parent falls out of a series list rather than becoming a
    /// row of its own. A film counted as a series of one is a sentence nobody
    /// asked for, and an empty identifier standing for "no series" would be one
    /// row every film in the range piled into.
    /// </remarks>
    /// <param name="grouping">What a row is.</param>
    /// <returns>How to read a play's key.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The grouping is not one this build knows.</exception>
    private static Func<PlayRecord, Guid?> KeyReaderFor(TopListGrouping grouping)
    {
        return grouping switch
        {
            TopListGrouping.Item => static play => play.ItemId,
            TopListGrouping.Series => static play => play.ParentId,
            _ => throw new ArgumentOutOfRangeException(nameof(grouping), grouping, "This build has no grouping of that name.")
        };
    }

    /// <summary>
    /// What to call a row, and null where nothing on the row names it.
    /// </summary>
    /// <remarks>
    /// A play keeps the name the item had at the time and no name for its
    /// parent, so a series is counted and cannot be labelled. It is left absent
    /// rather than filled with the name of one of the episodes under it, which
    /// would read as a series called after whichever episode was folded first.
    /// That is the half of issue #52 a stored series label is still owed for.
    /// </remarks>
    /// <param name="play">The play.</param>
    /// <param name="grouping">What a row is.</param>
    /// <returns>The name, or null.</returns>
    private static string? NameOf(PlayRecord play, TopListGrouping grouping)
        => grouping == TopListGrouping.Item ? play.ItemName : null;

    /// <summary>
    /// How two rows sort for one order: most of the chosen figure first, ties
    /// broken on the identifier.
    /// </summary>
    /// <remarks>
    /// The tie is never left to the dictionary. Two rows with the same figure
    /// in an unstable order are a list that changes between two readings of one
    /// range, and on a list that is then cut it is a row appearing and
    /// disappearing for no reason a reader can see.
    /// </remarks>
    /// <param name="order">Which figure decides.</param>
    /// <returns>The comparison.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The order is not one this build knows.</exception>
    private static Comparison<TitleRow> ComparisonFor(TopListOrder order)
    {
        return order switch
        {
            TopListOrder.WatchedTime => static (left, right) => Then(right.Watched.CompareTo(left.Watched), left, right),
            TopListOrder.Plays => static (left, right) => Then(right.Plays.CompareTo(left.Plays), left, right),
            _ => throw new ArgumentOutOfRangeException(nameof(order), order, "This build has no order of that name.")
        };
    }

    /// <summary>
    /// The figure's own comparison where it separates two rows, and the
    /// identifier where it does not.
    /// </summary>
    /// <param name="byFigure">What the chosen figure said.</param>
    /// <param name="left">One row.</param>
    /// <param name="right">The other.</param>
    /// <returns>The comparison.</returns>
    private static int Then(int byFigure, TitleRow left, TitleRow right)
        => byFigure != 0 ? byFigure : left.Key.CompareTo(right.Key);

    /// <summary>
    /// Opens the store, reads the window, and closes it again.
    /// </summary>
    /// <remarks>
    /// The one place in this layer that touches a store, so the bound and the
    /// range reach the file once rather than once per shape. A failure to open
    /// arrives as the type the endpoints translate into an outage, which is what
    /// keeps a broken file from reaching a caller as an empty report.
    /// <para>
    /// IT ASKS FOR ONE ROW MORE THAN IT WILL USE, AND THAT ROW IS THE WHOLE
    /// POINT. A read stopping exactly at the bound comes back with a full
    /// answer whether the range held exactly that many plays or ten times as
    /// many, and those two are a complete report and a wrong one. The extra row
    /// is what tells them apart, so the second is refused here instead of being
    /// folded and handed on. That is issue #56's first condition, and the price
    /// is one row fetched and discarded on a range that sits exactly on the
    /// bound.
    /// </para>
    /// </remarks>
    /// <param name="window">The range and the bound.</param>
    /// <returns>The plays, oldest first.</returns>
    /// <exception cref="TooManyPlaysToAnswerException">The range holds more plays than the bound allows.</exception>
    private IReadOnlyList<PlayRecord> Read(QueryWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var plays = ReadFromTheStore.Answering(
            _openStore,
            store => store.PlaysBetween(window.FromUtc, window.ToUtc, window.MostPlays + 1));

        if (plays.Count > window.MostPlays)
        {
            throw new TooManyPlaysToAnswerException(window.MostPlays);
        }

        return plays;
    }

    /// <summary>
    /// Folds one account's calendar year out of the store it is held in.
    /// </summary>
    /// <param name="userId">Whose year.</param>
    /// <param name="year">The calendar year, read in the zone below.</param>
    /// <param name="zone">The zone the year is read in.</param>
    /// <param name="topCount">How many rows each top list may hold.</param>
    /// <returns>The year, with each group of figures saying where it came from.</returns>
    public YearInReview YearFor(Guid userId, int year, TimeZoneInfo zone, int topCount)
    {
        ArgumentNullException.ThrowIfNull(zone);

        return ReadFromTheStore.Answering(_openStore, store => AYearOver(store, userId, year, zone, topCount));
    }

    /// <summary>
    /// The year, over a store somebody else opened.
    /// </summary>
    /// <remarks>
    /// One open for every read a year takes. What the answer says it covers,
    /// what its totals were added up from and what its item figures were read
    /// out of are then one reading of one store rather than several that a
    /// sweep running between them could put out of step.
    /// </remarks>
    /// <param name="store">The open store.</param>
    /// <param name="userId">Whose year.</param>
    /// <param name="year">The calendar year, read in the zone below.</param>
    /// <param name="zone">The zone the year is read in.</param>
    /// <param name="topCount">How many rows each top list may hold.</param>
    /// <returns>The year.</returns>
    public static YearInReview AYearOver(IPlayStore store, Guid userId, int year, TimeZoneInfo zone, int topCount)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(zone);

        // The oldest row comes from the same store and the same open as the
        // figures, so what the answer says it covers and what it was folded
        // from are one reading rather than two that a sweep running in between
        // could put out of step. It is asked over every account rather than
        // over this one, because what a window is about is the days the store
        // has lost and not the day this person started watching.
        return YearInReview.Over(
            RollupsForTheYear(store, userId, year, zone),
            (from, to) => APlayWindow(store, from, to),
            userId,
            year,
            zone,
            topCount,
            store.OldestPlayStartedUtc());
    }

    /// <summary>
    /// The sixth shape: the whole server's year, naming no account except the
    /// ones that agreed to be named.
    /// </summary>
    /// <remarks>
    /// One open and one read of the rows, folded three ways. A deletion running
    /// between two reads takes rows out of the second that the first counted, so
    /// a wrap-up whose figures, breakdowns and leaderboard came from separate
    /// reads would disagree with itself for a reason that is not an error in any
    /// of the three. Issue #68's third condition is an agreement between figures,
    /// and an agreement asserted across two readings of a moving store asserts
    /// nothing.
    /// <para>
    /// The bound is the year read in twelve windows between local midnights,
    /// each under the cap every other shape here reads under. A window over the
    /// cap costs the figures it would have fed and not the wrap-up, which is the
    /// rule issue #66 settled for a person's year and which holds here for the
    /// same reason: a year is a range the caller cannot shorten.
    /// </para>
    /// <para>
    /// The consent register is read inside this open and never kept. Issue #42's
    /// second condition asks that a withdrawal remove an account from every
    /// by-user view on the next request with no cache in between, and this is
    /// the only by-user view the plugin has.
    /// </para>
    /// </remarks>
    /// <param name="year">The calendar year, read in the zone below.</param>
    /// <param name="zone">The zone the year's days and its boundaries are read in.</param>
    /// <param name="topCount">How many rows each top list may hold.</param>
    /// <returns>The server's year.</returns>
    /// <exception cref="ArgumentNullException">No zone was given.</exception>
    /// <exception cref="StoreCouldNotBeOpenedException">The store could not be opened.</exception>
    public ServerYearInReview ServerYearFor(int year, TimeZoneInfo zone, int topCount)
    {
        ArgumentNullException.ThrowIfNull(zone);

        return ReadFromTheStore.Answering(_openStore, store => AServerYearOver(store, year, zone, topCount));
    }

    /// <summary>
    /// The server's year, over a store somebody else opened.
    /// </summary>
    /// <param name="store">The open store.</param>
    /// <param name="year">The calendar year, read in the zone below.</param>
    /// <param name="zone">The zone the year is read in.</param>
    /// <param name="topCount">How many rows each top list may hold.</param>
    /// <returns>The server's year.</returns>
    /// <exception cref="ArgumentNullException">No store or no zone was given.</exception>
    public static ServerYearInReview AServerYearOver(
        IPlayStore store,
        int year,
        TimeZoneInfo zone,
        int topCount)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(zone);

        var window = YearInReview.EveryonesPlaysInTheYear(
            (from, to) => APlayWindow(store, from, to),
            year,
            zone);

        var figures = YearInReview.OverEveryone(window, year, zone, topCount, store.OldestPlayStartedUtc());

        if (window.OverTheBound is not null)
        {
            // A refused window comes back holding no rows, and folding the three
            // shapes below over them would answer a year nobody could read with
            // a breakdown of nought plays and a leaderboard nobody is on. Every
            // one of them is absent instead, and the figures carry the reason.
            // An unknown answered as a nought is the failure issue #64's third
            // condition is against, met here at the shape that would produce it.
            return new ServerYearInReview(figures, null, null, null);
        }

        // What is NOT done here is reading the year a second time. One read is
        // what makes the four agree.
        var plays = window.Plays;

        return new ServerYearInReview(
            figures,
            ClientsBehindTheYear(plays),
            TranscodeReasonBreakdown.Over(plays),
            ConsentedLeaderboard.Over(
                plays,
                userId => store.ConsentFor(userId)?.Agreed == true,
                FewestAccountsBehindARow));
    }

    /// <summary>
    /// The client breakdown for a year, under the same rule the fourth shape
    /// applies to a range.
    /// </summary>
    /// <remarks>
    /// It is this method and not <see cref="Breakdown"/> because the rows are
    /// already read: calling that shape would open the store a second time and
    /// read the year again, which is the thing this wrap-up exists not to do.
    /// The rule itself is not restated - the same helper counts the accounts and
    /// the same constant decides.
    /// </remarks>
    /// <param name="plays">The year's rows.</param>
    /// <returns>The breakdown, or null where answering it would name somebody.</returns>
    private static DimensionBreakdown? ClientsBehindTheYear(IReadOnlyList<PlayRecord> plays)
    {
        var accounts = AccountsBehindEachGroup(plays, PlayDimension.Client);

        var folding = new List<string>();
        var behindTheFold = new HashSet<Guid>();

        foreach (var (key, behind) in accounts)
        {
            if (behind.Count >= FewestAccountsBehindARow)
            {
                continue;
            }

            folding.Add(key);
            behindTheFold.UnionWith(behind);
        }

        if (folding.Count > 0 && behindTheFold.Count < FewestAccountsBehindARow)
        {
            return null;
        }

        return DimensionBreakdown.Over(plays, PlayDimension.Client, folding);
    }

    /// <summary>
    /// The seventh shape: one account's own figures over one of three windows.
    /// </summary>
    /// <remarks>
    /// The only shape here that is entirely about one account, and the only one
    /// an ordinary caller may ask. It is served to that account alone, which the
    /// authorization matrix asserts, and nothing on it names anybody else.
    /// <para>
    /// One open, and every read inside it. The headline figures and the series
    /// fold from the rollups, bounded by days rather than by plays; the top
    /// items are the one figure a rollup cannot carry and read the play rows a
    /// month at a time under the same cap the other shapes read under. A figure
    /// that could not be taken is absent with its reason and never nought.
    /// Issue #274.
    /// </para>
    /// </remarks>
    /// <param name="userId">Whose figures.</param>
    /// <param name="window">Which window.</param>
    /// <param name="zone">The zone the window's days are read in.</param>
    /// <param name="now">The moment the window ends at, read off a clock the caller was given.</param>
    /// <param name="topCount">How many rows the top list may hold.</param>
    /// <returns>The figures.</returns>
    /// <exception cref="ArgumentNullException">No zone was given.</exception>
    /// <exception cref="StoreCouldNotBeOpenedException">The store could not be opened.</exception>
    public OwnFigures FiguresFor(
        Guid userId,
        PersonalWindow window,
        TimeZoneInfo zone,
        DateTimeOffset now,
        int topCount)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topCount);

        return ReadFromTheStore.Answering(
            _openStore,
            store => TheirFiguresOver(store, userId, window, zone, now, topCount));
    }

    /// <summary>
    /// One account's own figures, over a store somebody else opened.
    /// </summary>
    /// <param name="store">The open store.</param>
    /// <param name="userId">Whose figures.</param>
    /// <param name="window">Which window.</param>
    /// <param name="zone">The zone the window is read in.</param>
    /// <param name="now">The moment the window ends at.</param>
    /// <param name="topCount">How many rows the top list may hold.</param>
    /// <returns>The figures.</returns>
    /// <exception cref="ArgumentNullException">No store or no zone was given.</exception>
    public static OwnFigures TheirFiguresOver(
        IPlayStore store,
        Guid userId,
        PersonalWindow window,
        TimeZoneInfo zone,
        DateTimeOffset now,
        int topCount)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(zone);

        var today = LocalDay.Of(now, zone);
        var dayAfter = today.AddDays(1);
        var firstDay = FirstDayOf(window, today, store.OldestPlayStartedUtc(), zone);

        var rollups = RollupsForTheWindow(store, userId, firstDay, dayAfter, zone);
        var rows = TheirRowsOverTheWindow(store, userId, firstDay, dayAfter, zone);

        return OwnFiguresFold.Over(
            NameOf(window),
            window,
            zone,
            firstDay,
            dayAfter,
            rollups,
            rows.Rows,
            rows.Because,
            topCount);
    }

    /// <summary>
    /// The first day of a window, in the zone it is read in.
    /// </summary>
    /// <remarks>
    /// The thirty days and the twelve months are counted back from today, so the
    /// window a reader is shown is the window they would count themselves. All
    /// time starts at the oldest row the STORE holds over every account rather
    /// than at this account's own first play, for the reason a year's coverage is
    /// read that way: a window read off one person's rows reports a quiet start
    /// as a retention cut.
    /// </remarks>
    /// <param name="window">Which window.</param>
    /// <param name="today">Today, in the zone.</param>
    /// <param name="oldestPlayStartedUtc">When the oldest row anywhere in the store started.</param>
    /// <param name="zone">The zone.</param>
    /// <returns>The first day.</returns>
    private static DateOnly FirstDayOf(
        PersonalWindow window,
        DateOnly today,
        DateTime? oldestPlayStartedUtc,
        TimeZoneInfo zone)
        => window switch
        {
            PersonalWindow.Last30Days => today.AddDays(-29),
            PersonalWindow.Last12Months => new DateOnly(today.Year, today.Month, 1).AddMonths(-11),
            _ => oldestPlayStartedUtc is DateTime oldest
                ? LocalDay.Of(new DateTimeOffset(DateTime.SpecifyKind(oldest, DateTimeKind.Utc)), zone)
                : today,
        };

    /// <summary>
    /// What this window is called in the words a request names it with.
    /// </summary>
    /// <param name="window">Which window.</param>
    /// <returns>The name.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The window is not one this build knows.</exception>
    private static string NameOf(PersonalWindow window)
        => window switch
        {
            PersonalWindow.Last30Days => "last30Days",
            PersonalWindow.Last12Months => "last12Months",
            PersonalWindow.AllTime => "allTime",
            _ => throw new ArgumentOutOfRangeException(nameof(window)),
        };

    /// <summary>
    /// This account's rollups for the window, or null with the reason none may
    /// be used.
    /// </summary>
    /// <param name="store">The open store.</param>
    /// <param name="userId">The account.</param>
    /// <param name="firstDay">The first day of the window.</param>
    /// <param name="dayAfter">The first day after it.</param>
    /// <param name="zone">The zone the window is read in.</param>
    /// <returns>The rollups, or null where none may be used.</returns>
    private static IReadOnlyList<DailyRollup>? RollupsForTheWindow(
        IPlayStore store,
        Guid userId,
        DateOnly firstDay,
        DateOnly dayAfter,
        TimeZoneInfo zone)
    {
        // A store keyed in another zone holds days that are not the days this
        // window is about, so handing them over would report figures against
        // somebody else's midnights. Null carries no reason because it costs the
        // caller nothing: the rows answer the same figures, and the fold only
        // reports a figure as absent where both sources failed.
        if (store.RollupZone is not TimeZoneInfo keyed || !keyed.HasSameRules(zone))
        {
            return null;
        }

        var rollups = store.RollupsFor(userId, firstDay, dayAfter, MostRollupRowsAPersonalWindowMayHold + 1);

        // A window over the bound is answered as no rollups rather than as the
        // ones that fitted: a truncated fold is a figure wrong by whatever it
        // did not read with nothing on it saying so.
        return rollups.Count > MostRollupRowsAPersonalWindowMayHold ? null : rollups;
    }

    /// <summary>
    /// This account's play rows over the window, read a month at a time, or null
    /// with the reason they could not be read.
    /// </summary>
    /// <remarks>
    /// The walk stops at the first month that refuses. Every figure these rows
    /// feed is a figure over the whole window, so a partial read cannot answer
    /// one, and reading the remaining months would spend the reads to produce
    /// numbers that would then have to be thrown away.
    /// </remarks>
    /// <param name="store">The open store.</param>
    /// <param name="userId">The account.</param>
    /// <param name="firstDay">The first day of the window.</param>
    /// <param name="dayAfter">The first day after it.</param>
    /// <param name="zone">The zone the window is read in.</param>
    /// <returns>The rows, or null and why.</returns>
    private static (IReadOnlyList<PlayRecord>? Rows, string? Because) TheirRowsOverTheWindow(
        IPlayStore store,
        Guid userId,
        DateOnly firstDay,
        DateOnly dayAfter,
        TimeZoneInfo zone)
    {
        var months = new List<(DateOnly From, DateOnly To)>();
        var month = new DateOnly(firstDay.Year, firstDay.Month, 1);

        while (month < dayAfter)
        {
            var next = month.AddMonths(1);

            months.Add((month < firstDay ? firstDay : month, next < dayAfter ? next : dayAfter));
            month = next;
        }

        if (months.Count > MostWindowsAPersonalFigureIsReadOver)
        {
            return (null, "This window reaches further back than one answer may read the rows over.");
        }

        var rows = new List<PlayRecord>();

        foreach (var each in months)
        {
            var read = APlayWindow(store, LocalDay.StartOf(each.From, zone), LocalDay.StartOf(each.To, zone));

            if (read.OverTheBound is string because)
            {
                return (null, because);
            }

            foreach (var play in read.Plays)
            {
                // The account is filtered here and the window is filtered in the
                // fold, because a month read between two local midnights already
                // holds only rows inside it and an account read is not offered
                // by the store at all.
                if (play.UserId == userId)
                {
                    rows.Add(play);
                }
            }
        }

        return (rows, null);
    }

    /// <summary>
    /// This account's rollups for the year, where the store keyed them in the
    /// zone the year is being read in, and null where it did not.
    /// </summary>
    /// <remarks>
    /// A store states the zone it was first keyed in and not the one the setting
    /// names today, so a store keyed in another zone holds days that are not the
    /// days this year is about. Handing them over would report a busiest day
    /// that is somebody else's midnight, so they are not handed over at all and
    /// the fold reads the year from the play rows instead. Null is also what a
    /// store that has never keyed a rollup answers, which is not the same as one
    /// keyed in the default zone.
    /// <para>
    /// The read is bounded and a year that reaches the bound is treated as no
    /// rollups rather than as the ones that fitted. A truncated year would be a
    /// wrap-up that is wrong by whatever it did not read with nothing on it
    /// saying so, which is the failure issue #56 is about, met here from the
    /// aggregate side.
    /// </para>
    /// </remarks>
    /// <param name="store">The open store.</param>
    /// <param name="userId">Whose year.</param>
    /// <param name="year">The calendar year.</param>
    /// <param name="zone">The zone the year is read in.</param>
    /// <returns>The rollups, or null where none may be used.</returns>
    public static IReadOnlyList<DailyRollup>? RollupsForTheYear(
        IPlayStore store,
        Guid userId,
        int year,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(zone);

        if (store.RollupZone is not TimeZoneInfo keyed || !keyed.HasSameRules(zone))
        {
            return null;
        }

        var rollups = store.RollupsFor(
            userId,
            new DateOnly(year, 1, 1),
            new DateOnly(year + 1, 1, 1),
            MostRollupRowsAYearMayHold + 1);

        return rollups.Count > MostRollupRowsAYearMayHold ? null : rollups;
    }

    /// <summary>
    /// One month of play rows, read under the bound every other report reads
    /// under.
    /// </summary>
    /// <remarks>
    /// The bound is the query layer's own and is not a second one written here:
    /// a month over it is refused by the same number that refuses a range over
    /// it anywhere else, and the refusal becomes a figure the wrap-up reports as
    /// not computed rather than a wrap-up nobody gets.
    /// </remarks>
    /// <param name="store">The open store.</param>
    /// <param name="fromUtc">The first moment of the month.</param>
    /// <param name="toUtc">The first moment after it.</param>
    /// <returns>The rows, or the reason there are too many of them.</returns>
    public static WindowOfPlays APlayWindow(IPlayStore store, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        ArgumentNullException.ThrowIfNull(store);

        var plays = store.PlaysBetween(
            fromUtc.UtcDateTime,
            toUtc.UtcDateTime,
            QueryWindow.MostPlaysAnyShapeReads + 1);

        if (plays.Count > QueryWindow.MostPlaysAnyShapeReads)
        {
            return WindowOfPlays.TooManyToRead(
                FormattableString.Invariant(
                    $"One month of this year holds more than the {QueryWindow.MostPlaysAnyShapeReads} plays a single read may hold, so the figures taken from the play rows are left out rather than taken from part of the year."));
        }

        return WindowOfPlays.Holding(plays);
    }

    /// <summary>
    /// One item's running total while a top list is being folded.
    /// </summary>
    private sealed class Tally
    {
        public Tally(string? name)
        {
            Name = name;
        }

        /// <summary>
        /// Gets the name the first play under this key carried, and null where
        /// no play carried one.
        /// </summary>
        /// <remarks>
        /// The first and not the last, so an item renamed between two plays
        /// comes back under one of the two names rather than under whichever the
        /// fold saw last. Which of them is arbitrary; that it is stable is not.
        /// </remarks>
        public string? Name { get; }

        public long Plays { get; private set; }

        public TimeSpan Watched { get; private set; }

        public void Add(TimeSpan watched)
        {
            Plays++;
            Watched += watched;
        }
    }
}
