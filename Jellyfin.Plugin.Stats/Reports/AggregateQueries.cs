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
/// rather than preventing it. So a breakdown whose rows do not all stand on at
/// least <see cref="FewestAccountsBehindARow"/> accounts is withheld entirely,
/// and the total beside it stays available because a total on its own is not
/// half of a subtraction. Issue #41 is where that was decided and what it costs
/// is written there.
/// </para>
/// <para>
/// Every shape opens the store, reads what its window allows, folds it, and
/// closes the store again. Nothing here holds a connection between calls and
/// nothing caches: a sweep that ran between two reports is a report drawn from
/// what the file holds now rather than from what it held when the process
/// started.
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
    public ServerTotals Total(QueryWindow window)
    {
        var plays = Read(window);
        var watched = TimeSpan.Zero;

        foreach (var play in plays)
        {
            watched += play.WatchedDuration;
        }

        return new ServerTotals(plays.Count, watched, DeliveryMethodShares.Over(plays));
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
    public HourAndWeekdayGrid Distribution(QueryWindow window, TimeZoneInfo zone)
        => HourAndWeekdayGrid.Over(Read(window), zone, window.FromUtc, window.ToUtc);

    /// <summary>
    /// The fourth shape: one row per member of a dimension, or nothing where
    /// answering it would name somebody.
    /// </summary>
    /// <remarks>
    /// The shape the privacy rule actually bites on. A client or a device that
    /// one account alone used is that account under another name, and the rows
    /// beside it plus the total are enough to recover what that account watched
    /// whether or not the thin row is shown. So the breakdown is answered only
    /// when every row it would return stands on at least
    /// <see cref="FewestAccountsBehindARow"/> accounts, and is withheld whole
    /// otherwise.
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
    /// <returns>The rows and what they add up to, or null where the breakdown is withheld.</returns>
    /// <exception cref="StoreCouldNotBeOpenedException">The store could not be opened.</exception>
    public DimensionBreakdown? Breakdown(QueryWindow window, PlayDimension dimension)
    {
        var plays = Read(window);

        if (!EveryGroupStandsOnEnoughAccounts(plays, dimension))
        {
            return null;
        }

        return DimensionBreakdown.Over(plays, dimension);
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
    public TranscodeReasonBreakdown ReasonBreakdown(QueryWindow window)
        => TranscodeReasonBreakdown.Over(Read(window));

    /// <summary>
    /// The fifth shape: the items watched most, longest first.
    /// </summary>
    /// <remarks>
    /// An item is not an account, so this shape carries no privacy rule of its
    /// own beyond naming no account, which it does not. What a top list can
    /// still say about one person is said by the range being narrow enough, and
    /// a range is the caller's to choose here as it is everywhere else in this
    /// layer.
    /// <para>
    /// Ordered by watched time and not by play count, and the two disagree
    /// often enough to matter: a series somebody left running is many plays and
    /// little watching. Ties break on the item's identifier, so the order is
    /// fixed rather than whichever one the fold happened to produce.
    /// </para>
    /// </remarks>
    /// <param name="window">The range and the bound.</param>
    /// <param name="howMany">How many rows at most.</param>
    /// <returns>The rows, longest watched first, and empty where the range holds no plays.</returns>
    /// <exception cref="StoreCouldNotBeOpenedException">The store could not be opened.</exception>
    public IReadOnlyList<TitleRow> Top(QueryWindow window, int howMany)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(howMany);

        var tallies = new Dictionary<Guid, Tally>();

        foreach (var play in Read(window))
        {
            if (!tallies.TryGetValue(play.ItemId, out var tally))
            {
                tally = new Tally(play.ItemName);
                tallies[play.ItemId] = tally;
            }

            tally.Add(play.WatchedDuration);
        }

        var rows = new List<TitleRow>(tallies.Count);
        foreach (var (itemId, tally) in tallies)
        {
            rows.Add(new TitleRow(itemId, tally.Name, tally.Plays, tally.Watched));
        }

        rows.Sort(static (left, right) =>
        {
            var byWatched = right.Watched.CompareTo(left.Watched);

            return byWatched != 0 ? byWatched : left.Key.CompareTo(right.Key);
        });

        return rows.Count <= howMany ? rows : rows.GetRange(0, howMany);
    }

    /// <summary>
    /// Whether every member of a dimension in this set of plays was used by
    /// enough distinct accounts for the breakdown to be answerable.
    /// </summary>
    /// <remarks>
    /// A set with no plays in it passes, and that is deliberate rather than an
    /// edge nobody thought about: an empty range has no rows to stand on
    /// anybody, and answering it with nothing tells a reader the range is empty,
    /// which is true and names nobody.
    /// </remarks>
    /// <param name="plays">The plays the breakdown would be folded from.</param>
    /// <param name="dimension">What the breakdown would group by.</param>
    /// <returns>Whether the breakdown may be answered.</returns>
    private static bool EveryGroupStandsOnEnoughAccounts(IReadOnlyList<PlayRecord> plays, PlayDimension dimension)
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

        foreach (var behind in accounts.Values)
        {
            if (behind.Count < FewestAccountsBehindARow)
            {
                return false;
            }
        }

        return true;
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
    /// Opens the store, reads the window, and closes it again.
    /// </summary>
    /// <remarks>
    /// The one place in this layer that touches a store, so the bound and the
    /// range reach the file once rather than once per shape. A failure to open
    /// arrives as the type the endpoints translate into an outage, which is what
    /// keeps a broken file from reaching a caller as an empty report.
    /// </remarks>
    /// <param name="window">The range and the bound.</param>
    /// <returns>The plays, oldest first.</returns>
    private IReadOnlyList<PlayRecord> Read(QueryWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return ReadFromTheStore.Answering(
            _openStore,
            store => store.PlaysBetween(window.FromUtc, window.ToUtc, window.MostPlays));
    }

    /// <summary>
    /// One item's running total while a top list is being folded.
    /// </summary>
    private sealed class Tally
    {
        public Tally(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Gets the name the first play of this item carried.
        /// </summary>
        /// <remarks>
        /// The first and not the last, so an item renamed between two plays
        /// comes back under one of the two names rather than under whichever the
        /// fold saw last. Which of them is arbitrary; that it is stable is not.
        /// </remarks>
        public string Name { get; }

        public long Plays { get; private set; }

        public TimeSpan Watched { get; private set; }

        public void Add(TimeSpan watched)
        {
            Plays++;
            Watched += watched;
        }
    }
}
