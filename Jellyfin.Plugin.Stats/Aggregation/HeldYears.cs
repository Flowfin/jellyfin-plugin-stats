using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// Keeps a completed year once it has been asked for, and lets go of it when
/// the rows underneath it move.
/// </summary>
/// <remarks>
/// Folding a year is the most expensive reading this plugin does, and it is the
/// one everybody opens in the same week. What makes it worth keeping is that a
/// year which has ended cannot gain a play from ordinary use: capture writes a
/// row for the moment it happened, and that moment is in the year the server is
/// in now. So a finished year is a fixed answer over a fixed set of rows, and
/// the current one is not.
/// <para>
/// The current year is folded on every call and kept nowhere. A held answer for
/// it would be wrong within the hour on any server anybody is watching, and a
/// hold that has to be given up that often is a slower way of computing the
/// thing twice.
/// </para>
/// <para>
/// Nothing is folded before it is asked for. There is no walk over the accounts
/// that have rows and no warming of anything, so an account that never opens a
/// wrap-up costs no fold and occupies nothing here. That is the cheapest way to
/// hold the third condition of issue #70 and it is also the honest one: a
/// precomputed year for somebody who never asked is a figure about a person
/// kept for nobody's benefit.
/// </para>
/// <para>
/// What is kept is kept in memory, and deliberately rather than for want of a
/// table. A held year is one account's figures under their identifier, and the
/// rows it was folded from are deleted by three routes that also give the pages
/// back to the file. A copy in the store would outlive the rows it describes
/// and become the only remaining record of them, which is the opposite of what
/// a deletion is asked for. In memory it goes when the process does, and a
/// server that restarts pays one fold per account that opens the page.
/// </para>
/// <para>
/// The key carries the zone and the list bound as well as the account and the
/// year, because both change what the answer says. A year read in one zone and
/// a year read in another have different boundaries at both ends, and a top
/// list of ten and a top list of three are different lists. Keying on them
/// means a changed setting produces a fold rather than an answer that was true
/// under the old one.
/// </para>
/// <para>
/// This holds no store and opens nothing. The fold arrives as a function, so
/// what the rows are read through is decided where this is assembled, and the
/// suite can count how many times a year was folded without a file anywhere.
/// </para>
/// </remarks>
public sealed class HeldYears
{
    private readonly Func<Guid, int, TimeZoneInfo, int, YearInReview> _fold;
    private readonly TimeProvider _clock;
    private readonly Dictionary<Key, YearInReview> _held = [];
    private readonly object _gate = new();
    private long _generation;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeldYears"/> class.
    /// </summary>
    /// <param name="fold">Folds one account's calendar year, given the account, the year, the zone it is read in and how many rows a top list may hold.</param>
    /// <param name="clock">Says which year the server is in now, so that the year it is in is never held.</param>
    public HeldYears(Func<Guid, int, TimeZoneInfo, int, YearInReview> fold, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(fold);
        ArgumentNullException.ThrowIfNull(clock);

        _fold = fold;
        _clock = clock;
    }

    /// <summary>
    /// Gets how many answers are being kept.
    /// </summary>
    /// <remarks>
    /// Read by the suite rather than by the plugin. It is what lets a test say
    /// that nothing is held for an account that never asked, which is a
    /// statement about this object rather than about how often the fold ran.
    /// </remarks>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _held.Count;
            }
        }
    }

    /// <summary>
    /// Answers one account's calendar year, folding it only where there is no
    /// answer already being kept for it.
    /// </summary>
    /// <remarks>
    /// The fold runs outside the lock it would otherwise hold for its whole
    /// duration, which means two callers asking for the same year at the same
    /// moment can both fold it. That is the trade taken on purpose: folding a
    /// year twice costs a walk over one account's rows, and holding a lock
    /// across that walk would stop every other account's request for as long as
    /// it took. The second answer replaces the first, and both were folded from
    /// the same rows.
    /// <para>
    /// What that trade must not cost is a stale answer, and this is where the
    /// generation earns its place. A deletion arriving while a fold is in
    /// flight would otherwise be undone by that fold storing what it had
    /// already read, and the account would be left holding a year computed from
    /// rows that are now gone. The generation is read before the fold and
    /// compared after it, so an answer that was overtaken is returned to the
    /// caller who asked for it and kept for nobody.
    /// </para>
    /// </remarks>
    /// <param name="userId">Whose year this is.</param>
    /// <param name="year">The calendar year, read in the zone below.</param>
    /// <param name="zone">The zone the year's days and its boundaries are read in.</param>
    /// <param name="topCount">How many rows each top list may hold.</param>
    /// <returns>The year.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The top list bound is not a positive number.</exception>
    public YearInReview For(Guid userId, int year, TimeZoneInfo zone, int topCount)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topCount);

        if (year >= LocalDay.Of(_clock.GetUtcNow(), zone).Year)
        {
            return _fold(userId, year, zone, topCount);
        }

        var key = new Key(userId, year, zone.Id, topCount);

        long startedUnder;
        lock (_gate)
        {
            if (_held.TryGetValue(key, out var already))
            {
                return already;
            }

            startedUnder = _generation;
        }

        var folded = _fold(userId, year, zone, topCount);

        lock (_gate)
        {
            if (_generation == startedUnder)
            {
                _held[key] = folded;
            }
        }

        return folded;
    }

    /// <summary>
    /// Lets go of every answer being kept for one account.
    /// </summary>
    /// <remarks>
    /// Every year of theirs rather than the one a deletion touched, because
    /// what reaches this is a count of rows that went and not which years they
    /// were in. Finding out would mean reading the rows that have just been
    /// deleted.
    /// </remarks>
    /// <param name="userId">The account whose rows moved.</param>
    public void Forget(Guid userId)
    {
        lock (_gate)
        {
            // Materialised before the first removal rather than removed while
            // enumerating, which would invalidate the dictionary's enumerator.
            // ToList is what forces that here, so the two phases stay two.
            var theirs = _held.Keys.Where(key => key.UserId == userId).ToList();

            for (var i = 0; i < theirs.Count; i++)
            {
                _held.Remove(theirs[i]);
            }

            // Moved for every account rather than for the one named, because a
            // fold in flight for somebody else may be reading rows this same
            // deletion is taking out from under it. What that costs is a
            // discarded fold on a request that happened to overlap a deletion,
            // and what a narrower generation would buy is not worth a second
            // counter to get wrong.
            _generation++;
        }
    }

    /// <summary>
    /// Lets go of everything being kept.
    /// </summary>
    /// <remarks>
    /// What a retention sweep calls. It deletes by a moment rather than by an
    /// account, so every account is a candidate and there is nothing narrower
    /// to let go of.
    /// </remarks>
    public void ForgetEverything()
    {
        lock (_gate)
        {
            _held.Clear();
            _generation++;
        }
    }

    /// <summary>
    /// What one kept answer is filed under.
    /// </summary>
    /// <param name="UserId">Whose year it is.</param>
    /// <param name="Year">The calendar year.</param>
    /// <param name="ZoneId">The zone it was read in, as that zone calls itself.</param>
    /// <param name="TopCount">How many rows its top lists were allowed to hold.</param>
    private sealed record Key(Guid UserId, int Year, string ZoneId, int TopCount);
}
