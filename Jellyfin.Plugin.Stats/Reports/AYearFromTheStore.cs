using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Reports;

/// <summary>
/// The reads one account's calendar year is folded from, and the bounds on each
/// of them.
/// </summary>
/// <remarks>
/// A wrap-up used to be folded by walking every play row one account has, with
/// no bound on the count and no range cap in front of it. That is the read this
/// replaces, and the two halves of what replaces it are bounded in different
/// ways on purpose.
/// <para>
/// THE TOTALS COME OFF THE DAY-BY-DAY ROLLUPS, WHICH ARE BOUNDED BY DAYS AND NOT
/// BY PLAYS. One account's year there costs the days it recorded on, times the
/// kinds of item, times the clients, and never the plays themselves, so a heavy
/// watcher and a light one with the same habits cost the same read. The store's
/// range read is keyed and it searches rather than scanning, which is what makes
/// it worth having at all. Issue #254.
/// </para>
/// <para>
/// THE FIGURES ONLY A ROW CARRIES COME OFF THE PLAY ROWS, A MONTH AT A TIME. A
/// year is a range the caller cannot shorten, so refusing the whole wrap-up
/// because the year holds too many plays would be a permanent refusal of
/// somebody's own history rather than one they could retry over a shorter range.
/// Twelve bounded reads take the place of one unbounded one, each under the
/// bound every other report reads under, and a month still over it degrades
/// exactly the figures it would have fed. Issue #66.
/// </para>
/// <para>
/// It is here rather than in the container so that what a year reads can be
/// driven by a test against a real store. The container is where the store is
/// opened and this is what it does with the store once it is open.
/// </para>
/// </remarks>
public static class AYearFromTheStore
{
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
    /// Folds one account's calendar year out of the store it is held in.
    /// </summary>
    /// <param name="store">The open store.</param>
    /// <param name="userId">Whose year.</param>
    /// <param name="year">The calendar year, read in the zone below.</param>
    /// <param name="zone">The zone the year is read in.</param>
    /// <param name="topCount">How many rows each top list may hold.</param>
    /// <returns>The year, with each group of figures saying where it came from.</returns>
    public static YearInReview For(IPlayStore store, Guid userId, int year, TimeZoneInfo zone, int topCount)
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
}
